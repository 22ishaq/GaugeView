using System.Windows;
using GaugeView.Presentation;
using Microsoft.Win32;

namespace GaugeView.Wpf.Services;

/// <summary>Windows file dialogs and message boxes for the running app.</summary>
public sealed class WpfDialogService : IDialogService
{
    public string? PickFileToOpen(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? PickFileToSave(string title, string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedName,
            DefaultExt = ".csv",
            AddExtension = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public void ShowError(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static bool Show(CommonDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
