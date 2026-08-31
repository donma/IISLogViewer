using System.Diagnostics;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class PerformanceTests
{
    [Fact]
    public async Task Parse_and_index_100k_records_within_reasonable_time()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(100_000));

        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var parseStopwatch = Stopwatch.StartNew();
        var parsed = 0;
        await foreach (var _ in parser.ParseAsync(file, 1))
        {
            parsed++;
        }

        parseStopwatch.Stop();
        Assert.Equal(100_000, parsed);
        Assert.True(parseStopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Parser too slow: {parseStopwatch.Elapsed}");

        var factory = new SqliteConnectionFactory(Path.Combine(root, "perf.db"));
        await new DatabaseInitializer(factory).InitializeAsync();
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new SqliteIndexService(new LogFileScanner(), parser, files, entries, new FileFingerprintService(), factory);
        var source = await sources.SaveAsync(new Core.Models.LogSource { SourceType = Core.Models.LogSourceType.File, DisplayName = "perf", Path = file });

        var indexStopwatch = Stopwatch.StartNew();
        await index.IndexAsync(source);
        indexStopwatch.Stop();

        Assert.Equal(100_000, await entries.CountAsync());
        Assert.True(indexStopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Index too slow: {indexStopwatch.Elapsed}");
    }
}
