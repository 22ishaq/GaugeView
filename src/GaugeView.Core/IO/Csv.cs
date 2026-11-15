using System.Text;

namespace GaugeView.Core.IO;

/// <summary>
/// Minimal RFC 4180 CSV reading shared by the file readers: a header row names the columns, which may come in
/// any order and in any case; blank lines and lines starting with <c>#</c> are skipped.
/// </summary>
internal static class Csv
{
    /// <summary>
    /// Yields each data row's line number and the trimmed values of <paramref name="columns"/>, in the order asked for.
    /// </summary>
    /// <exception cref="InvalidDataException">The header is missing a column, or a row is short or badly quoted.</exception>
    public static IEnumerable<(int Line, string[] Fields)> Read(TextReader reader, params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int[]? indexes = null;
        var columnsNeeded = 0;
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var fields = SplitLine(line, lineNumber);
            if (indexes is null)
            {
                var names = fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
                indexes = columns.Select(c => names.IndexOf(c)).ToArray();
                if (indexes.Any(i => i < 0))
                    throw new InvalidDataException($"Line {lineNumber}: expected a header with {Describe(columns)} columns.");

                columnsNeeded = indexes.Max() + 1;
                continue;
            }

            if (fields.Count < columnsNeeded)
                throw new InvalidDataException($"Line {lineNumber}: expected {columnsNeeded} columns but found {fields.Count}.");

            yield return (lineNumber, indexes.Select(i => fields[i].Trim()).ToArray());
        }

        if (indexes is null)
            throw new InvalidDataException($"The file is empty: expected a header with {Describe(columns)} columns.");
    }

    // RFC 4180 fields: commas inside double quotes are kept, and "" inside quotes is a literal quote.
    private static List<string> SplitLine(string line, int lineNumber)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (inQuotes)
            throw new InvalidDataException($"Line {lineNumber}: a quoted field is not closed.");

        fields.Add(field.ToString());
        return fields;
    }

    private static string Describe(string[] columns) =>
        columns.Length == 1 ? columns[0] : $"{string.Join(", ", columns[..^1])} and {columns[^1]}";
}
