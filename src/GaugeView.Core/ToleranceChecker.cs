namespace GaugeView.Core;

/// <summary>
/// Classifies readings as pass, warning or fail. An in-spec reading becomes a warning once it uses
/// <see cref="WarningThreshold"/> or more of its tolerance, so drift is flagged before parts go out of spec.
/// </summary>
public sealed class ToleranceChecker
{
    public const decimal DefaultWarningThreshold = 0.8m;

    public ToleranceChecker(decimal warningThreshold = DefaultWarningThreshold)
    {
        if (warningThreshold <= 0 || warningThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(warningThreshold), warningThreshold, "Warning threshold must be above 0 and at most 1.");
        }

        WarningThreshold = warningThreshold;
    }

    /// <summary>Fraction of the tolerance, above 0 and at most 1, at which an in-spec reading becomes a warning.</summary>
    public decimal WarningThreshold { get; }

    public InspectionStatus Classify(Tolerance tolerance, decimal? value)
    {
        if (value is not { } reading)
            return InspectionStatus.Missing;
        if (!tolerance.Contains(reading))
            return InspectionStatus.Fail;
        return tolerance.FractionUsed(reading) >= WarningThreshold ? InspectionStatus.Warning : InspectionStatus.Pass;
    }

    public FeatureResult Check(Feature feature, decimal? value)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return new FeatureResult(feature, value, Classify(feature.Tolerance, value));
    }
}

/// <summary>Checks every feature in an inspection plan for each part that was measured.</summary>
public sealed class Inspector
{
    public Inspector(InspectionPlan plan, ToleranceChecker checker)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(checker);
        Plan = plan;
        Checker = checker;
    }

    public InspectionPlan Plan { get; }

    public ToleranceChecker Checker { get; }

    /// <summary>
    /// Groups readings by part, keeping parts in the order they first appear. If a feature was measured
    /// more than once on a part, the last reading wins, as when a part is re-measured.
    /// </summary>
    /// <exception cref="InvalidDataException">A reading names a feature that is not in the plan.</exception>
    public IReadOnlyList<PartInspection> InspectAll(IEnumerable<Measurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        var readingsByPart = new Dictionary<string, Dictionary<Feature, decimal>>(StringComparer.OrdinalIgnoreCase);
        var partOrder = new List<string>();

        foreach (var measurement in measurements)
        {
            var feature = Plan.FindFeature(measurement.FeatureName)
                ?? throw new InvalidDataException(
                    $"Part '{measurement.PartSerial}' has a reading for '{measurement.FeatureName}', " +
                    $"which is not in plan {Plan.PartNumber}.");

            if (!readingsByPart.TryGetValue(measurement.PartSerial, out var readings))
            {
                readings = new Dictionary<Feature, decimal>();
                readingsByPart.Add(measurement.PartSerial, readings);
                partOrder.Add(measurement.PartSerial);
            }

            readings[feature] = measurement.Value;
        }

        return partOrder.Select(serial => Inspect(serial, readingsByPart[serial])).ToList();
    }

    /// <summary>Checks one part. Features with no reading are reported as <see cref="InspectionStatus.Missing"/>.</summary>
    public PartInspection Inspect(string serial, IReadOnlyDictionary<Feature, decimal> readings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(readings);

        var results = Plan.Features
            .Select(feature => Checker.Check(feature, readings.TryGetValue(feature, out var value) ? value : null))
            .ToList();
        return new PartInspection(serial, results);
    }
}
