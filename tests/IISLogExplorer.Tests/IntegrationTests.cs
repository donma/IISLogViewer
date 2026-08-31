using IISLogExplorer.Core.Files;
using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class IntegrationTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "iislog-integration-" + Guid.NewGuid().ToString("N"));

    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search) Build()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "iislog-integration-" + Guid.NewGuid().ToString("N"));
        var factory = new SqliteConnectionFactory(Path.Combine(testRoot, "test.db"));
        Directory.CreateDirectory(testRoot);
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new LogFileScanner();
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(scanner, parser, files, entries, new FileFingerprintService(), factory);
        var search = new HybridSearchService(scanner, parser, files, entries, new FileFingerprintService());
        return (factory, sources, files, entries, index, search);
    }

    [Fact]
    public async Task End_to_end_raw_search_then_index_then_query()
    {
        var (factory, sources, _, entries, index, search) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var logPath = Path.Combine(TempRoot, "site");
        Directory.CreateDirectory(logPath);
        var file = Path.Combine(logPath, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "site", Path = logPath });

        var rawHits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order" })) rawHits.Add(result);
        Assert.Equal(100, rawHits.Count);

        await index.IndexAsync(source);
        var indexedState = await index.GetFileStatesAsync(source);
        Assert.Single(indexedState);
        Assert.True(indexedState[0].IsFullyIndexed);

        var dbHits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order" })) dbHits.Add(result);
        Assert.Equal(100, dbHits.Count);
        Assert.True(await entries.CountAsync() >= 1000);
    }

    [Fact]
    public async Task Incremental_index_only_adds_new_lines()
    {
        var (factory, sources, _, entries, index, _) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var logPath = Path.Combine(TempRoot, "inc");
        Directory.CreateDirectory(logPath);
        var file = Path.Combine(logPath, "u_ex260828.log");
        var content = TestHelpers.SampleW3CLog(100);
        await File.WriteAllTextAsync(file, content);
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "inc", Path = logPath });
        await index.IndexAsync(source);
        Assert.Equal(100, await entries.CountAsync());

        await File.AppendAllTextAsync(file, content);
        await index.IndexAsync(source);
        Assert.Equal(200, await entries.CountAsync());

        await index.IndexAsync(source);
        Assert.Equal(200, await entries.CountAsync());
        var state = (await index.GetFileStatesAsync(source)).Single();
        Assert.NotNull(state.FileFingerprint);
        Assert.True(state.IsFullyIndexed);
    }

    [Fact]
    public async Task Search_respects_status_filter()
    {
        var (factory, sources, _, _, index, search) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var logPath = Path.Combine(TempRoot, "status");
        Directory.CreateDirectory(logPath);
        var file = Path.Combine(logPath, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.File, DisplayName = "status", Path = file });
        await index.IndexAsync(source);
        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, StatusCode = 404 })) hits.Add(result);
        Assert.Equal(10, hits.Count);
    }
}