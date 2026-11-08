using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GaugeView.Presentation;

/// <summary>Base class for view models: raises <see cref="PropertyChanged"/> so bindings update.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// An <see cref="ICommand"/> backed by delegates. It has no WPF dependency, so call
/// <see cref="RaiseCanExecuteChanged"/> when the answer to <see cref="CanExecute"/> may have changed.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>File pickers and message boxes, behind an interface so view models can be unit-tested.</summary>
public interface IDialogService
{
    /// <returns>The chosen path, or null if the user cancelled.</returns>
    string? PickFileToOpen(string title, string filter);

    /// <returns>The chosen path, or null if the user cancelled.</returns>
    string? PickFileToSave(string title, string filter, string suggestedName);

    void ShowError(string title, string message);
}
