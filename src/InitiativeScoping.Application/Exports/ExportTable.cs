using System.Globalization;
using CsvHelper;

namespace InitiativeScoping.Application.Exports;

/// <summary>Format-neutral tabular export: one table per CSV file or XLSX worksheet.</summary>
public sealed record ExportTable(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

public interface IExportWriter
{
    string ContentType { get; }
    string Extension { get; }
    byte[] Write(IReadOnlyList<ExportTable> tables);
}

public static class ExportFormats
{
    public const string Csv = "csv";
    public const string Xlsx = "xlsx";
    public static readonly string[] All = [Csv, Xlsx];

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray()).Trim('-');
        return cleaned.Length == 0 ? "export" : cleaned;
    }
}

/// <summary>Writes all tables into a single CSV, separated by a blank line and a "# name" marker when there is more than one.</summary>
public sealed class CsvExportWriter : IExportWriter
{
    public string ContentType => "text/csv";
    public string Extension => ExportFormats.Csv;

    public byte[] Write(IReadOnlyList<ExportTable> tables)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                if (tables.Count > 1)
                {
                    if (t > 0)
                    {
                        csv.NextRecord();
                    }
                    csv.WriteField($"# {table.Name}");
                    csv.NextRecord();
                }

                foreach (var header in table.Headers)
                {
                    csv.WriteField(header);
                }
                csv.NextRecord();

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row)
                    {
                        csv.WriteField(Format(cell));
                    }
                    csv.NextRecord();
                }
            }
        }

        return stream.ToArray();
    }

    private static string Format(object? cell) => cell switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        decimal d => (d / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? string.Empty
    };
}
