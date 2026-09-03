using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InitiativeScoping.Web.Models;

public class ActualsIndexModel
{
    public required IReadOnlyList<ActualsImport> Imports { get; init; }
    public int UnmappedCount { get; init; }
}

public class ActualsImportModel
{
    [Required]
    public IFormFile? File { get; set; }
}

public class EntriesModel
{
    public required IReadOnlyList<ActualEntry> Entries { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public required SelectList Initiatives { get; init; }
    public required SelectList People { get; init; }
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public class ImportDetailsModel
{
    public required ActualsImport Import { get; init; }
    public required EntriesModel Entries { get; init; }
    public bool UnmappedOnly { get; init; }
}
