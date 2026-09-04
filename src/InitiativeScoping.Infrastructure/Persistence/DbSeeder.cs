using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.ResourceTypes.AnyAsync(ct))
        {
            return;
        }

        var bu = new BusinessUnit { Name = "Boarding" };
        db.BusinessUnits.Add(bu);

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

        foreach (var (key, name) in new[] { ("M", "Medium feature - standard squad"), ("L", "Large feature - standard squad") })
        {
            db.AllocationTemplates.Add(new AllocationTemplate
            {
                Method = SizingMethod.TShirt, SizeKey = key, Name = name,
                Lines =
                [
                    new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = types[2], Seniority = Seniority.Senior, Percent = 10 },
                    new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = types[4], Seniority = Seniority.Mid, Percent = 10 },
                    new AllocationTemplateLine { PhaseName = "Build", ResourceType = types[0], Seniority = Seniority.Senior, Percent = 45 },
                    new AllocationTemplateLine { PhaseName = "Build", ResourceType = types[1], Seniority = Seniority.Mid, Percent = 15 },
                    new AllocationTemplateLine { PhaseName = "Build", ResourceType = types[3], Seniority = Seniority.Mid, Percent = 10 },
                    new AllocationTemplateLine { PhaseName = "Launch", ResourceType = types[0], Seniority = Seniority.Senior, Percent = 10 }
                ]
            });
        }

        var card = new RateCard
        {
            Name = $"{DateTime.UtcNow.Year} Rate Card",
            EffectiveStart = new DateOnly(DateTime.UtcNow.Year, 1, 1),
            Status = RateCardStatus.Published
        };
        foreach (var t in types)
        {
            foreach (var s in Enum.GetValues<Seniority>())
            {
                card.Entries.Add(new RateCardEntry
                {
                    ResourceType = t, BusinessUnit = bu, Seniority = s, Location = "Onshore",
                    ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 60 + 20 * (int)s
                });
                card.Entries.Add(new RateCardEntry
                {
                    ResourceType = t, BusinessUnit = bu, Seniority = s, Location = "Onshore",
                    ResourcingClass = ResourcingClass.Vendor, HourlyRate = 90 + 25 * (int)s
                });
            }
        }
        db.RateCards.Add(card);

        await db.SaveChangesAsync(ct);
    }
}
