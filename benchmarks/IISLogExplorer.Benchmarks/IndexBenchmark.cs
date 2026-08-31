using BenchmarkDotNet.Attributes;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;

namespace IISLogExplorer.Benchmarks;

[MemoryDiagnoser]
public class IndexBenchmark
{
    private IisW3cLogParser _parser = null!;
    private string _path = string.Empty;
    private string _dbPath = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        _path = BenchmarkData.SampleLogPath(100_000);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "iislog-index-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "bench.db");
        new DatabaseInitializer(new SqliteConnectionFactory(_dbPath)).InitializeAsync().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> Index100k()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(new LogFileScanner(), _parser, files, entries, new FileFingerprintService(), factory);
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.File, DisplayName = "bench", Path = _path });
        await index.IndexAsync(source);
        return (int)await entries.CountAsync();
    }
}