using System.Globalization;
using System.Text;

namespace GaugeView.Core.IO;

/// <summary>
/// Reads gauge or CMM readings from CSV with <c>serial</c>, <c>feature</c> and <c>value</c> columns in any
/// order. Numbers always use a full stop as the decimal separator, whatever the PC's regional settings,
/// so a file exported in Glasgow reads the same in Stuttgart.
/// </summary>
public static class MeasurementCsvReader
{
    public static IReadOnlyList<Measurement> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader);
    }

    /// <exception cref="InvalidDataException">The header or a row is malformed; the message gives the line number.</exception>
    public static IReadOnlyList<Measurement> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var measurements = new List<Measurement>();
        int serialColumn = -1, featureColumn = -1, valueColumn = -1, columnsNeeded = 0;
        var haveHeader = false;
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var fields = SplitCsvLine(line, lineNumber);
            if (!haveHeader)
            {
                var names = fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
                serialColumn = names.IndexOf("serial");
                featureColumn = names.IndexOf("feature");
                valueColumn = names.IndexOf("value");
                if (serialColumn < 0 || featureColumn < 0 || valueColumn < 0)
                    throw new InvalidDataException($"Line {lineNumber}: expected a header with serial, feature and value columns.");

                columnsNeeded = Math.Max(serialColumn, Math.Max(featureColumn, valueColumn)) + 1;
                haveHeader = true;
                continue;
            }

            if (fields.Count < columnsNeeded)
                throw new InvalidDataException($"Line {lineNumber}: expected {columnsNeeded} columns but found {fields.Count}.");

            var serial = fields[serialColumn].Trim();
            var feature = fields[featureColumn].Trim();
            var text = fields[valueColumn].Trim();
            if (serial.Length == 0 || feature.Length == 0)
                throw new InvalidDataException($"Line {lineNumber}: serial and feature must not be empty.");
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"Line {lineNumber}: '{text}' is not a number.");

            measurements.Add(new Measurement(serial, feature, value));
        }

        if (!haveHeader)
            throw new InvalidDataException("The file is empty: expected a header with serial, feature and value columns.");
        return measurements;
    }

    // RFC 4180 fields: commas inside double quotes are kept, and "" inside quotes is a literal quote.
    private static List<string> SplitCsvLine(string line, int lineNumber)
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
}
