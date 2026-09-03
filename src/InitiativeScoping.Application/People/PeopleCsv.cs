using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Application.People;

public sealed record PeopleCsvRow(
    string DisplayName,
    IReadOnlyList<string> ExternalIds,
    string ResourceType,
    string BusinessUnit,
    Seniority Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    bool IsActive);

public sealed record PeopleCsvError(int Line, string Message);

public sealed record PeopleCsvResult(IReadOnlyList<PeopleCsvRow> Rows, IReadOnlyList<PeopleCsvError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// CSV format: DisplayName,ExternalIds,ResourceType,BusinessUnit,Seniority,Location,ResourcingClass[,IsActive]
/// ExternalIds is ';'-separated (may be empty). Seniority: Associate|Mid|Senior|Staff|Principal.
/// ResourcingClass: InternalFte|Vendor. IsActive defaults to true.
/// </summary>
public static class PeopleCsv
{
    public static readonly string[] Headers = ["DisplayName", "ExternalIds", "ResourceType", "BusinessUnit", "Seniority", "Location", "ResourcingClass", "IsActive"];
    private static readonly string[] Required = Headers[..^1];

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        PrepareHeaderForMatch = a => a.Header.Replace(" ", string.Empty).ToLowerInvariant()
    };

    public static PeopleCsvResult Parse(TextReader reader)
    {
        var rows = new List<PeopleCsvRow>();
        var errors = new List<PeopleCsvError>();

        using var csv = new CsvReader(reader, Config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            errors.Add(new PeopleCsvError(1, "Missing header row."));
            return new PeopleCsvResult(rows, errors);
        }

        var header = csv.HeaderRecord ?? [];
        var missing = Required.Where(h => !header.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new PeopleCsvError(1, $"Missing column(s): {string.Join(", ", missing)}"));
            return new PeopleCsvResult(rows, errors);
        }

        var hasActive = header.Contains("IsActive", StringComparer.OrdinalIgnoreCase);

        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var name = csv.GetField("DisplayName") ?? string.Empty;
            var resourceType = csv.GetField("ResourceType") ?? string.Empty;
            var businessUnit = csv.GetField("BusinessUnit") ?? string.Empty;
            var location = csv.GetField("Location") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(businessUnit) || string.IsNullOrWhiteSpace(location))
            {
                errors.Add(new PeopleCsvError(line, "DisplayName, ResourceType, BusinessUnit and Location are required."));
                continue;
            }

            if (!Enum.TryParse<Seniority>(csv.GetField("Seniority"), true, out var seniority) || !Enum.IsDefined(seniority))
            {
                errors.Add(new PeopleCsvError(line, $"Unknown Seniority '{csv.GetField("Seniority")}'."));
                continue;
            }

            if (!TryParseClass(csv.GetField("ResourcingClass"), out var resourcingClass))
            {
                errors.Add(new PeopleCsvError(line, $"Unknown ResourcingClass '{csv.GetField("ResourcingClass")}'."));
                continue;
            }

            var isActive = true;
            if (hasActive)
            {
                var text = csv.GetField("IsActive");
                if (!string.IsNullOrWhiteSpace(text) && !TryParseBool(text, out isActive))
                {
                    errors.Add(new PeopleCsvError(line, $"Invalid IsActive '{text}' (use true/false)."));
                    continue;
                }
            }

            var ids = (csv.GetField("ExternalIds") ?? string.Empty)
                .Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            rows.Add(new PeopleCsvRow(name, ids, resourceType, businessUnit, seniority, location, resourcingClass, isActive));
        }

        foreach (var dup in rows.GroupBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add(new PeopleCsvError(0, $"Duplicate DisplayName '{dup.Key}'."));
        }

        foreach (var dup in rows.SelectMany(r => r.ExternalIds.Select(id => (id, r.DisplayName)))
                     .GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            errors.Add(new PeopleCsvError(0, $"External ID '{dup.Key}' appears on more than one person."));
        }

        return new PeopleCsvResult(rows, errors);
    }

    public static void Write(TextWriter writer, IEnumerable<PeopleCsvRow> rows)
    {
        using var csv = new CsvWriter(writer, Config, leaveOpen: true);
        foreach (var h in Headers)
        {
            csv.WriteField(h);
        }
        csv.NextRecord();
        foreach (var r in rows)
        {
            csv.WriteField(r.DisplayName);
            csv.WriteField(string.Join(";", r.ExternalIds));
            csv.WriteField(r.ResourceType);
            csv.WriteField(r.BusinessUnit);
            csv.WriteField(r.Seniority.ToString());
            csv.WriteField(r.Location);
            csv.WriteField(r.ResourcingClass.ToString());
            csv.WriteField(r.IsActive ? "true" : "false");
            csv.NextRecord();
        }
        csv.Flush();
    }

    private static bool TryParseBool(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1" or "active":
                result = true;
                return true;
            case "false" or "no" or "n" or "0" or "inactive":
                result = false;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryParseClass(string? value, out ResourcingClass result)
    {
        switch (value?.Trim().Replace(" ", string.Empty).Replace("/", string.Empty).ToLowerInvariant())
        {
            case "internalfte" or "internal" or "fte":
                result = ResourcingClass.InternalFte;
                return true;
            case "vendor" or "contractor" or "vendorcontractor":
                result = ResourcingClass.Vendor;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
