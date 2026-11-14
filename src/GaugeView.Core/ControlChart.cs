namespace GaugeView.Core;

/// <summary>Out-of-control tests from the Nelson rules, numbered as in the rules.</summary>
public enum SpcRule
{
    /// <summary>One reading more than 3σ from the mean.</summary>
    BeyondControlLimits = 1,

    /// <summary>Nine readings in a row on the same side of the mean: the process has shifted.</summary>
    RunOnOneSide = 2,

    /// <summary>Six readings in a row all rising or all falling: the process is drifting, as with tool wear.</summary>
    Trend = 3,
}

/// <summary>Whether a feature's process is stable, judged from its control chart.</summary>
public enum ChartState
{
    /// <summary>Fewer than two readings, or no variation at all, so there are no control limits.</summary>
    NotEnoughData,

    /// <summary>No out-of-control signals.</summary>
    InControl,

    /// <summary>At least one reading broke a Nelson rule.</summary>
    OutOfControl,
}

/// <summary>One plotted reading. <see cref="PartIndex"/> is the part's position in production order.</summary>
public sealed record ChartPoint(int PartIndex, string Serial, decimal Value, InspectionStatus Status, IReadOnlyList<SpcRule> Rules)
{
    /// <summary>Position in production order, counting from 1.</summary>
    public int PartNumber => PartIndex + 1;

    public bool HasSignal => Rules.Count > 0;
}

/// <summary>A reading that broke a rule.</summary>
public sealed record SpcSignal(SpcRule Rule, int PartIndex, string Serial)
{
    /// <summary>Position in production order, counting from 1.</summary>
    public int PartNumber => PartIndex + 1;

    public string Description => Describe(Rule);

    public static string Describe(SpcRule rule) => rule switch
    {
        SpcRule.BeyondControlLimits => "Beyond the 3σ control limits",
        SpcRule.RunOnOneSide => $"{ControlChartCalculator.RunLength} in a row on one side of the mean",
        SpcRule.Trend => $"{ControlChartCalculator.TrendLength} in a row all rising or all falling",
        _ => rule.ToString(),
    };
}

/// <summary>
/// An individuals (X) control chart for one feature: readings in production order, the centre line and
/// the 3σ control limits, plus the drawing's tolerance limits for comparison.
/// </summary>
/// <remarks>
/// Control limits describe what the process actually does; tolerance limits describe what the drawing
/// allows. A process can be in control and still make scrap, or out of control while every part passes.
/// </remarks>
public sealed record ControlChart(
    Feature Feature,
    int PartCount,
    IReadOnlyList<ChartPoint> Points,
    double? Mean,
    double? AverageMovingRange,
    double? Sigma,
    IReadOnlyList<SpcSignal> Signals)
{
    public double? UpperControlLimit => HasControlLimits ? Mean + 3 * Sigma : null;

    public double? LowerControlLimit => HasControlLimits ? Mean - 3 * Sigma : null;

    public bool HasControlLimits => Mean is not null && Sigma is > 0;

    public ChartState State => !HasControlLimits
        ? ChartState.NotEnoughData
        : Signals.Count == 0 ? ChartState.InControl : ChartState.OutOfControl;
}

/// <summary>
/// Builds individuals control charts. σ is estimated from the average moving range (MR̄ / d2) rather than the
/// overall standard deviation, so a drifting process doesn't widen its own limits and hide the drift.
/// </summary>
public static class ControlChartCalculator
{
    /// <summary>Readings in a row on one side of the mean that trigger rule 2.</summary>
    public const int RunLength = 9;

    /// <summary>Readings in a row, all rising or all falling, that trigger rule 3.</summary>
    public const int TrendLength = 6;

    /// <summary>Charts every feature in the plan. Parts must be in production order.</summary>
    public static IReadOnlyList<ControlChart> Calculate(InspectionPlan plan, IReadOnlyList<PartInspection> parts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parts);
        return plan.Features.Select(feature => Calculate(feature, parts)).ToList();
    }

    /// <summary>Charts one feature. Parts without a reading for it leave a gap.</summary>
    public static ControlChart Calculate(Feature feature, IReadOnlyList<PartInspection> parts)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(parts);

        var measured = new List<(int Index, string Serial, decimal Value, InspectionStatus Status)>();
        for (var i = 0; i < parts.Count; i++)
        {
            var result = parts[i].Results.FirstOrDefault(r => r.Feature == feature);
            if (result?.MeasuredValue is { } value)
                measured.Add((i, parts[i].Serial, value, result.Status));
        }

        var values = measured.Select(m => m.Value).ToList();
        decimal? mean = values.Count > 0 ? values.Average() : null;
        var movingRange = Statistics.AverageMovingRange(values);
        var sigma = Statistics.WithinSigma(values);
        var rules = DetectRules(values, mean, sigma);

        var points = measured
            .Select((m, i) => new ChartPoint(m.Index, m.Serial, m.Value, m.Status, rules[i]))
            .ToList();
        var signals = points
            .SelectMany(p => p.Rules.Select(rule => new SpcSignal(rule, p.PartIndex, p.Serial)))
            .ToList();

        return new ControlChart(feature, parts.Count, points, (double?)mean, (double?)movingRange, sigma, signals);
    }

    // Each rule flags the reading that completes the pattern, and every later reading that extends it.
    private static List<SpcRule>[] DetectRules(IReadOnlyList<decimal> values, decimal? mean, double? sigma)
    {
        var rules = new List<SpcRule>[values.Count];
        for (var i = 0; i < rules.Length; i++)
            rules[i] = [];

        if (mean is not { } centre || sigma is not > 0)
            return rules;

        var limit = 3 * sigma.Value;
        int sameSide = 0, side = 0, rising = 0, falling = 0;

        for (var i = 0; i < values.Count; i++)
        {
            if (Math.Abs((double)(values[i] - centre)) > limit)
                rules[i].Add(SpcRule.BeyondControlLimits);

            // A reading exactly on the centre line breaks a run.
            var thisSide = Math.Sign(values[i] - centre);
            sameSide = thisSide != 0 && thisSide == side ? sameSide + 1 : thisSide != 0 ? 1 : 0;
            side = thisSide;
            if (sameSide >= RunLength)
                rules[i].Add(SpcRule.RunOnOneSide);

            // Counts readings, not steps: six readings make five rises. A repeated value breaks a trend.
            if (i == 0)
            {
                rising = falling = 1;
            }
            else
            {
                rising = values[i] > values[i - 1] ? rising + 1 : 1;
                falling = values[i] < values[i - 1] ? falling + 1 : 1;
            }

            if (rising >= TrendLength || falling >= TrendLength)
                rules[i].Add(SpcRule.Trend);
        }

        return rules;
    }
}
