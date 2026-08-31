using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Database;

public sealed class LogFileRepository
{
    private readonly SqliteConnectionFactory _factory;

    public LogFileRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<LogFileInfo> UpsertAsync(long sourceId, FileInfo file, string fingerprint, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LogFiles (SourceId, FullPath, FileName, FileSize, LastWriteUtc, FileFingerprint)
            VALUES ($source, $path, $name, $size, $write, $fingerprint)
            ON CONFLICT(SourceId, FullPath) DO NOTHING;
            SELECT Id, SourceId, FullPath, FileName, FileSize, LastWriteUtc, IndexedLength, IndexedLineCount, IsFullyIndexed, HeaderHash, FieldsHeader, FileFingerprint, LastIndexedAt
            FROM LogFiles WHERE SourceId = $source AND FullPath = $path;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$path", file.FullName);
        command.Parameters.AddWithValue("$name", file.Name);
        command.Parameters.AddWithValue("$size", file.Length);
        command.Parameters.AddWithValue("$write", file.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Read(reader);
    }

    public async Task<IReadOnlyList<LogFileInfo>> GetBySourceAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        var result = new List<LogFileInfo>();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SourceId, FullPath, FileName, FileSize, LastWriteUtc, IndexedLength, IndexedLineCount, IsFullyIndexed, HeaderHash, FieldsHeader, FileFingerprint, LastIndexedAt FROM LogFiles WHERE SourceId = $source ORDER BY LastWriteUtc DESC";
        command.Parameters.AddWithValue("$source", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public async Task<LogFileInfo?> FindByPathAsync(long sourceId, string fullPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SourceId, FullPath, FileName, FileSize, LastWriteUtc, IndexedLength, IndexedLineCount, IsFullyIndexed, HeaderHash, FieldsHeader, FileFingerprint, LastIndexedAt FROM LogFiles WHERE SourceId = $source AND FullPath = $path";
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$path", fullPath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task UpdateProgressAsync(long fileId, long fileSize, DateTime lastWriteUtc, long indexedLength, long lineCount, bool complete, string fingerprint, string? fieldsHeader, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE LogFiles SET FileSize = $size, LastWriteUtc = $write, IndexedLength = $length, IndexedLineCount = $lines, IsFullyIndexed = $complete, FileFingerprint = $fingerprint, FieldsHeader = $fields, LastIndexedAt = $indexed WHERE Id = $id";
        command.Parameters.AddWithValue("$size", fileSize);
        command.Parameters.AddWithValue("$write", lastWriteUtc.ToString("O"));
        command.Parameters.AddWithValue("$length", indexedLength);
        command.Parameters.AddWithValue("$lines", lineCount);
        command.Parameters.AddWithValue("$complete", complete ? 1 : 0);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$fields", fieldsHeader ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$indexed", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", fileId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetAsync(long fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LogEntries WHERE FileId = $id; UPDATE LogFiles SET IndexedLength = 0, IndexedLineCount = 0, IsFullyIndexed = 0, LastIndexedAt = NULL WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", fileId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LogFiles";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static LogFileInfo Read(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var size = reader.GetInt64(4);
        var indexed = reader.GetInt64(6);
        var complete = reader.GetInt32(8) != 0;
        var lastWrite = DateTimeOffset.Parse(reader.GetString(5));
        DateTimeOffset? lastIndexed = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12));
        var state = complete && lastIndexed is not null
            ? lastWrite > lastIndexed ? IndexState.Outdated : IndexState.Indexed
            : indexed > 0 ? IndexState.Partial : IndexState.NotIndexed;
        return new LogFileInfo
        {
            Id = reader.GetInt64(0), SourceId = reader.GetInt64(1), FullPath = reader.GetString(2), FileName = reader.GetString(3), FileSize = size,
            LastWriteUtc = lastWrite, IndexedLength = indexed, IndexedLineCount = reader.GetInt64(7), IsFullyIndexed = complete,
            HeaderHash = reader.IsDBNull(9) ? null : reader.GetString(9), FieldsHeader = reader.IsDBNull(10) ? null : reader.GetString(10), FileFingerprint = reader.IsDBNull(11) ? null : reader.GetString(11), LastIndexedAt = lastIndexed, State = state
        };
    }
}
