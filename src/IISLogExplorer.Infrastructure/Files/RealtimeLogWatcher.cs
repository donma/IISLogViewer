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
    private readonly Dictionary<string, RealtimeFilePosition> _positions = new(StringComparer.OrdinalIgnoreCase);
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
                    position = await GetAttachPositionAsync(file, cancellationToken).ConfigureAwait(false);
                    _positions[file.FullName] = position;
                    continue;
                }

                if (file.Length < position.Offset)
                {
                    IisW3cLogParser.InvalidateHeaderCache(file.FullName);
                    _positions[file.FullName] = await GetAttachPositionAsync(file, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var lastOffset = position.Offset;
                var lastLine = position.LineNumber;
                var header = position.FieldsHeader ?? IisW3cLogParser.GetActiveHeader(file.FullName);
                await foreach (var record in _parser.ParseRecordsAsync(file.FullName, _source.Id, 0, lastOffset, lastLine, header, cancellationToken).ConfigureAwait(false))
                {
                    if (!record.IsCompleteLine)
                    {
                        continue;
                    }

                    added.Add(record.Entry);
                    lastOffset = record.EndByteOffset;
                    lastLine = record.Entry.LineNumber;
                }

                var activeHeader = IisW3cLogParser.GetActiveHeader(file.FullName) ?? header;
                _positions[file.FullName] = new RealtimeFilePosition(lastOffset, lastLine, activeHeader);
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
    /// 優先使用 DB 已持久化的 checkpoint（IndexedLength / IndexedLineCount / FieldsHeader），
    /// 避免重啟後重掃巨大檔案；無 DB state 時改用尾部 backward scan，不掃完整檔案。
    /// </summary>
    private async Task<RealtimeFilePosition> GetAttachPositionAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (_source is not null)
        {
            try
            {
                var state = await _files.FindByPathAsync(_source.Id, file.FullName, cancellationToken).ConfigureAwait(false);
                if (state is not null && state.IndexedLength > 0 && state.IndexedLength <= file.Length)
                {
                    return new RealtimeFilePosition(state.IndexedLength, state.IndexedLineCount, state.FieldsHeader);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        var tail = await FindTailFromEndAsync(file.FullName, cancellationToken).ConfigureAwait(false);
        return new RealtimeFilePosition(tail.Offset, -1, null);
    }

    /// <summary>
    /// 從檔案尾端向前掃描，找最後一個完整 newline 之後的位置；不計算 absolute line number，
    /// 避免對未索引的巨大 log 做 full scan。回傳實際讀取的 byte 數以便測試/診斷驗證。
    /// </summary>
    internal static async Task<(long Offset, long BytesRead)> FindTailFromEndAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
        var length = stream.Length;
        if (length == 0)
        {
            return (0, 0);
        }

        var buffer = new byte[64 * 1024];
        var offset = length;
        long bytesRead = 0;
        var position = length;
        while (position > 0)
        {
            var chunkSize = (int)Math.Min(buffer.Length, position);
            position -= chunkSize;
            stream.Seek(position, SeekOrigin.Begin);
            var read = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken).ConfigureAwait(false);
            bytesRead += read;
            for (var index = chunkSize - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    return (position + index + 1, bytesRead);
                }
            }
        }

        return (0, bytesRead);
    }
}

internal sealed record RealtimeFilePosition(long Offset, long LineNumber, string? FieldsHeader);