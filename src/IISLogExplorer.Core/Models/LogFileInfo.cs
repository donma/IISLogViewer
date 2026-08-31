namespace IISLogExplorer.Core.Models;

public sealed record LogFileInfo
{
    public long Id { get; init; }
    public required long SourceId { get; init; }
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public DateTimeOffset LastWriteUtc { get; init; }
    public long IndexedLength { get; init; }
    public long IndexedLineCount { get; init; }
    public bool IsFullyIndexed { get; init; }
    public string? HeaderHash { get; init; }
    public string? FieldsHeader { get; init; }
    public string? FileFingerprint { get; init; }
    public DateTimeOffset? LastIndexedAt { get; init; }
    public IndexState State { get; init; }
}
