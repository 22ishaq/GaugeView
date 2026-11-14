using System.Collections.ObjectModel;
using GaugeView.Core;

namespace GaugeView.Presentation;

/// <summary>
/// The SPC tab: one individuals control chart per feature, rebuilt whenever a reading changes. The selected
/// feature is kept across rebuilds so editing a reading doesn't jump the chart back to the first feature.
/// </summary>
public sealed class SpcViewModel : ObservableObject
{
    private ControlChart? selectedChart;
    private string unit = "mm";

    public ObservableCollection<ControlChart> Charts { get; } = [];

    public ControlChart? SelectedChart
    {
        get => selectedChart;
        set
        {
            if (!SetProperty(ref selectedChart, value))
                return;

            OnPropertyChanged(nameof(Axis));
            OnPropertyChanged(nameof(SelectedSummary));
        }
    }

    /// <summary>Unit of the readings, from the inspection plan.</summary>
    public string Unit
    {
        get => unit;
        private set => SetProperty(ref unit, value);
    }

    /// <summary>Scale for the selected chart, or null when nothing is selected.</summary>
    public ChartAxis? Axis => SelectedChart is { } chart ? ChartAxis.For(chart) : null;

    /// <summary>Features with at least one out-of-control signal.</summary>
    public int OutOfControlCount => Charts.Count(c => c.State == ChartState.OutOfControl);

    /// <summary>A one-line verdict for the selected chart.</summary>
    public string SelectedSummary => SelectedChart switch
    {
        null => "Load measurements to see control charts.",
        { State: ChartState.NotEnoughData } => "Not enough readings with variation to set control limits yet.",
        { State: ChartState.InControl } => "In statistical control: no Nelson rule broken.",
        { Signals.Count: var n } => $"Out of control: {n} signal{(n == 1 ? string.Empty : "s")}. Look for a special cause.",
    };

    /// <summary>Rebuilds every chart from the parts, which must be in production order.</summary>
    public void Update(InspectionPlan? plan, IReadOnlyList<PartInspection> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var keep = SelectedChart?.Feature.Name;
        Unit = plan?.Unit ?? "mm";
        Charts.Clear();
        if (plan is not null && parts.Count > 0)
        {
            foreach (var chart in ControlChartCalculator.Calculate(plan, parts))
                Charts.Add(chart);
        }

        SelectedChart = Charts.FirstOrDefault(c => c.Feature.Name == keep) ?? Charts.FirstOrDefault();
        OnPropertyChanged(nameof(OutOfControlCount));
    }
}
