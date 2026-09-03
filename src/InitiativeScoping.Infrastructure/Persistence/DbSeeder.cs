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

        var types = new[]
        {
            new ResourceType { Name = "Software Engineer", Discipline = "Engineering" },
            new ResourceType { Name = "QA Analyst", Discipline = "QA" },
            new ResourceType { Name = "Product Manager", Discipline = "Product" },
            new ResourceType { Name = "Project Manager", Discipline = "PM" },
            new ResourceType { Name = "UX Designer", Discipline = "Design" }
        };
        db.ResourceTypes.AddRange(types);

        db.SizingConversions.AddRange(
            new SizingConversion { Method = SizingMethod.TShirt, Key = "S", Hours = 120 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "M", Hours = 240 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "L", Hours = 480 },
            new SizingConversion { Method = SizingMethod.TShirt, Key = "XL", Hours = 960 });

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
