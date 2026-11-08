namespace GaugeView.Core;

/// <summary>How capable a process is of holding a feature's tolerance, judged by Cpk.</summary>
public enum CapabilityRating
{
    /// <summary>Fewer than two readings, or no variation at all, so Cp and Cpk are undefined.</summary>
    InsufficientData,

    /// <summary>Cpk below 1.00: the process will make out-of-tolerance parts.</summary>
    NotCapable,

    /// <summary>Cpk from 1.00 up to 1.33.</summary>
    Marginal,

    /// <summary>Cpk of 1.33 or more, the usual minimum for a production process.</summary>
    Capable,
}

/// <summary>Process capability for one feature across many parts.</summary>
public sealed record CapabilityResult(
    Feature Feature, int SampleCount, double Mean, double StandardDeviation, double? Cp, double? Cpk)
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
/// Cp compares the tolerance band with the process spread (6σ). Cpk also accounts for how far the mean
/// has drifted towards the nearer limit, so Cpk is never above Cp. σ is the sample standard deviation (n − 1).
/// </summary>
public static class CapabilityCalculator
{
    public static CapabilityResult Calculate(Feature feature, IEnumerable<decimal> readings)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(readings);

        var values = readings.ToArray();
        if (values.Length == 0)
            return new CapabilityResult(feature, 0, double.NaN, double.NaN, null, null);

        // Mean and variance in decimal, so identical readings give exactly zero spread.
        var mean = values.Average();
        if (values.Length == 1)
            return new CapabilityResult(feature, 1, (double)mean, 0, null, null);

        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        if (variance == 0)
            return new CapabilityResult(feature, values.Length, (double)mean, 0, null, null);

        var sigma = Math.Sqrt((double)variance);
        var tolerance = feature.Tolerance;
        var cp = (double)tolerance.Band / (6 * sigma);
        var cpk = (double)Math.Min(tolerance.UpperLimit - mean, mean - tolerance.LowerLimit) / (3 * sigma);
        return new CapabilityResult(feature, values.Length, (double)mean, sigma, cp, cpk);
    }

    /// <summary>Capability of every feature in the plan, using only the parts where it was measured.</summary>
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
