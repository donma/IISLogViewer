using System.Diagnostics;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Files;
using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Logging;

namespace IISLogExplorer.Infrastructure.Indexing;

public sealed class SqliteIndexService : IIndexService
{
    private static readonly TimeSpan CheckpointTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogFileScanner _scanner;
    private readonly IIisLogParser _parser;
    private readonly LogFileRepository _files;
    private readonly LogEntryRepository _entries;
    private readonly FileFingerprintService _fingerprints;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IndexPlanner _planner = new();
    private readonly ISettingsService? _settingsService;
    private readonly AppLogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<IndexProgress>? ProgressChanged;

    public SqliteIndexService(ILogFileScanner scanner, IIisLogParser parser, LogFileRepository files, LogEntryRepository entries, FileFingerprintService fingerprints, SqliteConnectionFactory connectionFactory, ISettingsService? settingsService = null, AppLogger? logger = null)
    {
        _scanner = scanner;
        _parser = parser;
        _files = files;
        _entries = entries;
        _fingerprints = fingerprints;
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task IndexAsync(LogSource source, SearchRequest? priorityRequest = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await IndexCoreAsync(source, priorityRequest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task IndexCoreAsync(LogSource source, SearchRequest? priorityRequest, CancellationToken cancellationToken)
    {
        var total = 0L;
        var records = 0L;
        var stopwatch = Stopwatch.StartNew();
        var scanned = await _scanner.ScanFilesAsync(source, cancellationToken).ConfigureAwait(false);
        var candidates = _planner.Order(scanned, priorityRequest);
        total = candidates.Sum(file => file.Length);
        long completedBytes = 0;

        foreach (var scannedFile in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(scannedFile.FullName);
                var fingerprint = await _fingerprints.ComputeAsync(file, cancellationToken).ConfigureAwait(false);
                var state = await _files.UpsertAsync(source.Id, file, fingerprint, cancellationToken).ConfigureAwait(false);
                if (RequiresReset(state, file, fingerprint))
                {
                    IisW3cLogParser.InvalidateHeaderCache(file.FullName);
                    await _files.ResetAsync(state.Id, cancellationToken).ConfigureAwait(false);
                    state = state with { IndexedLength = 0, IndexedLineCount = 0, IsFullyIndexed = false };
                }

                if (state.IsFullyIndexed && state.IndexedLength >= file.Length)
                {
                    completedBytes += file.Length;
                    continue;
                }

                var batchSize = Math.Clamp(_settingsService?.Current.IndexBatchSize ?? 2000, 100, 10000);
                var batch = new List<LogEntry>(batchSize);
                var sawNewRecord = false;
                var lastOffset = state.IndexedLength;
                var lastLine = state.IndexedLineCount;
                await foreach (var record in _parser.ParseRecordsAsync(file.FullName, source.Id, state.Id, state.IndexedLength, state.IndexedLineCount, state.FieldsHeader, cancellationToken).ConfigureAwait(false))
                {
                    if (!record.IsCompleteLine)
                    {
                        continue;
                    }

                    sawNewRecord = true;
                    batch.Add(record.Entry);
                    lastOffset = record.EndByteOffset;
                    lastLine = record.Entry.LineNumber;
                    if (batch.Count < batchSize)
                    {
                        continue;
                    }

                    await _entries.InsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    records += batch.Count;
                    batch.Clear();
                    await SaveCheckpointSafelyAsync(state.Id, file, lastOffset, lastLine, fingerprint).ConfigureAwait(false);
                    ReportProgress(file.Name, total, completedBytes, lastOffset, records);
                    await Task.Yield();
                }

                if (batch.Count > 0)
                {
                    await _entries.InsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    records += batch.Count;
                }

                var finalFile = new FileInfo(file.FullName);
                var finalFingerprint = await _fingerprints.ComputeAsync(finalFile, CancellationToken.None).ConfigureAwait(false);
                var complete = lastOffset >= finalFile.Length || state.IndexedLineCount == 0 && !sawNewRecord && EndsWithNewLine(finalFile);
                var indexedLength = complete ? finalFile.Length : lastOffset;
                await _files.UpdateProgressAsync(state.Id, finalFile.Length, finalFile.LastWriteTimeUtc, indexedLength, lastLine, complete, finalFingerprint, IisW3cLogParser.GetActiveHeader(file.FullName), CancellationToken.None).ConfigureAwait(false);
                completedBytes += finalFile.Length;
                ReportProgress(file.Name, total, completedBytes, 0, records);
            }
            catch (FileNotFoundException)
            {
                if (_logger is not null) await _logger.LogAsync($"Index file deleted: {scannedFile.FullName}").ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException)
            {
                if (_logger is not null) await _logger.LogAsync($"Index directory missing: {scannedFile.FullName}").ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                if (_logger is not null) await _logger.LogAsync($"Index file unauthorized: {scannedFile.FullName}").ConfigureAwait(false);
            }
        }

        stopwatch.Stop();
        if (_logger is not null)
        {
            var seconds = Math.Max(0.0001, stopwatch.Elapsed.TotalSeconds);
            await _logger.LogAsync($"Index done; files={candidates.Count} bytes={total} records={records} elapsed={stopwatch.Elapsed.TotalSeconds:0.###}s records/sec={records / seconds:0} MB/sec={total / 1024d / 1024d / seconds:0.###}").ConfigureAwait(false);
        }

        ReportProgress(string.Empty, total, total, 0, records, isRunning: false);
    }

    private void ReportProgress(string fileName, long total, long completedBytes, long currentFileOffset, long records, bool isRunning = true)
    {
        var processed = Math.Min(total, completedBytes + currentFileOffset);
        ProgressChanged?.Invoke(this, new IndexProgress(fileName, processed, total, records, isRunning));
    }

    public Task<IReadOnlyList<LogFileInfo>> GetFileStatesAsync(LogSource source, CancellationToken cancellationToken = default) => _files.GetBySourceAsync(source.Id, cancellationToken);

    public async Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(_connectionFactory.DatabasePath);
        var indexedRecords = await _entries.CountAsync(cancellationToken).ConfigureAwait(false);
        var indexedFiles = await _files.CountAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(LastIndexedAt) FROM LogFiles";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? lastIndexed = value is string text && DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;
        return new DatabaseStats(fileInfo.Exists ? fileInfo.Length : 0, indexedRecords, indexedFiles, lastIndexed);
    }

    public async Task RebuildAsync(LogSource source, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var files = await _files.GetBySourceAsync(source.Id, cancellationToken).ConfigureAwait(false);
            foreach (var file in files)
            {
                await _files.ResetAsync(file.Id, cancellationToken).ConfigureAwait(false);
            }

            await IndexCoreAsync(source, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _entries.ClearAsync(cancellationToken).ConfigureAwait(false);
            IisW3cLogParser.InvalidateHeaderCache();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var optimize = connection.CreateCommand())
            {
                optimize.CommandText = "PRAGMA optimize;";
                await optimize.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CleanupAsync(int? retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays is null)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            // 只刪除過期 LogEntries；不重設 LogFiles checkpoint，
            // 否則下一輪 IndexAsync 會從 0 byte 重建，把已刪除的舊資料重新寫回（Retention 資料復活）。
            // 檔案 truncate / replace / rotation / prefix changed 仍由 RequiresReset() 與 fingerprint 判斷。
            command.CommandText = "DELETE FROM LogEntries WHERE TimestampUtc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-retentionDays.Value).ToString("O"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCheckpointSafelyAsync(long fileId, FileInfo file, long indexedLength, long lineNumber, string fingerprint)
    {
        using var timeout = new CancellationTokenSource(CheckpointTimeout);
        try
        {
            var current = new FileInfo(file.FullName);
            await _files.UpdateProgressAsync(fileId, current.Length, current.LastWriteTimeUtc, indexedLength, lineNumber, false, fingerprint, IisW3cLogParser.GetActiveHeader(file.FullName), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
        }
    }

    private static bool RequiresReset(LogFileInfo state, FileInfo file, string fingerprint)
    {
        if (file.Length < state.IndexedLength)
        {
            return true;
        }

        if (state.FileFingerprint is not null && !PrefixHash(state.FileFingerprint).Equals(PrefixHash(fingerprint), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return file.Length == state.FileSize && new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) != state.LastWriteUtc;
    }

    private static bool EndsWithNewLine(FileInfo file)
    {
        if (file.Length == 0)
        {
            return true;
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            stream.Seek(-1, SeekOrigin.End);
            return stream.ReadByte() == '\n';
        }
        catch
        {
            return false;
        }
    }

    private static string PrefixHash(string fingerprint) => fingerprint[(fingerprint.LastIndexOf('|') + 1)..];
}
