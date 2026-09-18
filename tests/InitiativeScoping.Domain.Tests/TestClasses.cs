using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Tests;

/// <summary>The two seeded resourcing classes, for domain tests that build entity graphs by hand.</summary>
public static class TestClasses
{
    public static ResourcingClass Internal => new() { Id = ResourcingClass.InternalId, Name = ResourcingClass.InternalName, DefaultCapexPercent = 70, SortOrder = 1 };
    public static ResourcingClass Vendor => new() { Id = ResourcingClass.VendorId, Name = ResourcingClass.VendorName, IsVendor = true, DefaultCapexPercent = 100, SortOrder = 2 };
}
