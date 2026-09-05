using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Integration.Tests;

public class DbSeederTests
{
    [Fact]
    public async Task Fresh_seed_creates_a_template_for_every_tshirt_conversion()
    {
        await using var db = NewDb();
        await DbSeeder.SeedAsync(db);

        var conversions = await db.SizingConversions.Where(c => c.Method == SizingMethod.TShirt).Select(c => c.Key).ToListAsync();
        var templates = await db.AllocationTemplates.Where(t => t.Method == SizingMethod.TShirt).Select(t => t.SizeKey).ToListAsync();

        Assert.Equal(new[] { "S", "M", "L", "XL" }.Order(), conversions.Order());
        Assert.Equal(conversions.Order(), templates.Order());
    }

    [Fact]
    public async Task Reseed_backfills_missing_tshirt_templates_and_is_idempotent()
    {
        await using var db = NewDb();
        await DbSeeder.SeedAsync(db);
        db.AllocationTemplates.RemoveRange(db.AllocationTemplates.Where(t => t.SizeKey == "S" || t.SizeKey == "XL"));
        db.SizingConversions.RemoveRange(db.SizingConversions.Where(c => c.Key == "XL"));
        await db.SaveChangesAsync();

        await DbSeeder.SeedAsync(db);
        await DbSeeder.SeedAsync(db);

        var templates = await db.AllocationTemplates.Include(t => t.Lines).Where(t => t.Method == SizingMethod.TShirt).ToListAsync();
        Assert.Equal(new[] { "L", "M", "S" }, templates.Select(t => t.SizeKey).Order());
        Assert.Equal(6, templates.Single(t => t.SizeKey == "S").Lines.Count);
        Assert.Equal(5, await db.ResourceTypes.CountAsync());
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"is-seed-{Guid.NewGuid():N}.db")}")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
