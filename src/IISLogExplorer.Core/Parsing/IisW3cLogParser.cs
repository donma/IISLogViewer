using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;

namespace IISLogExplorer.Core.Parsing;

public sealed class IisW3cLogParser : IIisLogParser
{
    private static readonly ConcurrentDictionary<string, FieldDefinition[]> HeaderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly FieldsHeaderParser _headerParser;
    private readonly ClientIpResolver _ipResolver;

    public IisW3cLogParser(FieldsHeaderParser headerParser, ClientIpResolver ipResolver)
    {
        _headerParser = headerParser;
        _ipResolver = ipResolver;
    }

    public static void InvalidateHeaderCache(string? path = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            HeaderCache.Clear();
        }
        else
        {
            HeaderCache.TryRemove(path, out _);
        }
    }

    public async IAsyncEnumerable<LogEntry> ParseAsync(
        string path,
        long sourceId,
        long fileId = 0,
        long startByteOffset = 0,
        long startLineNumber = 0,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var record in ParseRecordsAsync(path, sourceId, fileId, startByteOffset, startLineNumber, cancellationToken).ConfigureAwait(false))
        {
            yield return record.Entry;
        }
    }

    public async IAsyncEnumerable<ParsedLogRecord> ParseRecordsAsync(
        string path,
        long sourceId,
        long fileId = 0,
        long startByteOffset = 0,
        long startLineNumber = 0,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var encoding = await DetectEncodingAsync(path, cancellationToken).ConfigureAwait(false);
        var fields = Array.Empty<FieldDefinition>();
        if (startByteOffset > 0)
        {
            if (!HeaderCache.TryGetValue(path, out fields!))
            {
                fields = await ReadFieldsBeforeOffsetAsync(path, encoding, startByteOffset, cancellationToken).ConfigureAwait(false);
                if (fields.Length > 0)
                {
                    HeaderCache[path] = fields;
                }
            }
        }
        var lineNumber = startLineNumber;

        await foreach (var line in ReadLinesAsync(path, encoding, startByteOffset, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var text = line.Text.TrimStart('\uFEFF');
            if (text.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                fields = _headerParser.Parse(text).ToArray();
                continue;
            }

            if (text.Length == 0 || text[0] == '#' || fields.Length == 0)
            {
                continue;
            }

            var values = W3cLineTokenizer.Tokenize(text);
            if (values.Count == 0 || !IsRequestRecord(values, fields))
            {
                continue;
            }

            LogEntry entry;
            try
            {
                entry = Map(values, fields, text, sourceId, fileId, lineNumber);
            }
            catch
            {
                continue;
            }

            yield return new ParsedLogRecord(entry, line.StartOffset, line.EndOffset, line.IsComplete);
        }
    }

    private static bool IsRequestRecord(IReadOnlyList<string> values, IReadOnlyList<FieldDefinition> fields)
    {
        var dateIndex = FindFieldIndex(fields, "date");
        if (dateIndex >= 0 && dateIndex < values.Count)
        {
            var raw = values[dateIndex];
            if (string.IsNullOrWhiteSpace(raw) || raw == "-")
            {
                return true;
            }

            return DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        return true;
    }

    private LogEntry Map(IReadOnlyList<string> values, IReadOnlyList<FieldDefinition> fields, string rawLine, long sourceId, long fileId, long lineNumber)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            map[fields[index].Name] = index < values.Count ? NullIfMissing(values[index]) : null;
        }

        var timestampUtc = ParseTimestamp(Get(map, "date"), Get(map, "time"));
        var entry = new LogEntry
        {
            SourceId = sourceId,
            FileId = fileId,
            LineNumber = lineNumber,
            TimestampUtc = timestampUtc,
            TimestampLocal = timestampUtc?.ToLocalTime(),
            ServerIp = Get(map, "s-ip"),
            Method = Get(map, "cs-method"),
            UriStem = Get(map, "cs-uri-stem"),
            UriQuery = Get(map, "cs-uri-query"),
            ServerPort = ParseInt(Get(map, "s-port")),
            Username = Get(map, "cs-username"),
            ClientIp = Get(map, "c-ip"),
            UserAgent = Get(map, "cs(User-Agent)", "cs-user-agent"),
            Referer = Get(map, "cs(Referer)", "cs-referer"),
            StatusCode = ParseInt(Get(map, "sc-status")),
            SubStatusCode = ParseInt(Get(map, "sc-substatus")),
            Win32Status = ParseInt(Get(map, "sc-win32-status")),
            TimeTakenMs = ParseInt(Get(map, "time-taken")),
            BytesSent = ParseLong(Get(map, "sc-bytes")),
            BytesReceived = ParseLong(Get(map, "cs-bytes")),
            Host = Get(map, "s-host", "cs-host"),
            ProtocolVersion = Get(map, "cs-version"),
            Cookie = Get(map, "cs(Cookie)", "cs-cookie"),
            ForwardedFor = Get(map, "X-Forwarded-For", "x-forwarded-for"),
            RealClientIp = Get(map, "X-Real-IP", "x-real-ip"),
            RawLine = rawLine,
            AdditionalFields = map
        };

        return entry with { ResolvedClientIp = _ipResolver.Resolve(entry) };
    }

    private static int FindFieldIndex(IReadOnlyList<FieldDefinition> fields, string name)
    {
        var normalized = Normalize(name);
        for (var index = 0; index < fields.Count; index++)
        {
            if (Normalize(fields[index].Name) == normalized)
            {
                return index;
            }
        }

        return -1;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, params string[] names)
    {
        foreach (var name in names)
        {
            var normalizedName = Normalize(name);
            foreach (var pair in values)
            {
                if (Normalize(pair.Key) == normalizedName)
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    private static string Normalize(string value) => value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    private static string? NullIfMissing(string? value) => string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static long? ParseLong(string? value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateTimeOffset? ParseTimestamp(string? date, string? time)
    {
        if (date is null || time is null || !DateTime.TryParseExact($"{date} {time}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            return null;
        }

        return new DateTimeOffset(value, TimeSpan.Zero);
    }

    private static async Task<Encoding> DetectEncodingAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = new byte[4096];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bytes.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(bytes, 0, read);
            return utf8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default;
        }
    }

    private static async IAsyncEnumerable<RawLine> ReadLinesAsync(
        string path,
        Encoding encoding,
        long startByteOffset,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (startByteOffset > 0)
        {
            stream.Position = Math.Min(startByteOffset, stream.Length);
        }

        var buffer = new byte[64 * 1024];
        var lineBytes = new List<byte>(256);
        var lineStart = stream.Position;
        var position = stream.Position;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (lineBytes.Count > 0)
                {
                    yield return new RawLine(Decode(lineBytes, encoding), lineStart, position, false);
                }

                yield break;
            }

            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                position++;
                if (value == (byte)'\n')
                {
                    if (lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r')
                    {
                        lineBytes.RemoveAt(lineBytes.Count - 1);
                    }

                    yield return new RawLine(Decode(lineBytes, encoding), lineStart, position, true);
                    lineBytes.Clear();
                    lineStart = position;
                }
                else
                {
                    lineBytes.Add(value);
                }
            }
        }
    }

    private static string Decode(List<byte> bytes, Encoding encoding)
    {
        var data = bytes.ToArray();
        try
        {
            return encoding.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(data);
        }
    }
    private sealed record RawLine(string Text, long StartOffset, long EndOffset, bool IsComplete);

    private static async Task<FieldDefinition[]> ReadFieldsBeforeOffsetAsync(string path, Encoding encoding, long startByteOffset, CancellationToken cancellationToken)
    {
        var reader = new FieldsHeaderParser();
        var latest = Array.Empty<FieldDefinition>();
        await foreach (var line in ReadLinesAsync(path, encoding, 0, cancellationToken).ConfigureAwait(false))
        {
            if (line.StartOffset >= startByteOffset)
            {
                break;
            }

            var text = line.Text.TrimStart('\uFEFF');
            if (text.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                latest = reader.Parse(text).ToArray();
            }
        }

        return latest;
    }
}