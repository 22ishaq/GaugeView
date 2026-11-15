namespace GaugeView.Core;

/// <summary>One reading in a gauge study: an operator measuring a part (once per trial).</summary>
public sealed record GrrReading(string Part, string Operator, decimal Value);

/// <summary>Whether a gauge is fit for checking a tolerance, from %GRR of tolerance.</summary>
public enum GrrVerdict
{
    /// <summary>Under 10% of the tolerance: the gauge is acceptable.</summary>
    Acceptable,

    /// <summary>10% to 30%: may be acceptable, depending on the feature and the cost of improving the gauge.</summary>
    Marginal,

    /// <summary>Over 30%: the gauge is not fit to judge this tolerance.</summary>
    Unacceptable,
}

/// <summary>A row of the ANOVA table. Mean square, F and p are null where they don't apply.</summary>
public sealed record AnovaRow(string Source, int DegreesOfFreedom, double SumOfSquares, double? MeanSquare, double? F, double? P);

/// <summary>A source of variation and its share of the study variation and the tolerance.</summary>
public sealed record GrrComponent(
    string Source, int Indent, double Variance, double PercentContribution, double PercentStudyVariation, double PercentOfTolerance)
{
    public double StandardDeviation => Math.Sqrt(Variance);

    /// <summary>The spread that covers 99.73% of readings: 6σ.</summary>
    public double StudyVariation => 6 * StandardDeviation;

    /// <summary>The source name, indented to show what it is part of.</summary>
    public string DisplaySource => new string(' ', Indent * 4) + Source;
}

/// <summary>How one operator's readings compare with everyone's.</summary>
public sealed record OperatorSummary(string Name, double Mean, double AverageRange, double Bias);

/// <summary>The outcome of a crossed gauge R&amp;R study analysed by ANOVA.</summary>
public sealed class GaugeRrResult
{
    internal GaugeRrResult(
        int partCount,
        int operatorCount,
        int trialCount,
        decimal tolerance,
        IReadOnlyList<AnovaRow> anova,
        double? interactionP,
        bool interactionRemoved,
        double repeatability,
        double operatorVariance,
        double interaction,
        double partVariance,
        IReadOnlyList<OperatorSummary> operators)
    {
        PartCount = partCount;
        OperatorCount = operatorCount;
        TrialCount = trialCount;
        Tolerance = tolerance;
        Anova = anova;
        InteractionP = interactionP;
        InteractionRemoved = interactionRemoved;
        RepeatabilityVariance = repeatability;
        OperatorVariance = operatorVariance;
        InteractionVariance = interaction;
        PartVariance = partVariance;
        Operators = operators;

        Components =
        [
            Component("Total gauge R&R", 0, GaugeRrVariance),
            Component("Repeatability (equipment)", 1, RepeatabilityVariance),
            Component("Reproducibility (operators)", 1, ReproducibilityVariance),
            Component("Operator", 2, OperatorVariance),
            Component("Operator × part", 2, InteractionVariance),
            Component("Part-to-part", 0, PartVariance),
            Component("Total variation", 0, TotalVariance),
        ];
    }

    public int PartCount { get; }

    public int OperatorCount { get; }

    public int TrialCount { get; }

    public int ReadingCount => PartCount * OperatorCount * TrialCount;

    /// <summary>The tolerance band (upper limit minus lower limit) the gauge has to judge.</summary>
    public decimal Tolerance { get; }

    /// <summary>The ANOVA table for the model used: with the interaction term, or without it if it was removed.</summary>
    public IReadOnlyList<AnovaRow> Anova { get; }

    /// <summary>p-value of the operator × part interaction in the full model, or null if it couldn't be tested.</summary>
    public double? InteractionP { get; }

    /// <summary>True when the interaction was not significant and was pooled into repeatability.</summary>
    public bool InteractionRemoved { get; }

    public double RepeatabilityVariance { get; }

    public double OperatorVariance { get; }

    public double InteractionVariance { get; }

    public double PartVariance { get; }

    public double ReproducibilityVariance => OperatorVariance + InteractionVariance;

    public double GaugeRrVariance => RepeatabilityVariance + ReproducibilityVariance;

    public double TotalVariance => GaugeRrVariance + PartVariance;

    /// <summary>Gauge R&amp;R study variation (6σ) as a percentage of the tolerance: the headline figure.</summary>
    public double PercentOfTolerance => PercentTolerance(GaugeRrVariance);

    /// <summary>Gauge R&amp;R σ as a percentage of the total σ.</summary>
    public double PercentStudyVariation => TotalVariance > 0 ? 100 * Math.Sqrt(GaugeRrVariance / TotalVariance) : 0;

