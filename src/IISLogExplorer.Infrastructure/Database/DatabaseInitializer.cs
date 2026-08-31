namespace IISLogExplorer.Infrastructure.Database;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _factory;

    public DatabaseInitializer(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA cache_size=-32768;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS Sources (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceType INTEGER NOT NULL,
                DisplayName TEXT NOT NULL,
                Path TEXT NOT NULL,
                IncludeSubfolders INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NULL,
                UNIQUE(SourceType, Path)
            );
            CREATE TABLE IF NOT EXISTS LogFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId INTEGER NOT NULL,
                FullPath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                LastWriteUtc TEXT NOT NULL,
                IndexedLength INTEGER NOT NULL DEFAULT 0,
                IndexedLineCount INTEGER NOT NULL DEFAULT 0,
                IsFullyIndexed INTEGER NOT NULL DEFAULT 0,
                HeaderHash TEXT NULL,
                FieldsHeader TEXT NULL,
                FileFingerprint TEXT NULL,
                LastIndexedAt TEXT NULL,
                UNIQUE(SourceId, FullPath)
            );
            CREATE TABLE IF NOT EXISTS LogEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId INTEGER NOT NULL,
                FileId INTEGER NOT NULL,
                LineNumber INTEGER NOT NULL,
                TimestampUtc TEXT NULL,
                TimestampLocal TEXT NULL,
                ServerIp TEXT NULL,
                Method TEXT NULL,
                UriStem TEXT NULL,
                UriQuery TEXT NULL,
                ServerPort INTEGER NULL,
                Username TEXT NULL,
                ClientIp TEXT NULL,
                ResolvedClientIp TEXT NULL,
                UserAgent TEXT NULL,
                Referer TEXT NULL,
                StatusCode INTEGER NULL,
                SubStatusCode INTEGER NULL,
                Win32Status INTEGER NULL,
                TimeTakenMs INTEGER NULL,
                BytesSent INTEGER NULL,
                BytesReceived INTEGER NULL,
                Host TEXT NULL,
                ProtocolVersion TEXT NULL,
                Cookie TEXT NULL,
                ForwardedFor TEXT NULL,
                RawLine TEXT NULL,
                UNIQUE(FileId, LineNumber)
            );
            CREATE INDEX IF NOT EXISTS IX_LogEntries_TimestampUtc ON LogEntries(TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_ClientIp ON LogEntries(ClientIp);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_ResolvedClientIp ON LogEntries(ResolvedClientIp);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_StatusCode ON LogEntries(StatusCode);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_UriStem ON LogEntries(UriStem);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_TimeTaken ON LogEntries(TimeTakenMs);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_Source_Timestamp ON LogEntries(SourceId, TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_File_Line ON LogEntries(FileId, LineNumber);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await MigrateLegacySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await TryDropUnusedFtsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateLegacySchemaAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, "LogFiles", "FieldsHeader", cancellationToken).ConfigureAwait(false))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE LogFiles ADD COLUMN FieldsHeader TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ColumnExistsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task TryDropUnusedFtsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS LogEntriesFts_ai;
            DROP TRIGGER IF EXISTS LogEntriesFts_ad;
            DROP TRIGGER IF EXISTS LogEntriesFts_au;
            DROP TABLE IF EXISTS LogEntriesFts;
            """;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
        }
    }
}
