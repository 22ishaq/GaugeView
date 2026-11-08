using System.Globalization;
using System.Text;

namespace GaugeView.Core.IO;

/// <summary>Writes inspection results as CSV, one row per feature per part, ready for Excel or a QMS.</summary>
public static class InspectionReportWriter
{
    public const string Header =
        "serial,part_verdict,feature,nominal,lower_limit,upper_limit,measured,deviation,tolerance_used_pct,status";

    public static void WriteFile(string path, IEnumerable<PartInspection> parts)
    {
        // A byte-order mark lets Excel show characters such as Ø correctly.
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Write(writer, parts);
    }

    public static void Write(TextWriter writer, IEnumerable<PartInspection> parts)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(parts);

        writer.WriteLine(Header);
        foreach (var part in parts)
        {
            foreach (var result in part.Results)
            {
                var tolerance = result.Feature.Tolerance;
                writer.WriteLine(string.Join(',',
                    Quote(part.Serial),
                    Status(part.Verdict),
                    Quote(result.Feature.Name),
                    Number(tolerance.Nominal),
                    Number(tolerance.LowerLimit),
                    Number(tolerance.UpperLimit),
                    Number(result.MeasuredValue),
                    Number(result.Deviation),
                    result.FractionUsed is { } used ? Number(Math.Round(used * 100, 1)) : string.Empty,
                    Status(result.Status)));
            }
        }
    }

    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Status(InspectionStatus status) => status.ToString().ToUpperInvariant();

    private static string Quote(string text) =>
        text.Contains(',') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
