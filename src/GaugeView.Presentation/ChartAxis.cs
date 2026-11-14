using System.Globalization;
using GaugeView.Core;

namespace GaugeView.Presentation;

/// <summary>
/// A value axis with "nice" tick marks (steps of 1, 2, 2.5 or 5 × 10ⁿ). Kept free of WPF so the scaling
/// the chart control relies on is unit-tested.
/// </summary>
public sealed record ChartAxis(double Minimum, double Maximum, double Step, IReadOnlyList<double> Ticks)
{
    /// <summary>Decimal places needed to show the tick values without noise.</summary>
    public int Decimals => Math.Max(0, (int)Math.Ceiling(-Math.Log10(Step) - 1e-9) + (IsQuarterStep ? 1 : 0));

    private bool IsQuarterStep
    {
        get
        {
            var mantissa = Step / Math.Pow(10, Math.Floor(Math.Log10(Step)));
            return Math.Abs(mantissa - 2.5) < 1e-9;
        }
    }

    /// <summary>Where a value sits on the axis: 0 at the minimum, 1 at the maximum.</summary>
    public double ToFraction(double value) => (value - Minimum) / (Maximum - Minimum);

    public string Format(double value) => value.ToString("F" + Decimals, CultureInfo.CurrentCulture);

    /// <summary>An axis covering every reading, both tolerance limits and both control limits, with a margin.</summary>
    public static ChartAxis For(ControlChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var tolerance = chart.Feature.Tolerance;
        var values = chart.Points.Select(p => (double)p.Value)
            .Append((double)tolerance.LowerLimit)
            .Append((double)tolerance.UpperLimit);
        if (chart.HasControlLimits)
            values = values.Append(chart.LowerControlLimit!.Value).Append(chart.UpperControlLimit!.Value);

        var list = values.ToList();
        double min = list.Min(), max = list.Max();
        var margin = (max - min) * 0.06;
        return Nice(min - margin, max + margin);
    }

    /// <summary>Rounds a range out to tick marks, aiming for about <paramref name="targetTicks"/> of them.</summary>
    public static ChartAxis Nice(double minimum, double maximum, int targetTicks = 6)
    {
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || maximum < minimum)
            throw new ArgumentException("The axis needs a minimum no greater than its maximum.");
        if (targetTicks < 2)
            throw new ArgumentOutOfRangeException(nameof(targetTicks), targetTicks, "An axis needs at least two ticks.");

        if (maximum - minimum < 1e-12)
        {
            var pad = Math.Abs(minimum) * 0.001 + 1e-6;
            minimum -= pad;
            maximum += pad;
        }

        var rough = (maximum - minimum) / (targetTicks - 1);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = (rough / magnitude) switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        } * magnitude;

        var first = Math.Floor(minimum / step + 1e-9);
        var last = Math.Ceiling(maximum / step - 1e-9);

        // Ticks are counted from an integer index so they don't accumulate floating-point error.
        var ticks = new List<double>();
        for (var i = first; i <= last; i++)
            ticks.Add(Math.Round(i * step, 10));

        return new ChartAxis(ticks[0], ticks[^1], step, ticks);
    }
}
