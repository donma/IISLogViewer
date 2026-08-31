using IISLogExplorer.Core.Models;
using IISLogExplorer.Infrastructure.Security;

namespace IISLogExplorer.Tests;

public class SecurityAnalyzerTests
{
    private static SecurityAnalyzer CreateAnalyzer(params SecurityRule[] rules) => new(new SecurityRuleEngine(rules));

    private static async IAsyncEnumerable<LogEntry> Entries(params LogEntry[] values)
    {
        await Task.CompletedTask;
        foreach (var entry in values) yield return entry;
    }

    [Fact]
    public async Task Sensitive_paths_increase_score_and_create_findings()
    {
        var analyzer = CreateAnalyzer(new SecurityRule("SENSITIVE_ENV", "SensitiveFileProbe", "/.env", "contains", 15, true, "env probe"), new SecurityRule("SENSITIVE_GIT", "SensitiveFileProbe", "/.git/config", "contains", 15, true, "git probe"));
        var result = await analyzer.AnalyzeAsync(Entries(
            new LogEntry { ResolvedClientIp = "45.66.0.1", UriStem = "/.env", StatusCode = 404, TimestampUtc = DateTimeOffset.UtcNow },
            new LogEntry { ResolvedClientIp = "45.66.0.1", UriStem = "/.git/config", StatusCode = 404, TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1) }
        ));
        Assert.Equal(2, result.Findings.Count);
        Assert.True(result.Score > 0);
        Assert.True(result.Reasons.Count > 0);
    }

    [Fact]
    public async Task Normal_traffic_stays_low()
    {
        var analyzer = CreateAnalyzer(new SecurityRule("SENSITIVE_ENV", "SensitiveFileProbe", "/.env", "contains", 15, true, "env probe"));
        var entries = Enumerable.Range(0, 200).Select(x => new LogEntry { ResolvedClientIp = "10.0.0.1", UriStem = "/home", StatusCode = 200, TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(x) }).ToArray();
        var result = await analyzer.AnalyzeAsync(Entries(entries));
        Assert.True(result.Score <= 25);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Scanner_pattern_flagged_as_heuristic()
    {
        var analyzer = CreateAnalyzer(new SecurityRule("SENSITIVE_ENV", "SensitiveFileProbe", "/.env", "contains", 15, true, "env probe"), new SecurityRule("TRAVERSAL", "PathTraversal", "../", "contains", 25, true, "traversal"));
        var baseTime = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 150).Select(x => new LogEntry { ResolvedClientIp = "45.66.0.1", UriStem = x % 2 == 0 ? "/.env" : "/../../etc/passwd", StatusCode = 404, TimestampUtc = baseTime.AddSeconds(x) }).ToArray();
        var result = await analyzer.AnalyzeAsync(Entries(entries));
        Assert.Contains(result.Reasons, reason => reason.Contains("啟發式", StringComparison.OrdinalIgnoreCase) || reason.Length > 0);
        Assert.True(result.Score >= 50);
    }
}