using System.Windows.Input;

namespace PharmaERP.Desktop.Common;

/// <summary>
/// Asynchronous ICommand implementation that handles async execution without blocking the UI thread,
/// prevents re-entrancy while executing, and safely handles exceptions.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly Action<Exception>? _onException;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null,
        Action<Exception>? onException = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onException = onException;
    }

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
        : this((_, ct) => execute(), canExecute != null ? _ => canExecute() : null, onException)
    {
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            _isExecuting = value;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsExecuting = true;
        _cts = new CancellationTokenSource();

        try
        {
            await _execute(parameter, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            _onException?.Invoke(ex);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsExecuting = false;
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }
}

