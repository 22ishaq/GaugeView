using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GaugeView.Wpf.Converters;

namespace GaugeView.Wpf.Controls;

/// <summary>
/// A horizontal scale from 0 to <see cref="Maximum"/>% with green, amber and red bands split at
/// <see cref="FirstBand"/> and <see cref="SecondBand"/>, and a marker at <see cref="Value"/>. Used to show
/// %GRR against the usual 10% and 30% limits.
/// </summary>
public sealed class BandGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), double.NaN);
    public static readonly DependencyProperty FirstBandProperty = Register(nameof(FirstBand), 10.0);
    public static readonly DependencyProperty SecondBandProperty = Register(nameof(SecondBand), 30.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 50.0);

    private static readonly Brush Muted = Frozen(Color.FromRgb(0x5F, 0x6B, 0x7A));
    private static readonly Brush Ink = Frozen(Color.FromRgb(0x2B, 0x33, 0x3F));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double FirstBand
    {
        get => (double)GetValue(FirstBandProperty);
        set => SetValue(FirstBandProperty, value);
    }

    public double SecondBand
    {
        get => (double)GetValue(SecondBandProperty);
        set => SetValue(SecondBandProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 52);

    protected override void OnRender(DrawingContext dc)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double barTop = 16, barHeight = 12, inset = 6;
        var width = Math.Max(1, ActualWidth - 2 * inset);
        double X(double percent) => inset + Math.Clamp(percent / Maximum, 0, 1) * width;

        dc.DrawRectangle(Faded(StatusToBrushConverter.PassBrush), null, new Rect(X(0), barTop, X(FirstBand) - X(0), barHeight));
        dc.DrawRectangle(Faded(StatusToBrushConverter.WarningBrush), null, new Rect(X(FirstBand), barTop, X(SecondBand) - X(FirstBand), barHeight));
        dc.DrawRectangle(Faded(StatusToBrushConverter.FailBrush), null, new Rect(X(SecondBand), barTop, X(Maximum) - X(SecondBand), barHeight));

        foreach (var tick in new[] { 0, FirstBand, SecondBand, Maximum })
        {
            var label = Text(tick.ToString("0", CultureInfo.CurrentCulture) + "%", 11, Muted, dpi);
            dc.DrawText(label, new Point(Math.Clamp(X(tick) - label.Width / 2, 0, ActualWidth - label.Width), barTop + barHeight + 4));
        }

        if (double.IsNaN(Value))
            return;

        // A downward triangle above the bar, with the value; past the end of the scale it sits at the end.
        var x = X(Value);
        var marker = new StreamGeometry();
        using (var context = marker.Open())
        {
            context.BeginFigure(new Point(x - 6, barTop - 9), isFilled: true, isClosed: true);
            context.LineTo(new Point(x + 6, barTop - 9), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(x, barTop + barHeight), isStroked: true, isSmoothJoin: false);
        }

        marker.Freeze();
        dc.DrawGeometry(Ink, null, marker);
    }

    private static DependencyProperty Register(string name, double defaultValue) => DependencyProperty.Register(
        name, typeof(double), typeof(BandGauge),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Brush Faded(Brush brush)
    {
        var color = ((SolidColorBrush)brush).Color;
        return Frozen(Color.FromArgb(0xB0, color.R, color.G, color.B));
    }

    private static FormattedText Text(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
