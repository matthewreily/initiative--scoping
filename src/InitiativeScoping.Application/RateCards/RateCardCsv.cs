using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Application.RateCards;

public sealed record RateCardCsvRow(
    string ResourceType,
    string BusinessUnit,
    Seniority Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    decimal HourlyRate);

public sealed record RateCardCsvError(int Line, string Message);

public sealed record RateCardCsvResult(IReadOnlyList<RateCardCsvRow> Rows, IReadOnlyList<RateCardCsvError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// CSV format: ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate
/// Seniority: Associate|Mid|Senior|Staff|Principal. ResourcingClass: InternalFte|Vendor.
/// </summary>
public static class RateCardCsv
{
    public static readonly string[] Headers = ["ResourceType", "BusinessUnit", "Seniority", "Location", "ResourcingClass", "HourlyRate"];

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        PrepareHeaderForMatch = a => a.Header.Replace(" ", string.Empty).ToLowerInvariant()
    };

    public static RateCardCsvResult Parse(TextReader reader)
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
        var missing = Headers.Where(h => !header.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new RateCardCsvError(1, $"Missing column(s): {string.Join(", ", missing)}"));
            return new RateCardCsvResult(rows, errors);
        }

        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var resourceType = csv.GetField("ResourceType") ?? string.Empty;
            var businessUnit = csv.GetField("BusinessUnit") ?? string.Empty;
            var location = csv.GetField("Location") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(businessUnit) || string.IsNullOrWhiteSpace(location))
            {
                errors.Add(new RateCardCsvError(line, "ResourceType, BusinessUnit and Location are required."));
                continue;
            }

            if (!Enum.TryParse<Seniority>(csv.GetField("Seniority"), true, out var seniority) || !Enum.IsDefined(seniority))
            {
                errors.Add(new RateCardCsvError(line, $"Unknown Seniority '{csv.GetField("Seniority")}'."));
                continue;
            }

            if (!TryParseClass(csv.GetField("ResourcingClass"), out var resourcingClass))
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

            rows.Add(new RateCardCsvRow(resourceType, businessUnit, seniority, location, resourcingClass, rate));
        }

        var duplicates = rows
            .GroupBy(r => (r.ResourceType.ToLowerInvariant(), r.BusinessUnit.ToLowerInvariant(), r.Seniority, r.Location.ToLowerInvariant(), r.ResourcingClass))
            .Where(g => g.Count() > 1)
            .Select(g => g.First());
        foreach (var d in duplicates)
        {
            errors.Add(new RateCardCsvError(0, $"Duplicate entry for {d.ResourceType}/{d.BusinessUnit}/{d.Seniority}/{d.Location}/{d.ResourcingClass}."));
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
            csv.WriteField(r.BusinessUnit);
            csv.WriteField(r.Seniority.ToString());
            csv.WriteField(r.Location);
            csv.WriteField(r.ResourcingClass.ToString());
            csv.WriteField(r.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture));
            csv.NextRecord();
        }
        csv.Flush();
    }

    private static bool TryParseClass(string? value, out ResourcingClass result)
    {
        switch (value?.Trim().Replace(" ", string.Empty).Replace("/", string.Empty).ToLowerInvariant())
        {
            case "internalfte":
            case "internal":
            case "fte":
                result = ResourcingClass.InternalFte;
                return true;
            case "vendor":
            case "contractor":
            case "vendorcontractor":
                result = ResourcingClass.Vendor;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
