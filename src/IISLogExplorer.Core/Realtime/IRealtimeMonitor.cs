using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Realtime;

public interface IRealtimeMonitor : IAsyncDisposable
{
    bool IsRunning { get; }
    event EventHandler<IReadOnlyList<LogEntry>>? EntriesAdded;
    Task StartAsync(LogSource source, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
