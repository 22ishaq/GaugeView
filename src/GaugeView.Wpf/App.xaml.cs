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
        var sampleDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "samples");
        var viewModel = new MainViewModel(new WpfDialogService(), sampleDirectory);
        if (viewModel.LoadSampleCommand.CanExecute(null))
            viewModel.LoadSampleCommand.Execute(null);

        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();
    }
}
