using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Web.Areas.Admin.Models;

public class ApplicationPlatformEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class PodEditModel : ApplicationPlatformEditModel
{
    [Range(1, int.MaxValue)] public int BusinessUnitId { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    public int[] PersonIds { get; set; } = [];
    public int[] ApplicationIds { get; set; } = [];
    public List<BusinessUnit> BusinessUnits { get; set; } = [];
    public List<Person> People { get; set; } = [];
    public List<ApplicationPlatform> Applications { get; set; } = [];
}
