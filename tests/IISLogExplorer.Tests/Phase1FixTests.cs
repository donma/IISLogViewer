using System.Text;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Searching;

namespace IISLogExplorer.Tests;

public class Phase1FixTests
{
    private const string Header = """
        #Software: Microsoft Internet Information Services 10.0
        #Version: 1.0
        #Date: 2026-08-28 00:00:00
        #Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken
        """;

    private static string Record(string date, int hour, int second, string uri) =>
        $"{date} {hour:00}:00:{second:00} 10.0.0.1 GET {uri} - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 10";

    private static string SampleLine(int second, string uri) => Record("2026-08-28", (second / 3600) % 24, second % 60, uri);

    private static string RetentionLog(DateTimeOffset oldDay, DateTimeOffset newDay)
    {
        var builder = new StringBuilder(Header);
        var oldDate = oldDay.Date.ToString("yyyy-MM-dd");
        var newDate = newDay.Date.ToString("yyyy-MM-dd");
        for (var hour = 0; hour < 24; hour++)
        {
            builder.Append('\n').Append(Record(oldDate, hour, 0, "/old"));
        }

        for (var hour = 0; hour < 24; hour++)
        {
            builder.Append('\n').Append(Record(newDate, hour, 0, "/new"));
        }

        return builder.Append('\n').ToString();
    }

