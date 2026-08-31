using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Parsing;

public sealed record ParsedLogRecord(LogEntry Entry, long StartByteOffset, long EndByteOffset, bool IsCompleteLine);

public interface IIisLogParser
{
    IAsyncEnumerable<LogEntry> ParseAsync(string path, long sourceId, long fileId = 0, long startByteOffset = 0, long startLineNumber = 0, string? fieldsHeader = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ParsedLogRecord> ParseRecordsAsync(string path, long sourceId, long fileId = 0, long startByteOffset = 0, long startLineNumber = 0, string? fieldsHeader = null, CancellationToken cancellationToken = default);
}
