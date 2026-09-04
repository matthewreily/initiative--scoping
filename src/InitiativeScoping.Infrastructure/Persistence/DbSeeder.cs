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

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.ResourceTypes.AnyAsync(ct))
        {
            await BackfillTemplatesAsync(db, ct);
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

        foreach (var (key, name) in TShirtTemplates)
        {
            db.AllocationTemplates.Add(StandardSquadTemplate(key, name, types[0], types[1], types[2], types[3], types[4]));
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
        var missing = TShirtTemplates.Where(t => !existing.Contains(t.Key, StringComparer.OrdinalIgnoreCase)).ToArray();
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

        foreach (var (key, name) in missing)
        {
            if (!await db.SizingConversions.AnyAsync(c => c.Method == SizingMethod.TShirt && c.Key == key, ct))
            {
                continue;
            }

            db.AllocationTemplates.Add(StandardSquadTemplate(key, name, eng, qa, product, pm, ux));
        }

        await db.SaveChangesAsync(ct);
    }

    private static AllocationTemplate StandardSquadTemplate(
        string key, string name, ResourceType eng, ResourceType qa, ResourceType product, ResourceType pm, ResourceType ux) => new()
    {
        Method = SizingMethod.TShirt, SizeKey = key, Name = name,
        Lines =
        [
            new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = product, Seniority = Seniority.Senior, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Discovery", ResourceType = ux, Seniority = Seniority.Mid, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = eng, Seniority = Seniority.Senior, Percent = 45 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = qa, Seniority = Seniority.Mid, Percent = 15 },
            new AllocationTemplateLine { PhaseName = "Build", ResourceType = pm, Seniority = Seniority.Mid, Percent = 10 },
            new AllocationTemplateLine { PhaseName = "Launch", ResourceType = eng, Seniority = Seniority.Senior, Percent = 10 }
        ]
    };
}
