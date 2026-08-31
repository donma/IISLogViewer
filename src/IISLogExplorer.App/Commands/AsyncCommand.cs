using System.Windows.Input;
using IISLogExplorer.App.Diagnostics;

namespace IISLogExplorer.App.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly IErrorHandler? _errorHandler;
    private bool _running;

    public static IErrorHandler? DefaultHandler { get; set; }

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, IErrorHandler? errorHandler = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _errorHandler = errorHandler ?? DefaultHandler;
    }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);
    public event EventHandler? CanExecuteChanged;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _ = ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _errorHandler?.Handle(exception);
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}