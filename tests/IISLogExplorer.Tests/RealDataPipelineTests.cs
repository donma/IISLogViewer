using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Analysis;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;
using IISLogExplorer.Infrastructure.Security;

namespace IISLogExplorer.Tests;

public class RealDataPipelineTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iislog-real-" + Guid.NewGuid().ToString("N"));

    private (SqliteConnectionFactory Factory, SourceRepository Sources, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search) Build(string logFolder)
    {
        Directory.CreateDirectory(_root);
        var factory = new SqliteConnectionFactory(Path.Combine(_root, "test.db"));
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new LogFileScanner();
        var fingerprints = new FileFingerprintService();
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(scanner, parser, files, entries, fingerprints, factory);
        var search = new HybridSearchService(scanner, parser, files, entries, fingerprints);
        return (factory, sources, entries, index, search);
    }

    private static string PrepareFolder(string[] names)
    {
        var dir = Path.Combine(Path.GetTempPath(), "iislog-realdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in names)
        {
            var src = Path.Combine(AppContext.BaseDirectory, "TestData", name);
            if (File.Exists(src)) File.Copy(src, Path.Combine(dir, name));
        }

        return dir;
    }

    private static LogSource FolderSource(SourceRepository sources, string dir) => sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = Path.GetFileName(dir), Path = dir }).GetAwaiter().GetResult();

    [Fact]
    public async Task Real_u_ex_logs_full_pipeline_raw_then_indexed()
    {
        var logDir = PrepareFolder(["u_ex240115.log", "u_ex240116.log"]);
        var (factory, sources, entries, index, search) = Build(logDir);
        await new DatabaseInitializer(factory).InitializeAsync();
        var source = FolderSource(sources, logDir);

        var rawSqlmap = new List<SearchResult>();
        await foreach (var r in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) rawSqlmap.Add(r);
        Assert.Single(rawSqlmap);
        Assert.Equal(500, rawSqlmap[0].Entry.StatusCode);

        var rawSlow = new List<SearchResult>();
        await foreach (var r in search.SearchAsync(new SearchRequest { Source = source, Keyword = "slow.aspx" })) rawSlow.Add(r);
        Assert.Single(rawSlow);
        Assert.Equal(200, rawSlow[0].Entry.TimeTakenMs);

        await index.IndexAsync(source);
        Assert.True(await entries.CountAsync() >= 6);

        var indexedSqlmap = new List<SearchResult>();
        await foreach (var r in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) indexedSqlmap.Add(r);
        Assert.Single(indexedSqlmap);
        Assert.Equal(500, indexedSqlmap[0].Entry.StatusCode);
    }

    [Fact]
    public async Task Real_u_ex_logs_security_and_slow_analysis_detect_cases()
    {
        var logDir = PrepareFolder(["u_ex240115.log", "u_ex240116.log"]);
        var (factory, sources, _, index, search) = Build(logDir);
        await new DatabaseInitializer(factory).InitializeAsync();
        var source = FolderSource(sources, logDir);
        await index.IndexAsync(source);

        var engine = new SecurityRuleEngine(new SecurityRule("SQL_SLEEP", "SqlInjectionIndicator", "sqlmap", "contains", 20, true, "sqlmap agent"),
            new SecurityRule("METHOD_POST", "SuspiciousMethod", "POST", "method", 3, true, "post to login"));
        var security = new SecurityAnalyzer(engine);
        var securityResult = await security.AnalyzeAsync(Entries(search, source));
        Assert.True(securityResult.Score > 0);
        Assert.Contains(securityResult.Findings, f => f.Reason.Contains("sqlmap", StringComparison.OrdinalIgnoreCase));

        var slow = new SlowRequestAnalyzer();
        var slowResult = await slow.AnalyzeAsync(Entries(search, source), 1000);
        Assert.True(slowResult.RequestCount >= 1);
        Assert.Equal(15000, slowResult.MaxDurationMs);
    }

    [Fact]
    public async Task Realistic_iis_volume_supports_search_index_and_security()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "iislog-volume-" + Guid.NewGuid().ToString("N"));
        var file = TestHelpers.WriteSampleLog(logDir, TestHelpers.RealisticW3CLog(20_000), "u_ex260828.log");
        var (factory, sources, entries, index, search) = Build(logDir);
        await new DatabaseInitializer(factory).InitializeAsync();
        var source = FolderSource(sources, logDir);

        var raw = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) raw.Add(result);
        Assert.NotEmpty(raw);

        await index.IndexAsync(source);
        Assert.Equal(20_000, await entries.CountAsync());

        var indexed = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) indexed.Add(result);
        Assert.Equal(raw.Count, indexed.Count);

        var security = new SecurityAnalyzer(new SecurityRuleEngine());
        var securityResult = await security.AnalyzeAsync(Entries(search, source));
        Assert.NotEmpty(securityResult.Findings);
        Assert.True(securityResult.Score > 0);
    }

    [Fact]
    public async Task Realistic_iis_large_100k_full_pipeline()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "iislog-volume100k-" + Guid.NewGuid().ToString("N"));
        var file = TestHelpers.WriteSampleLog(logDir, TestHelpers.RealisticW3CLog(100_000), "u_ex260828.log");
        var (factory, sources, entries, index, search) = Build(logDir);
        await new DatabaseInitializer(factory).InitializeAsync();
        var source = FolderSource(sources, logDir);

        var raw = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) raw.Add(result);
        Assert.NotEmpty(raw);

        await index.IndexAsync(source);
        Assert.Equal(100_000, await entries.CountAsync());

        var indexed = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "sqlmap" })) indexed.Add(result);
        Assert.Equal(raw.Count, indexed.Count);

        var statusFiltered = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, StatusCode = 404 })) statusFiltered.Add(result);
        Assert.NotEmpty(statusFiltered);
        Assert.All(statusFiltered, r => Assert.Equal(404, r.Entry.StatusCode));

        var security = new SecurityAnalyzer(new SecurityRuleEngine());
        var securityResult = await security.AnalyzeAsync(Entries(search, source));
        Assert.NotEmpty(securityResult.Findings);
        Assert.True(securityResult.Score > 0);
    }

    private static async IAsyncEnumerable<LogEntry> Entries(HybridSearchService search, LogSource source)
    {
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source }).ConfigureAwait(false))
        {
            yield return result.Entry;
        }
    }
}
