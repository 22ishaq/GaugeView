using System.Collections.ObjectModel;
using System.Globalization;
using GaugeView.Core;
using GaugeView.Core.IO;

namespace GaugeView.Presentation;

/// <summary>
/// The gauge R&amp;R tab: imports a study, takes the tolerance from a plan feature (or typed in), and shows
/// whether the gauge is good enough to judge that tolerance. Re-analyses whenever the tolerance changes.
/// </summary>
public sealed class GaugeRrViewModel : ObservableObject
{
    public const string SampleStudyFile = "bore-gauge-study.csv";

    /// <summary>The plan feature the sample study was run on.</summary>
    public const string SampleStudyFeature = "Bore Ø12 H7";

    private const string CsvFilter = "Gauge study (*.csv)|*.csv|All files (*.*)|*.*";
    private const string NoStudy = "Import a gauge study (CSV with part, operator and value columns) or load the sample.";

    private readonly IDialogService dialogs;
    private readonly string? sampleDirectory;
    private IReadOnlyList<GrrReading> readings = [];
    private string studyName = string.Empty;
    private Feature? selectedFeature;
    private string toleranceText = string.Empty;
    private GaugeRrResult? result;
    private string message = NoStudy;

    public GaugeRrViewModel(IDialogService dialogs, string? sampleDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        this.dialogs = dialogs;
        this.sampleDirectory = sampleDirectory;

        ImportStudyCommand = new RelayCommand(ImportStudy);
        LoadSampleStudyCommand = new RelayCommand(LoadSampleStudy, () => SampleAvailable);
    }

    public RelayCommand ImportStudyCommand { get; }

    public RelayCommand LoadSampleStudyCommand { get; }

    /// <summary>Features of the loaded plan, to take the tolerance from.</summary>
    public ObservableCollection<Feature> Features { get; } = [];

    public Feature? SelectedFeature
    {
        get => selectedFeature;
        set
        {
            if (SetProperty(ref selectedFeature, value) && value is not null)
                ToleranceText = value.Tolerance.Band.ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>The tolerance band (upper limit minus lower limit) the gauge must judge.</summary>
    public string ToleranceText
    {
        get => toleranceText;
        set
        {
            if (SetProperty(ref toleranceText, value ?? string.Empty))
                Analyse();
        }
    }

    public GaugeRrResult? Result
    {
        get => result;
        private set
        {
            if (!SetProperty(ref result, value))
                return;

            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(StudyTitle));
            OnPropertyChanged(nameof(InteractionNote));
        }
    }

    public bool HasResult => Result is not null;

    public string StudyTitle => Result is { } r
        ? $"{studyName}: {r.PartCount} parts × {r.OperatorCount} operators × {r.TrialCount} trials ({r.ReadingCount} readings)"
        : readings.Count > 0 ? $"{studyName}: {readings.Count} readings" : "No gauge study loaded";

    /// <summary>The verdict in words, or what is stopping the analysis.</summary>
    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    /// <summary>Explains whether the operator × part interaction was kept or pooled.</summary>
    public string InteractionNote => Result switch
    {
        null => string.Empty,
        { InteractionP: null } => "The operator × part interaction could not be tested.",
        { InteractionRemoved: true, InteractionP: var p } =>
            $"Operator × part interaction: p = {p:0.000} > {GaugeRrCalculator.InteractionAlpha:0.00}, so it was pooled into repeatability.",
        { InteractionP: var p } =>
            $"Operator × part interaction: p = {p:0.000}, significant, so it is kept in reproducibility. Some operators measure some parts differently.",
    };

    private bool SampleAvailable =>
        sampleDirectory is not null && File.Exists(Path.Combine(sampleDirectory, SampleStudyFile));

    /// <summary>Offers the plan's features as tolerance sources, keeping the selection where possible.</summary>
    public void SetPlan(InspectionPlan? plan)
    {
        var keep = SelectedFeature?.Name;
        Features.Clear();
        foreach (var feature in plan?.Features ?? [])
            Features.Add(feature);

        // Setting the feature re-fills the tolerance, which re-analyses the study.
        SelectedFeature = Features.FirstOrDefault(f => f.Name == keep);
    }

    /// <summary>Analyses a study against the current tolerance.</summary>
    public void LoadStudy(string name, IReadOnlyList<GrrReading> study)
    {
        ArgumentNullException.ThrowIfNull(study);
        studyName = string.IsNullOrWhiteSpace(name) ? "Gauge study" : name;
        readings = study;
        Analyse();
    }

    /// <summary>Loads the sample study and judges it against the bore tolerance from the plan, or 0.018 mm.</summary>
    public void LoadSampleStudy()
    {
        if (sampleDirectory is null)
            return;

        try
        {
            var study = GaugeStudyCsvReader.ReadFile(Path.Combine(sampleDirectory, SampleStudyFile));
            var feature = Features.FirstOrDefault(f => f.Name == SampleStudyFeature);
            if (feature is not null)
                SelectedFeature = feature;
            else if (toleranceText.Length == 0)
                toleranceText = "0.018";

            LoadStudy("Bore air gauge (sample)", study);
            OnPropertyChanged(nameof(ToleranceText));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Could not load the sample gauge study", ex.Message);
        }
    }

    private void ImportStudy()
    {
        var path = dialogs.PickFileToOpen("Import gauge study", CsvFilter);
        if (path is null)
            return;

        try
        {
            LoadStudy(Path.GetFileNameWithoutExtension(path), GaugeStudyCsvReader.ReadFile(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Could not import the gauge study", ex.Message);
        }
    }

    private void Analyse()
    {
        if (readings.Count == 0)
        {
            Result = null;
            Message = NoStudy;
            return;
        }

        if (!TryParseTolerance(toleranceText, out var tolerance))
        {
            Result = null;
            Message = "Enter the tolerance band (upper limit − lower limit), or pick a feature, to judge the gauge against.";
            OnPropertyChanged(nameof(StudyTitle));
            return;
        }

        try
        {
            Result = GaugeRrCalculator.Calculate(readings, tolerance);
            Message = Describe(Result);
        }
        catch (InvalidDataException ex)
        {
            Result = null;
            Message = ex.Message;
        }

        OnPropertyChanged(nameof(StudyTitle));
    }

    private static string Describe(GaugeRrResult r)
    {
        var used = $"gauge R&R uses {r.PercentOfTolerance:0.0}% of the tolerance";
        var categories = r.DistinctCategories is { } ndc ? $" and tells parts apart into {ndc} categories" : string.Empty;
        return r.Verdict switch
        {
            GrrVerdict.Acceptable => $"Acceptable: {used}{categories}. Under {GaugeRrCalculator.AcceptablePercent:0}%, the gauge is fit to judge this tolerance.",
            GrrVerdict.Marginal => $"Marginal: {used}{categories}. Between {GaugeRrCalculator.AcceptablePercent:0}% and {GaugeRrCalculator.MarginalPercent:0}%, usable with care; check the operator averages for a cause.",
            _ => $"Unacceptable: {used}{categories}. Over {GaugeRrCalculator.MarginalPercent:0}%, the gauge can't reliably judge this tolerance.",
        };
    }

    private static bool TryParseTolerance(string text, out decimal tolerance)
    {
        text = text.Trim();
        var parsed = decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out tolerance)
            || decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance);
        return parsed && tolerance > 0;
    }
}
