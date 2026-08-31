using System.Globalization;
using IISLogExplorer.Core.Models;
using Microsoft.Data.Sqlite;

namespace IISLogExplorer.Infrastructure.Database;

public sealed class LogEntryRepository
{
    private readonly SqliteConnectionFactory _factory;

    public LogEntryRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InsertBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO LogEntries
            (SourceId, FileId, LineNumber, TimestampUtc, TimestampLocal, ServerIp, Method, UriStem, UriQuery, ServerPort, Username, ClientIp, ResolvedClientIp, UserAgent, Referer, StatusCode, SubStatusCode, Win32Status, TimeTakenMs, BytesSent, BytesReceived, Host, ProtocolVersion, Cookie, ForwardedFor, RawLine)
            VALUES ($source, $file, $line, $utc, $local, $server, $method, $stem, $query, $port, $username, $client, $resolved, $agent, $referer, $status, $substatus, $win32, $taken, $sent, $received, $host, $protocol, $cookie, $forwarded, $raw)
            """;
        foreach (var entry in entries)
        {
            command.Parameters.Clear();
            AddParameters(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, IEnumerable<long>? fileIds = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var conditions = new List<string> { "e.SourceId = $source" };
        command.Parameters.AddWithValue("$source", request.Source.Id);
        AddFilterConditions(command, conditions, request);
        if (fileIds is not null)
        {
            var ids = fileIds.ToArray();
            if (ids.Length == 0)
            {
                yield break;
            }

            var names = new string[ids.Length];
            for (var index = 0; index < ids.Length; index++)
            {
                names[index] = $"$file{index}";
                command.Parameters.AddWithValue(names[index], ids[index]);
            }

            conditions.Add($"e.FileId IN ({string.Join(',', names)})");
        }

        command.CommandText = $"""
            SELECT e.Id, e.SourceId, e.FileId, e.LineNumber, e.TimestampUtc, e.TimestampLocal, e.ServerIp, e.Method, e.UriStem, e.UriQuery, e.ServerPort, e.Username, e.ClientIp, e.ResolvedClientIp, e.UserAgent, e.Referer, e.StatusCode, e.SubStatusCode, e.Win32Status, e.TimeTakenMs, e.BytesSent, e.BytesReceived, e.Host, e.ProtocolVersion, e.Cookie, e.ForwardedFor, e.RawLine, f.FileName, f.FullPath
            FROM LogEntries e JOIN LogFiles f ON f.Id = e.FileId
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY e.TimestampUtc DESC, e.Id DESC
            LIMIT $max;
            """;
        command.Parameters.AddWithValue("$max", request.MaxResults);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new SearchResult { Entry = Read(reader), SourceFile = reader.GetString(27), SourcePath = reader.GetString(28), IsIndexed = true };
        }
    }

    public async IAsyncEnumerable<LogEntry> GetEntriesAsync(SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in SearchAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return result.Entry;
        }
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LogEntries";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LogEntries; DELETE FROM LogFiles;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddFilterConditions(SqliteCommand command, List<string> conditions, SearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            conditions.Add("(e.RawLine LIKE $keyword ESCAPE '\\' OR e.ClientIp LIKE $keyword ESCAPE '\\' OR e.ResolvedClientIp LIKE $keyword ESCAPE '\\' OR e.UriStem LIKE $keyword ESCAPE '\\' OR e.UriQuery LIKE $keyword ESCAPE '\\' OR e.UserAgent LIKE $keyword ESCAPE '\\' OR e.Method LIKE $keyword ESCAPE '\\' OR e.StatusCode = $statusKeyword)");
            command.Parameters.AddWithValue("$keyword", $"%{EscapeLike(request.Keyword)}%");
            command.Parameters.AddWithValue("$statusKeyword", int.TryParse(request.Keyword, out var status) ? status : -1);
        }

        if (request.From is not null)
        {
            conditions.Add("e.TimestampUtc >= $from");
            command.Parameters.AddWithValue("$from", request.From.Value.UtcDateTime.ToString("O"));
        }

        if (request.To is not null)
        {
            conditions.Add("e.TimestampUtc <= $to");
            command.Parameters.AddWithValue("$to", request.To.Value.UtcDateTime.ToString("O"));
        }

        if (request.TimeFrom is not null)
        {
            conditions.Add("time(e.TimestampUtc) >= $timeFrom");
            command.Parameters.AddWithValue("$timeFrom", request.TimeFrom.Value.ToString(@"hh\:mm\:ss"));
        }

        if (request.TimeTo is not null)
        {
            conditions.Add("time(e.TimestampUtc) <= $timeTo");
            command.Parameters.AddWithValue("$timeTo", request.TimeTo.Value.ToString(@"hh\:mm\:ss"));
        }

        if (!string.IsNullOrWhiteSpace(request.Method))
        {
            conditions.Add("e.Method = $method");
            command.Parameters.AddWithValue("$method", request.Method);
        }

        if (request.StatusCode is not null)
        {
            conditions.Add("e.StatusCode = $status");
            command.Parameters.AddWithValue("$status", request.StatusCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ClientIp))
        {
            conditions.Add("(e.ClientIp LIKE $client ESCAPE '\\' OR e.ResolvedClientIp LIKE $client ESCAPE '\\')");
            command.Parameters.AddWithValue("$client", $"%{EscapeLike(request.ClientIp)}%");
        }

        if (!string.IsNullOrWhiteSpace(request.UrlContains))
        {
            conditions.Add("(e.UriStem LIKE $url ESCAPE '\\' OR e.UriQuery LIKE $url ESCAPE '\\')");
            command.Parameters.AddWithValue("$url", $"%{EscapeLike(request.UrlContains)}%");
        }

        if (!string.IsNullOrWhiteSpace(request.UserAgentContains))
        {
            conditions.Add("e.UserAgent LIKE $agent ESCAPE '\\'");
            command.Parameters.AddWithValue("$agent", $"%{EscapeLike(request.UserAgentContains)}%");
        }

        if (request.MinTimeTakenMs is not null)
        {
            conditions.Add("e.TimeTakenMs >= $minTime");
            command.Parameters.AddWithValue("$minTime", request.MinTimeTakenMs.Value);
        }

        if (request.MaxTimeTakenMs is not null)
        {
            conditions.Add("e.TimeTakenMs <= $maxTime");
            command.Parameters.AddWithValue("$maxTime", request.MaxTimeTakenMs.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            conditions.Add("e.Username LIKE $username ESCAPE '\\'");
            command.Parameters.AddWithValue("$username", $"%{EscapeLike(request.Username)}%");
        }
    }

    private static void AddParameters(SqliteCommand command, LogEntry entry)
    {
        Add(command, "$source", entry.SourceId); Add(command, "$file", entry.FileId); Add(command, "$line", entry.LineNumber);
        Add(command, "$utc", entry.TimestampUtc?.UtcDateTime.ToString("O")); Add(command, "$local", entry.TimestampLocal?.ToString("O")); Add(command, "$server", entry.ServerIp); Add(command, "$method", entry.Method); Add(command, "$stem", entry.UriStem); Add(command, "$query", entry.UriQuery); Add(command, "$port", entry.ServerPort); Add(command, "$username", entry.Username); Add(command, "$client", entry.ClientIp); Add(command, "$resolved", entry.ResolvedClientIp); Add(command, "$agent", entry.UserAgent); Add(command, "$referer", entry.Referer); Add(command, "$status", entry.StatusCode); Add(command, "$substatus", entry.SubStatusCode); Add(command, "$win32", entry.Win32Status); Add(command, "$taken", entry.TimeTakenMs); Add(command, "$sent", entry.BytesSent); Add(command, "$received", entry.BytesReceived); Add(command, "$host", entry.Host); Add(command, "$protocol", entry.ProtocolVersion); Add(command, "$cookie", entry.Cookie); Add(command, "$forwarded", entry.ForwardedFor); Add(command, "$raw", entry.RawLine);
    }

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static LogEntry Read(SqliteDataReader reader)
    {
        var additional = new Dictionary<string, string?>();
        return new LogEntry
        {
            Id = reader.GetInt64(0), SourceId = reader.GetInt64(1), FileId = reader.GetInt64(2), LineNumber = reader.GetInt64(3),
            TimestampUtc = ReadDate(reader, 4), TimestampLocal = ReadDate(reader, 5), ServerIp = ReadString(reader, 6), Method = ReadString(reader, 7), UriStem = ReadString(reader, 8), UriQuery = ReadString(reader, 9), ServerPort = ReadInt(reader, 10), Username = ReadString(reader, 11), ClientIp = ReadString(reader, 12), ResolvedClientIp = ReadString(reader, 13), UserAgent = ReadString(reader, 14), Referer = ReadString(reader, 15), StatusCode = ReadInt(reader, 16), SubStatusCode = ReadInt(reader, 17), Win32Status = ReadInt(reader, 18), TimeTakenMs = ReadInt(reader, 19), BytesSent = ReadLong(reader, 20), BytesReceived = ReadLong(reader, 21), Host = ReadString(reader, 22), ProtocolVersion = ReadString(reader, 23), Cookie = ReadString(reader, 24), ForwardedFor = ReadString(reader, 25), RawLine = ReadString(reader, 26), AdditionalFields = additional
        };
    }

    private static string? ReadString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static int? ReadInt(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static long? ReadLong(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
