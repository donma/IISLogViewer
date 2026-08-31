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
        var source = command.Parameters.Add("$source", SqliteType.Integer);
        var file = command.Parameters.Add("$file", SqliteType.Integer);
        var line = command.Parameters.Add("$line", SqliteType.Integer);
        var utc = command.Parameters.Add("$utc", SqliteType.Text);
        var local = command.Parameters.Add("$local", SqliteType.Text);
        var server = command.Parameters.Add("$server", SqliteType.Text);
        var method = command.Parameters.Add("$method", SqliteType.Text);
        var stem = command.Parameters.Add("$stem", SqliteType.Text);
        var query = command.Parameters.Add("$query", SqliteType.Text);
        var port = command.Parameters.Add("$port", SqliteType.Integer);
        var username = command.Parameters.Add("$username", SqliteType.Text);
        var client = command.Parameters.Add("$client", SqliteType.Text);
        var resolved = command.Parameters.Add("$resolved", SqliteType.Text);
        var agent = command.Parameters.Add("$agent", SqliteType.Text);
        var referer = command.Parameters.Add("$referer", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Integer);
        var substatus = command.Parameters.Add("$substatus", SqliteType.Integer);
        var win32 = command.Parameters.Add("$win32", SqliteType.Integer);
        var taken = command.Parameters.Add("$taken", SqliteType.Integer);
        var sent = command.Parameters.Add("$sent", SqliteType.Integer);
        var received = command.Parameters.Add("$received", SqliteType.Integer);
        var host = command.Parameters.Add("$host", SqliteType.Text);
        var protocol = command.Parameters.Add("$protocol", SqliteType.Text);
        var cookie = command.Parameters.Add("$cookie", SqliteType.Text);
        var forwarded = command.Parameters.Add("$forwarded", SqliteType.Text);
        var raw = command.Parameters.Add("$raw", SqliteType.Text);
        foreach (var entry in entries)
        {
            source.Value = entry.SourceId;
            file.Value = entry.FileId;
            line.Value = entry.LineNumber;
            utc.Value = entry.TimestampUtc?.UtcDateTime.ToString("O") ?? (object)DBNull.Value;
            local.Value = entry.TimestampLocal?.ToString("O") ?? (object)DBNull.Value;
            server.Value = entry.ServerIp ?? (object)DBNull.Value;
            method.Value = entry.Method ?? (object)DBNull.Value;
            stem.Value = entry.UriStem ?? (object)DBNull.Value;
            query.Value = entry.UriQuery ?? (object)DBNull.Value;
            port.Value = entry.ServerPort ?? (object)DBNull.Value;
            username.Value = entry.Username ?? (object)DBNull.Value;
            client.Value = entry.ClientIp ?? (object)DBNull.Value;
            resolved.Value = entry.ResolvedClientIp ?? (object)DBNull.Value;
            agent.Value = entry.UserAgent ?? (object)DBNull.Value;
            referer.Value = entry.Referer ?? (object)DBNull.Value;
            status.Value = entry.StatusCode ?? (object)DBNull.Value;
            substatus.Value = entry.SubStatusCode ?? (object)DBNull.Value;
            win32.Value = entry.Win32Status ?? (object)DBNull.Value;
            taken.Value = entry.TimeTakenMs ?? (object)DBNull.Value;
            sent.Value = entry.BytesSent ?? (object)DBNull.Value;
            received.Value = entry.BytesReceived ?? (object)DBNull.Value;
            host.Value = entry.Host ?? (object)DBNull.Value;
            protocol.Value = entry.ProtocolVersion ?? (object)DBNull.Value;
            cookie.Value = entry.Cookie ?? (object)DBNull.Value;
            forwarded.Value = entry.ForwardedFor ?? (object)DBNull.Value;
            raw.Value = entry.RawLine ?? (object)DBNull.Value;
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
            // 純每日 time-of-day 過濾會橫跨多天（每天相同時段），無法轉成單一連續 UTC range，
            // 故保留 time() 分支；若同時有 From/To 日期過濾，SQLite 會先依 TimestampUtc index
            // 收斂日期範圍後再套用 time()，避免全表掃描。
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

    private static LogEntry Read(SqliteDataReader reader)
    {
        return new LogEntry
        {
            Id = reader.GetInt64(0), SourceId = reader.GetInt64(1), FileId = reader.GetInt64(2), LineNumber = reader.GetInt64(3),
            TimestampUtc = ReadDate(reader, 4), TimestampLocal = ReadDate(reader, 5), ServerIp = ReadString(reader, 6), Method = ReadString(reader, 7), UriStem = ReadString(reader, 8), UriQuery = ReadString(reader, 9), ServerPort = ReadInt(reader, 10), Username = ReadString(reader, 11), ClientIp = ReadString(reader, 12), ResolvedClientIp = ReadString(reader, 13), UserAgent = ReadString(reader, 14), Referer = ReadString(reader, 15), StatusCode = ReadInt(reader, 16), SubStatusCode = ReadInt(reader, 17), Win32Status = ReadInt(reader, 18), TimeTakenMs = ReadInt(reader, 19), BytesSent = ReadLong(reader, 20), BytesReceived = ReadLong(reader, 21), Host = ReadString(reader, 22), ProtocolVersion = ReadString(reader, 23), Cookie = ReadString(reader, 24), ForwardedFor = ReadString(reader, 25), RawLine = ReadString(reader, 26)
        };
    }

    private static string? ReadString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static int? ReadInt(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static long? ReadLong(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
