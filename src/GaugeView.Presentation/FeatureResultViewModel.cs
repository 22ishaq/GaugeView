using System.Collections;
using System.ComponentModel;
using System.Globalization;
using GaugeView.Core;

namespace GaugeView.Presentation;

/// <summary>
/// One row of the results grid. The measured value is editable: typing a new reading re-checks the
/// feature straight away, and text that is not a number is reported through INotifyDataErrorInfo.
/// </summary>
public sealed class FeatureResultViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Func<ToleranceChecker> checker;
    private FeatureResult result;
    private string measuredText;
    private string? inputError;

    public FeatureResultViewModel(Feature feature, decimal? measured, Func<ToleranceChecker> checker)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(checker);
        Feature = feature;
        this.checker = checker;
        result = checker().Check(feature, measured);
        measuredText = measured?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>Raised after a reading has been checked or re-checked.</summary>
    public event EventHandler? ResultChanged;

    public Feature Feature { get; }

    public string Name => Feature.Name;

    public decimal Nominal => Feature.Tolerance.Nominal;

    public string ToleranceText => Feature.Tolerance.DeviationText;

    public decimal LowerLimit => Feature.Tolerance.LowerLimit;

    public decimal UpperLimit => Feature.Tolerance.UpperLimit;

    public FeatureResult Result => result;

    public InspectionStatus Status => result.Status;

    public decimal? Deviation => result.Deviation;

    /// <summary>Share of the tolerance used, as a percentage to one decimal place.</summary>
    public decimal? ToleranceUsedPercent => result.FractionUsed is { } used ? Math.Round(used * 100, 1) : null;

    /// <summary>The reading as typed. Empty means not measured yet.</summary>
    public string MeasuredText
    {
        get => measuredText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref measuredText, value))
                return;

            var text = value.Trim();
            if (text.Length == 0)
            {
                SetInputError(null);
                Apply(null);
            }
            else if (TryParseReading(text, out var reading))
            {
                SetInputError(null);
                Apply(reading);
            }
            else
            {
                SetInputError($"'{text}' is not a number.");
                Apply(null);
            }
        }
    }

    public bool HasErrors => inputError is not null;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName == nameof(MeasuredText) && inputError is not null ? new[] { inputError } : Array.Empty<string>();

    /// <summary>Re-checks the current reading, for example after the warning threshold changes.</summary>
    public void Recheck() => Apply(result.MeasuredValue);

    private void Apply(decimal? reading)
    {
        result = checker().Check(Feature, reading);
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Deviation));
        OnPropertyChanged(nameof(ToleranceUsedPercent));
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetInputError(string? error)
    {
        if (inputError == error)
            return;

        inputError = error;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(MeasuredText)));
        OnPropertyChanged(nameof(HasErrors));
    }

    // Accept the PC's own decimal separator first, then fall back to a full stop.
    private static bool TryParseReading(string text, out decimal reading) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out reading)
        || decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out reading);
}

/// <summary>A part in the parts list: its feature rows and overall verdict.</summary>
public sealed class PartViewModel : ObservableObject
{
    private InspectionStatus verdict;

    public PartViewModel(PartInspection inspection, Func<ToleranceChecker> checker)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        Serial = inspection.Serial;
        Features = inspection.Results
            .Select(r => new FeatureResultViewModel(r.Feature, r.MeasuredValue, checker))
            .ToList();
        foreach (var feature in Features)
            feature.ResultChanged += (_, _) => Refresh();
        verdict = ToInspection().Verdict;
    }

    /// <summary>Raised when any feature's result changes.</summary>
    public event EventHandler? ResultsChanged;

    public string Serial { get; }

    public IReadOnlyList<FeatureResultViewModel> Features { get; }

    public InspectionStatus Verdict
    {
        get => verdict;
        private set => SetProperty(ref verdict, value);
    }

    public PartInspection ToInspection() => new(Serial, Features.Select(f => f.Result).ToList());

    private void Refresh()
    {
        Verdict = ToInspection().Verdict;
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }
}
