using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Indexing;

public sealed record IndexCoordinationState(bool IsRunning, LogSource? Source);

public interface IIndexCoordinator
{
    bool IsRunning { get; }
    event EventHandler<IndexCoordinationState>? StateChanged;
    void Enqueue(LogSource source, SearchRequest? priorityRequest = null);
    Task CancelAsync(CancellationToken cancellationToken = default);
}