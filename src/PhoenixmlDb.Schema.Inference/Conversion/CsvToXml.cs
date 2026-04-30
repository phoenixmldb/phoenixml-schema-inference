using System.Text;
using PhoenixmlDb.Schema.Inference.Internal;

namespace PhoenixmlDb.Schema.Inference.Conversion;

/// <summary>
/// Converts delimited text (CSV/TSV) to XML. The first row is treated as the
/// header; each subsequent row produces one row-element with one child per
/// column. Quoted fields with embedded delimiters and escaped quotes are
/// supported. Logic preserved verbatim from ConduitXP.
/// </summary>
public static class CsvToXml
{
    /// <summary>
    /// Converts <paramref name="csv"/> to XML. The first non-empty line is
    /// treated as the header; column headers are sanitized via
    /// <see cref="XmlNameSanitizer"/>.
    /// </summary>
    public static string Convert(
        string csv,
        string rootElement = "rows",
        string rowElement = "row",
        string delimiter = ",")
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(delimiter);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return $"<{rootElement}/>";

        var headers = ParseCsvLine(lines[0], delimiter[0])
            .Select(XmlNameSanitizer.Sanitize)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"<{rootElement}>");

        for (var i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i], delimiter[0]);
            sb.Append($"  <{rowElement}>");
            for (var j = 0; j < headers.Length && j < values.Length; j++)
            {
                var v = XmlNameSanitizer.EscapeXml(values[j].Trim());
                sb.Append($"<{headers[j]}>{v}</{headers[j]}>");
            }
            sb.AppendLine($"</{rowElement}>");
        }

        sb.AppendLine($"</{rootElement}>");
        return sb.ToString();
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (c != '\r') // Skip carriage returns
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
