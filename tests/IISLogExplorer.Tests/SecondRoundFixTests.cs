using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class SecondRoundFixTests
{
    private static IisW3cLogParser CreateParser(ParserOptions? options = null) => new(new FieldsHeaderParser(), new ClientIpResolver(), options);

    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-round2-" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubSettings : IISLogExplorer.Core.Configuration.ISettingsService
    {
        public StubSettings(IISLogExplorer.Core.Models.AppSettings settings) => Current = settings;
        public AppSettings Current { get; }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(RealtimeLogWatcher Watcher, string Root, SqliteIndexService Index, SourceRepository Sources)> BuildRealtimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-round2-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = new SqliteConnectionFactory(Path.Combine(root, "test.db"));
        await new DatabaseInitializer(factory).InitializeAsync();
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new LogFileScanner();
        var files = new LogFileRepository(factory);
        var sources = new SourceRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(scanner, parser, files, entries, new FileFingerprintService(), factory);
        var settings = new StubSettings(new AppSettings { RealtimeRefreshIntervalSeconds = 1 });
        var watcher = new RealtimeLogWatcher(parser, scanner, settings, files);
        return (watcher, root, index, sources);
    }

    [Fact]
    public async Task ParserHandlesHeaderWithoutDate()
    {
        var content = """
            #Software: IIS
            #Version: 1.0
            #Fields: c-ip cs-method cs-uri-stem sc-status
            1.2.3.4 GET /health 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), "iislog-round2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = TestHelpers.WriteSampleLog(dir, content);
        var entries = await CreateParser().ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.TimestampUtc);
        Assert.Null(entry.TimestampLocal);
        Assert.Equal("/health", entry.UriStem);
        Assert.Equal(200, entry.StatusCode);
    }

    [Fact]
    public async Task ParserHandlesHeaderWithoutTime()
    {
        var content = """
            #Software: IIS
            #Version: 1.0
            #Fields: date c-ip cs-method cs-uri-stem sc-status
            2026-08-28 1.2.3.4 GET /health 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), "iislog-round2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = TestHelpers.WriteSampleLog(dir, content);
        var entries = await CreateParser().ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.TimestampUtc);
        Assert.Equal("/health", entry.UriStem);
    }

    [Fact]
    public async Task HybridSearchUsesPersistedHeaderAfterRestart()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(500, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        await index.IndexAsync(source);
        var state = (await index.GetFileStatesAsync(source)).Single();
        Assert.NotNull(state.FieldsHeader);
        Assert.Equal(504, state.IndexedLineCount);

        IisW3cLogParser.InvalidateHeaderCache(file);
        Assert.Null(IisW3cLogParser.GetActiveHeader(file));

        var tail = string.Join('\n', Enumerable.Range(0, 10).Select(i => $"2026-08-28 11:00:{i:00} 10.0.0.1 GET /restart-tail/{i} - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200"));
        await File.AppendAllTextAsync(file, tail + '\n');

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/restart-tail/", MaxResults = 100 })) hits.Add(result);
        Assert.Equal(10, hits.Count);
        Assert.Equal(Enumerable.Range(505, 10).Select(i => (long)i).ToArray(), hits.OrderBy(h => h.Entry.LineNumber).Select(h => h.Entry.LineNumber).ToArray());

        var after = (await index.GetFileStatesAsync(source)).Single();
        Assert.Equal(state.IndexedLength, after.IndexedLength);
        Assert.Equal(504, after.IndexedLineCount);
    }

    [Fact]
    public async Task RealtimeUsesPersistedHeaderAfterRestart()
    {
        var (watcher, root, index, sources) = await BuildRealtimeAsync();
        try
        {
            var file = Path.Combine(root, "u_ex260828.log");
            await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(4, "/api/order"));
            var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
            await index.IndexAsync(source);
            Assert.NotNull((await index.GetFileStatesAsync(source)).Single().FieldsHeader);
            IisW3cLogParser.InvalidateHeaderCache(file);

            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<LogEntry>? added = null;
            watcher.EntriesAdded += (_, entries) =>
            {
                added ??= entries;
                completed.TrySetResult();
            };

            await watcher.StartAsync(source);
            await Task.Delay(1500);
            await File.AppendAllTextAsync(file, "2026-08-28 11:00:00 10.0.0.1 GET /persisted-header-tail - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200" + '\n');
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await watcher.StopAsync();

            Assert.NotNull(added);
            Assert.Equal("/persisted-header-tail", Assert.Single(added!).UriStem);
            Assert.Equal(9, Assert.Single(added).LineNumber);
        }
        finally
        {
            await watcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealtimeAttachWithoutIndexDoesNotScanWholeFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-round2-tail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(100_000));

        var (offset, bytesRead) = await RealtimeLogWatcher.FindTailFromEndAsync(file, CancellationToken.None);
        var size = new FileInfo(file).Length;
        Assert.True(bytesRead <= 64 * 1024, $"expected at most one 64KB chunk, read {bytesRead}");
        Assert.True(bytesRead * 100 < size, $"expected tail scan to read far less than whole file: read {bytesRead} of {size}");
        Assert.Equal(size, offset);
    }

    [Fact]
    public async Task CustomClientIpHeaderStillWorksWithoutAdditionalFields()
    {
        var content = """
            #Software: IIS
            #Fields: date time c-ip cs-method cs-uri-stem cnd-src-ip x(My-Field) sc-status
            2026-08-28 10:00:00 1.2.3.4 GET /health 203.0.113.9 "custom-value" 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), "iislog-round2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = TestHelpers.WriteSampleLog(dir, content);
        var entries = await CreateParser().ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("1.2.3.4", entry.ClientIp);
        Assert.Equal("203.0.113.9", entry.ResolvedClientIp);
        Assert.NotNull(entry.AdditionalFields);
        Assert.Contains("cnd-src-ip", entry.AdditionalFields!.Keys);
        Assert.DoesNotContain("x(My-Field)", entry.AdditionalFields.Keys);
    }

    [Fact]
    public async Task HybridSearchNullTimestampAlwaysLast()
    {
        var (factory, sources, _, _, _, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "u_ex_a.log"), TestHelpers.SampleW3CLog(50, "/kpi"));
        await Task.Delay(10);
        var noTime = new System.Text.StringBuilder("""
            #Software: IIS
            #Version: 1.0
            #Fields: c-ip cs-method cs-uri-stem sc-status
            """);
        for (var i = 0; i < 10; i++)
        {
            noTime.Append($"\n1.2.3.{i % 3 + 1} GET /kpi 200");
        }

        noTime.Append('\n');
        await File.WriteAllTextAsync(Path.Combine(dir, "u_ex_b.log"), noTime.ToString());
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/kpi", MaxResults = 100 })) hits.Add(result);

        Assert.Equal(15, hits.Count);
        var nullStart = hits.FindIndex(h => h.Entry.TimestampUtc is null);
        Assert.True(nullStart >= 5, $"null timestamps should be last, first null at {nullStart}");
        for (var i = nullStart + 1; i < hits.Count; i++)
        {
            Assert.Null(hits[i].Entry.TimestampUtc);
        }

        for (var i = 1; i < nullStart; i++)
        {
            Assert.True(hits[i - 1].Entry.TimestampUtc >= hits[i].Entry.TimestampUtc);
        }
    }

    [Fact]
    public async Task HybridSearchRawAndIndexedTieBreakIsDeterministic()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(100, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        await index.IndexAsync(source);

        await File.AppendAllTextAsync(file, string.Join('\n', Enumerable.Range(0, 5).Select(i => $"2026-08-28 10:00:{i:00} 10.0.0.1 GET /api/order - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200")) + '\n');

        var first = new List<long>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 100 })) first.Add(result.Entry.LineNumber);

        var second = new List<long>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 100 })) second.Add(result.Entry.LineNumber);

        Assert.Equal(first, second);
        Assert.True(first.Count >= 15);
        for (var i = 1; i < first.Count; i++)
        {
            var prev = first[i - 1];
            var cur = first[i];
            Assert.True(prev != cur, "no duplicate lines");
        }
    }
}