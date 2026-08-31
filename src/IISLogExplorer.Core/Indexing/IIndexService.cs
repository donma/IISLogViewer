using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Indexing;

public interface IIndexService
{
    event EventHandler<IndexProgress>? ProgressChanged;
    Task IndexAsync(LogSource source, SearchRequest? priorityRequest = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LogFileInfo>> GetFileStatesAsync(LogSource source, CancellationToken cancellationToken = default);
    Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task RebuildAsync(LogSource source, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task OptimizeAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupAsync(int? retentionDays, CancellationToken cancellationToken = default);
}

public sealed record IndexProgress(string FileName, long ProcessedBytes, long TotalBytes, long IndexedRecords, bool IsRunning)
{
    public double Percentage => TotalBytes <= 0 ? 0 : Math.Min(100, ProcessedBytes * 100d / TotalBytes);
}

public sealed record DatabaseStats(long SizeBytes, long IndexedRecords, int IndexedFiles, DateTimeOffset? LastIndexed);
