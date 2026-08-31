using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Indexing;

public sealed class IndexCoordinator : IIndexCoordinator
{
    private readonly IIndexService _index;
    private readonly object _lock = new();
    private (LogSource Source, SearchRequest? Priority)? _pending;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _running;

    public IndexCoordinator(IIndexService index)
    {
        _index = index;
    }

    public bool IsRunning { get { lock (_lock) return _running; } }

    public event EventHandler<IndexCoordinationState>? StateChanged;

    public void Enqueue(LogSource source, SearchRequest? priorityRequest = null)
    {
        lock (_lock)
        {
            _pending = (source, priorityRequest);
            if (_worker is { IsCompleted: false })
            {
                return;
            }

            _worker = Task.Run(() => RunLoopAsync());
        }

        RaiseState(source, true);
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _cts;
        }

        cts?.Cancel();
        var worker = _worker;
        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            (LogSource Source, SearchRequest? Priority) job;
            lock (_lock)
            {
                if (_pending is null)
                {
                    _running = false;
                    _worker = null;
                    RaiseState(null, false);
                    return;
                }

                job = _pending.Value;
                _pending = null;
                _cts = new CancellationTokenSource();
                _running = true;
            }

            RaiseState(job.Source, true);
            try
            {
                await _index.IndexAsync(job.Source, job.Priority, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_lock)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }
    }

    private void RaiseState(LogSource? source, bool running)
    {
        EventHandler<IndexCoordinationState>? handler;
        lock (_lock)
        {
            handler = StateChanged;
        }

        handler?.Invoke(this, new IndexCoordinationState(running, source));
    }
}