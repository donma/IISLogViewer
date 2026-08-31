namespace IISLogExplorer.Core.Models;

public sealed record LogEntry
{
    public long Id { get; init; }
    public long SourceId { get; init; }
    public long FileId { get; init; }
    public long LineNumber { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
    public DateTimeOffset? TimestampLocal { get; init; }
    public string? ServerIp { get; init; }
    public string? Method { get; init; }
    public string? UriStem { get; init; }
    public string? UriQuery { get; init; }
    public int? ServerPort { get; init; }
    public string? Username { get; init; }
    public string? ClientIp { get; init; }
    public string? ResolvedClientIp { get; init; }
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public int? StatusCode { get; init; }
    public int? SubStatusCode { get; init; }
    public int? Win32Status { get; init; }
    public int? TimeTakenMs { get; init; }
    public long? BytesSent { get; init; }
    public long? BytesReceived { get; init; }
    public string? Host { get; init; }
    public string? ProtocolVersion { get; init; }
    public string? Cookie { get; init; }
    public string? ForwardedFor { get; init; }
    public string? RealClientIp { get; init; }
    public string? RawLine { get; init; }
    public IReadOnlyDictionary<string, string?> AdditionalFields { get; init; } = new Dictionary<string, string?>();

    public string DisplayUrl => string.IsNullOrWhiteSpace(UriQuery) || UriQuery == "-" ? UriStem ?? string.Empty : $"{UriStem}?{UriQuery}";
    public string DisplayStatus => StatusCode is null ? string.Empty : SubStatusCode is null ? StatusCode.Value.ToString() : $"{StatusCode}.{SubStatusCode}";
}
