using GaugeView.Core;

namespace GaugeView.Core.Tests;

public class ControlChartTests
{
    private static readonly Feature Thickness = new("Thickness", Tolerance.Symmetric(10.00m, 0.10m));
    private static readonly InspectionPlan Plan = new("T-1", "Test part", "mm", [Thickness]);

    [Fact]
    public void Sigma_comes_from_the_average_moving_range()
    {
        var chart = Chart(10.00m, 10.02m, 10.01m, 10.03m);

        // MR̄ = (0.02 + 0.01 + 0.02) / 3 = 0.016667; σ = MR̄ / 1.128
        Assert.Equal(10.015, chart.Mean!.Value, 6);
        Assert.Equal(0.016667, chart.AverageMovingRange!.Value, 6);
        Assert.Equal(0.014775, chart.Sigma!.Value, 6);
        Assert.Equal(10.059326, chart.UpperControlLimit!.Value, 6);
        Assert.Equal(9.970674, chart.LowerControlLimit!.Value, 6);
        Assert.Equal(ChartState.InControl, chart.State);
    }

    [Fact]
    public void Rule_1_flags_a_reading_beyond_three_sigma()
    {
        var chart = Chart(10.00m, 10.01m, 10.00m, 10.01m, 10.00m, 10.01m, 10.00m, 10.10m);

        var signal = Assert.Single(chart.Signals);
        Assert.Equal(SpcRule.BeyondControlLimits, signal.Rule);
        Assert.Equal(7, signal.PartIndex);
        Assert.Equal("P8", signal.Serial);
        Assert.Equal(ChartState.OutOfControl, chart.State);
    }

    [Fact]
    public void Rule_2_flags_the_ninth_reading_in_a_row_on_one_side_of_the_mean()
    {
        // Nine readings above the mean, then nine below: the process has shifted.
        var chart = Chart(
            10.02m, 10.01m, 10.02m, 10.01m, 10.02m, 10.01m, 10.02m, 10.01m, 10.02m,
            9.97m, 9.98m, 9.97m, 9.98m, 9.97m, 9.98m, 9.97m, 9.98m, 9.97m);

        Assert.Equal(new[] { 8, 17 }, Indexes(chart, SpcRule.RunOnOneSide));
        Assert.Empty(Indexes(chart, SpcRule.BeyondControlLimits));
    }

    [Fact]
    public void Rule_3_flags_the_sixth_reading_in_a_steady_rise()
    {
        var chart = Chart(10.00m, 10.01m, 10.00m, 10.01m, 10.02m, 10.03m, 10.04m, 10.05m);

        Assert.Equal(new[] { 7 }, Indexes(chart, SpcRule.Trend));
        Assert.Equal(new[] { SpcRule.BeyondControlLimits, SpcRule.Trend }, chart.Points[7].Rules);
    }

    [Fact]
    public void A_repeated_reading_breaks_a_trend()
    {
        var chart = Chart(10.00m, 10.01m, 10.02m, 10.02m, 10.03m, 10.04m, 10.05m);

        Assert.Empty(Indexes(chart, SpcRule.Trend));
    }

    [Fact]
    public void Parts_without_a_reading_leave_a_gap()
    {
        var chart = Chart(10.00m, null, 10.02m, 10.01m);

        Assert.Equal(4, chart.PartCount);
        Assert.Equal(new[] { 0, 2, 3 }, chart.Points.Select(p => p.PartIndex));
        Assert.Equal(0.015 / Statistics.D2, chart.Sigma!.Value, 9); // MR̄ over the readings either side of the gap
    }

    [Fact]
    public void Points_carry_the_inspection_status()
    {
        var chart = Chart(10.00m, 10.09m, 10.11m);

        Assert.Equal(
            new[] { InspectionStatus.Pass, InspectionStatus.Warning, InspectionStatus.Fail },
            chart.Points.Select(p => p.Status));
    }

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new double[] { 10.01 })]
    [InlineData(new double[] { 10.01, 10.01, 10.01 })]
    public void Too_little_data_or_no_variation_has_no_control_limits(double[] readings)
    {
        var chart = Chart(readings.Select(r => (decimal?)(decimal)r).ToArray());

        Assert.False(chart.HasControlLimits);
        Assert.Null(chart.UpperControlLimit);
        Assert.Empty(chart.Signals);
        Assert.Equal(ChartState.NotEnoughData, chart.State);
    }

    [Fact]
    public void Charts_every_feature_in_the_plan()
    {
        var second = new Feature("Width", Tolerance.Symmetric(5m, 0.1m));
        var plan = new InspectionPlan("T-2", "Two features", "mm", [Thickness, second]);
        var parts = new Inspector(plan, new ToleranceChecker()).InspectAll(
            [new Measurement("A", "Thickness", 10m), new Measurement("A", "Width", 5m)]);

        Assert.Equal(new[] { "Thickness", "Width" }, ControlChartCalculator.Calculate(plan, parts).Select(c => c.Feature.Name));
    }

    private static ControlChart Chart(params decimal?[] readings)
    {
        var inspector = new Inspector(Plan, new ToleranceChecker());
        var parts = readings
            .Select((value, i) => inspector.Inspect(
                $"P{i + 1}",
                value is { } v ? new Dictionary<Feature, decimal> { [Thickness] = v } : new Dictionary<Feature, decimal>()))
            .ToList();
        return ControlChartCalculator.Calculate(Thickness, parts);
    }

    private static int[] Indexes(ControlChart chart, SpcRule rule) =>
        chart.Signals.Where(s => s.Rule == rule).Select(s => s.PartIndex).ToArray();
}
