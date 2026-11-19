namespace InitiativeScoping.Domain.Entities;

public class ApplicationPlatform
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Pod
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public List<PodMember> Members { get; set; } = [];
    public List<PodApplication> Applications { get; set; } = [];
}

public class PodMember
{
    public decimal MembershipPercent { get; set; } = 100;
    public int PodId { get; set; }
    public Pod? Pod { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
}

public class PodApplication
{
    public int PodId { get; set; }
    public Pod? Pod { get; set; }
    public int ApplicationPlatformId { get; set; }
    public ApplicationPlatform? ApplicationPlatform { get; set; }
}

public class InitiativeApplication
{
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int ApplicationPlatformId { get; set; }
    public ApplicationPlatform? ApplicationPlatform { get; set; }
}
