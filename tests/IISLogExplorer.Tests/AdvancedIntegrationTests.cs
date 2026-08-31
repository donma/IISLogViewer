using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class AdvancedIntegrationTests
{
    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-adv-" + Guid.NewGuid().ToString("N"));
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
    public async Task Cancel_index_keeps_committed_batches_and_resumes()
    {
        var (factory, sources, _, entries, index, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(20_000));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(150);
        try
        {
            await index.IndexAsync(source, cancellationToken: cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var partial = (await index.GetFileStatesAsync(source)).Single();
        Assert.False(partial.IsFullyIndexed);
        var partialCount = await entries.CountAsync();
        Assert.True(partialCount >= 0 && partialCount < 20_000);

        await index.IndexAsync(source);
        Assert.Equal(20_000, await entries.CountAsync());
        var final = (await index.GetFileStatesAsync(source)).Single();
        Assert.True(final.IsFullyIndexed);
        Assert.True(final.IndexedLength == final.FileSize);
    }

    [Fact]
    public async Task Search_enforces_max_results()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(3000));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, MaxResults = 100, PageSize = 100 })) hits.Add(result);
        Assert.Equal(100, hits.Count);
    }

    [Fact]
    public async Task Time_range_filter_limits_results()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        var from = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 28, 10, 4, 59, 0, TimeSpan.Zero);
        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, From = from, To = to })) hits.Add(result);
        Assert.True(hits.Count is > 0 and <= 500);
        Assert.All(hits, r => Assert.True(r.Entry.TimestampUtc >= from && r.Entry.TimestampUtc <= to));
    }

    [Fact]
    public async Task Partially_indexed_file_search_has_no_duplicates_and_full_coverage()
    {
        var (factory, sources, _, entries, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(30);
        try
        {
            await index.IndexAsync(source, cancellationToken: cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order" })) hits.Add(result);
        Assert.Equal(100, hits.Count);
        Assert.Equal(100, hits.Select(r => $"{r.SourceFile}|{r.Entry.LineNumber}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(await entries.CountAsync() < 1000);
    }
}
