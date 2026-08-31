namespace IISLogExplorer.Core.Models;

public sealed record AppSettings
{
    public int DefaultPageSize { get; init; } = 200;
    public int IndexBatchSize { get; init; } = 2000;
    public int? DatabaseRetentionDays { get; init; }
    public int MaxSearchResults { get; init; } = 10000;
    public int RealtimeRefreshIntervalSeconds { get; init; } = 5;
    public int SlowRequestThresholdMs { get; init; } = 1000;
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public string DisplayTimeZone { get; init; } = "Local";
    public bool IncludeSubfolders { get; init; }
    public bool AllowIdleCleanup { get; init; }
    public string? DatabaseRetention { get; init; }
    public string ClientIpHeaderPriorityText { get; init; } = "CF-Connecting-IP, True-Client-IP, X-Forwarded-For, X-Real-IP, cnd-src-ip, c-ip";
    public IReadOnlyList<string> ClientIpHeaderPriority { get; init; } = ["CF-Connecting-IP", "True-Client-IP", "X-Forwarded-For", "X-Real-IP", "cnd-src-ip", "c-ip"];
}
