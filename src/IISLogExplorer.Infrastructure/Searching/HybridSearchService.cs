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
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    public HybridSearchService(LogFileScanner scanner, IIisLogParser parser, LogFileRepository files, LogEntryRepository entries, FileFingerprintService fingerprints)
    {
        _scanner = scanner;
        _parser = parser;
        _files = files;
        _entries = entries;
        _fingerprints = fingerprints;
    }

    public async IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _searchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

            var yielded = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await foreach (var result in ScanRawAsync(rawFiles, request, cancellationToken).ConfigureAwait(false))
            {
                if (yielded >= request.MaxResults) yield break;
                if (seen.Add(Key(result)))
                {
                    yielded++;
                    yield return result;
                }
            }

            if (indexedFileIds.Count > 0)
            {
                await foreach (var result in _entries.SearchAsync(request, indexedFileIds, cancellationToken).ConfigureAwait(false))
                {
                    if (yielded >= request.MaxResults) yield break;
                    if (seen.Add(Key(result)))
                    {
                        yielded++;
                        yield return result;
                    }
                }
            }
        }
        finally
        {
            _searchGate.Release();
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

    private static string Key(SearchResult result) => $"{result.SourcePath ?? result.SourceFile}|{result.Entry.LineNumber}";

    private async IAsyncEnumerable<SearchResult> ScanRawAsync(IEnumerable<(FileInfo File, LogFileInfo? State)> files, SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var (file, state) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = state?.IndexedLength ?? 0;
            var line = state?.IndexedLineCount ?? 0;
            await foreach (var result in ParseFileAsync(file, request, start, line, cancellationToken).ConfigureAwait(false))
            {
                yield return result;
            }
        }
    }

    private async IAsyncEnumerable<SearchResult> ParseFileAsync(FileInfo file, SearchRequest request, long start, long line, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = _parser.ParseAsync(file.FullName, request.Source.Id, 0, start, line, cancellationToken).GetAsyncEnumerator(cancellationToken);
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

                if (Matches(entry, request))
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

    private static bool Matches(LogEntry entry, SearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Keyword) && !Contains(entry.RawLine, request.Keyword) && !Contains(entry.ClientIp, request.Keyword) && !Contains(entry.ResolvedClientIp, request.Keyword) && !Contains(entry.UriStem, request.Keyword) && !Contains(entry.UriQuery, request.Keyword) && !Contains(entry.UserAgent, request.Keyword) && !Contains(entry.Method, request.Keyword) && entry.StatusCode != (int.TryParse(request.Keyword, out var status) ? status : -1)) return false;
        if (request.From is not null && (entry.TimestampUtc is null || entry.TimestampUtc < request.From)) return false;
        if (request.To is not null && (entry.TimestampUtc is null || entry.TimestampUtc > request.To)) return false;
        if (request.TimeFrom is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay < request.TimeFrom)) return false;
        if (request.TimeTo is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay > request.TimeTo)) return false;
        if (!string.IsNullOrWhiteSpace(request.Method) && !string.Equals(entry.Method, request.Method, StringComparison.OrdinalIgnoreCase)) return false;
        if (request.StatusCode is not null && entry.StatusCode != request.StatusCode) return false;
        if (!string.IsNullOrWhiteSpace(request.ClientIp) && !Contains(entry.ClientIp, request.ClientIp) && !Contains(entry.ResolvedClientIp, request.ClientIp)) return false;
        if (!string.IsNullOrWhiteSpace(request.UrlContains) && !Contains(entry.DisplayUrl, request.UrlContains)) return false;
        if (!string.IsNullOrWhiteSpace(request.UserAgentContains) && !Contains(entry.UserAgent, request.UserAgentContains)) return false;
        if (request.MinTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs < request.MinTimeTakenMs)) return false;
        if (request.MaxTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs > request.MaxTimeTakenMs)) return false;
        if (!string.IsNullOrWhiteSpace(request.Username) && !Contains(entry.Username, request.Username)) return false;
        return true;
    }

    private static bool Contains(string? value, string? search) => value?.Contains(search ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;
    private static string PrefixHash(string fingerprint) => fingerprint[(fingerprint.LastIndexOf('|') + 1)..];
}
