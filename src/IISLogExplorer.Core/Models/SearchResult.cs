namespace IISLogExplorer.Core.Models;

public sealed record SearchResult
{
    public required LogEntry Entry { get; init; }
    public required string SourceFile { get; init; }
    public string? SourcePath { get; init; }
    public bool IsIndexed { get; init; }
}

public sealed record SearchStatistics
{
    public long ResultCount { get; init; }
    public long ScannedLines { get; init; }
    public int ScannedFiles { get; init; }
    public bool IsComplete { get; init; }
}
