using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GaugeView.Core;

namespace GaugeView.Wpf.Converters;

/// <summary>Traffic-light colours: green for good, amber for watch, red for bad, grey for no data.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly Brush PassBrush = Frozen(0x2E, 0x7D, 0x32);
    public static readonly Brush WarningBrush = Frozen(0xD9, 0x82, 0x00);
    public static readonly Brush FailBrush = Frozen(0xC6, 0x28, 0x28);
    public static readonly Brush NoDataBrush = Frozen(0x8A, 0x94, 0xA0);

    public static Brush BrushFor(object? value) => value switch
    {
        InspectionStatus.Pass or CapabilityRating.Capable or ChartState.InControl => PassBrush,
        InspectionStatus.Warning or CapabilityRating.Marginal or ChartState.OutOfControl => WarningBrush,
        InspectionStatus.Fail or CapabilityRating.NotCapable => FailBrush,
        _ => NoDataBrush,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => BrushFor(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Short upper-case labels for status badges.</summary>
public sealed class StatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        InspectionStatus.Pass => "PASS",
        InspectionStatus.Warning => "WARN",
        InspectionStatus.Fail => "FAIL",
        InspectionStatus.Missing => "MISSING",
        CapabilityRating.Capable => "CAPABLE",
        CapabilityRating.Marginal => "MARGINAL",
        CapabilityRating.NotCapable => "NOT CAPABLE",
        CapabilityRating.InsufficientData => "NO DATA",
        ChartState.InControl => "IN CONTROL",
        ChartState.OutOfControl => "SIGNAL",
        ChartState.NotEnoughData => "NO DATA",
        SpcRule rule => $"RULE {(int)rule}",
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
