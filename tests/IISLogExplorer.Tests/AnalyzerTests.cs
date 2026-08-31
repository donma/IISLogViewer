using IISLogExplorer.Core.Models;
using IISLogExplorer.Infrastructure.Analysis;

namespace IISLogExplorer.Tests;

public class AnalyzerTests
{
    private static async IAsyncEnumerable<LogEntry> Entries(params LogEntry[] values)
    {
        await Task.CompletedTask;
        foreach (var entry in values) yield return entry;
    }

    [Fact]
    public async Task Ip_analyzer_aggregates_requests()
    {
        var analyzer = new IpAnalyzer();
        var entries = new[]
        {
            new LogEntry { ResolvedClientIp = "1.2.3.4", Method = "GET", UriStem = "/", StatusCode = 200, TimestampUtc = DateTimeOffset.UtcNow, UserAgent = "Chrome" },
            new LogEntry { ResolvedClientIp = "1.2.3.4", Method = "GET", UriStem = "/robots.txt", StatusCode = 404, TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1), UserAgent = "Chrome" },
            new LogEntry { ResolvedClientIp = "9.9.9.9", Method = "GET", UriStem = "/", StatusCode = 200, TimestampUtc = DateTimeOffset.UtcNow, UserAgent = "curl" }
        };
        var result = await analyzer.AnalyzeAsync(Entries(entries), "1.2.3.4");
        Assert.Equal(2, result.RequestCount);
        Assert.Equal(1, result.NotFoundCount);
        Assert.Equal(2, result.UniqueUrls);
        Assert.Equal(2, result.Timeline.Count);
    }

    [Fact]
    public async Task Slow_request_analyzer_computes_percentiles()
    {
        var entries = Enumerable.Range(101, 100).Select(x => new LogEntry { UriStem = "/slow", TimeTakenMs = x }).ToArray();
        var analyzer = new SlowRequestAnalyzer();
        var result = await analyzer.AnalyzeAsync(Entries(entries), 100);
        Assert.Equal(100, result.RequestCount);
        Assert.Equal(200, result.MaxDurationMs);
        Assert.Equal(195, (int)result.P95);
        Assert.Equal(199, (int)result.P99);
        Assert.True(result.P95 < result.P99);
    }

    [Fact]
    public async Task Error_analyzer_counts_4xx_5xx()
    {
        var analyzer = new ErrorAnalyzer();
        var entries = new[]
        {
            new LogEntry { StatusCode = 404, UriStem = "/no" },
            new LogEntry { StatusCode = 500, UriStem = "/crash" },
            new LogEntry { StatusCode = 200, UriStem = "/ok" }
        };
        var result = await analyzer.AnalyzeAsync(Entries(entries));
        Assert.Equal(2, result.TotalErrors);
        Assert.Equal(1, result.StatusDistribution[404]);
        Assert.Equal(1, result.StatusDistribution[500]);
    }

    [Fact]
    public async Task Traffic_analyzer_computes_totals_and_trend()
    {
        var analyzer = new TrafficAnalyzer();
        var baseTime = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 60).Select(x => new LogEntry { ResolvedClientIp = x % 3 == 0 ? "1.1.1.1" : "2.2.2.2", UriStem = "/page", StatusCode = 200, TimestampUtc = baseTime.AddSeconds(x) }).ToArray();
        var result = await analyzer.AnalyzeAsync(Entries(entries));
        Assert.Equal(60, result.TotalRequests);
        Assert.Equal(2, result.UniqueIps);
        Assert.True(result.Trend.Count > 0);
    }
}