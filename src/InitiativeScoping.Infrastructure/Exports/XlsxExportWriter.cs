using ClosedXML.Excel;
using InitiativeScoping.Application.Exports;

namespace InitiativeScoping.Infrastructure.Exports;

/// <summary>One worksheet per table; header row bold and frozen, numeric cells kept numeric so totals can be added in Excel.</summary>
public sealed class XlsxExportWriter : IExportWriter
{
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string Extension => ExportFormats.Xlsx;

    public byte[] Write(IReadOnlyList<ExportTable> tables)
    {
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var sheet = workbook.Worksheets.Add(SheetName(table.Name, usedNames));
            for (var c = 0; c < table.Headers.Count; c++)
            {
                sheet.Cell(1, c + 1).Value = table.Headers[c];
            }
            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    switch (row[c])
                    {
                        case null:
                            break;
                        case decimal d:
                            cell.Value = d;
                            break;
                        case int i:
                            cell.Value = i;
                            break;
                        case DateOnly d:
                            cell.Value = d.ToDateTime(TimeOnly.MinValue);
                            cell.Style.DateFormat.Format = "yyyy-mm-dd";
                            break;
                        case DateTimeOffset d:
                            cell.Value = d.UtcDateTime;
                            cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                            break;
                        case bool b:
                            cell.Value = b ? "Yes" : "No";
                            break;
                        default:
                            cell.Value = row[c]!.ToString();
                            break;
                    }
                }
            }

            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SheetName(string name, ISet<string> used)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }
        if (cleaned.Length > 31)
        {
            cleaned = cleaned[..31];
        }

        var candidate = cleaned;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = cleaned[..Math.Min(cleaned.Length, 31 - suffix.Length)] + suffix;
        }
        return candidate;
    }
}
