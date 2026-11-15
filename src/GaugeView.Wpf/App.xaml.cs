using System.IO;
using System.Windows;
using GaugeView.Presentation;
using GaugeView.Wpf.Services;

namespace GaugeView.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root: the one place that wires WPF services into the view models.
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "samples");
        var viewModel = new MainViewModel(new WpfDialogService(), sampleDirectory);
        if (viewModel.LoadSampleCommand.CanExecute(null))
            viewModel.LoadSampleCommand.Execute(null);

        // CI renders the README screenshots with: GaugeView.exe --screenshots <folder>
        if (e.Args is ["--screenshots", var folder])
        {
            RenderScreenshots(viewModel, folder);
            return;
        }

        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();
    }

    private void RenderScreenshots(MainViewModel viewModel, string folder)
    {
        try
        {
            ScreenshotRenderer.RenderAll(viewModel, folder);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
            Shutdown(1);
        }
    }
}
