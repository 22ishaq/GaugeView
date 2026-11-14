namespace GaugeView.Core;

/// <summary>Shared statistics for capability and control charts.</summary>
public static class Statistics
{
    /// <summary>
    /// The d2 bias-correction constant for ranges of two consecutive readings. For individual readings,
    /// the within-process σ is estimated as the average moving range divided by d2.
    /// </summary>
    public const double D2 = 1.128;

    /// <summary>
    /// Mean of the absolute differences between consecutive readings, in production order, or null with
    /// fewer than two readings. Worked in <see cref="decimal"/> so identical readings give exactly zero.
    /// </summary>
    public static decimal? AverageMovingRange(IReadOnlyList<decimal> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (readings.Count < 2)
            return null;

        decimal total = 0;
        for (var i = 1; i < readings.Count; i++)
            total += Math.Abs(readings[i] - readings[i - 1]);
        return total / (readings.Count - 1);
    }

    /// <summary>
    /// Short-term (within-process) σ, estimated from the average moving range: MR̄ / d2. It reflects only
    /// part-to-part noise, so drift between parts does not inflate it. Null with fewer than two readings.
    /// </summary>
    public static double? WithinSigma(IReadOnlyList<decimal> readings) =>
        AverageMovingRange(readings) is { } movingRange ? (double)movingRange / D2 : null;

    /// <summary>Long-term (overall) σ: the sample standard deviation (n − 1). Null with fewer than two readings.</summary>
    public static double? OverallSigma(IReadOnlyList<decimal> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (readings.Count < 2)
            return null;

        var mean = readings.Average();
        var variance = readings.Sum(v => (v - mean) * (v - mean)) / (readings.Count - 1);
        return Math.Sqrt((double)variance);
    }
}
