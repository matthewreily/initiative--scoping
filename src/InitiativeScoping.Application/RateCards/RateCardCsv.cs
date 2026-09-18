using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Application.RateCards;

public sealed record RateCardCsvRow(
    string ResourceType,
    string Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    decimal HourlyRate,
    string? Vendor = null,
    string? Discipline = null);

public sealed record RateCardCsvError(int Line, string Message);

public sealed record RateCardCsvResult(IReadOnlyList<RateCardCsvRow> Rows, IReadOnlyList<RateCardCsvError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// CSV format: ResourceType,Seniority,Location,ResourcingClass,HourlyRate,Vendor,Discipline
/// A legacy BusinessUnit column is accepted and ignored.
/// ResourceType and Seniority are catalog names; unknown names are added to the catalogs on import. Discipline (optional) is only used
/// when a new resource type is created; existing types keep their discipline. ResourcingClass is a catalog name (e.g. Internal|Vendor; the legacy "InternalFte" is accepted).
/// Vendor names a specific vendor for vendor-class rows (blank = generic "any vendor" rate) and must be blank for other rows; the column may be omitted.
/// </summary>
public static class RateCardCsv
{
    public static readonly string[] RequiredHeaders = ["ResourceType", "Seniority", "Location", "ResourcingClass", "HourlyRate"];
    public static readonly string[] Headers = [.. RequiredHeaders, "Vendor", "Discipline"];

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        PrepareHeaderForMatch = a => a.Header.Replace(" ", string.Empty).ToLowerInvariant()
    };

    public static RateCardCsvResult Parse(TextReader reader, IReadOnlyList<ResourcingClass> classes)
    {
        var rows = new List<RateCardCsvRow>();
        var errors = new List<RateCardCsvError>();

        using var csv = new CsvReader(reader, Config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            errors.Add(new RateCardCsvError(1, "Missing header row."));
            return new RateCardCsvResult(rows, errors);
        }

        var header = csv.HeaderRecord ?? [];
        var missing = RequiredHeaders.Where(h => !header.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        var hasVendorColumn = header.Contains("Vendor", StringComparer.OrdinalIgnoreCase);
        var hasDisciplineColumn = header.Contains("Discipline", StringComparer.OrdinalIgnoreCase);
        if (missing.Count > 0)
        {
            errors.Add(new RateCardCsvError(1, $"Missing column(s): {string.Join(", ", missing)}"));
            return new RateCardCsvResult(rows, errors);
        }

        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var resourceType = (csv.GetField("ResourceType") ?? string.Empty).Trim();
            var location = csv.GetField("Location") ?? string.Empty;
            var seniority = (csv.GetField("Seniority") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(seniority) || string.IsNullOrWhiteSpace(location))
            {
                errors.Add(new RateCardCsvError(line, "ResourceType, Seniority and Location are required."));
                continue;
            }

            if (resourceType.Length > ResourceType.MaxNameLength)
            {
                errors.Add(new RateCardCsvError(line, $"ResourceType must be at most {ResourceType.MaxNameLength} characters."));
                continue;
            }

            var discipline = hasDisciplineColumn ? csv.GetField("Discipline") : null;
            discipline = string.IsNullOrWhiteSpace(discipline) ? null : discipline.Trim();
            if (discipline is not null && discipline.Length > Discipline.MaxNameLength)
            {
                errors.Add(new RateCardCsvError(line, $"Discipline must be at most {Discipline.MaxNameLength} characters."));
                continue;
            }

            if (seniority.Length > SeniorityLevel.MaxNameLength)
            {
                errors.Add(new RateCardCsvError(line, $"Seniority must be at most {SeniorityLevel.MaxNameLength} characters."));
                continue;
            }

            if (ResolveClass(csv.GetField("ResourcingClass"), classes) is not { } resourcingClass)
            {
                errors.Add(new RateCardCsvError(line, $"Unknown ResourcingClass '{csv.GetField("ResourcingClass")}'."));
                continue;
            }

            var rateText = (csv.GetField("HourlyRate") ?? string.Empty).TrimStart('$');
            if (!decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) || rate < 0)
            {
                errors.Add(new RateCardCsvError(line, $"Invalid HourlyRate '{csv.GetField("HourlyRate")}'."));
                continue;
            }

            var vendor = hasVendorColumn ? csv.GetField("Vendor") : null;
            vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim();
            if (!resourcingClass.IsVendor && vendor is not null)
            {
                errors.Add(new RateCardCsvError(line, $"Vendor must be blank for {resourcingClass.Name} rows."));
                continue;
            }

            rows.Add(new RateCardCsvRow(resourceType, seniority, location, resourcingClass, rate, vendor, discipline));
        }

        var duplicates = rows
            .GroupBy(r => (r.ResourceType.ToLowerInvariant(), r.Seniority.ToLowerInvariant(), r.Location.ToLowerInvariant(), r.ResourcingClass.Id, r.Vendor?.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => g.First());
        foreach (var d in duplicates)
        {
            errors.Add(new RateCardCsvError(0, $"Duplicate entry for {d.ResourceType}/{d.Seniority}/{d.Location}/{d.ResourcingClass.Name}{(d.Vendor is null ? string.Empty : "/" + d.Vendor)}."));
        }

        return new RateCardCsvResult(rows, errors);
    }

    public static void Write(TextWriter writer, IEnumerable<RateCardCsvRow> rows)
    {
        using var csv = new CsvWriter(writer, Config, leaveOpen: true);
        foreach (var h in Headers)
        {
            csv.WriteField(h);
        }
        csv.NextRecord();
        foreach (var r in rows)
        {
            csv.WriteField(r.ResourceType);
            csv.WriteField(r.Seniority);
            csv.WriteField(r.Location);
            csv.WriteField(r.ResourcingClass.Name);
            csv.WriteField(r.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture));
            csv.WriteField(r.Vendor ?? string.Empty);
            csv.WriteField(r.Discipline ?? string.Empty);
            csv.NextRecord();
        }
        csv.Flush();
    }

    /// <summary>Resolves a class by catalog name; the pre-catalog names "InternalFte"/"FTE" and "Contractor" still map to the seeded Internal and Vendor classes.</summary>
    internal static ResourcingClass? ResolveClass(string? value, IReadOnlyList<ResourcingClass> classes)
    {
        var text = value?.Trim() ?? string.Empty;
        var byName = classes.FirstOrDefault(c => ResourcingClass.NameMatches(c.Name, text));
        if (byName is not null)
        {
            return byName;
        }

        return text.Replace(" ", string.Empty).Replace("/", string.Empty).ToLowerInvariant() switch
        {
            "internalfte" or "internal" or "fte" => classes.FirstOrDefault(c => c.Id == ResourcingClass.InternalId),
            "vendor" or "contractor" or "vendorcontractor" => classes.FirstOrDefault(c => c.Id == ResourcingClass.VendorId),
            _ => null
        };
    }
}
