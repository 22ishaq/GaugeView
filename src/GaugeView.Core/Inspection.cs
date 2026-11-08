namespace GaugeView.Core;

/// <summary>A toleranced dimension on the drawing that gets measured, such as "Bore Ø12 H7".</summary>
public sealed record Feature
{
    public Feature(string name, Tolerance tolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Tolerance = tolerance;
    }

    public string Name { get; }

    public Tolerance Tolerance { get; }
}

/// <summary>One reading from a gauge or CMM: a feature on a particular part.</summary>
public sealed record Measurement(string PartSerial, string FeatureName, decimal Value);

/// <summary>Result of checking a feature, ordered by severity so the worst result wins.</summary>
public enum InspectionStatus
{
    /// <summary>In tolerance, comfortably inside the limits.</summary>
    Pass = 0,

    /// <summary>In tolerance but close to a limit, so worth watching before parts start to fail.</summary>
    Warning = 1,

    /// <summary>No reading for this feature yet.</summary>
    Missing = 2,

    /// <summary>Out of tolerance.</summary>
    Fail = 3,
}

/// <summary>The outcome of checking one feature on one part.</summary>
public sealed record FeatureResult(Feature Feature, decimal? MeasuredValue, InspectionStatus Status)
{
    /// <summary>Measured value minus nominal, or null if not measured.</summary>
    public decimal? Deviation => MeasuredValue - Feature.Tolerance.Nominal;

    /// <summary>Share of the tolerance used (see <see cref="Tolerance.FractionUsed"/>), or null if not measured.</summary>
    public decimal? FractionUsed => MeasuredValue is { } value ? Feature.Tolerance.FractionUsed(value) : null;
}

/// <summary>All feature results for one part, with an overall verdict.</summary>
public sealed record PartInspection(string Serial, IReadOnlyList<FeatureResult> Results)
{
    /// <summary>The worst feature result: one failed feature fails the whole part.</summary>
    public InspectionStatus Verdict => Results.Count == 0 ? InspectionStatus.Missing : Results.Max(r => r.Status);
}

/// <summary>The features to check on one part number, with their tolerances.</summary>
public sealed class InspectionPlan
{
    private readonly Dictionary<string, Feature> featuresByName = new(StringComparer.OrdinalIgnoreCase);

    public InspectionPlan(string partNumber, string description, string unit, IEnumerable<Feature> features)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partNumber);
        ArgumentNullException.ThrowIfNull(features);

        PartNumber = partNumber.Trim();
        Description = description?.Trim() ?? string.Empty;
        Unit = string.IsNullOrWhiteSpace(unit) ? "mm" : unit.Trim();
        Features = features.ToList();

        if (Features.Count == 0)
            throw new ArgumentException("An inspection plan needs at least one feature.", nameof(features));

        foreach (var feature in Features)
        {
            if (!featuresByName.TryAdd(feature.Name, feature))
                throw new ArgumentException($"Feature '{feature.Name}' appears more than once.", nameof(features));
        }
    }

    public string PartNumber { get; }

    public string Description { get; }

    public string Unit { get; }

    public IReadOnlyList<Feature> Features { get; }

    /// <summary>Finds a feature by name, ignoring case and surrounding spaces.</summary>
    public Feature? FindFeature(string name) => featuresByName.GetValueOrDefault(name.Trim());
}
