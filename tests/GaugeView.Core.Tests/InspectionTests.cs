using GaugeView.Core;
using static GaugeView.Core.Tests.TestValues;

namespace GaugeView.Core.Tests;

public class ToleranceCheckerTests
{
    private static readonly Feature Bore = new("Bore Ø12 H7", new Tolerance(12.000m, 0.018m, 0m));
    private readonly ToleranceChecker checker = new();

    [Theory]
    [InlineData("12.009", InspectionStatus.Pass)]
    [InlineData("12.002", InspectionStatus.Pass)]    // 77.8% of the tolerance used
    [InlineData("12.0162", InspectionStatus.Warning)] // exactly 80%
    [InlineData("12.017", InspectionStatus.Warning)]  // 88.9%
    [InlineData("12.018", InspectionStatus.Warning)]  // on the limit: still in spec
    [InlineData("12.019", InspectionStatus.Fail)]
    [InlineData("11.999", InspectionStatus.Fail)]
    public void Classifies_readings(string reading, InspectionStatus expected) =>
        Assert.Equal(expected, checker.Check(Bore, D(reading)).Status);

    [Fact]
    public void Missing_reading_is_reported_as_missing()
    {
        var result = checker.Check(Bore, null);

        Assert.Equal(InspectionStatus.Missing, result.Status);
        Assert.Null(result.Deviation);
        Assert.Null(result.FractionUsed);
    }

    [Fact]
    public void Result_reports_deviation_from_nominal() =>
        Assert.Equal(0.017m, checker.Check(Bore, 12.017m).Deviation);

    [Fact]
    public void Warning_threshold_is_configurable()
    {
        var strict = new ToleranceChecker(0.5m);

        Assert.Equal(InspectionStatus.Warning, strict.Check(Bore, 12.014m).Status); // 55.6% used
        Assert.Equal(InspectionStatus.Pass, checker.Check(Bore, 12.014m).Status);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("1.01")]
    public void Rejects_thresholds_outside_zero_to_one(string threshold) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToleranceChecker(D(threshold)));
}

public class InspectorTests
{
    private static readonly InspectionPlan Plan = new("BRK-1042", "Bracket", "mm",
    [
        new Feature("Bore", new Tolerance(12.000m, 0.018m, 0m)),
        new Feature("Length", Tolerance.Symmetric(85.00m, 0.10m)),
    ]);

    private readonly Inspector inspector = new(Plan, new ToleranceChecker());

    [Fact]
    public void Groups_readings_by_part_in_the_order_parts_first_appear()
    {
        var parts = inspector.InspectAll(
        [
            new Measurement("P2", "Bore", 12.009m),
            new Measurement("P1", "Bore", 12.010m),
            new Measurement("P2", "Length", 85.00m),
            new Measurement("P1", "Length", 85.01m),
        ]);

        Assert.Equal(new[] { "P2", "P1" }, parts.Select(p => p.Serial));
        Assert.All(parts, p => Assert.Equal(InspectionStatus.Pass, p.Verdict));
    }

    [Fact]
    public void Unmeasured_feature_is_missing_and_holds_the_part()
    {
        var part = Assert.Single(inspector.InspectAll([new Measurement("P1", "Bore", 12.009m)]));

        Assert.Equal(InspectionStatus.Missing, part.Results.Single(r => r.Feature.Name == "Length").Status);
        Assert.Equal(InspectionStatus.Missing, part.Verdict);
    }

    [Fact]
    public void One_failed_feature_fails_the_part()
    {
        var part = Assert.Single(inspector.InspectAll(
        [
            new Measurement("P1", "Bore", 12.017m),  // warning
            new Measurement("P1", "Length", 85.20m), // fail
        ]));

        Assert.Equal(InspectionStatus.Fail, part.Verdict);
    }

    [Fact]
    public void Last_reading_wins_when_a_part_is_remeasured()
    {
        var part = Assert.Single(inspector.InspectAll(
        [
            new Measurement("P1", "Bore", 12.030m),
            new Measurement("P1", "Length", 85.00m),
            new Measurement("P1", "bore ", 12.009m), // feature names match ignoring case and spaces
        ]));

        Assert.Equal(12.009m, part.Results[0].MeasuredValue);
        Assert.Equal(InspectionStatus.Pass, part.Verdict);
    }

