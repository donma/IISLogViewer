namespace IISLogExplorer.Core.Models;

public sealed record SearchRequest
{
    public required LogSource Source { get; init; }
    public string? Keyword { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public TimeSpan? TimeFrom { get; init; }
    public TimeSpan? TimeTo { get; init; }
    public string? Method { get; init; }
    public int? StatusCode { get; init; }
    public string? ClientIp { get; init; }
    public string? UrlContains { get; init; }
    public string? UserAgentContains { get; init; }
    public int? MinTimeTakenMs { get; init; }
    public int? MaxTimeTakenMs { get; init; }
    public string? Username { get; init; }
    public int PageSize { get; init; } = 200;
    public int MaxResults { get; init; } = 10000;
}
