using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

/// <summary>Default split of a relative-sized estimate (e.g. "L") into resource type / phase percentages.</summary>
public class AllocationTemplate
{
    public int Id { get; set; }
    public SizingMethod Method { get; set; }
    public required string SizeKey { get; set; }
    public required string Name { get; set; }
    public List<AllocationTemplateLine> Lines { get; set; } = [];
}

public class AllocationTemplateLine
{
    public int Id { get; set; }
    public int AllocationTemplateId { get; set; }
    public AllocationTemplate? AllocationTemplate { get; set; }
    public required string PhaseName { get; set; }
    public int ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public Seniority Seniority { get; set; }
    public decimal Percent { get; set; }
}
