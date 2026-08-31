namespace IISLogExplorer.Core.Models;

public sealed record IpTimelineItem
{
    public DateTimeOffset? Timestamp { get; init; }
    public string? Method { get; init; }
    public string? Url { get; init; }
    public string? Status { get; init; }
}

public sealed record IpAnalysisResult
{
    public required string Ip { get; init; }
    public DateTimeOffset? FirstSeen { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public long RequestCount { get; init; }
    public int UniqueUrls { get; init; }
    public long NotFoundCount { get; init; }
    public long ServerErrorCount { get; init; }
    public double AverageTimeTakenMs { get; init; }
    public IReadOnlyList<string> UserAgents { get; init; } = [];
    public IReadOnlyList<string> TopUrls { get; init; } = [];
    public IReadOnlyDictionary<string, long> Methods { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<int, long> StatusDistribution { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<IpTimelineItem> Timeline { get; init; } = [];
}

public sealed record ErrorAnalysisResult
{
    public long TotalErrors { get; init; }
    public IReadOnlyList<(string Url, long Count)> TopErrorUrls { get; init; } = [];
    public IReadOnlyList<(string Ip, long Count)> TopErrorIps { get; init; } = [];
    public IReadOnlyDictionary<int, long> StatusDistribution { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<LogEntry> Timeline { get; init; } = [];
}

public sealed record SlowRequestAnalysisResult
{
    public int ThresholdMs { get; init; }
    public long RequestCount { get; init; }
    public double AverageDurationMs { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
    public long MaxDurationMs { get; init; }
    public IReadOnlyList<(string Url, double AverageMs, long Count)> TopSlowUrls { get; init; } = [];
}

public sealed record TrafficAnalysisResult
{
    public long TotalRequests { get; init; }
    public int UniqueIps { get; init; }
    public double RequestsPerMinute { get; init; }
    public IReadOnlyList<(string Url, long Count)> TopUrls { get; init; } = [];
    public IReadOnlyList<(string Ip, long Count)> TopIps { get; init; } = [];
    public IReadOnlyList<(string UserAgent, long Count)> TopUserAgents { get; init; } = [];
    public IReadOnlyDictionary<int, long> StatusDistribution { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<(DateTimeOffset Bucket, long Count)> Trend { get; init; } = [];
}

public sealed record SecurityFinding
{
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required SecuritySeverity Severity { get; init; }
    public required string Reason { get; init; }
    public string? ClientIp { get; init; }
    public string? Uri { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public long? LogEntryId { get; init; }
}

public sealed record SecurityAnalysisResult
{
    public int Score { get; init; }
    public SecuritySeverity Severity { get; init; }
    public bool LikelyScanner { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<SecurityFinding> Findings { get; init; } = [];
}
