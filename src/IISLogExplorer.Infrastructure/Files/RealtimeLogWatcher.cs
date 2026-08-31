using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Core.Realtime;

namespace IISLogExplorer.Infrastructure.Files;

public sealed class RealtimeLogWatcher : IRealtimeMonitor
{
    private readonly IisW3cLogParser _parser;
    private readonly LogFileScanner _scanner;
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, (long Offset, long Line)> _positions = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private LogSource? _source;
    public bool IsRunning => _worker is { IsCompleted: false };
    public event EventHandler<IReadOnlyList<LogEntry>>? EntriesAdded;

    public RealtimeLogWatcher(IisW3cLogParser parser, LogFileScanner scanner, ISettingsService settings)
    {
        _parser = parser; _scanner = scanner; _settings = settings;
    }

    public async Task StartAsync(LogSource source, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        _source = source;
        _positions.Clear();
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = RunAsync(_stop.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stop is null) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_worker is not null)
        {
            try { await _worker.WaitAsync(cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _stop.Dispose(); _stop = null; _worker = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _source is not null)
        {
            var added = new List<LogEntry>();
            IReadOnlyList<FileInfo> files;
            try
            {
                files = await _scanner.ScanFilesAsync(_source, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.RealtimeRefreshIntervalSeconds)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.RealtimeRefreshIntervalSeconds)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                activePaths.Add(file.FullName);
                if (!_positions.TryGetValue(file.FullName, out var position))
                {
                    _positions[file.FullName] = (file.Length, 0);
                    continue;
                }

                if (file.Length < position.Offset)
                {
                    _positions[file.FullName] = (file.Length, 0);
                    continue;
                }

                var lastOffset = position.Offset;
                var lastLine = position.Line;
                await foreach (var record in _parser.ParseRecordsAsync(file.FullName, _source.Id, 0, lastOffset, lastLine, cancellationToken).ConfigureAwait(false))
                {
                    if (!record.IsCompleteLine)
                    {
                        continue;
                    }

                    added.Add(record.Entry);
                    lastOffset = record.EndByteOffset;
                    lastLine = record.Entry.LineNumber;
                }

                _positions[file.FullName] = (lastOffset, lastLine);
            }

            foreach (var stale in _positions.Keys.Where(path => !activePaths.Contains(path)).ToArray())
            {
                _positions.Remove(stale);
            }

            if (added.Count > 0) EntriesAdded?.Invoke(this, added);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.RealtimeRefreshIntervalSeconds)), cancellationToken).ConfigureAwait(false);
        }
    }
}
