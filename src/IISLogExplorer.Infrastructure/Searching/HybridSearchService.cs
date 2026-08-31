using System.Runtime.CompilerServices;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Core.Searching;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;

namespace IISLogExplorer.Infrastructure.Searching;

public sealed class HybridSearchService : ISearchService
{
    private readonly LogFileScanner _scanner;
    private readonly IIisLogParser _parser;
    private readonly LogFileRepository _files;
    private readonly LogEntryRepository _entries;
    private readonly FileFingerprintService _fingerprints;

    public HybridSearchService(LogFileScanner scanner, IIisLogParser parser, LogFileRepository files, LogEntryRepository entries, FileFingerprintService fingerprints)
    {
        _scanner = scanner;
        _parser = parser;
        _files = files;
        _entries = entries;
        _fingerprints = fingerprints;
    }

    public async IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.MaxResults <= 0)
        {
            yield break;
        }

        var diskFiles = await _scanner.ScanFilesAsync(request.Source, cancellationToken).ConfigureAwait(false);
        var states = await _files.GetBySourceAsync(request.Source.Id, cancellationToken).ConfigureAwait(false);
        var stateByPath = states.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
        var rawFiles = new List<(FileInfo File, LogFileInfo? State)>();
        var indexedFileIds = new List<long>();

        foreach (var file in diskFiles)
        {
            stateByPath.TryGetValue(file.FullName, out var state);
            if (state is not null && state.IndexedLength > 0 && await IsStateUsableAsync(file, state, cancellationToken).ConfigureAwait(false))
            {
                indexedFileIds.Add(state.Id);
                if (state.IsFullyIndexed && state.IndexedLength >= file.Length)
                {
                    continue;
                }

                rawFiles.Add((file, state));
                continue;
            }

            rawFiles.Add((file, null));
        }

        var candidates = new PriorityQueue<SearchResult, SearchOrderKey>();
        var retainedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var result in ScanRawAsync(rawFiles, request, cancellationToken).ConfigureAwait(false))
        {
            AddCandidate(result, request.MaxResults, candidates, retainedKeys);
        }

        await foreach (var result in _entries.SearchAsync(request, indexedFileIds, cancellationToken).ConfigureAwait(false))
        {
            AddCandidate(result, request.MaxResults, candidates, retainedKeys);
        }

        var ordered = new List<SearchResult>(candidates.Count);
        while (candidates.TryDequeue(out var result, out _))
        {
            ordered.Add(result);
        }

        ordered.Sort(static (left, right) => OrderKey(right).CompareTo(OrderKey(left)));
        foreach (var result in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return result;
        }
    }

    public async Task<SearchStatistics> GetStatisticsAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        long count = 0;
        await foreach (var _ in SearchAsync(request, cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return new SearchStatistics { ResultCount = count, ScannedLines = count, IsComplete = true };
    }

    private async Task<bool> IsStateUsableAsync(FileInfo file, LogFileInfo state, CancellationToken cancellationToken)
    {
        if (file.Length < state.IndexedLength)
        {
            return false;
        }

        if (state.FileFingerprint is null)
        {
            return true;
        }

        var fingerprint = await _fingerprints.ComputeAsync(file, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(PrefixHash(state.FileFingerprint), PrefixHash(fingerprint), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return file.Length > state.FileSize || new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) == state.LastWriteUtc;
    }

    private static void AddCandidate(SearchResult result, int maxResults, PriorityQueue<SearchResult, SearchOrderKey> candidates, HashSet<string> retainedKeys)
    {
        var key = Key(result);
        if (retainedKeys.Contains(key))
        {
            return;
        }

        var orderKey = OrderKey(result);
        if (candidates.Count < maxResults)
        {
            candidates.Enqueue(result, orderKey);
            retainedKeys.Add(key);
            return;
        }

        candidates.TryPeek(out _, out var worstKey);
        if (orderKey.CompareTo(worstKey) <= 0)
        {
            return;
        }

        candidates.TryDequeue(out var removed, out _);
        retainedKeys.Remove(Key(removed!));
        candidates.Enqueue(result, orderKey);
        retainedKeys.Add(key);
    }

    private static SearchOrderKey OrderKey(SearchResult result) => new(result.Entry.TimestampUtc, result.Entry.Id, result.Entry.LineNumber);

    private static string Key(SearchResult result) => $"{result.SourcePath ?? result.SourceFile}|{result.Entry.LineNumber}";

    private async IAsyncEnumerable<SearchResult> ScanRawAsync(
        IEnumerable<(FileInfo File, LogFileInfo? State)> files,
        SearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var (file, state) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var result in ParseFileAsync(file, request, state?.IndexedLength ?? 0, state?.IndexedLineCount ?? 0, cancellationToken).ConfigureAwait(false))
            {
                yield return result;
            }
        }
    }

    private async IAsyncEnumerable<SearchResult> ParseFileAsync(FileInfo file, SearchRequest request, long start, long line, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = _parser.ParseAsync(file.FullName, request.Source.Id, 0, start, line, IisW3cLogParser.GetActiveHeader(file.FullName), cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                LogEntry entry;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    entry = enumerator.Current;
                }
                catch (FileNotFoundException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }

                if (SearchPredicate.Matches(entry, request))
                {
                    yield return new SearchResult { Entry = entry, SourceFile = file.Name, SourcePath = file.FullName, IsIndexed = false };
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string PrefixHash(string fingerprint) => fingerprint[(fingerprint.LastIndexOf('|') + 1)..];

    private readonly record struct SearchOrderKey(DateTimeOffset? TimestampUtc, long Id, long LineNumber) : IComparable<SearchOrderKey>
    {
        public int CompareTo(SearchOrderKey other)
        {
            var timestamp = Nullable.Compare(TimestampUtc, other.TimestampUtc);
            if (timestamp != 0)
            {
                return timestamp;
            }

            var id = Id.CompareTo(other.Id);
            return id != 0 ? id : LineNumber.CompareTo(other.LineNumber);
        }
    }
}