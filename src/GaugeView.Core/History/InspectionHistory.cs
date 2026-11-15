namespace GaugeView.Core.History;

/// <summary>A saved inspection: who measured which batch, when, and how it came out.</summary>
public sealed record InspectionRecord(
    long Id,
    string PartNumber,
    string Operator,
    string Batch,
    DateTimeOffset InspectedAt,
    int PartCount,
    int PassCount,
    int WarningCount,
    int FailCount,
    int MissingCount)
{
    /// <summary>When the inspection was saved, in this PC's time zone.</summary>
    public DateTimeOffset LocalTime => InspectedAt.ToLocalTime();

    /// <summary>Share of parts with every feature in tolerance, as a percentage.</summary>
    public double YieldPercent => PartCount == 0 ? 0 : Math.Round(100.0 * (PassCount + WarningCount) / PartCount, 1);
}

/// <summary>A saved inspection with everything needed to reopen it exactly as it was.</summary>
public sealed record SavedInspection(
    InspectionRecord Record, InspectionPlan Plan, IReadOnlyList<PartInspection> Parts, decimal WarningThreshold);

/// <summary>One saved batch's figures for one feature, for reviewing trends across batches.</summary>
public sealed record FeatureTrendPoint(
    long InspectionId,
    string Batch,
    string Operator,
    DateTimeOffset InspectedAt,
    int SampleCount,
    double? Mean,
    decimal? Minimum,
    decimal? Maximum,
    double? Cpk,
    int FailCount)
{
    public DateTimeOffset LocalTime => InspectedAt.ToLocalTime();
}

/// <summary>
/// Keeps every inspection with a timestamp, operator and batch. Saved inspections are records of what was
/// measured, so they are never edited or deleted, only added.
/// </summary>
public interface IInspectionHistory
{
    /// <summary>Where the history is kept, to show the user.</summary>
    string Location { get; }

    /// <summary>Saves an inspection and returns its record.</summary>
    InspectionRecord Save(
        InspectionPlan plan,
        IReadOnlyList<PartInspection> parts,
        string operatorName,
        string batch,
        decimal warningThreshold,
        DateTimeOffset inspectedAt);

    /// <summary>Saved inspections, newest first; only those for <paramref name="partNumber"/> if given.</summary>
    IReadOnlyList<InspectionRecord> List(string? partNumber = null);

    /// <summary>Loads a saved inspection with its plan and readings.</summary>
    /// <exception cref="KeyNotFoundException">There is no inspection with this id.</exception>
    SavedInspection Load(long id);

    /// <summary>
    /// One point per saved inspection of the part number that measured the feature, oldest first.
    /// Part number and feature name match ignoring ASCII case.
    /// </summary>
    IReadOnlyList<FeatureTrendPoint> Trend(string partNumber, string featureName);
}