    /// <summary>
    /// Number of distinct categories the gauge can tell parts apart into: 1.41 × σ(part) / σ(gauge), rounded
    /// down. Five or more is the usual minimum. Null for a gauge with no measurable error at all.
    /// </summary>
    public int? DistinctCategories => GaugeRrVariance > 0
        ? Math.Max(1, (int)Math.Floor(1.41 * Math.Sqrt(PartVariance / GaugeRrVariance)))
        : null;

    public GrrVerdict Verdict => PercentOfTolerance switch
    {
        < GaugeRrCalculator.AcceptablePercent => GrrVerdict.Acceptable,
        <= GaugeRrCalculator.MarginalPercent => GrrVerdict.Marginal,
        _ => GrrVerdict.Unacceptable,
    };

    /// <summary>Variance components with their shares, in the usual report order.</summary>
    public IReadOnlyList<GrrComponent> Components { get; }

    public IReadOnlyList<OperatorSummary> Operators { get; }

    private GrrComponent Component(string source, int indent, double variance) => new(
        source,
        indent,
        variance,
        TotalVariance > 0 ? 100 * variance / TotalVariance : 0,
        TotalVariance > 0 ? 100 * Math.Sqrt(variance / TotalVariance) : 0,
        PercentTolerance(variance));

    private double PercentTolerance(double variance) => 100 * 6 * Math.Sqrt(variance) / (double)Tolerance;
}

/// <summary>
/// Crossed gauge R&amp;R by two-way ANOVA (parts × operators, with repeat trials), as in the AIAG MSA manual.
/// Every operator measures every part the same number of times. The operator × part interaction is tested
/// first; if it is not significant it is pooled into repeatability. Negative variance estimates are set to zero.
/// </summary>
public static class GaugeRrCalculator
{
    /// <summary>The interaction term is removed when its p-value is above this.</summary>
    public const double InteractionAlpha = 0.05;

    public const double AcceptablePercent = 10;

    public const double MarginalPercent = 30;

