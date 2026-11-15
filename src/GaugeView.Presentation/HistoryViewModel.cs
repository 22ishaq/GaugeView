using System.Collections.ObjectModel;
using System.Data.Common;
using GaugeView.Core;
using GaugeView.Core.History;

namespace GaugeView.Presentation;

/// <summary>What is on screen now, ready to save.</summary>
public sealed record InspectionSnapshot(InspectionPlan Plan, IReadOnlyList<PartInspection> Parts, decimal WarningThreshold);

/// <summary>
/// The history tab: saves the current inspection with an operator and batch, lists earlier inspections of the
/// same part number, reopens one, and shows how a feature has moved from batch to batch.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly IInspectionHistory? store;
    private readonly IDialogService dialogs;
    private readonly TimeProvider clock;
    private readonly Func<InspectionSnapshot?> snapshot;
    private readonly Action<SavedInspection> reopen;
    private string operatorName = string.Empty;
    private string batch = string.Empty;
    private string? partNumber;
    private InspectionRecord? selectedRecord;
    private string? selectedTrendFeature;
    private string message;

    public HistoryViewModel(
        IInspectionHistory? store,
        IDialogService dialogs,
        TimeProvider clock,
        Func<InspectionSnapshot?> snapshot,
        Action<SavedInspection> reopen)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reopen);
        this.store = store;
        this.dialogs = dialogs;
        this.clock = clock;
        this.snapshot = snapshot;
        this.reopen = reopen;
        message = store is null
            ? "Inspection history is not available, so inspections can't be saved."
            : "Enter your name and the batch, then save the inspection to keep it.";

        SaveCommand = new RelayCommand(Save, CanSave);
        ReopenCommand = new RelayCommand(Reopen, () => store is not null && SelectedRecord is not null);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ReopenCommand { get; }

    public bool IsAvailable => store is not null;

    /// <summary>Where the history file is, so it can be backed up.</summary>
    public string Location => store is null ? "Not available" : store.Location;

    public string OperatorName
    {
        get => operatorName;
        set
        {
            if (SetProperty(ref operatorName, value ?? string.Empty))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The batch or lot the parts came from.</summary>
    public string Batch
    {
        get => batch;
        set
        {
            if (SetProperty(ref batch, value ?? string.Empty))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public string Heading => partNumber is null ? "Saved inspections" : $"Saved inspections of {partNumber}";

    public ObservableCollection<InspectionRecord> Records { get; } = [];

    public InspectionRecord? SelectedRecord
    {
        get => selectedRecord;
        set
        {
            if (SetProperty(ref selectedRecord, value))
                ReopenCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Features of the current plan that can be followed across batches.</summary>
    public ObservableCollection<string> TrendFeatures { get; } = [];

    public string? SelectedTrendFeature
    {
        get => selectedTrendFeature;
        set
        {
            if (SetProperty(ref selectedTrendFeature, value))
                LoadTrend();
        }
    }

    /// <summary>One row per saved batch for the selected feature, oldest first.</summary>
    public ObservableCollection<FeatureTrendPoint> Trend { get; } = [];

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    /// <summary>Shows the history of the plan's part number and offers its features for trends.</summary>
    public void SetPlan(InspectionPlan? plan)
    {
        partNumber = plan?.PartNumber;
        OnPropertyChanged(nameof(Heading));

        var keep = SelectedTrendFeature;
        TrendFeatures.Clear();
        foreach (var feature in plan?.Features ?? [])
            TrendFeatures.Add(feature.Name);

        selectedTrendFeature = TrendFeatures.FirstOrDefault(f => f == keep) ?? TrendFeatures.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedTrendFeature));
        Refresh();
    }

    /// <summary>Re-reads the saved inspections and the trend.</summary>
    public void Refresh()
    {
        if (store is null)
            return;

        Attempt("Could not read the inspection history", () =>
        {
            var keep = SelectedRecord?.Id;
            Records.Clear();
            foreach (var record in store.List(partNumber))
                Records.Add(record);
            SelectedRecord = Records.FirstOrDefault(r => r.Id == keep);
        });
        LoadTrend();
    }

    /// <summary>Called when the parts on screen change, since that decides whether there is anything to save.</summary>
    public void NotifyInspectionChanged() => SaveCommand.RaiseCanExecuteChanged();

    private bool CanSave() =>
        store is not null
        && !string.IsNullOrWhiteSpace(OperatorName)
        && !string.IsNullOrWhiteSpace(Batch)
        && snapshot() is not null;

    private void Save()
    {
        if (store is null || snapshot() is not { } current)
            return;

        Attempt("Could not save the inspection", () =>
        {
            var record = store.Save(current.Plan, current.Parts, OperatorName, Batch, current.WarningThreshold, clock.GetUtcNow());
            Message = $"Saved batch {record.Batch}: {record.PartCount} parts, {record.FailCount} out of spec, inspected by {record.Operator}.";
            Batch = string.Empty;
            Refresh();
            SelectedRecord = Records.FirstOrDefault(r => r.Id == record.Id);
        });
    }

    private void Reopen()
    {
        if (store is null || SelectedRecord is not { } record)
            return;

        Attempt("Could not reopen the inspection", () => reopen(store.Load(record.Id)));
    }

    private void LoadTrend()
    {
        Trend.Clear();
        if (store is null || partNumber is null || SelectedTrendFeature is null)
            return;

        Attempt("Could not read the trend", () =>
        {
            foreach (var point in store.Trend(partNumber, SelectedTrendFeature))
                Trend.Add(point);
        });
    }

    private void Attempt(string failure, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is DbException or IOException or InvalidDataException
                                       or UnauthorizedAccessException or KeyNotFoundException)
        {
            dialogs.ShowError(failure, ex.Message);
            Message = $"{failure}.";
        }
    }
}
