using System.Text;

namespace GaugeView.Core.IO;

/// <summary>
/// Reads a gauge R&amp;R study from CSV with <c>part</c>, <c>operator</c> and <c>value</c> columns in any order.
/// Other columns, such as <c>trial</c>, are ignored: repeat readings of a part by the same operator are the trials.
/// </summary>
public static class GaugeStudyCsvReader
{
    public static IReadOnlyList<GrrReading> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader);
    }

    /// <exception cref="InvalidDataException">The header or a row is malformed; the message gives the line number.</exception>
    public static IReadOnlyList<GrrReading> Read(TextReader reader)
    {
        var readings = new List<GrrReading>();
        foreach (var (line, fields) in Csv.Read(reader, "part", "operator", "value"))
        {
            var (part, appraiser, text) = (fields[0], fields[1], fields[2]);
            if (part.Length == 0 || appraiser.Length == 0)
                throw new InvalidDataException($"Line {line}: part and operator must not be empty.");

            readings.Add(new GrrReading(part, appraiser, MeasurementCsvReader.ParseNumber(text, line)));
        }

        return readings;
    }
}