    private static (SqliteConnectionFactory Factory, SourceRepository Sources, LogFileRepository Files, LogEntryRepository Entries, SqliteIndexService Index, HybridSearchService Search, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-phase1-" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubSettings : ISettingsService
    {
        public StubSettings(AppSettings settings) => Current = settings;

        public AppSettings Current { get; }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(RealtimeLogWatcher Watcher, string Root, SqliteIndexService Index, SourceRepository Sources)> BuildRealtimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-realtime-" + Guid.NewGuid().ToString("N"));
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

    private static string WriteLog(string root, string fileName, int records, string uri = "/page")
    {
        var builder = new StringBuilder(Header);
        for (var index = 0; index < records; index++)
        {
            builder.Append('\n').Append(SampleLine(index, index % 10 == 0 ? "/api/order" : uri));
        }

        builder.Append('\n');
        return TestHelpers.WriteSampleLog(root, builder.ToString(), fileName);
    }

    [Fact]
    public async Task RetentionDoesNotResurrectDeletedRows()
    {
        var (factory, sources, _, entries, index, _, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var today = DateTimeOffset.UtcNow.Date;
        var oldDay = today.AddDays(-10);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, RetentionLog(oldDay, today));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        await index.IndexAsync(source);
        Assert.Equal(48, await entries.CountAsync());

        var deleted = await index.CleanupAsync(1);
        Assert.Equal(24, deleted);
        Assert.Equal(24, await entries.CountAsync());
        Assert.DoesNotContain(await AllTimestampsAsync(entries, source), ts => ts.Date == oldDay.Date);

        await index.IndexAsync(source);
        Assert.Equal(24, await entries.CountAsync());
        Assert.DoesNotContain(await AllTimestampsAsync(entries, source), ts => ts.Date == oldDay.Date);

        var tomorrow = today.AddDays(1).ToString("yyyy-MM-dd");
        await File.AppendAllTextAsync(file, Record(tomorrow, 0, 0, "/kpi") + '\n');
        await index.IndexAsync(source);
        Assert.Equal(25, await entries.CountAsync());
        Assert.Contains(await AllTimestampsAsync(entries, source), ts => ts.Date == today.AddDays(1).Date);
    }

    private static async Task<List<DateTimeOffset>> AllTimestampsAsync(LogEntryRepository entries, LogSource source)
    {
        var result = new List<DateTimeOffset>();
        await foreach (var entry in entries.GetEntriesAsync(new SearchRequest { Source = source, MaxResults = 100000 }))
        {
            if (entry.TimestampUtc is not null)
            {
                result.Add(entry.TimestampUtc.Value);
            }
        }

        return result;
    }

    [Fact]
    public async Task RealtimeDoesNotLosePartialLine()
    {
        var (watcher, root, index, sources) = await BuildRealtimeAsync();
        try
        {
            var file = WriteLog(root, "u_ex260828.log", 4);
            var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
            await index.IndexAsync(source);
            var batches = new List<IReadOnlyList<LogEntry>>();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.EntriesAdded += (_, added) =>
            {
                lock (batches)
                {
                    batches.Add(added);
                    if (batches.Count >= 1)
                    {
                        completed.TrySetResult();
                    }
                }
            };

            await watcher.StartAsync(source);
            await Task.Delay(1500);
            await File.AppendAllTextAsync(file, SampleLine(0, "/append-1") + '\n' + SampleLine(1, "/append-2") + '\n' + SampleLine(2, "/append-partial"));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await watcher.StopAsync();

            var batch = Assert.Single(batches);
            Assert.Equal(["/append-1", "/append-2"], batch.Select(e => e.UriStem!).ToArray());
            Assert.Equal(9, batch[0].LineNumber);
            Assert.Equal(10, batch[1].LineNumber);
        }
        finally
        {
            await watcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealtimePartialLineCompletesAfterNewline()
    {
        var (watcher, root, index, sources) = await BuildRealtimeAsync();
        try
        {
            var file = WriteLog(root, "u_ex260828.log", 4);
            var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
            await index.IndexAsync(source);
            var batches = new List<IReadOnlyList<LogEntry>>();
            var secondBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.EntriesAdded += (_, added) =>
            {
                lock (batches)
                {
                    batches.Add(added);
                    if (batches.Count >= 2)
                    {
                        secondBatch.TrySetResult();
                    }
                }
            };

            await watcher.StartAsync(source);
            await Task.Delay(1500);
            await File.AppendAllTextAsync(file, SampleLine(0, "/append-1") + '\n' + SampleLine(1, "/append-2") + '\n' + SampleLine(2, "/append-partial"));
            await Task.Delay(4000);
            await File.AppendAllTextAsync(file, "\n");
            await secondBatch.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await watcher.StopAsync();

            lock (batches)
            {
                Assert.True(batches.Count >= 2);
                Assert.Equal("/append-partial", Assert.Single(batches[^1]).UriStem);
                Assert.Equal(11, Assert.Single(batches[^1]).LineNumber);
            }
        }
        finally
        {
            await watcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealtimeLineNumberIsCorrect()
    {
        var (watcher, root, index, sources) = await BuildRealtimeAsync();
        try
        {
            var file = WriteLog(root, "u_ex260828.log", 100);
            var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
            await index.IndexAsync(source);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<LogEntry>? added = null;
            watcher.EntriesAdded += (_, entries) =>
            {
                added ??= entries;
                completed.TrySetResult();
            };

            await watcher.StartAsync(source);
            await Task.Delay(1500);
            await File.AppendAllTextAsync(file, SampleLine(0, "/tail-1") + '\n' + SampleLine(1, "/tail-2") + '\n' + SampleLine(2, "/tail-3") + '\n');
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await watcher.StopAsync();

            Assert.NotNull(added);
            Assert.Equal([105L, 106L, 107L], added!.Select(e => e.LineNumber).ToArray());
            Assert.Equal(["/tail-1", "/tail-2", "/tail-3"], added.Select(e => e.UriStem!).ToArray());
        }
        finally
        {
            await watcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealtimeHandlesTruncate()
    {
        var (watcher, root, index, sources) = await BuildRealtimeAsync();
        try
        {
            var file = WriteLog(root, "u_ex260828.log", 4);
            var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = root });
            await index.IndexAsync(source);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<LogEntry>? added = null;
            watcher.EntriesAdded += (_, entries) =>
            {
                added ??= entries;
                completed.TrySetResult();
            };

            await watcher.StartAsync(source);
            await Task.Delay(1500);

            var truncated = new StringBuilder(Header);
            for (var i = 0; i < 2; i++)
            {
                truncated.Append('\n').Append(SampleLine(i, "/old"));
            }

            truncated.Append('\n');
            await File.WriteAllTextAsync(file, truncated.ToString());
            await Task.Delay(3000);

            await File.AppendAllTextAsync(file, SampleLine(2, "/after-truncate") + '\n');
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await watcher.StopAsync();

            Assert.NotNull(added);
            Assert.Equal("/after-truncate", Assert.Single(added!).UriStem);
            Assert.True(Assert.Single(added).LineNumber >= 0);
        }
        finally
        {
            await watcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task HybridSearchMaintainsGlobalOrdering()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(1000, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        await index.IndexAsync(source);

        await File.AppendAllTextAsync(file, string.Join('\n', Enumerable.Range(0, 5).Select(i => SampleW3cAppend(11, i, "/api/order"))) + '\n');

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 10000 })) hits.Add(result);

        Assert.Equal(105, hits.Count);
        Assert.Equal(105, hits.Select(r => $"{r.SourcePath}|{r.Entry.LineNumber}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (var i = 1; i < hits.Count; i++)
        {
            Assert.True(hits[i - 1].Entry.TimestampUtc >= hits[i].Entry.TimestampUtc, $"order broke at {i}");
        }

        Assert.True(hits[0].Entry.TimestampUtc >= new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.Zero));
        Assert.True(hits.Take(5).All(h => !h.IsIndexed));
    }

    [Fact]
    public async Task HybridSearchDoesNotLoseIndexedResultsAtLimit()
    {
        var (factory, sources, _, _, index, search, root) = Build();
        await new DatabaseInitializer(factory).InitializeAsync();
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(300, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });
        await index.IndexAsync(source);

        await File.AppendAllTextAsync(file, string.Join('\n', Enumerable.Range(0, 100).Select(i => SampleW3cAppend(9, i, "/api/order"))) + '\n');

        var hits = new List<SearchResult>();
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/api/order", MaxResults = 50 })) hits.Add(result);

        Assert.Equal(50, hits.Count);
        Assert.Contains(hits, h => h.IsIndexed);
        Assert.True(hits.Take(30).All(h => h.IsIndexed));
        for (var i = 1; i < hits.Count; i++)
        {
            Assert.True(hits[i - 1].Entry.TimestampUtc >= hits[i].Entry.TimestampUtc, $"order broke at {i}");
        }
    }

    private static string SampleW3cAppend(int hour, int second, string uri) =>
        $"2026-08-28 {hour:00}:{(second / 60):00}:{second % 60:00} 10.0.0.1 GET {uri} - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200";
}