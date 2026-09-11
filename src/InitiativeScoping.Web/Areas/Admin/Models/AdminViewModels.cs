using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InitiativeScoping.Web.Areas.Admin.Models;

public sealed record BulkBarOptions(string PluralNoun, string DeleteAction = "BulkDelete", string? ActivateAction = "BulkActivate", string? DeactivateAction = "BulkDeactivate");

public class BusinessUnitEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class BusinessUnitListItem
{
    public required BusinessUnit Unit { get; init; }
    public int ReferenceCount { get; init; }
}

public class DisciplineEditModel
{
    public int Id { get; set; }
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class DisciplineListItem
{
    public required Discipline Discipline { get; init; }
    public int ReferenceCount { get; init; }
}

public class SeniorityEditModel
{
    public int Id { get; set; }
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
    [Required, Range(0, 1000), Display(Name = "Sort order")]
    public int SortOrder { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class SeniorityListItem
{
    public required SeniorityLevel Level { get; init; }
    public int RateCardEntries { get; init; }
    public int Allocations { get; init; }
    public int People { get; init; }
    public int TemplateLines { get; init; }
}

public class VendorEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class VendorListItem
{
    public required Vendor Vendor { get; init; }
    public int RateCardEntries { get; init; }
    public int Allocations { get; init; }
    public int People { get; init; }
}

public class ResourceTypeEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Select a discipline."), Range(1, int.MaxValue, ErrorMessage = "Select a discipline.")]
    public int? DisciplineId { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SelectListItem> Disciplines { get; set; } = [];
}

public class ResourceTypeListItem
{
    public required ResourceType Type { get; init; }
    public int ReferenceCount { get; init; }
}

public class RateCardEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [Required, DataType(DataType.Date), Display(Name = "Effective start")]
    public DateOnly EffectiveStart { get; set; } = new(DateTime.UtcNow.Year, 1, 1);
}

public class RateCardEntryEditModel
{
    public int Id { get; set; }
    public int RateCardId { get; set; }
    [Required, Display(Name = "Resource type")]
    public int ResourceTypeId { get; set; }
    [Required, Display(Name = "Seniority")]
    public int SeniorityId { get; set; }
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Display(Name = "Vendor")]
    public int? VendorId { get; set; }
    [Required, Range(0, 100000), Display(Name = "Hourly rate")]
    public decimal HourlyRate { get; set; }
}

public class RateCardDetailsModel
{
    public required RateCard Card { get; init; }
    public required RateCardEntryEditModel NewEntry { get; init; }
    public required SelectList ResourceTypes { get; init; }
    public required SelectList Seniorities { get; init; }
    public required SelectList Vendors { get; init; }
    public string? FilterResourceType { get; init; }
    public bool IsEditable => Card.Status != RateCardStatus.Retired;
}

public class RateCardImportModel
{
    [Required]
    public IFormFile? File { get; set; }
    [Display(Name = "Replace existing entries")]
    public bool Replace { get; set; }
}

public class SizingConversionEditModel
{
    public int Id { get; set; }
    [Required]
    public SizingMethod Method { get; set; } = SizingMethod.TShirt;
    [Required, StringLength(50)]
    public string Key { get; set; } = string.Empty;
    [Required, Range(0.5, 100000)]
    public decimal Hours { get; set; }
}

public class AllocationTemplateEditModel
{
    public int Id { get; set; }
    [Required]
    public SizingMethod Method { get; set; } = SizingMethod.TShirt;
    [Required, StringLength(50), Display(Name = "Size key")]
    public string SizeKey { get; set; } = string.Empty;
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    public List<AllocationTemplateLineEditModel> Lines { get; set; } = [];
}

public class AllocationTemplateLineEditModel
{
    [Required, StringLength(200), Display(Name = "Phase")]
    public string PhaseName { get; set; } = string.Empty;
    [Required, Display(Name = "Resource type")]
    public int ResourceTypeId { get; set; }
    [Required, Display(Name = "Seniority")]
    public int SeniorityId { get; set; }
    [Required, Range(0.01, 100)]
    public decimal Percent { get; set; }
}

public class SizingIndexModel
{
    public required IReadOnlyList<SizingConversion> Conversions { get; init; }
    public required IReadOnlyList<AllocationTemplate> Templates { get; init; }
}

public class PersonEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200), Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;
    [StringLength(1000), Display(Name = "External IDs")]
    public string? ExternalIds { get; set; }
    [Required, Display(Name = "Resource type")]
    public int ResourceTypeId { get; set; }
    [Required, Display(Name = "Business unit")]
    public int BusinessUnitId { get; set; }
    [Required, Display(Name = "Seniority")]
    public int SeniorityId { get; set; }
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Display(Name = "Vendor")]
    public int? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PeopleImportModel
{
    [Required]
    public IFormFile? File { get; set; }
}

public class PersonListItem
{
    public required Person Person { get; init; }
    public int EntryCount { get; init; }
}

public class WorkCalendarViewModel
{
    [Display(Name = "Hours per working day")]
    [Range(0.5, 24)]
    public decimal HoursPerDay { get; set; } = 8;
    public IReadOnlyList<Holiday> Holidays { get; set; } = [];
}

public class HolidayEditModel
{
    public int Id { get; set; }
    [Required]
    public DateOnly? Date { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
}

public class CostCatalogItemEditModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Select a category.")]
    public CostCategory? Category { get; set; } = CostCategory.SoftwareLicense;
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [StringLength(200)]
    public string? Vendor { get; set; }
    [Required(ErrorMessage = "Select a billing model."), Display(Name = "Billing model")]
    public BillingModel? BillingModel { get; set; } = Domain.Enums.BillingModel.Monthly;
    [Required, Range(0, 999_999_999), Display(Name = "Unit cost")]
    public decimal? UnitCost { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CostCatalogItemListItem
{
    public required CostCatalogItem Item { get; init; }
    public int ReferenceCount { get; init; }
}
