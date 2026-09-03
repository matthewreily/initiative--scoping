using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using InitiativeScoping.Application.Abstractions;

namespace InitiativeScoping.Application.Actuals;

public sealed record ActualsCsvError(int Line, string Message);

public sealed record ActualsCsvResult(IReadOnlyList<ExternalTimeEntry> Rows, IReadOnlyList<ActualsCsvError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// CSV format: ExternalProjectId,ExternalPersonId,WorkDate,Hours[,Cost][,Reference]
/// WorkDate is ISO (yyyy-MM-dd). Cost is optional (blank = price from roster). Reference defaults to file name + line.
/// </summary>
public static class ActualsCsv
{
    public static readonly string[] RequiredHeaders = ["ExternalProjectId", "ExternalPersonId", "WorkDate", "Hours"];
    public static readonly string[] Headers = [.. RequiredHeaders, "Cost", "Reference"];

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        PrepareHeaderForMatch = a => a.Header.Replace(" ", string.Empty).ToLowerInvariant()
    };

    public static ActualsCsvResult Parse(TextReader reader, string referencePrefix)
    {
        var rows = new List<ExternalTimeEntry>();
        var errors = new List<ActualsCsvError>();

        using var csv = new CsvReader(reader, Config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            errors.Add(new ActualsCsvError(1, "Missing header row."));
            return new ActualsCsvResult(rows, errors);
        }

        var header = csv.HeaderRecord ?? [];
        var missing = RequiredHeaders.Where(h => !header.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new ActualsCsvError(1, $"Missing column(s): {string.Join(", ", missing)}"));
            return new ActualsCsvResult(rows, errors);
        }

        var hasCost = header.Contains("Cost", StringComparer.OrdinalIgnoreCase);
        var hasReference = header.Contains("Reference", StringComparer.OrdinalIgnoreCase);

        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var projectId = csv.GetField("ExternalProjectId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(projectId))
            {
                errors.Add(new ActualsCsvError(line, "ExternalProjectId is required."));
                continue;
            }

            if (!DateOnly.TryParseExact(csv.GetField("WorkDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
            {
                errors.Add(new ActualsCsvError(line, $"Invalid WorkDate '{csv.GetField("WorkDate")}' (expected yyyy-MM-dd)."));
                continue;
            }

            if (!decimal.TryParse(csv.GetField("Hours"), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
            {
                errors.Add(new ActualsCsvError(line, $"Invalid Hours '{csv.GetField("Hours")}' (must be > 0)."));
                continue;
            }

            decimal? cost = null;
            if (hasCost)
            {
                var costText = (csv.GetField("Cost") ?? string.Empty).TrimStart('$');
                if (!string.IsNullOrWhiteSpace(costText))
                {
                    if (!decimal.TryParse(costText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    {
                        errors.Add(new ActualsCsvError(line, $"Invalid Cost '{csv.GetField("Cost")}'."));
                        continue;
                    }

                    cost = parsed;
                }
            }

            var reference = hasReference ? csv.GetField("Reference") : null;
            if (string.IsNullOrWhiteSpace(reference))
            {
                reference = $"{referencePrefix}#{line}";
            }

            var personId = csv.GetField("ExternalPersonId");
            rows.Add(new ExternalTimeEntry(reference, projectId, string.IsNullOrWhiteSpace(personId) ? null : personId, workDate, hours, cost));
        }

        var duplicates = rows.GroupBy(r => r.SourceReference, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var d in duplicates)
        {
            errors.Add(new ActualsCsvError(0, $"Duplicate Reference '{d}' in file."));
        }

        return new ActualsCsvResult(rows, errors);
    }
}
