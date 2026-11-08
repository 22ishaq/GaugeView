using System.Text.Json;

namespace GaugeView.Core.IO;

/// <summary>
/// Loads an inspection plan from JSON. Each feature gives its nominal and signed upper and lower deviations,
/// for example <c>{ "name": "Bore Ø12 H7", "nominal": 12.000, "upper": 0.018, "lower": 0.000 }</c>.
/// </summary>
public static class InspectionPlanReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static InspectionPlan ReadFile(string path) => Read(File.ReadAllText(path));

    /// <exception cref="InvalidDataException">The JSON is malformed or a feature is incomplete.</exception>
    public static InspectionPlan Read(string json)
    {
        PlanDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PlanDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The inspection plan is not valid JSON: {ex.Message}", ex);
        }

        if (document is null || string.IsNullOrWhiteSpace(document.PartNumber))
            throw new InvalidDataException("The inspection plan needs a partNumber.");
        if (document.Features is not { Count: > 0 })
            throw new InvalidDataException("The inspection plan needs at least one feature.");

        var features = document.Features.Select((feature, index) => ToFeature(feature, index + 1)).ToList();
        try
        {
            return new InspectionPlan(document.PartNumber, document.Description ?? string.Empty, document.Unit ?? "mm", features);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static Feature ToFeature(FeatureDocument document, int position)
    {
        if (string.IsNullOrWhiteSpace(document.Name))
            throw new InvalidDataException($"Feature {position} needs a name.");
        if (document.Nominal is not { } nominal || document.Upper is not { } upper || document.Lower is not { } lower)
            throw new InvalidDataException($"Feature '{document.Name}' needs nominal, upper and lower values.");

        try
        {
            return new Feature(document.Name, new Tolerance(nominal, upper, lower));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Feature '{document.Name}': {ex.Message}", ex);
        }
    }

    private sealed record PlanDocument(string? PartNumber, string? Description, string? Unit, List<FeatureDocument>? Features);

    private sealed record FeatureDocument(string? Name, decimal? Nominal, decimal? Upper, decimal? Lower);
}
