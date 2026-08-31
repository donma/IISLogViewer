namespace IISLogExplorer.Core.Models;

public sealed record LogSource
{
    public long Id { get; init; }
    public required LogSourceType SourceType { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public bool IncludeSubfolders { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; init; }

    public override string ToString() => DisplayName;
}
