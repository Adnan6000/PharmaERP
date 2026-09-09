using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace PharmaERP.Desktop.Common;

/// <summary>
/// Lightweight synchronous ICommand implementation.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// Notifies the command that its execution status may have changed.
    /// Kept as an instance method to align with standard MVVM command contracts and usage.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance API conforms to standard MVVM ICommand contract patterns where callers invoke RaiseCanExecuteChanged on specific command references.")]
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

