using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class Phase3FixTests
{
    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-phase3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = new SqliteConnectionFactory(Path.Combine(root, "test.db"));
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new LogFileScanner();
        var fingerprints = new FileFingerprintService();
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(scanner, parser, files, entries, fingerprints, factory);
        var search = new HybridSearchService(scanner, parser, files, entries, fingerprints);
        return (factory, sources, files, entries, index, search, root);
    }

    [Fact]
    public async Task IndexProgressIsMonotonic()
    {
        var (factory, sources, _, entries, index, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(20_000));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var progresses = new List<long>();
        index.ProgressChanged += (_, progress) =>
        {
            if (progress.ProcessedBytes > 0)
            {
                progresses.Add(progress.ProcessedBytes);
            }
        };

        await index.IndexAsync(source);
        Assert.True(progresses.Count > 2, $"expected multiple progress reports, got {progresses.Count}");
        for (var i = 1; i < progresses.Count; i++)
        {
            Assert.True(progresses[i] >= progresses[i - 1], $"progress went backwards at {i}: {progresses[i - 1]} -> {progresses[i]}");
        }

        Assert.Equal(20_000, await entries.CountAsync());
    }

    [Fact]
    public async Task IncrementalIndexAfterRestartDoesNotRescanPrefix()
    {
        var (factory, sources, _, entries, index, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(500, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        await index.IndexAsync(source);
        Assert.Equal(504, (await index.GetFileStatesAsync(source)).Single().IndexedLineCount);

        IisW3cLogParser.InvalidateHeaderCache(file);
        await File.AppendAllTextAsync(file, TailLine(0, "/restart-a") + '\n');
        await index.IndexAsync(source);
        Assert.Equal(501, await entries.CountAsync());
        Assert.Equal(505, (await index.GetFileStatesAsync(source)).Single().IndexedLineCount);

        IisW3cLogParser.InvalidateHeaderCache(file);
        await File.AppendAllTextAsync(file, TailLine(1, "/restart-b") + '\n');
        await index.IndexAsync(source);
        Assert.Equal(502, await entries.CountAsync());
        Assert.Equal(506, (await index.GetFileStatesAsync(source)).Single().IndexedLineCount);
        Assert.True((await index.GetFileStatesAsync(source)).Single().IsFullyIndexed);
    }

    [Fact]
    public async Task RawAndIndexedFiltersHaveParity()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var request = new SearchRequest { Source = source, Keyword = "/api/order", StatusCode = 200, MaxResults = 10000 };
        var rawKeys = new List<string>();
        await foreach (var result in search.SearchAsync(request)) rawKeys.Add(Key(result));

        await index.IndexAsync(source);

        var request2 = new SearchRequest { Source = source, Keyword = "/api/order", StatusCode = 200, MaxResults = 10000 };
        var indexedKeys = new List<string>();
        await foreach (var result in search.SearchAsync(request2)) indexedKeys.Add(Key(result));

        var rawSet = rawKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indexedSet = indexedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(rawSet.Count, indexedSet.Count);
        Assert.Empty(indexedSet.Except(rawSet));
    }

    [Fact]
    public async Task BatchInsertWorks()
    {
        var (factory, sources, files, entries, _, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
        var file = new FileInfo(Path.Combine(root, "u_ex.log"));
        await File.WriteAllTextAsync(file.FullName, "dummy");
        var state = await files.UpsertAsync(source.Id, file, "fingerprint-1");

        await entries.InsertBatchAsync(
        [
            new LogEntry { SourceId = source.Id, FileId = state.Id, LineNumber = 1, Method = "GET", UriStem = "/one", StatusCode = 200, TimestampUtc = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), RawLine = "raw1" },
            new LogEntry { SourceId = source.Id, FileId = state.Id, LineNumber = 2, Method = "POST", UriStem = "/two", StatusCode = 404, TimestampUtc = new DateTimeOffset(2026, 8, 28, 10, 0, 1, TimeSpan.Zero), RawLine = "raw2" },
            new LogEntry { SourceId = source.Id, FileId = state.Id, LineNumber = 3, Method = "GET", UriStem = "/three", StatusCode = 500, TimestampUtc = new DateTimeOffset(2026, 8, 28, 10, 0, 2, TimeSpan.Zero), RawLine = "raw3" }
        ]);

        Assert.Equal(3, await entries.CountAsync());
        var hits = new List<SearchResult>();
        await foreach (var result in entries.SearchAsync(new SearchRequest { Source = source, MaxResults = 10 }, [state.Id])) hits.Add(result);
        Assert.Equal(3, hits.Count);
        Assert.Equal(["/one", "/three", "/two"], hits.OrderBy(h => h.Entry.UriStem).Select(h => h.Entry.UriStem!).ToArray());
        Assert.Contains(hits, h => h.Entry.StatusCode == 404 && h.Entry.Method == "POST");
    }

    [Fact]
    public async Task SearchSupportsConcurrentReaders()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(3000, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        await index.IndexAsync(source);

        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 300 }))
            {
                count++;
            }

            return count;
        })));

        Assert.All(counts, count => Assert.Equal(300, count));
    }

    [Fact]
    public async Task ParserHandlesChangedFieldsHeader()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem sc-status
            2026-08-28 10:00:01 1.2.3.4 GET /first 200
            #Fields: date time cs-method cs-uri-stem c-ip sc-status time-taken
            2026-08-28 10:00:02 POST /second 5.6.7.8 201 15
            """;
        var dir = Path.Combine(Path.GetTempPath(), "iislog-phase3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("/first", entries[0].UriStem);
        Assert.Equal(200, entries[0].StatusCode);
        Assert.Equal("/second", entries[1].UriStem);
        Assert.Equal("5.6.7.8", entries[1].ClientIp);
        Assert.Equal(201, entries[1].StatusCode);
        Assert.Equal(15, entries[1].TimeTakenMs);
    }

    private static string TailLine(int secondsAfter, string uri) => $"2026-08-28 11:{(secondsAfter / 60):00}:{secondsAfter % 60:00} 10.0.0.1 GET {uri} - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200";
    private static string Key(SearchResult result) => $"{result.SourcePath ?? result.SourceFile}|{result.Entry.LineNumber}";
}