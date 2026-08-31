using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Database;

public sealed class SourceRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SourceRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<LogSource> SaveAsync(LogSource source, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sources (SourceType, DisplayName, Path, IncludeSubfolders, CreatedAt, LastUsedAt)
            VALUES ($type, $name, $path, $include, $created, $used)
            ON CONFLICT(SourceType, Path) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                IncludeSubfolders = excluded.IncludeSubfolders,
                LastUsedAt = excluded.LastUsedAt;
            SELECT Id, SourceType, DisplayName, Path, IncludeSubfolders, CreatedAt, LastUsedAt
            FROM Sources WHERE SourceType = $type AND Path = $path;
            """;
        AddParameters(command, source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Read(reader);
    }

    public async Task<IReadOnlyList<LogSource>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = new List<LogSource>();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SourceType, DisplayName, Path, IncludeSubfolders, CreatedAt, LastUsedAt FROM Sources ORDER BY COALESCE(LastUsedAt, CreatedAt) DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Read(reader));
        }

        return result;
    }

    private static void AddParameters(Microsoft.Data.Sqlite.SqliteCommand command, LogSource source)
    {
        command.Parameters.AddWithValue("$type", (int)source.SourceType);
        command.Parameters.AddWithValue("$name", source.DisplayName);
        command.Parameters.AddWithValue("$path", source.Path);
        command.Parameters.AddWithValue("$include", source.IncludeSubfolders ? 1 : 0);
        command.Parameters.AddWithValue("$created", source.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$used", DateTimeOffset.UtcNow.ToString("O"));
    }

    internal static LogSource Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SourceType = (LogSourceType)reader.GetInt32(1),
        DisplayName = reader.GetString(2),
        Path = reader.GetString(3),
        IncludeSubfolders = reader.GetInt32(4) != 0,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(5)),
        LastUsedAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))
    };
}
