using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Web.Models;

public class PodAssignmentModel
{
    [Range(1, int.MaxValue)] public int PodId { get; set; }
    [MinLength(1)] public int[] PhaseIds { get; set; } = [];
    [Range(typeof(decimal), "0.01", "100")] public decimal AllocationPercent { get; set; } = 100;
}

public class InitiativePodsModel : PodAssignmentModel
{
    public Initiative Initiative { get; set; } = null!;
    public bool CanEdit { get; set; }
    public List<ApplicationPlatform> Applications { get; set; } = [];
    public int[] ApplicationIds { get; set; } = [];
    public List<Pod> Pods { get; set; } = [];
    public List<Pod> Suggested { get; set; } = [];
    public PodPlan? Preview { get; set; }
    public Pod? Pod { get; set; }
}
