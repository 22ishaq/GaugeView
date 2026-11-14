using GaugeView.Core;

namespace GaugeView.Presentation.Tests;

public class SpcViewModelTests
{
    private static readonly InspectionPlan Plan = new("BRK-1042", "Bracket", "mm",
    [
        new Feature("Bore", new Tolerance(12.000m, 0.018m, 0m)),
        new Feature("Length", Tolerance.Symmetric(85.00m, 0.10m)),
    ]);

    [Fact]
    public void Loading_measurements_builds_a_chart_per_feature()
    {
        var vm = CreateLoaded();

        Assert.Equal(new[] { "Bore", "Length" }, vm.Spc.Charts.Select(c => c.Feature.Name));
        Assert.Equal("Bore", vm.Spc.SelectedChart?.Feature.Name);
        Assert.NotNull(vm.Spc.Axis);
    }

    [Fact]
    public void Editing_a_reading_rebuilds_the_chart_and_keeps_the_selected_feature()
    {
        var vm = CreateLoaded();
        vm.Spc.SelectedChart = vm.Spc.Charts[1];
        var before = vm.Spc.SelectedChart.Mean;

        vm.Parts[0].Features[1].MeasuredText = "85.05";

        Assert.Equal("Length", vm.Spc.SelectedChart?.Feature.Name);
        Assert.NotEqual(before, vm.Spc.SelectedChart?.Mean);
    }

    [Fact]
    public void Summary_describes_the_selected_chart()
    {
        var spc = new SpcViewModel();
        Assert.Equal("Load measurements to see control charts.", spc.SelectedSummary);

        var vm = CreateLoaded();
        Assert.StartsWith("In statistical control", vm.Spc.SelectedSummary);
    }

    [Fact]
    public void Sample_data_shows_the_bore_drifting_out_of_control()
    {
        var vm = new MainViewModel(new NoDialogs(), Path.Combine(AppContext.BaseDirectory, "samples"));
        vm.LoadSampleCommand.Execute(null);

        var bore = vm.Spc.Charts.Single(c => c.Feature.Name == "Bore Ø12 H7");
        Assert.Equal(ChartState.OutOfControl, bore.State);
        Assert.Contains(bore.Signals, s => s.Rule == SpcRule.Trend);
        Assert.True(vm.Spc.OutOfControlCount >= 1);
    }

    private static MainViewModel CreateLoaded()
    {
        var vm = new MainViewModel(new NoDialogs());
        vm.LoadPlan(Plan);
        vm.LoadMeasurements(
        [
            new Measurement("P1", "Bore", 12.009m), new Measurement("P1", "Length", 85.00m),
            new Measurement("P2", "Bore", 12.011m), new Measurement("P2", "Length", 85.02m),
            new Measurement("P3", "Bore", 12.008m), new Measurement("P3", "Length", 84.99m),
            new Measurement("P4", "Bore", 12.010m), new Measurement("P4", "Length", 85.01m),
        ]);
        return vm;
    }

    private sealed class NoDialogs : IDialogService
    {
        public string? PickFileToOpen(string title, string filter) => null;

        public string? PickFileToSave(string title, string filter, string suggestedName) => null;

        public void ShowError(string title, string message) => throw new InvalidOperationException(message);
    }
}

public class ChartAxisTests
{
    [Fact]
    public void Ticks_use_round_steps()
    {
        var axis = ChartAxis.Nice(0, 10);

        Assert.Equal(new[] { 0.0, 2, 4, 6, 8, 10 }, axis.Ticks);
        Assert.Equal(0, axis.Decimals);
    }

    [Fact]
    public void Range_is_rounded_outwards_to_whole_steps()
    {
        var axis = ChartAxis.Nice(11.9986, 12.0204);

        Assert.Equal(0.005, axis.Step, 12);
        Assert.Equal(11.995, axis.Minimum, 12);
        Assert.Equal(12.025, axis.Maximum, 12);
        Assert.Equal(3, axis.Decimals);
    }

    [Theory]
    [InlineData(0.0, 0.0115, 0.0025, 4)] // 2.5 × 10⁻³ needs a fourth decimal place
    [InlineData(0.0, 1.0, 0.2, 1)]
    [InlineData(100.0, 300.0, 50, 0)]
    public void Decimals_fit_the_step(double min, double max, double step, int decimals)
    {
        var axis = ChartAxis.Nice(min, max);

        Assert.Equal(step, axis.Step, 12);
        Assert.Equal(decimals, axis.Decimals);
    }

    [Fact]
    public void Flat_range_still_gets_a_usable_axis()
    {
        var axis = ChartAxis.Nice(10, 10);

        Assert.True(axis.Minimum < 10 && axis.Maximum > 10);
        Assert.Equal(0.5, axis.ToFraction(10), 6);
    }

    [Fact]
    public void Axis_for_a_chart_covers_readings_tolerance_and_control_limits()
    {
        var feature = new Feature("Bore", new Tolerance(12.000m, 0.018m, 0m));
        var plan = new InspectionPlan("P", "Part", "mm", [feature]);
        var parts = new Inspector(plan, new ToleranceChecker()).InspectAll(
        [
            new Measurement("A", "Bore", 12.004m), new Measurement("B", "Bore", 12.016m),
            new Measurement("C", "Bore", 12.006m), new Measurement("D", "Bore", 12.014m),
        ]);
        var chart = ControlChartCalculator.Calculate(feature, parts);

        var axis = ChartAxis.For(chart);

        Assert.True(axis.Minimum <= chart.LowerControlLimit && axis.Minimum <= 12.000);
        Assert.True(axis.Maximum >= chart.UpperControlLimit && axis.Maximum >= 12.018);
        Assert.InRange(axis.ToFraction(12.009), 0.0, 1.0);
    }
}
