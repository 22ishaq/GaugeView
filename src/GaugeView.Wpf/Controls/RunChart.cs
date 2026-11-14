using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GaugeView.Core;
using GaugeView.Presentation;
using GaugeView.Wpf.Converters;

namespace GaugeView.Wpf.Controls;

/// <summary>
/// Draws an individuals control chart: readings in production order, the 3σ control limits, the centre line
/// and the drawing's tolerance limits. Rendering only; the axis scaling comes from <see cref="ChartAxis"/>,
/// which is unit-tested. Hover over a point to see its part, reading and any rules it broke.
/// </summary>
public sealed class RunChart : FrameworkElement
{
    public static readonly DependencyProperty ChartProperty = DependencyProperty.Register(
        nameof(Chart), typeof(ControlChart), typeof(RunChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(RunChart),
        new FrameworkPropertyMetadata("mm", FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 72;
    private const double RightMargin = 112;
    private const double TopMargin = 40;
    private const double BottomMargin = 46;
    private const double PointRadius = 4;
    private const double SignalRadius = 8;

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x2B, 0x33, 0x3F));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x5F, 0x6B, 0x7A));
    private static readonly Brush ControlBrush = Frozen(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0xEC, 0xEE, 0xF1), 1);
    private static readonly Pen FramePen = FrozenPen(Color.FromRgb(0xC5, 0xCA, 0xD3), 1);
    private static readonly Pen SpecPen = FrozenPen(Color.FromRgb(0xC6, 0x28, 0x28), 1.5);
    private static readonly Pen ControlPen = FrozenPen(Color.FromRgb(0x15, 0x65, 0xC0), 1.2, DashStyles.Dash);
    private static readonly Pen MeanPen = FrozenPen(Color.FromRgb(0x15, 0x65, 0xC0), 1);
    private static readonly Pen LinePen = FrozenPen(Color.FromRgb(0x90, 0xA4, 0xAE), 1.3);
    private static readonly Pen SignalPen = FrozenPen(Color.FromRgb(0xD9, 0x82, 0x00), 2);
    private static readonly Pen PointOutline = FrozenPen(Colors.White, 1);

    private readonly List<(Point Centre, ChartPoint Point)> hitTargets = [];
    private readonly ToolTip tip = new() { Placement = PlacementMode.Relative };

    public ControlChart? Chart
    {
        get => (ControlChart?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        hitTargets.Clear();
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.White, null, bounds);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (Chart is not { } chart || chart.Points.Count == 0)
        {
            var empty = Text("No readings for this feature yet.", 13, Muted, dpi);
            dc.DrawText(empty, new Point((bounds.Width - empty.Width) / 2, (bounds.Height - empty.Height) / 2));
            return;
        }

        var plot = new Rect(
            LeftMargin,
            TopMargin,
            Math.Max(1, bounds.Width - LeftMargin - RightMargin),
            Math.Max(1, bounds.Height - TopMargin - BottomMargin));
        var axis = ChartAxis.For(chart);
        double Y(double value) => plot.Bottom - axis.ToFraction(value) * plot.Height;
        double X(int index) => chart.PartCount <= 1
            ? plot.Left + plot.Width / 2
            : plot.Left + index * plot.Width / (chart.PartCount - 1);

        DrawLegend(dc, plot, dpi);

        // Value grid and labels.
        foreach (var tick in axis.Ticks)
        {
            var y = Snap(Y(tick));
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(axis.Format(tick), 11, Muted, dpi);
            dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        var unit = Text(Unit, 11, Muted, dpi);
        dc.DrawText(unit, new Point(plot.Left - 8 - unit.Width, plot.Top - unit.Height - 6));

        // Part numbers along the bottom, thinned out so they never overlap.
        var every = NiceEvery(chart.PartCount, plot.Width);
        for (var i = 0; i < chart.PartCount; i++)
        {
            var number = i + 1;
            if (number != 1 && number % every != 0)
                continue;

            var x = Snap(X(i));
            dc.DrawLine(FramePen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            var label = Text(number.ToString(CultureInfo.CurrentCulture), 11, Muted, dpi);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 6));
        }

        var xTitle = Text("Part, in production order", 11, Muted, dpi);
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 24));

        // Reference lines: tolerance limits (what the drawing allows) and control limits (what the process does).
        var tolerance = chart.Feature.Tolerance;
        var lines = new List<(double Value, string Name, Pen Pen, Brush Brush)>
        {
            ((double)tolerance.UpperLimit, "USL", SpecPen, SpecPen.Brush),
            ((double)tolerance.LowerLimit, "LSL", SpecPen, SpecPen.Brush),
        };
        if (chart.HasControlLimits)
        {
            lines.Add((chart.UpperControlLimit!.Value, "UCL", ControlPen, ControlBrush));
            lines.Add((chart.LowerControlLimit!.Value, "LCL", ControlPen, ControlBrush));
        }

        if (chart.Mean is { } mean)
            lines.Add((mean, "X̄", MeanPen, ControlBrush));

        foreach (var line in lines)
        {
            var y = Snap(Y(line.Value));
            dc.DrawLine(line.Pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        DrawLineLabels(dc, plot, lines.Select(l => (Y(l.Value), $"{l.Name} {Format(l.Value, axis)}", l.Brush)), dpi);
        dc.DrawRectangle(null, FramePen, plot);

        // The readings, joined in order, then the points on top.
        var centres = chart.Points.Select(p => new Point(X(p.PartIndex), Y((double)p.Value))).ToList();
        if (centres.Count > 1)
        {
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(centres[0], isFilled: false, isClosed: false);
                context.PolyLineTo(centres.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
            }

            path.Freeze();
            dc.DrawGeometry(null, LinePen, path);
        }

        for (var i = 0; i < chart.Points.Count; i++)
        {
            var point = chart.Points[i];
            if (point.HasSignal)
                dc.DrawEllipse(null, SignalPen, centres[i], SignalRadius, SignalRadius);

            dc.DrawEllipse(StatusToBrushConverter.BrushFor(point.Status), PointOutline, centres[i], PointRadius, PointRadius);
            hitTargets.Add((centres[i], point));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        var nearest = hitTargets
            .Select(t => (t.Centre, t.Point, Distance: (t.Centre - position).Length))
            .Where(t => t.Distance <= SignalRadius + 2)
            .OrderBy(t => t.Distance)
            .FirstOrDefault();

        if (nearest.Point is null)
        {
            tip.IsOpen = false;
            return;
        }

        tip.PlacementTarget = this;
        tip.HorizontalOffset = nearest.Centre.X + 12;
        tip.VerticalOffset = nearest.Centre.Y + 12;
        tip.Content = Describe(nearest.Point);
        tip.IsOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        tip.IsOpen = false;
    }

    private string Describe(ChartPoint point)
    {
        var lines = new List<string>
        {
            $"Part {point.PartNumber} · {point.Serial}",
            $"{point.Value.ToString(CultureInfo.CurrentCulture)} {Unit} · {point.Status.ToString().ToUpperInvariant()}",
        };
        lines.AddRange(point.Rules.Select(rule => $"Rule {(int)rule}: {SpcSignal.Describe(rule)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static void DrawLegend(DrawingContext dc, Rect plot, double dpi)
    {
        var x = plot.Left;
        var y = plot.Top - 26;

        void Item(Action<Point> glyph, string text)
        {
            glyph(new Point(x, y + 8));
            var label = Text(text, 11, Muted, dpi);
            dc.DrawText(label, new Point(x + 22, y + 8 - label.Height / 2));
            x += 22 + label.Width + 18;
        }

        Item(p => dc.DrawEllipse(StatusToBrushConverter.PassBrush, PointOutline, new Point(p.X + 8, p.Y), PointRadius, PointRadius), "Reading");
        Item(p => dc.DrawEllipse(null, SignalPen, new Point(p.X + 8, p.Y), SignalRadius - 1, SignalRadius - 1), "Rule broken");
        Item(p => dc.DrawLine(SpecPen, new Point(p.X, p.Y), new Point(p.X + 16, p.Y)), "Tolerance limits");
        Item(p => dc.DrawLine(ControlPen, new Point(p.X, p.Y), new Point(p.X + 16, p.Y)), "Control limits (3σ)");
        Item(p => dc.DrawLine(MeanPen, new Point(p.X, p.Y), new Point(p.X + 16, p.Y)), "Mean");
    }

    // Labels sit to the right of the plot; when two lines are close, the labels are pushed apart.
    private static void DrawLineLabels(DrawingContext dc, Rect plot, IEnumerable<(double Y, string Text, Brush Brush)> labels, double dpi)
    {
        const double gap = 15;
        var placed = labels.OrderBy(l => l.Y).Select(l => (l.Y, Label: Text(l.Text, 11, l.Brush, dpi), Wanted: l.Y)).ToList();

        for (var i = 1; i < placed.Count; i++)
        {
            if (placed[i].Y - placed[i - 1].Y >= gap)
                continue;

            var moved = placed[i];
            moved.Y = placed[i - 1].Y + gap;
            placed[i] = moved;
        }

        foreach (var (y, label, wanted) in placed)
        {
            if (Math.Abs(y - wanted) > 0.5)
                dc.DrawLine(FramePen, new Point(plot.Right + 1, wanted), new Point(plot.Right + 5, y));
            dc.DrawText(label, new Point(plot.Right + 7, y - label.Height / 2));
        }
    }

    private static string Format(double value, ChartAxis axis) =>
        value.ToString("F" + (axis.Decimals + 1), CultureInfo.CurrentCulture);

    // Label every 1, 2, 5, 10, 20, 50 … parts so labels are at least ~36 px apart.
    private static int NiceEvery(int count, double width)
    {
        if (count <= 1)
            return 1;

        var spacing = width / (count - 1);
        foreach (var every in new[] { 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000 })
        {
            if (every * spacing >= 36)
                return every;
        }

        return 1000;
    }

    private static double Snap(double value) => Math.Round(value) + 0.5;

    private static FormattedText Text(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(Frozen(color), thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }
}
