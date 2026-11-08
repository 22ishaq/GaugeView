using System.Globalization;

namespace GaugeView.Core;

/// <summary>
/// A size tolerance as written on a drawing: a nominal size plus signed upper and lower deviations,
/// as in ISO 286. A 12 mm H7 bore is (12.000, +0.018, 0); an 8 mm g6 spigot is (8.000, -0.005, -0.014),
/// so both of its limits sit below nominal.
/// </summary>
/// <remarks>
/// Values are <see cref="decimal"/> rather than <see cref="double"/>. In double, 0.7 + 0.1 is
/// 0.7999999999999999, which would fail a part measured exactly on a 0.8 limit. Limits are inclusive.
/// </remarks>
public readonly record struct Tolerance
{
    public Tolerance(decimal nominal, decimal upperDeviation, decimal lowerDeviation)
    {
        if (upperDeviation <= lowerDeviation)
        {
            throw new ArgumentException(
                $"Upper deviation ({upperDeviation.ToString(CultureInfo.InvariantCulture)}) must be greater than " +
                $"lower deviation ({lowerDeviation.ToString(CultureInfo.InvariantCulture)}).");
        }

        Nominal = nominal;
        UpperDeviation = upperDeviation;
        LowerDeviation = lowerDeviation;
    }

    public decimal Nominal { get; }

    public decimal UpperDeviation { get; }

    public decimal LowerDeviation { get; }

    public decimal UpperLimit => Nominal + UpperDeviation;

    public decimal LowerLimit => Nominal + LowerDeviation;

    /// <summary>Width of the tolerance zone.</summary>
    public decimal Band => UpperDeviation - LowerDeviation;

    /// <summary>Centre of the tolerance zone. Not always the nominal: a 12 H7 bore centres on 12.009.</summary>
    public decimal MidLimit => Nominal + (UpperDeviation + LowerDeviation) / 2;

    /// <summary>Deviations as written on the drawing, e.g. "+0.018/0".</summary>
    public string DeviationText => $"{FormatDeviation(UpperDeviation)}/{FormatDeviation(LowerDeviation)}";

    public static Tolerance Symmetric(decimal nominal, decimal plusMinus) => new(nominal, plusMinus, -plusMinus);

    public bool Contains(decimal value) => value >= LowerLimit && value <= UpperLimit;

    /// <summary>
    /// How much of the tolerance a value uses, measured from the centre of the zone:
    /// 0 at the centre, 1 on either limit, above 1 when out of tolerance.
    /// </summary>
    public decimal FractionUsed(decimal value) => Math.Abs(value - MidLimit) / (Band / 2);

    public override string ToString() => $"{Nominal.ToString(CultureInfo.InvariantCulture)} {DeviationText}";

    private static string FormatDeviation(decimal deviation) =>
        deviation == 0 ? "0" : deviation.ToString("+0.000###;-0.000###", CultureInfo.InvariantCulture);
}