    /// <exception cref="InvalidDataException">The study is not balanced, or is too small to analyse.</exception>
    public static GaugeRrResult Calculate(IEnumerable<GrrReading> readings, decimal tolerance)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (tolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be greater than zero.");

        var list = readings.ToList();
        var parts = list.Select(r => r.Part.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var operators = list.Select(r => r.Operator.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (parts.Count < 2)
            throw new InvalidDataException("A gauge study needs at least two parts.");
        if (operators.Count < 2)
            throw new InvalidDataException("A gauge study needs at least two operators.");

        var partIndex = Index(parts);
        var operatorIndex = Index(operators);
        var cells = new List<decimal>[parts.Count, operators.Count];
        for (var p = 0; p < parts.Count; p++)
        {
            for (var o = 0; o < operators.Count; o++)
                cells[p, o] = [];
        }

        foreach (var reading in list)
            cells[partIndex[reading.Part.Trim()], operatorIndex[reading.Operator.Trim()]].Add(reading.Value);

        var trials = cells[0, 0].Count;
        for (var p = 0; p < parts.Count; p++)
        {
            for (var o = 0; o < operators.Count; o++)
            {
                if (cells[p, o].Count != trials)
                {
                    throw new InvalidDataException(
                        $"Every operator must measure every part the same number of times: {operators[0]} measured part " +
                        $"{parts[0]} {Times(trials)}, but {operators[o]} measured part {parts[p]} {Times(cells[p, o].Count)}.");
                }
            }
        }

        if (trials < 2)
            throw new InvalidDataException("A gauge study needs at least two trials, so repeatability can be measured.");

        return Analyse(parts, operators, cells, trials, tolerance);
    }

    private static GaugeRrResult Analyse(
        List<string> parts, List<string> operators, List<decimal>[,] cells, int trials, decimal tolerance)
    {
        int p = parts.Count, o = operators.Count, r = trials;

        // Means and sums of squares in decimal, so a perfectly repeatable gauge gives exactly zero.
        var cellMeans = new decimal[p, o];
        var partMeans = new decimal[p];
        var operatorMeans = new decimal[o];
        decimal grandTotal = 0;
        for (var i = 0; i < p; i++)
        {
            for (var j = 0; j < o; j++)
            {
                var sum = cells[i, j].Sum();
                cellMeans[i, j] = sum / r;
                partMeans[i] += sum;
                operatorMeans[j] += sum;
                grandTotal += sum;
            }
        }

        for (var i = 0; i < p; i++)
            partMeans[i] /= o * r;
        for (var j = 0; j < o; j++)
            operatorMeans[j] /= p * r;
        var grand = grandTotal / (p * o * r);

        decimal ssPart = 0, ssOperator = 0, ssInteraction = 0, ssRepeat = 0, ssTotal = 0;
        for (var i = 0; i < p; i++)
            ssPart += Square(partMeans[i] - grand);
        ssPart *= o * r;
        for (var j = 0; j < o; j++)
            ssOperator += Square(operatorMeans[j] - grand);
        ssOperator *= p * r;

        for (var i = 0; i < p; i++)
        {
            for (var j = 0; j < o; j++)
            {
                ssInteraction += Square(cellMeans[i, j] - partMeans[i] - operatorMeans[j] + grand);
                foreach (var value in cells[i, j])
                {
                    ssRepeat += Square(value - cellMeans[i, j]);
                    ssTotal += Square(value - grand);
                }
            }
        }

        ssInteraction *= r;

        int dfPart = p - 1, dfOperator = o - 1, dfInteraction = (p - 1) * (o - 1), dfRepeat = p * o * (r - 1);
        double msPart = (double)ssPart / dfPart;
        double msOperator = (double)ssOperator / dfOperator;
        double msInteraction = (double)ssInteraction / dfInteraction;
        double msRepeat = (double)ssRepeat / dfRepeat;

        var fInteraction = Ratio(msInteraction, msRepeat);
        double? pInteraction = fInteraction is { } fi ? FDistribution.UpperTail(fi, dfInteraction, dfRepeat) : null;
        var removeInteraction = pInteraction is { } pi ? pi > InteractionAlpha : msInteraction == 0;

        List<AnovaRow> anova;
        double repeatability, operatorVariance, interaction, partVariance;
        if (removeInteraction)
        {
            // Reduced model: the interaction's variation is treated as repeatability.
            var dfPooled = dfInteraction + dfRepeat;
            var msPooled = (double)(ssInteraction + ssRepeat) / dfPooled;
            var fPart = Ratio(msPart, msPooled);
            var fOperator = Ratio(msOperator, msPooled);
            anova =
            [
                new("Part", dfPart, (double)ssPart, msPart, fPart, Tail(fPart, dfPart, dfPooled)),
                new("Operator", dfOperator, (double)ssOperator, msOperator, fOperator, Tail(fOperator, dfOperator, dfPooled)),
                new("Repeatability", dfPooled, (double)(ssInteraction + ssRepeat), msPooled, null, null),
                new("Total", p * o * r - 1, (double)ssTotal, null, null, null),
            ];

            repeatability = msPooled;
            operatorVariance = Math.Max(0, (msOperator - msPooled) / (p * r));
            interaction = 0;
            partVariance = Math.Max(0, (msPart - msPooled) / (o * r));
        }
        else
        {
            var fPart = Ratio(msPart, msInteraction);
            var fOperator = Ratio(msOperator, msInteraction);
            anova =
            [
                new("Part", dfPart, (double)ssPart, msPart, fPart, Tail(fPart, dfPart, dfInteraction)),
                new("Operator", dfOperator, (double)ssOperator, msOperator, fOperator, Tail(fOperator, dfOperator, dfInteraction)),
                new("Part × operator", dfInteraction, (double)ssInteraction, msInteraction, fInteraction, pInteraction),
                new("Repeatability", dfRepeat, (double)ssRepeat, msRepeat, null, null),
                new("Total", p * o * r - 1, (double)ssTotal, null, null, null),
            ];

            repeatability = msRepeat;
            operatorVariance = Math.Max(0, (msOperator - msInteraction) / (p * r));
            interaction = Math.Max(0, (msInteraction - msRepeat) / r);
            partVariance = Math.Max(0, (msPart - msInteraction) / (o * r));
        }

        var summaries = operators
            .Select((name, j) => new OperatorSummary(
                name,
                (double)operatorMeans[j],
                (double)Enumerable.Range(0, p).Average(i => cells[i, j].Max() - cells[i, j].Min()),
                (double)(operatorMeans[j] - grand)))
            .ToList();

        return new GaugeRrResult(
            p, o, r, tolerance, anova, pInteraction, removeInteraction,
            repeatability, operatorVariance, interaction, partVariance, summaries);
    }

    private static Dictionary<string, int> Index(List<string> names)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
            index[names[i]] = i;
        return index;
    }

    private static decimal Square(decimal value) => value * value;

    private static double? Ratio(double numerator, double denominator) => denominator > 0 ? numerator / denominator : null;

    private static double? Tail(double? f, int df1, int df2) => f is { } value ? FDistribution.UpperTail(value, df1, df2) : null;

    private static string Times(int count) => count == 1 ? "once" : $"{count} times";
}
