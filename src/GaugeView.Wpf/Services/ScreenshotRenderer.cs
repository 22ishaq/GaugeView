using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GaugeView.Core;
using GaugeView.Core.History;
using GaugeView.Core.IO;
using GaugeView.Presentation;

namespace GaugeView.Wpf.Services;

/// <summary>
/// Renders each tab of the main window to a PNG for the README: <c>GaugeView.exe --screenshots &lt;folder&gt;</c>.
/// CI runs this on Windows. The window's content is laid out off-screen at a fixed size, so the images don't
/// depend on the build machine's screen resolution.
/// </summary>
internal static class ScreenshotRenderer
{
    private const double Width = 1320;
    private const double Height = 820;
    private const double TitleBarHeight = 32;
    private const double Scale = 1.5;

    /// <summary>
    /// Saves the sample as three batches of ten parts, each measured by a different operator on a different day,
    /// so the History tab has something to show. The bore's drift shows up in the batch means.
    /// </summary>
    public static void SeedHistory(IInspectionHistory history, string sampleDirectory)
    {
        var plan = InspectionPlanReader.ReadFile(Path.Combine(sampleDirectory, MainViewModel.SamplePlanFile));
        var parts = new Inspector(plan, new ToleranceChecker())
            .InspectAll(MeasurementCsvReader.ReadFile(Path.Combine(sampleDirectory, MainViewModel.SampleMeasurementsFile)));

        var batches = new (string Batch, string Operator, DateTimeOffset At)[]
        {
            ("2611-A", "Ali", new DateTimeOffset(2026, 11, 9, 7, 40, 0, TimeSpan.Zero)),
            ("2611-B", "Beth", new DateTimeOffset(2026, 11, 10, 13, 15, 0, TimeSpan.Zero)),
            ("2611-C", "Calum", new DateTimeOffset(2026, 11, 11, 8, 5, 0, TimeSpan.Zero)),
        };

        for (var i = 0; i < batches.Length; i++)
        {
            var (batch, appraiser, at) = batches[i];
            history.Save(plan, parts.Skip(i * 10).Take(10).ToList(), appraiser, batch, ToleranceChecker.DefaultWarningThreshold, at);
        }
    }

    public static void RenderAll(MainViewModel viewModel, string folder)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Directory.CreateDirectory(folder);

        var window = new MainWindow { DataContext = viewModel };
        var root = (FrameworkElement)window.Content;
        window.Content = null;

        // Inherited properties the window would normally pass down.
        var host = new Border { Background = window.Background, Child = root, DataContext = viewModel };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);

        foreach (var shot in Shots(viewModel))
        {
            shot.Prepare();
            Layout(host);
            ScrollSelectionIntoView(host);
            Layout(host);
            Save(host, window.Title, Path.Combine(folder, shot.File));
        }
    }

    private static IEnumerable<(string File, Action Prepare)> Shots(MainViewModel vm)
    {
        // A part with both an amber and a red feature.
        yield return ("inspection.png", () =>
        {
            vm.SelectedTab = 0;
            vm.SelectedPart = vm.Parts.FirstOrDefault(p => p.Serial == "BRK-0005") ?? vm.Parts.FirstOrDefault();
        });

        // The bore drifting out of control as the boring tool wears.
        yield return ("spc-chart.png", () =>
        {
            vm.SelectedTab = 1;
            vm.Spc.SelectedChart = vm.Spc.Charts.FirstOrDefault(c => c.Feature.Name.StartsWith("Bore", StringComparison.Ordinal))
                ?? vm.Spc.Charts.FirstOrDefault();
        });

        // The sample bore gauge study: marginal, with one operator reading high.
        yield return ("gauge-rr.png", () => vm.SelectedTab = 2);

        // Three saved batches, with the bore's mean falling batch by batch.
        yield return ("history.png", () =>
        {
            vm.SelectedTab = 3;
            vm.History.SelectedRecord = vm.History.Records.FirstOrDefault();
            vm.History.OperatorName = "Ali";
        });
    }

    private static void Layout(FrameworkElement element)
    {
        var size = new Size(Width, Height);
        for (var pass = 0; pass < 3; pass++)
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    private static void ScrollSelectionIntoView(DependencyObject root)
    {
        foreach (var list in Descendants<ListBox>(root))
        {
            if (list.SelectedItem is { } item)
                list.ScrollIntoView(item);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    // Draws a plain title bar above the content so the image reads as a desktop window.
    private static void Save(FrameworkElement content, string title, string path)
    {
        var total = new Size(Width, Height + TitleBarHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(total));
            var titleText = new FormattedText(
                title, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(0x2B, 0x33, 0x3F)), 1.0);
            dc.DrawText(titleText, new Point(14, (TitleBarHeight - titleText.Height) / 2));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, TitleBarHeight, Width, Height));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xC5, 0xCA, 0xD3)), 1),
                new Rect(0.5, 0.5, total.Width - 1, total.Height - 1));
        }

        var bitmap = new RenderTargetBitmap(
            (int)(total.Width * Scale), (int)(total.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
