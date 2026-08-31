using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Core.Realtime;
using IISLogExplorer.Infrastructure.Database;

namespace IISLogExplorer.Infrastructure.Files;

public sealed class RealtimeLogWatcher : IRealtimeMonitor
{
    private readonly IisW3cLogParser _parser;
    private readonly LogFileScanner _scanner;
    private readonly ISettingsService _settings;
    private readonly LogFileRepository _files;
    private readonly Dictionary<string, (long Offset, long Line)> _positions = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private LogSource? _source;
    public bool IsRunning => _worker is { IsCompleted: false };
    public event EventHandler<IReadOnlyList<LogEntry>>? EntriesAdded;

    public RealtimeLogWatcher(IisW3cLogParser parser, LogFileScanner scanner, ISettingsService settings, LogFileRepository files)
    {
        _parser = parser; _scanner = scanner; _settings = settings; _files = files;
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
                    // 新發現檔案：定位到最後完整換行之後，並以已索引行號為基準（避免重掃巨大檔案、避免 append 後行號從 1 開始）
                    position = await GetAttachPositionAsync(file, cancellationToken).ConfigureAwait(false);
                    _positions[file.FullName] = position;
                    continue;
                }

                if (file.Length < position.Offset)
                {
                    // truncate / rotate / recreate：安全重置並失效舊 header cache
                    IisW3cLogParser.InvalidateHeaderCache(file.FullName);
                    _positions[file.FullName] = await GetAttachPositionAsync(file, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var lastOffset = position.Offset;
                var lastLine = position.Line;
                await foreach (var record in _parser.ParseRecordsAsync(file.FullName, _source.Id, 0, lastOffset, lastLine, IisW3cLogParser.GetActiveHeader(file.FullName), cancellationToken).ConfigureAwait(false))
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
                IisW3cLogParser.InvalidateHeaderCache(stale);
                _positions.Remove(stale);
            }

            if (added.Count > 0) EntriesAdded?.Invoke(this, added);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.RealtimeRefreshIntervalSeconds)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 決定初次 attach 某檔案的讀取位置。
    /// 優先使用已索引的 checkpoint（IndexedLength/IndexedLineCount），
    /// 否則從檔案尾端往前定位到最後完整換行，並在單次順向掃描中同時取得正確行號。
    /// </summary>
    private async Task<(long Offset, long Line)> GetAttachPositionAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (_source is not null)
        {
            try
            {
                var state = await _files.FindByPathAsync(_source.Id, file.FullName, cancellationToken).ConfigureAwait(false);
                if (state is not null && state.IndexedLength > 0 && state.IndexedLength <= file.Length)
                {
                    return (state.IndexedLength, state.IndexedLineCount);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 資料庫暫時不可用時退回檔案尾端定位，不影響 realtime 基本功能
            }
        }

        return await FindTailPositionAsync(file.FullName, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(long Offset, long Line)> FindTailPositionAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        long lastCompleteEnd = 0;
        long lineCount = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    lineCount++;
                    lastCompleteEnd = stream.Position - (read - index) + 1;
                }
            }
        }

        return (lastCompleteEnd, lineCount);
    }
}
