using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class NamedPeopleTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);
    private const string Header = "ExternalProjectId,ExternalPersonId,WorkDate,Hours,Cost,Reference\n";

    [Fact]
    public async Task Person_must_match_the_allocation_dimensions_and_is_shown_on_details_team_and_capacity()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (engineerId, qaId, buId, otherBuId) = await LookupsAsync();

        var jane = await CreatePersonAsync(client, $"Jane {tag}", engineerId, buId, seniorityId: 3);
        var quinn = await CreatePersonAsync(client, $"Quinn {tag}", qaId, buId, seniorityId: 3);
        var outsider = await CreatePersonAsync(client, $"Out {tag}", engineerId, otherBuId, seniorityId: 3);
        var gone = await CreatePersonAsync(client, $"Gone {tag}", engineerId, buId, seniorityId: 3, active: false);

        var id = await CreateInitiativeAsync(client, $"Named {tag}", buId);
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-02", ["PlannedEnd"] = "2026-03-31" });
        int phaseId;
        using (var scope = factory.Services.CreateScope())
        {
            phaseId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        }

        // Only Jane is offered: right type/seniority/BU/class and active. Outsider's BU is not participating; Gone is inactive.
        var form = await client.GetStringAsync(details);
        Assert.Contains($"Jane {tag}", form);
        Assert.Contains($"Quinn {tag}", form);
        Assert.DoesNotContain($"Out {tag}", form);
        Assert.DoesNotContain($"Gone {tag}", form);

        Dictionary<string, string> Allocation(string qty = "1", int? typeId = null, params int[] people)
        {
            var form = new Dictionary<string, string>
            {
                ["PhaseId"] = phaseId.ToString(), ["BusinessUnitId"] = buId.ToString(), ["ResourceTypeId"] = (typeId ?? engineerId).ToString(),
                ["SeniorityId"] = "3", ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte),
                ["Quantity"] = qty, ["EstimatedHours"] = "40"
            };
            for (var i = 0; i < people.Length; i++)
            {
                form[$"PersonIds[{i}]"] = people[i].ToString();
            }

            return form;
        }

        var wrongType = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(people: quinn));
        Assert.Equal(HttpStatusCode.Redirect, wrongType.StatusCode);
        Assert.Contains("do not match this allocation", await client.GetStringAsync(details));

        var inactive = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(people: gone));
        Assert.Equal(HttpStatusCode.Redirect, inactive.StatusCode);
        Assert.Contains("is inactive on the roster", await client.GetStringAsync(details));

        var tooMany = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(qty: "1", people: [jane, gone]));
        Assert.Equal(HttpStatusCode.Redirect, tooMany.StatusCode);
        Assert.Contains("2 people are named but the quantity is 1", await client.GetStringAsync(details));

        // Jane fills one of three engineer seats; the other two stay unassigned.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(qty: "3", people: jane))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(qty: "2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(typeId: qaId, people: quinn))).StatusCode);
        _ = outsider;

        int namedId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocations = await db.InitiativeAllocations.Include(a => a.People).Where(a => a.InitiativeId == id).ToListAsync();
            Assert.Equal(3, allocations.Count);
            Assert.Equal([jane, quinn], allocations.SelectMany(a => a.PersonIds).OrderBy(x => x));
            Assert.Single(allocations, a => !a.HasNamedPeople && a.Quantity == 2);
            var named = Assert.Single(allocations, a => a.Quantity == 3);
            Assert.Equal(2, named.UnassignedSeats);
            namedId = named.Id;
        }

        // Editing keeps Jane and adds a second engineer seat holder; the quantity floor is enforced.
        var second = await CreatePersonAsync(client, $"Sam {tag}", engineerId, buId, seniorityId: 3);
        var editForm = Allocation(qty: "3", people: [jane, second]);
        editForm["Id"] = namedId.ToString();
        editForm["InitiativeId"] = id.ToString();
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, $"/Initiatives/EditAllocation/{namedId}", $"/Initiatives/EditAllocation/{namedId}", editForm)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var named = await db.InitiativeAllocations.Include(a => a.People).SingleAsync(a => a.Id == namedId);
            Assert.Equal([jane, second], named.PersonIds.OrderBy(x => x));
            Assert.Equal(1, named.UnassignedSeats);
        }

        var page = await client.GetStringAsync(details);
        Assert.Contains($"Jane {tag}", page);
        Assert.Contains($"Sam {tag}", page);
        Assert.Contains($"Quinn {tag}", page);
        Assert.Contains("+ 1 unassigned", page);
        Assert.DoesNotContain("No named people yet", page);

        var capacity = await client.GetStringAsync($"/Capacity?view=People&businessUnitId={buId}");
        Assert.Contains($"Jane {tag}", capacity);
        Assert.Contains("Unassigned", capacity);

        var export = await client.GetAsync($"/Capacity/Export?format=csv&view=People&businessUnitId={buId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains($"Jane {tag}", await export.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Imported_actuals_match_people_named_on_the_initiative_by_display_name()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (engineerId, _, buId, _) = await LookupsAsync();

        var jane = await CreatePersonAsync(client, $"Jane {tag}", engineerId, buId, seniorityId: 3);
        var twin = await CreatePersonAsync(client, $"Jane {tag}", engineerId, buId, seniorityId: 3, externalIds: $"TWIN-{tag}");

        var id = await CreateInitiativeAsync(client, $"Actuals {tag}", buId);
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddSourceMapping/{id}", new() { ["source"] = "Csv", ["externalProjectId"] = $"PRJ-{tag}" });
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-02", ["PlannedEnd"] = "2026-03-31" });
        int phaseId;
        using (var scope = factory.Services.CreateScope())
        {
            phaseId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["BusinessUnitId"] = buId.ToString(), ["ResourceTypeId"] = engineerId.ToString(),
            ["SeniorityId"] = "3", ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte),
            ["PersonIds[0]"] = jane.ToString(), ["Quantity"] = "1", ["EstimatedHours"] = "40"
        })).StatusCode);

        var other = await CreateInitiativeAsync(client, $"Other {tag}", buId);
        await PostFormAsync(client, $"/Initiatives/Details/{other}", $"/Initiatives/AddSourceMapping/{other}", new() { ["source"] = "Csv", ["externalProjectId"] = $"OTH-{tag}" });

        var csv = Header +
            $"PRJ-{tag},Jane {tag},2026-03-10,8,,{tag}-a\n" +   // name matches the person named on this initiative
            $"PRJ-{tag},TWIN-{tag},2026-03-11,4,,{tag}-b\n" +   // external id wins even though the twin is not on the plan
            $"OTH-{tag},Jane {tag},2026-03-12,2,,{tag}-c\n";    // nobody named on the other initiative -> unmapped
        var content = new MultipartFormDataContent { { new StringContent(await GetTokenAsync(client, "/Actuals")), "__RequestVerificationToken" } };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "actuals.csv");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Actuals/Import", content)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.ActualEntries.Where(e => e.SourceReference.StartsWith(tag)).ToDictionaryAsync(e => e.SourceReference);
            Assert.Equal(jane, entries[$"{tag}-a"].PersonId);
            Assert.False(entries[$"{tag}-a"].IsUnmapped);
            Assert.Equal(twin, entries[$"{tag}-b"].PersonId);
            Assert.Null(entries[$"{tag}-c"].PersonId);
            Assert.True(entries[$"{tag}-c"].IsUnmapped);
        }
    }

    // ----- Helpers -----

    private async Task<(int EngineerId, int QaId, int BuId, int OtherBuId)> LookupsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var types = await db.ResourceTypes.OrderBy(t => t.Id).ToListAsync();
        var engineer = types.First(t => t.Name == "Software Engineer");
        var qa = types.First(t => t.Id != engineer.Id);
        var boarding = await db.BusinessUnits.FirstAsync(b => b.Name == "Boarding");
        var other = await db.BusinessUnits.FirstOrDefaultAsync(b => b.Name == "Named-people other BU");
        if (other is null)
        {
            other = new BusinessUnit { Name = "Named-people other BU" };
            db.BusinessUnits.Add(other);
            await db.SaveChangesAsync();
        }

        return (engineer.Id, qa.Id, boarding.Id, other.Id);
    }

    private async Task<int> CreatePersonAsync(HttpClient client, string name, int typeId, int buId, int seniorityId, bool active = true, string? externalIds = null)
    {
        var created = await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", new()
        {
            ["DisplayName"] = name, ["ExternalIds"] = externalIds ?? string.Empty, ["ResourceTypeId"] = typeId.ToString(), ["BusinessUnitId"] = buId.ToString(),
            ["SeniorityId"] = seniorityId.ToString(), ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["IsActive"] = active ? "true" : "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.People.Where(p => p.DisplayName == name).OrderByDescending(p => p.Id).FirstAsync()).Id;
    }

    private static async Task<int> CreateInitiativeAsync(HttpClient client, string name, int buId)
    {
        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-03-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {page}");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetTokenAsync(client, tokenPage);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
