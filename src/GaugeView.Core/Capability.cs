namespace GaugeView.Core;

/// <summary>How capable a process is of holding a feature's tolerance, judged by Cpk.</summary>
public enum CapabilityRating
{
    /// <summary>Fewer than two readings, or no variation at all, so the indices are undefined.</summary>
    InsufficientData,

    /// <summary>Cpk below 1.00: the process will make out-of-tolerance parts.</summary>
    NotCapable,

    /// <summary>Cpk from 1.00 up to 1.33.</summary>
    Marginal,

    /// <summary>Cpk of 1.33 or more, the usual minimum for a production process.</summary>
    Capable,
}

/// <summary>
/// Process capability for one feature across many parts. Cp and Cpk use the within-process σ (what the
/// process could do if it stayed put); Pp and Ppk use the overall σ (what it actually did). When Ppk is well
/// below Cpk, the process is moving between parts and a control chart will usually show why.
/// </summary>
public sealed record CapabilityResult(
    Feature Feature,
    int SampleCount,
    double Mean,
    double StandardDeviation,
    double WithinStandardDeviation,
    double? Cp,
    double? Cpk,
    double? Pp,
    double? Ppk)
{
    public const double CapableCpk = 1.33;

    public CapabilityRating Rating => Cpk switch
    {
        null => CapabilityRating.InsufficientData,
        >= CapableCpk => CapabilityRating.Capable,
        >= 1.0 => CapabilityRating.Marginal,
        _ => CapabilityRating.NotCapable,
    };
}

/// <summary>
/// Capability indices. Cp compares the tolerance band with the process spread (6σ); Cpk also accounts for
/// how far the mean sits from the centre, using the nearer limit, so Cpk is never above Cp. The same holds
/// for Pp and Ppk. Within σ is MR̄ / d2 from consecutive readings; overall σ is the sample standard deviation.
/// </summary>
public static class CapabilityCalculator
{
    /// <summary>Capability from readings in production order (the order matters for the within σ).</summary>
    public static CapabilityResult Calculate(Feature feature, IEnumerable<decimal> readings)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(readings);

        var values = readings.ToArray();
        if (values.Length == 0)
            return new CapabilityResult(feature, 0, double.NaN, double.NaN, double.NaN, null, null, null, null);

        var mean = values.Average();
        var overall = Statistics.OverallSigma(values) ?? 0;
        var within = Statistics.WithinSigma(values) ?? 0;

        // Identical readings have exactly zero spread (worked in decimal), and the indices are undefined.
        // Within σ is zero only when every consecutive pair is equal, which means every reading is equal.
        if (overall == 0 || within == 0)
            return new CapabilityResult(feature, values.Length, (double)mean, overall, within, null, null, null, null);

        var tolerance = feature.Tolerance;
        var band = (double)tolerance.Band;
        var nearerLimit = (double)Math.Min(tolerance.UpperLimit - mean, mean - tolerance.LowerLimit);

        return new CapabilityResult(
            feature,
            values.Length,
            (double)mean,
            overall,
            within,
            Cp: band / (6 * within),
            Cpk: nearerLimit / (3 * within),
            Pp: band / (6 * overall),
            Ppk: nearerLimit / (3 * overall));
    }

    /// <summary>Capability of every feature in the plan, using only the parts where it was measured, in order.</summary>
    public static IReadOnlyList<CapabilityResult> Calculate(InspectionPlan plan, IEnumerable<PartInspection> parts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parts);

        var results = parts.SelectMany(p => p.Results).ToList();
        return plan.Features
            .Select(feature => Calculate(feature, results
                .Where(r => r.Feature == feature && r.MeasuredValue.HasValue)
                .Select(r => r.MeasuredValue!.Value)))
            .ToList();
    }
}
