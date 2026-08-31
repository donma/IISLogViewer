using IISLogExplorer.Infrastructure.Logging;

namespace IISLogExplorer.App.Diagnostics;

public interface IErrorHandler
{
    void Handle(Exception exception);
}

public sealed class AppErrorHandler : IErrorHandler
{
    private readonly AppLogger _logger;

    public AppErrorHandler(AppLogger logger)
    {
        _logger = logger;
    }

    public void Handle(Exception exception)
    {
        _ = _logger.LogAsync("Command error", exception);
    }
}