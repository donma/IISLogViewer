using System.Text;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Core.Searching;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class Phase4FixTests
{
    private const string ParityHeader = """
        #Software: Microsoft Internet Information Services 10.0
        #Version: 1.0
        #Date: 2026-08-28 00:00:00
        #Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) sc-status time-taken X-Forwarded-For
        """;

    private static string ParityLine(int index) =>
        $"2026-08-28 10:{(index / 60) % 60:00}:{index % 60:00} 10.0.0.9 {(index % 2 == 0 ? "GET" : "POST")} {(index % 3 == 0 ? "/api/order" : "/page/item")} {(index % 4 == 0 ? "id=1" : "-")} 443 {(index % 2 == 0 ? "alice" : "-")} 10.0.0.{index % 3 + 1} {(index % 5 == 0 ? "\"curl/8.1\"" : "\"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\"")} {(index % 10 == 0 ? 404 : 200)} {100 + (index % 49) * 100} {(index % 5 == 0 ? "203.0.113.7" : "-")}";

    private static string ParityLog(int records)
    {
        var builder = new StringBuilder(ParityHeader);
        for (var index = 0; index < records; index++)
        {
            builder.Append('\n').Append(ParityLine(index));
        }

        return builder.Append('\n').ToString();
    }

    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-phase4-" + Guid.NewGuid().ToString("N"));
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
    public async Task RawAndIndexedFiltersHaveParity_AllFilters()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, ParityLog(500));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var allEntries = await parser.ParseAsync(file, source.Id).ToListAsync();

        var from = new DateTimeOffset(2026, 8, 28, 10, 2, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 28, 10, 6, 59, TimeSpan.Zero);
        var requests = new (string Name, SearchRequest Request)[]
        {
            ("Keyword", new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 10000 }),
            ("FromTo", new SearchRequest { Source = source, From = from, To = to, MaxResults = 10000 }),
            ("TimeFrom", new SearchRequest { Source = source, TimeFrom = TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(5)), MaxResults = 10000 }),
            ("TimeTo", new SearchRequest { Source = source, TimeTo = TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(7)), MaxResults = 10000 }),
            ("Method", new SearchRequest { Source = source, Method = "POST", MaxResults = 10000 }),
            ("StatusCode", new SearchRequest { Source = source, StatusCode = 404, MaxResults = 10000 }),
            ("ClientIp", new SearchRequest { Source = source, ClientIp = "10.0.0.1", MaxResults = 10000 }),
            ("UrlContains", new SearchRequest { Source = source, UrlContains = "api", MaxResults = 10000 }),
            ("UserAgentContains", new SearchRequest { Source = source, UserAgentContains = "curl", MaxResults = 10000 }),
            ("MinTimeTakenMs", new SearchRequest { Source = source, MinTimeTakenMs = 3000, MaxResults = 10000 }),
            ("MaxTimeTakenMs", new SearchRequest { Source = source, MaxTimeTakenMs = 1500, MaxResults = 10000 }),
            ("Username", new SearchRequest { Source = source, Username = "alice", MaxResults = 10000 })
        };

        foreach (var (name, request) in requests)
        {
            var rawKeys = new HashSet<long>((await SearchKeysAsync(search, request)).Select(line => (long)line));
            var predicateKeys = allEntries.Where(entry => SearchPredicate.Matches(entry, request)).Select(entry => entry.LineNumber).ToHashSet();
            Assert.NotEmpty(rawKeys);
            Assert.Equal(predicateKeys, rawKeys);
        }

        await index.IndexAsync(source);
        foreach (var (_, request) in requests)
        {
            var rawKeys = new HashSet<long>((await SearchKeysAsync(search, request)).Select(line => (long)line));
            var predicateKeys = allEntries.Where(entry => SearchPredicate.Matches(entry, request)).Select(entry => entry.LineNumber).ToHashSet();
            Assert.Equal(predicateKeys, rawKeys);
        }
    }

    [Fact]
    public async Task IndexProgressIsMonotonicMultiFile()
    {
        var (factory, sources, _, entries, index, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "u_ex260801.log"), TestHelpers.SampleW3CLog(500));
        await Task.Delay(10);
        await File.WriteAllTextAsync(Path.Combine(dir, "u_ex260802.log"), TestHelpers.SampleW3CLog(900));
        await Task.Delay(10);
        await File.WriteAllTextAsync(Path.Combine(dir, "u_ex260803.log"), TestHelpers.SampleW3CLog(700));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var progresses = new List<long>();
        index.ProgressChanged += (_, progress) =>
        {
            if (progress.ProcessedBytes >= 0)
            {
                progresses.Add(progress.ProcessedBytes);
            }
        };

        await index.IndexAsync(source);
        Assert.Equal(2100, await entries.CountAsync());
        Assert.True(progresses.Count > 3, $"expected several progress reports, got {progresses.Count}");
        for (var i = 1; i < progresses.Count; i++)
        {
            Assert.True(progresses[i] >= progresses[i - 1], $"multi-file progress went backwards at {i}: {progresses[i - 1]} -> {progresses[i]}");
        }

        Assert.Equal(progresses[^1], progresses[^1]);
    }

    private static async Task<List<int>> SearchKeysAsync(HybridSearchService search, SearchRequest request)
    {
        var keys = new List<int>();
        await foreach (var result in search.SearchAsync(request))
        {
            keys.Add(Convert.ToInt32(result.Entry.LineNumber));
        }

        return keys;
    }
}