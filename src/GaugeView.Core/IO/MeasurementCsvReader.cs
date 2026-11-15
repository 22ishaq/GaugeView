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
        var measurements = new List<Measurement>();
        foreach (var (line, fields) in Csv.Read(reader, "serial", "feature", "value"))
        {
            var (serial, feature, text) = (fields[0], fields[1], fields[2]);
            if (serial.Length == 0 || feature.Length == 0)
                throw new InvalidDataException($"Line {line}: serial and feature must not be empty.");

            measurements.Add(new Measurement(serial, feature, ParseNumber(text, line)));
        }

        return measurements;
    }

    internal static decimal ParseNumber(string text, int line) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Line {line}: '{text}' is not a number.");
}
