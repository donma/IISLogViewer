using BenchmarkDotNet.Attributes;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Benchmarks;

[MemoryDiagnoser]
public class SearchBenchmark
{
    private HybridSearchService _search = null!;
    private LogSource _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        var path = BenchmarkData.SampleLogPath(100_000);
        var dir = Path.Combine(Path.GetTempPath(), "iislog-search-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var factory = new SqliteConnectionFactory(Path.Combine(dir, "bench.db"));
        new DatabaseInitializer(factory).InitializeAsync().GetAwaiter().GetResult();
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new LogFileScanner();
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(scanner, parser, files, entries, new FileFingerprintService(), factory);
        var sources = new SourceRepository(factory);
        _source = sources.SaveAsync(new LogSource { SourceType = LogSourceType.File, DisplayName = "bench", Path = path }).GetAwaiter().GetResult();
        index.IndexAsync(_source).GetAwaiter().GetResult();
        _search = new HybridSearchService(scanner, parser, files, entries, new FileFingerprintService());
    }

    [Benchmark]
    public async Task<int> SearchKeyword()
    {
        var count = 0;
        await foreach (var _ in _search.SearchAsync(new SearchRequest { Source = _source, Keyword = "/api/order", MaxResults = 10_000 }))
        {
            count++;
        }

        return count;
    }
}