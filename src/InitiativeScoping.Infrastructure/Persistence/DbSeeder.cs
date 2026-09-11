using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public static class DbSeeder
{
    private static readonly (string Key, string Name)[] TShirtTemplates =
    [
        ("S", "Small feature - standard squad"),
        ("M", "Medium feature - standard squad"),
        ("L", "Large feature - standard squad"),
        ("XL", "Extra-large feature - standard squad")
    ];

    /// <summary>Default career ladder; the Seniority migration inserts the same rows (ids 1-5) into existing databases.</summary>
    public static readonly string[] DefaultSeniorityLevels = ["Associate", "Mid", "Senior", "Staff", "Principal"];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.SeniorityLevels.AnyAsync(ct))
        {
            db.SeniorityLevels.AddRange(DefaultSeniorityLevels.Select((n, i) => new SeniorityLevel { Name = n, SortOrder = i + 1 }));
            await db.SaveChangesAsync(ct);
        }

        if (await db.ResourceTypes.AnyAsync(ct))
        {
            await BackfillTemplatesAsync(db, ct);
            return;
        }

        var levels = await db.SeniorityLevels.OrderBy(s => s.SortOrder).ToListAsync(ct);
        var mid = levels.First(s => s.Name == "Mid");
        var senior = levels.First(s => s.Name == "Senior");

        db.BusinessUnits.Add(new BusinessUnit { Name = "Boarding" });

        var engineering = new Discipline { Name = "Engineering" };
        var qa = new Discipline { Name = "QA" };
        var product = new Discipline { Name = "Product" };
        var pm = new Discipline { Name = "PM" };
        var design = new Discipline { Name = "Design" };
        db.Disciplines.AddRange(engineering, qa, product, pm, design);

        var types = new[]
        {
            new ResourceType { Name = "Software Engineer", Discipline = engineering },
            new ResourceType { Name = "QA Analyst", Discipline = qa },
            new ResourceType { Name = "Product Manager", Discipline = product },
            new ResourceType { Name = "Project Manager", Discipline = pm },
            new ResourceType { Name = "UX Designer", Discipline = design }
        };
        db.ResourceTypes.AddRange(types);

        db.SizingConversions.AddRange(
            new SizingConversion { Method = SizingMethod.TShirt, Key = "S", Hours = 120 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "M", Hours = 240 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "L", Hours = 480 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "XL", Hours = 960 });

        foreach (var (key, name) in TShirtTemplates)
        {
            db.AllocationTemplates.Add(StandardSquadTemplate(key, name, types[0], types[1], types[2], types[3], types[4], mid, senior));
        }

        var card = new RateCard
        {
            Name = $"{DateTime.UtcNow.Year} Rate Card",
            EffectiveStart = new DateOnly(DateTime.UtcNow.Year, 1, 1),
            Status = RateCardStatus.Published
        };
        foreach (var t in types)
        {
            foreach (var s in levels)
            {
                card.Entries.Add(new RateCardEntry
                {
                    ResourceType = t, Seniority = s, Location = "Onshore",
                    ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 60 + 20 * s.SortOrder
                });
                card.Entries.Add(new RateCardEntry
                {
                    ResourceType = t, Seniority = s, Location = "Onshore",
                    ResourcingClass = ResourcingClass.Vendor, HourlyRate = 90 + 25 * s.SortOrder
                });
            }
        }
        db.RateCards.Add(card);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Databases seeded before S/XL templates existed only have M/L. Add the missing seeded sizes
    /// when the seeded resource types are still present, so every seeded conversion is selectable.
    /// </summary>
    private static async Task BackfillTemplatesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.AllocationTemplates
            .Where(t => t.Method == SizingMethod.TShirt)
            .Select(t => t.SizeKey)
            .ToListAsync(ct);
        var missing = TShirtTemplates.Where(t => !existing.Contains(t.Key, StringComparer.Ordinal)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var types = await db.ResourceTypes.ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase, ct);
        if (!types.TryGetValue("Software Engineer", out var eng) || !types.TryGetValue("QA Analyst", out var qa)
            || !types.TryGetValue("Product Manager", out var product) || !types.TryGetValue("Project Manager", out var pm)
            || !types.TryGetValue("UX Designer", out var ux))
        {
            return;
        }

        var levels = await db.SeniorityLevels.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, ct);
        if (!levels.TryGetValue("Mid", out var mid) || !levels.TryGetValue("Senior", out var senior))
        {
            return;
        }

        foreach (var (key, name) in missing)
        {
            if (!await db.SizingConversions.AnyAsync(c => c.Method == SizingMethod.TShirt && c.Key == key, ct))
            {
                continue;
            }

            db.AllocationTemplates.Add(StandardSquadTemplate(key, name, eng, qa, product, pm, ux, mid, senior));
        }

        await db.SaveChangesAsync(ct);
    }

    private static AllocationTemplate StandardSquadTemplate(
        string key, string name, ResourceType eng, ResourceType qa, ResourceType product, ResourceType pm, ResourceType ux,
        SeniorityLevel mid, SeniorityLevel senior) => new()
    {
        Method = SizingMethod.TShirt, SizeKey = key, Name = name,
        Lines =
        [
            new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = product, Seniority = senior, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = ux, Seniority = mid, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = eng, Seniority = senior, Percent = 45 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = qa, Seniority = mid, Percent = 15 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = pm, Seniority = mid, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Launch", ResourceType = eng, Seniority = senior, Percent = 10 }
        ]
    };
}
