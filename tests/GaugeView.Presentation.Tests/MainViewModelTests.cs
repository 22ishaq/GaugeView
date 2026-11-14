using GaugeView.Core;

namespace GaugeView.Presentation.Tests;

public class MainViewModelTests
{
    private static readonly InspectionPlan Plan = new("BRK-1042", "Bracket", "mm",
    [
        new Feature("Bore", new Tolerance(12.000m, 0.018m, 0m)),
        new Feature("Length", Tolerance.Symmetric(85.00m, 0.10m)),
    ]);

    private readonly FakeDialogs dialogs = new();

    [Fact]
    public void Loading_measurements_fills_parts_and_summary()
    {
        var vm = CreateLoaded();

        Assert.Equal(3, vm.TotalParts);
        Assert.Equal(1, vm.PassCount);
        Assert.Equal(1, vm.WarningCount);
        Assert.Equal(1, vm.FailCount);
        Assert.Equal(66.7, vm.YieldPercent);
        Assert.Equal("P1", vm.SelectedPart?.Serial);
        Assert.Equal(2, vm.Capability.Count);
    }

    [Fact]
    public void Typing_a_new_reading_rechecks_the_feature_part_and_summary()
    {
        var vm = CreateLoaded();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var part = vm.Parts[0];

        part.Features[0].MeasuredText = "12.025";

        Assert.Equal(InspectionStatus.Fail, part.Features[0].Status);
        Assert.Equal(InspectionStatus.Fail, part.Verdict);
        Assert.Equal(2, vm.FailCount);
        Assert.Contains(nameof(MainViewModel.FailCount), changed);
    }

    [Fact]
    public void Text_that_is_not_a_number_is_flagged_and_counts_as_missing()
    {
        var feature = CreateLoaded().Parts[0].Features[0];

        feature.MeasuredText = "12.0x";
        Assert.True(feature.HasErrors);
        Assert.Equal(InspectionStatus.Missing, feature.Status);
        Assert.NotEmpty(feature.GetErrors(nameof(feature.MeasuredText)).Cast<string>());

        feature.MeasuredText = "12.010";
        Assert.False(feature.HasErrors);
        Assert.Equal(InspectionStatus.Pass, feature.Status);
    }

    [Fact]
    public void Raising_the_warning_threshold_rechecks_every_part()
    {
        var vm = CreateLoaded();

        vm.WarningThresholdPercent = 95; // P2's bore uses 88.9% of its tolerance

        Assert.Equal(InspectionStatus.Pass, vm.Parts[1].Verdict);
        Assert.Equal(0, vm.WarningCount);
    }

    [Fact]
    public void Commands_are_enabled_once_there_is_something_to_act_on()
    {
        var vm = new MainViewModel(dialogs);
        Assert.True(vm.OpenPlanCommand.CanExecute(null));
        Assert.False(vm.ImportMeasurementsCommand.CanExecute(null));
        Assert.False(vm.ExportReportCommand.CanExecute(null));
        Assert.False(vm.LoadSampleCommand.CanExecute(null));

        vm.LoadPlan(Plan);
        Assert.True(vm.ImportMeasurementsCommand.CanExecute(null));
        Assert.False(vm.ExportReportCommand.CanExecute(null));
    }

    [Fact]
    public void Add_part_creates_a_part_with_every_feature_missing()
    {
        var vm = new MainViewModel(dialogs);
        vm.LoadPlan(Plan);

        vm.AddPartCommand.Execute(null);

        var part = Assert.Single(vm.Parts);
        Assert.Equal("MANUAL-001", part.Serial);
        Assert.All(part.Features, f => Assert.Equal(InspectionStatus.Missing, f.Status));
        Assert.Same(part, vm.SelectedPart);
        Assert.True(vm.ExportReportCommand.CanExecute(null));
    }

    [Fact]
    public void Export_writes_the_report_to_the_chosen_file()
    {
        var vm = CreateLoaded();
        dialogs.SavePath = Path.Combine(Path.GetTempPath(), $"gaugeview-{Guid.NewGuid():N}.csv");
        try
        {
            vm.ExportReportCommand.Execute(null);

            Assert.Equal(7, File.ReadAllLines(dialogs.SavePath).Length); // header + 3 parts × 2 features
        }
        finally
        {
            File.Delete(dialogs.SavePath);
        }
    }

    [Fact]
    public void Bad_file_shows_an_error_instead_of_crashing()
    {
        dialogs.OpenPath = Path.Combine(Path.GetTempPath(), $"gaugeview-{Guid.NewGuid():N}.json");
        File.WriteAllText(dialogs.OpenPath, "{ not json");
        try
        {
            var vm = new MainViewModel(dialogs);

            vm.OpenPlanCommand.Execute(null);

            Assert.Equal("Could not open the inspection plan", dialogs.LastErrorTitle);
            Assert.False(vm.ImportMeasurementsCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(dialogs.OpenPath);
        }
    }

    [Fact]
    public void Sample_data_loads_from_the_samples_folder()
    {
        var vm = new MainViewModel(dialogs, Path.Combine(AppContext.BaseDirectory, "samples"));
        Assert.True(vm.LoadSampleCommand.CanExecute(null));

        vm.LoadSampleCommand.Execute(null);

        Assert.Equal(30, vm.TotalParts);
        Assert.Equal(6, vm.Capability.Count);
        Assert.Equal(6, vm.Spc.Charts.Count);
        Assert.Null(dialogs.LastErrorTitle);
    }

    private MainViewModel CreateLoaded()
    {
        var vm = new MainViewModel(dialogs);
        vm.LoadPlan(Plan);
        vm.LoadMeasurements(
        [
            new Measurement("P1", "Bore", 12.009m), new Measurement("P1", "Length", 85.00m), // pass
            new Measurement("P2", "Bore", 12.017m), new Measurement("P2", "Length", 85.01m), // warning
            new Measurement("P3", "Bore", 12.009m), new Measurement("P3", "Length", 85.15m), // fail
        ]);
        return vm;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? OpenPath { get; set; }

        public string? SavePath { get; set; }

        public string? LastErrorTitle { get; private set; }

        public string? PickFileToOpen(string title, string filter) => OpenPath;

        public string? PickFileToSave(string title, string filter, string suggestedName) => SavePath;

        public void ShowError(string title, string message) => LastErrorTitle = title;
    }
}