    [Fact]
    public void Reading_for_an_unknown_feature_is_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => inspector.InspectAll([new Measurement("P1", "Width", 1m)]));
        Assert.Contains("Width", ex.Message);
    }

    [Fact]
    public void Plan_rejects_duplicate_feature_names() =>
        Assert.Throws<ArgumentException>(() => new InspectionPlan("X", "", "mm",
        [
            new Feature("Bore", Tolerance.Symmetric(1m, 0.1m)),
            new Feature("BORE", Tolerance.Symmetric(2m, 0.1m)),
        ]));
}

public class CapabilityTests
{
    private static readonly Feature Thickness = new("Thickness", Tolerance.Symmetric(10.00m, 0.10m));

    [Fact]
    public void Centred_process_has_equal_cp_and_cpk()
    {
        var result = CapabilityCalculator.Calculate(Thickness, [9.98m, 10.00m, 10.02m, 10.00m]);

        // Within σ = MR̄ / d2 = 0.02 / 1.128 = 0.0177305; Cp = 0.20 / 6σ
        Assert.Equal(10.00, result.Mean, 6);
        Assert.Equal(0.0177305, result.WithinStandardDeviation, 6);
        Assert.Equal(1.8800, result.Cp!.Value, 4);
        Assert.Equal(1.8800, result.Cpk!.Value, 4);
        Assert.Equal(CapabilityRating.Capable, result.Rating);
    }

    [Fact]
    public void Pp_and_ppk_use_the_overall_standard_deviation()
    {
        var result = CapabilityCalculator.Calculate(Thickness, [9.98m, 10.00m, 10.02m, 10.00m]);

        // Overall σ = √((0.02² + 0 + 0.02² + 0) / 3) = 0.0163299; Pp = 0.20 / 6σ
        Assert.Equal(0.0163299, result.StandardDeviation, 6);
        Assert.Equal(2.0412, result.Pp!.Value, 4);
        Assert.Equal(2.0412, result.Ppk!.Value, 4);
    }

    [Fact]
    public void Off_centre_process_has_lower_cpk()
    {
        var result = CapabilityCalculator.Calculate(Thickness, [10.02m, 10.04m, 10.06m, 10.04m]);

        Assert.Equal(1.8800, result.Cp!.Value, 4);
        Assert.Equal(1.1280, result.Cpk!.Value, 4); // (10.10 − 10.04) / 3σ
        Assert.Equal(1.2247, result.Ppk!.Value, 4);
        Assert.Equal(CapabilityRating.Marginal, result.Rating);
    }

    [Fact]
    public void Drifting_process_has_ppk_well_below_cpk()
    {
        // Every part is 0.01 bigger than the last: tiny part-to-part noise, large drift overall.
        var result = CapabilityCalculator.Calculate(
            Thickness, [10.00m, 10.01m, 10.02m, 10.03m, 10.04m, 10.05m, 10.06m, 10.07m]);

        Assert.Equal(2.4440, result.Cpk!.Value, 4);
        Assert.Equal(0.8845, result.Ppk!.Value, 4);
    }

    [Fact]
    public void Wide_spread_is_not_capable() =>
        Assert.Equal(
            CapabilityRating.NotCapable,
            CapabilityCalculator.Calculate(Thickness, [9.92m, 10.08m, 9.95m, 10.06m]).Rating);

    [Fact]
    public void Needs_at_least_two_readings()
    {
        var result = CapabilityCalculator.Calculate(Thickness, [10.01m]);

        Assert.Null(result.Cpk);
        Assert.Equal(CapabilityRating.InsufficientData, result.Rating);
    }

    [Fact]
    public void Identical_readings_have_no_defined_capability()
    {
        var result = CapabilityCalculator.Calculate(Thickness, [10.01m, 10.01m, 10.01m]);

        Assert.Equal(0, result.StandardDeviation);
        Assert.Equal(0, result.WithinStandardDeviation);
        Assert.Null(result.Cp);
        Assert.Null(result.Pp);
        Assert.Equal(CapabilityRating.InsufficientData, result.Rating);
    }
}
