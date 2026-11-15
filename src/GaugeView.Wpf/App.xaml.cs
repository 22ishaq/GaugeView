using System.Data.Common;
using System.IO;
using System.Windows;
using GaugeView.Core.History;
using GaugeView.Data;
using GaugeView.Presentation;
using GaugeView.Wpf.Services;

namespace GaugeView.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root: the one place that picks real implementations for the view models' interfaces.
        var dialogs = new WpfDialogService();
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "samples");

        // CI renders the README screenshots with: GaugeView.exe --screenshots <folder>
        if (e.Args is ["--screenshots", var folder])
        {
            RenderScreenshots(dialogs, sampleDirectory, folder);
            return;
        }

        var historyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaugeView", "history.db");
        var viewModel = new MainViewModel(dialogs, sampleDirectory, OpenHistory(historyPath, dialogs));
        if (viewModel.LoadSampleCommand.CanExecute(null))
            viewModel.LoadSampleCommand.Execute(null);

        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();
    }

    private static IInspectionHistory? OpenHistory(string path, IDialogService dialogs)
    {
        try
        {
            return new SqliteInspectionStore(path);
        }
        catch (Exception ex) when (ex is DbException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            dialogs.ShowError(
                "Inspection history is unavailable",
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}GaugeView will run, but inspections can't be saved.");
            return null;
        }
    }

    private void RenderScreenshots(IDialogService dialogs, string sampleDirectory, string folder)
    {
        // A throwaway history, so screenshots never touch the real one.
        var historyPath = Path.Combine(Path.GetTempPath(), $"gaugeview-screenshots-{Guid.NewGuid():N}.db");
        try
        {
            var history = new SqliteInspectionStore(historyPath);
            ScreenshotRenderer.SeedHistory(history, sampleDirectory);

            var viewModel = new MainViewModel(dialogs, sampleDirectory, history);
            viewModel.LoadSampleCommand.Execute(null);
            ScreenshotRenderer.RenderAll(viewModel, folder);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
            Shutdown(1);
        }
        finally
        {
            File.Delete(historyPath);
        }
    }
}
