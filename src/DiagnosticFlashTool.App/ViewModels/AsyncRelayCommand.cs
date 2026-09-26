using System.Windows.Input;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>True while the handler is running.</summary>
    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        // Created per run so a cancel request can never leak into the next invocation.
        using var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            await _execute(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A cancelled operation is reported by the handler itself, not an error.
        }
        catch (Exception ex)
        {
            CommandErrorHandler.Report(ex);
        }
        finally
        {
            _cts = null;
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Requests cancellation of the running handler. Does nothing when idle.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run completed between reading the field and cancelling it.
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
