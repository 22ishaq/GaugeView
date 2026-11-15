using GaugeView.Core;
using GaugeView.Core.History;

namespace GaugeView.Presentation.Tests;

public class HistoryViewModelTests
{
    private static readonly InspectionPlan Plan = new("BRK-1042", "Bracket", "mm",
    [
        new Feature("Bore", new Tolerance(12.000m, 0.018m, 0m)),
        new Feature("Length", Tolerance.Symmetric(85.00m, 0.10m)),
    ]);

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeHistory store = new();

    [Fact]
    public void Save_is_enabled_once_there_are_parts_an_operator_and_a_batch()
    {
        var vm = new MainViewModel(new Dialogs(), history: store);
        Assert.False(vm.History.SaveCommand.CanExecute(null));

        Load(vm);
        Assert.False(vm.History.SaveCommand.CanExecute(null));

        vm.History.OperatorName = "Ali";
        vm.History.Batch = "2611-A";
        Assert.True(vm.History.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Saving_stores_the_inspection_with_the_operator_batch_threshold_and_time()
    {
        var vm = Loaded();
        vm.WarningThresholdPercent = 90;
        vm.History.OperatorName = "Ali";
        vm.History.Batch = "2611-A";

        vm.History.SaveCommand.Execute(null);

        var saved = Assert.Single(store.Saved);
        Assert.Equal(("Ali", "2611-A", 0.9m, Now), (saved.Record.Operator, saved.Record.Batch, saved.WarningThreshold, saved.Record.InspectedAt));
        Assert.Equal(3, saved.Parts.Count);
        Assert.Same(vm.History.Records[0], vm.History.SelectedRecord);
        Assert.Empty(vm.History.Batch); // ready for the next batch
        Assert.StartsWith("Saved batch 2611-A", vm.History.Message);
    }

    [Fact]
    public void Reopening_restores_the_plan_parts_and_threshold_and_shows_the_inspection_tab()
    {
        var vm = Loaded();
        vm.WarningThresholdPercent = 95;
        vm.History.OperatorName = "Ali";
        vm.History.Batch = "2611-A";
        vm.History.SaveCommand.Execute(null);

        var fresh = new MainViewModel(new Dialogs(), history: store) { SelectedTab = 3 };
        fresh.LoadPlan(Plan);
        fresh.History.SelectedRecord = fresh.History.Records.Single();
        fresh.History.ReopenCommand.Execute(null);

        Assert.Equal(95, fresh.WarningThresholdPercent);
        Assert.Equal(new[] { "P1", "P2", "P3" }, fresh.Parts.Select(p => p.Serial));
        Assert.Equal(0, fresh.SelectedTab);
        Assert.StartsWith("Reopened batch 2611-A", fresh.StatusMessage);
    }

    [Fact]
    public void Trend_follows_the_selected_feature()
    {
        var vm = Loaded();
        vm.History.OperatorName = "Ali";
        vm.History.Batch = "A";
        vm.History.SaveCommand.Execute(null);

        Assert.Equal(new[] { "Bore", "Length" }, vm.History.TrendFeatures);
        Assert.Equal("Bore", vm.History.SelectedTrendFeature);
        Assert.Equal("Bore", store.LastTrendFeature);
        Assert.Single(vm.History.Trend);

        vm.History.SelectedTrendFeature = "Length";
        Assert.Equal("Length", store.LastTrendFeature);
    }

    [Fact]
    public void Store_errors_are_shown_instead_of_crashing()
    {
        var dialogs = new Dialogs();
        var vm = new MainViewModel(dialogs, history: new FailingHistory(), clock: new FixedClock(Now));
        Load(vm);
        vm.History.OperatorName = "Ali";
        vm.History.Batch = "A";

        vm.History.SaveCommand.Execute(null);

        Assert.Equal("Could not save the inspection", dialogs.LastErrorTitle);
    }

    [Fact]
    public void Without_a_store_history_is_unavailable()
    {
        var vm = Loaded(history: null);
        vm.History.OperatorName = "Ali";
        vm.History.Batch = "A";

        Assert.False(vm.History.IsAvailable);
        Assert.False(vm.History.SaveCommand.CanExecute(null));
        Assert.Contains("not available", vm.History.Message);
    }

    private MainViewModel Loaded() => Loaded(store);

    private static MainViewModel Loaded(IInspectionHistory? history)
    {
        var vm = new MainViewModel(new Dialogs(), history: history, clock: new FixedClock(Now));
        Load(vm);
        return vm;
    }

    private static void Load(MainViewModel vm)
    {
        vm.LoadPlan(Plan);
        vm.LoadMeasurements(
        [
            new Measurement("P1", "Bore", 12.009m), new Measurement("P1", "Length", 85.00m),
            new Measurement("P2", "Bore", 12.017m), new Measurement("P2", "Length", 85.01m),
            new Measurement("P3", "Bore", 12.009m), new Measurement("P3", "Length", 85.15m),
        ]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Dialogs : IDialogService
    {
        public string? LastErrorTitle { get; private set; }

        public string? PickFileToOpen(string title, string filter) => null;

        public string? PickFileToSave(string title, string filter, string suggestedName) => null;

        public void ShowError(string title, string message) => LastErrorTitle = title;
    }

    private sealed class FakeHistory : IInspectionHistory
    {
        public List<SavedInspection> Saved { get; } = [];

        public string? LastTrendFeature { get; private set; }

        public string Location => "memory";

        public InspectionRecord Save(InspectionPlan plan, IReadOnlyList<PartInspection> parts, string operatorName, string batch,
            decimal warningThreshold, DateTimeOffset inspectedAt)
        {
            var record = new InspectionRecord(Saved.Count + 1, plan.PartNumber, operatorName, batch, inspectedAt, parts.Count,
                parts.Count(p => p.Verdict == InspectionStatus.Pass), parts.Count(p => p.Verdict == InspectionStatus.Warning),
                parts.Count(p => p.Verdict == InspectionStatus.Fail), parts.Count(p => p.Verdict == InspectionStatus.Missing));
            Saved.Add(new SavedInspection(record, plan, parts, warningThreshold));
            return record;
        }

        public IReadOnlyList<InspectionRecord> List(string? partNumber = null) =>
            Saved.Select(s => s.Record).Where(r => partNumber is null || r.PartNumber == partNumber).Reverse().ToList();

        public SavedInspection Load(long id) => Saved.Single(s => s.Record.Id == id);

        public IReadOnlyList<FeatureTrendPoint> Trend(string partNumber, string featureName)
        {
            LastTrendFeature = featureName;
            return Saved.Select(s => new FeatureTrendPoint(s.Record.Id, s.Record.Batch, s.Record.Operator, s.Record.InspectedAt,
                s.Parts.Count, null, null, null, null, 0)).ToList();
        }
    }

    private sealed class FailingHistory : IInspectionHistory
    {
        public string Location => "nowhere";

        public InspectionRecord Save(InspectionPlan plan, IReadOnlyList<PartInspection> parts, string operatorName, string batch,
            decimal warningThreshold, DateTimeOffset inspectedAt) => throw new IOException("Disk full.");

        public IReadOnlyList<InspectionRecord> List(string? partNumber = null) => [];

        public SavedInspection Load(long id) => throw new KeyNotFoundException();

        public IReadOnlyList<FeatureTrendPoint> Trend(string partNumber, string featureName) => [];
    }
}
