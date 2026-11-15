using System.Collections.ObjectModel;
using GaugeView.Core;
using GaugeView.Core.History;
using GaugeView.Core.IO;

namespace GaugeView.Presentation;

/// <summary>
/// The main screen: loads an inspection plan and measurements, keeps the summary and capability figures
/// up to date as readings change, and exports the report. Contains no WPF types.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public const string SamplePlanFile = "bracket-plan.json";
    public const string SampleMeasurementsFile = "bracket-measurements.csv";

    private const string PlanFilter = "Inspection plan (*.json)|*.json|All files (*.*)|*.*";
    private const string CsvFilter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*";

    private readonly IDialogService dialogs;
    private readonly string? sampleDirectory;
    private ToleranceChecker checker = new();
    private InspectionPlan? plan;
    private PartViewModel? selectedPart;
    private double warningThresholdPercent = (double)(ToleranceChecker.DefaultWarningThreshold * 100);
    private string statusMessage = "Open an inspection plan, or load the sample, to start.";
    private int selectedTab;
    private bool rechecking;
    private int manualPartCount;

    /// <param name="dialogs">File pickers and message boxes.</param>
    /// <param name="sampleDirectory">Folder holding the sample files, if any.</param>
    /// <param name="history">Where inspections are saved; null if history is unavailable.</param>
    /// <param name="clock">Time source for saved inspections; the system clock by default.</param>
    public MainViewModel(
        IDialogService dialogs,
        string? sampleDirectory = null,
        IInspectionHistory? history = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        this.dialogs = dialogs;
        this.sampleDirectory = sampleDirectory;

        OpenPlanCommand = new RelayCommand(OpenPlan);
        ImportMeasurementsCommand = new RelayCommand(ImportMeasurements, () => plan is not null);
        AddPartCommand = new RelayCommand(AddPart, () => plan is not null);
        ExportReportCommand = new RelayCommand(ExportReport, () => Parts.Count > 0);
        LoadSampleCommand = new RelayCommand(LoadSample, () => SampleAvailable);
        GaugeRr = new GaugeRrViewModel(dialogs, sampleDirectory);
        History = new HistoryViewModel(history, dialogs, clock ?? TimeProvider.System, Snapshot, Reopen);
    }

    public ObservableCollection<PartViewModel> Parts { get; } = [];

    public ObservableCollection<CapabilityResult> Capability { get; } = [];

    /// <summary>Control charts for the SPC tab, kept in step with the readings.</summary>
    public SpcViewModel Spc { get; } = new();

    /// <summary>The gauge R&amp;R study for the Gauge R&amp;R tab.</summary>
    public GaugeRrViewModel GaugeRr { get; }

    /// <summary>Saved inspections for the History tab.</summary>
    public HistoryViewModel History { get; }

    public RelayCommand OpenPlanCommand { get; }

    public RelayCommand ImportMeasurementsCommand { get; }

    public RelayCommand AddPartCommand { get; }

    public RelayCommand ExportReportCommand { get; }

    public RelayCommand LoadSampleCommand { get; }

    public string PlanTitle => plan is null
        ? "No inspection plan loaded"
        : $"{plan.PartNumber} · {plan.Description} ({plan.Features.Count} features, {plan.Unit})";

    /// <summary>The open tab: 0 inspection, 1 SPC charts, 2 gauge R&amp;R, 3 history.</summary>
    public int SelectedTab
    {
        get => selectedTab;
        set => SetProperty(ref selectedTab, value);
    }

    public PartViewModel? SelectedPart
    {
        get => selectedPart;
        set => SetProperty(ref selectedPart, value);
    }

    /// <summary>Percentage of its tolerance at which an in-spec reading turns amber, from 50 to 100.</summary>
    public double WarningThresholdPercent
    {
        get => warningThresholdPercent;
        set
        {
            var percent = Math.Clamp(Math.Round(value), 50, 100);
            if (!SetProperty(ref warningThresholdPercent, percent))
                return;

            checker = new ToleranceChecker((decimal)percent / 100);
            RecheckAll();
        }
    }

    public int TotalParts => Parts.Count;

    public int PassCount => CountParts(InspectionStatus.Pass);

    public int WarningCount => CountParts(InspectionStatus.Warning);

    public int FailCount => CountParts(InspectionStatus.Fail);

    public int MissingCount => CountParts(InspectionStatus.Missing);

    /// <summary>Share of parts with every feature in tolerance (pass or warning), as a percentage.</summary>
    public double YieldPercent => TotalParts == 0 ? 0 : Math.Round(100.0 * (PassCount + WarningCount) / TotalParts, 1);

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    private bool SampleAvailable =>
        sampleDirectory is not null
        && File.Exists(Path.Combine(sampleDirectory, SamplePlanFile))
        && File.Exists(Path.Combine(sampleDirectory, SampleMeasurementsFile));

    /// <summary>Starts a new inspection session for <paramref name="newPlan"/>, clearing any parts.</summary>
    public void LoadPlan(InspectionPlan newPlan)
    {
        ArgumentNullException.ThrowIfNull(newPlan);
        plan = newPlan;
        manualPartCount = 0;
        Parts.Clear();
        SelectedPart = null;
        OnPropertyChanged(nameof(PlanTitle));
        GaugeRr.SetPlan(plan);
        History.SetPlan(plan);
        RefreshSummary();
        StatusMessage = $"Loaded plan {plan.PartNumber} with {plan.Features.Count} features.";
    }

    /// <summary>Checks a batch of readings against the plan, replacing the current parts.</summary>
    public void LoadMeasurements(IEnumerable<Measurement> measurements)
    {
        var currentPlan = plan ?? throw new InvalidOperationException("Load an inspection plan first.");
        var inspections = new Inspector(currentPlan, checker).InspectAll(measurements);

        Parts.Clear();
        foreach (var inspection in inspections)
            AddPartViewModel(inspection);
        SelectedPart = Parts.FirstOrDefault();
        RefreshSummary();
        StatusMessage =
            $"Inspected {TotalParts} parts: {FailCount} failed, {WarningCount} near a limit, {MissingCount} incomplete.";
    }

    /// <summary>Shows a set of already-inspected parts, re-checked against the current threshold.</summary>
    public void LoadInspections(IReadOnlyList<PartInspection> inspections)
    {
        ArgumentNullException.ThrowIfNull(inspections);
        if (plan is null)
            throw new InvalidOperationException("Load an inspection plan first.");

        Parts.Clear();
        foreach (var inspection in inspections)
            AddPartViewModel(inspection);
        SelectedPart = Parts.FirstOrDefault();
        RefreshSummary();
    }

    /// <summary>Reopens a saved inspection with the plan and warning threshold it was saved with.</summary>
    public void Reopen(SavedInspection saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        WarningThresholdPercent = (double)(saved.WarningThreshold * 100);
        LoadPlan(saved.Plan);
        LoadInspections(saved.Parts);
        SelectedTab = 0;

        var record = saved.Record;
        StatusMessage = $"Reopened batch {record.Batch} ({record.PartCount} parts), inspected by {record.Operator} " +
                        $"on {record.LocalTime:d MMM yyyy HH:mm}.";
    }

    private InspectionSnapshot? Snapshot() => plan is null || Parts.Count == 0
        ? null
        : new InspectionSnapshot(plan, Parts.Select(p => p.ToInspection()).ToList(), checker.WarningThreshold);

    private void OpenPlan()
    {
        var path = dialogs.PickFileToOpen("Open inspection plan", PlanFilter);
        if (path is not null)
            Attempt("Could not open the inspection plan", () => LoadPlan(InspectionPlanReader.ReadFile(path)));
    }

    private void ImportMeasurements()
    {
        var path = dialogs.PickFileToOpen("Import measurements", CsvFilter);
        if (path is not null)
            Attempt("Could not import the measurements", () => LoadMeasurements(MeasurementCsvReader.ReadFile(path)));
    }

    private void AddPart()
    {
        if (plan is null)
            return;

        string serial;
        do
        {
            serial = $"MANUAL-{++manualPartCount:000}";
        }
        while (Parts.Any(p => p.Serial == serial));

        var part = AddPartViewModel(new Inspector(plan, checker).Inspect(serial, new Dictionary<Feature, decimal>()));
        SelectedPart = part;
        RefreshSummary();
        StatusMessage = $"Added {serial}. Type its readings into the Measured column.";
    }

    private void ExportReport()
    {
        var path = dialogs.PickFileToSave("Export inspection report", CsvFilter, $"{plan?.PartNumber ?? "inspection"}-report.csv");
        if (path is null)
            return;

        Attempt("Could not export the report", () =>
        {
            InspectionReportWriter.WriteFile(path, Parts.Select(p => p.ToInspection()));
            StatusMessage = $"Exported {TotalParts} parts to {Path.GetFileName(path)}.";
        });
    }

    private void LoadSample()
    {
        if (sampleDirectory is null)
            return;

        Attempt("Could not load the sample data", () =>
        {
            LoadPlan(InspectionPlanReader.ReadFile(Path.Combine(sampleDirectory, SamplePlanFile)));
            LoadMeasurements(MeasurementCsvReader.ReadFile(Path.Combine(sampleDirectory, SampleMeasurementsFile)));
            if (GaugeRr.LoadSampleStudyCommand.CanExecute(null))
                GaugeRr.LoadSampleStudy();
        });
    }

    private PartViewModel AddPartViewModel(PartInspection inspection)
    {
        var part = new PartViewModel(inspection, () => checker);
        part.ResultsChanged += (_, _) =>
        {
            if (!rechecking)
                RefreshSummary();
        };
        Parts.Add(part);
        return part;
    }

    private void RecheckAll()
    {
        rechecking = true;
        try
        {
            foreach (var feature in Parts.SelectMany(p => p.Features))
                feature.Recheck();
        }
        finally
        {
            rechecking = false;
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(TotalParts));
        OnPropertyChanged(nameof(PassCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(FailCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(YieldPercent));

        var inspections = Parts.Select(p => p.ToInspection()).ToList();
        Capability.Clear();
        if (plan is not null && inspections.Count > 0)
        {
            foreach (var result in CapabilityCalculator.Calculate(plan, inspections))
                Capability.Add(result);
        }

        Spc.Update(plan, inspections);
        History.NotifyInspectionChanged();

        ImportMeasurementsCommand.RaiseCanExecuteChanged();
        AddPartCommand.RaiseCanExecuteChanged();
        ExportReportCommand.RaiseCanExecuteChanged();
    }

    private int CountParts(InspectionStatus verdict) => Parts.Count(p => p.Verdict == verdict);

    private void Attempt(string failure, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            dialogs.ShowError(failure, ex.Message);
            StatusMessage = $"{failure}.";
        }
    }
}
