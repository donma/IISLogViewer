using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;

namespace IISLogExplorer.Core.Parsing;

public sealed class IisW3cLogParser : IIisLogParser
{
    private const int HeaderCacheMax = 1024;
    private static readonly ConcurrentDictionary<string, string> HeaderCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object HeaderCacheGate = new();
    private readonly FieldsHeaderParser _headerParser;
    private readonly ClientIpResolver _ipResolver;
    private readonly ParserOptions _options;

    public IisW3cLogParser(FieldsHeaderParser headerParser, ClientIpResolver ipResolver, ParserOptions? options = null)
    {
        _headerParser = headerParser;
        _ipResolver = ipResolver;
        _options = options ?? new ParserOptions();
    }

    public static void InvalidateHeaderCache(string? path = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            lock (HeaderCacheGate)
            {
                HeaderCache.Clear();
            }
            return;
        }

        HeaderCache.TryRemove(path, out _);
    }

    public static string? GetActiveHeader(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return HeaderCache.TryGetValue(path, out var header) ? header : null;
    }

    private static void CacheHeader(string path, string header)
    {
        if (!HeaderCache.TryGetValue(path, out _) && HeaderCache.Count >= HeaderCacheMax)
        {
            lock (HeaderCacheGate)
            {
                if (HeaderCache.Count >= HeaderCacheMax)
                {
                    HeaderCache.Clear();
                }
            }
        }

        lock (HeaderCacheGate)
        {
            HeaderCache[path] = header;
        }
    }

    public async IAsyncEnumerable<LogEntry> ParseAsync(
        string path,
        long sourceId,
        long fileId = 0,
        long startByteOffset = 0,
        long startLineNumber = 0,
        string? fieldsHeader = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var record in ParseRecordsAsync(path, sourceId, fileId, startByteOffset, startLineNumber, fieldsHeader, cancellationToken).ConfigureAwait(false))
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
        string? fieldsHeader = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var encoding = await DetectEncodingAsync(path, cancellationToken).ConfigureAwait(false);
        var fieldsMap = new W3cFieldMap();
        if (startByteOffset > 0)
        {
            var headerLine = fieldsHeader;
            if (headerLine is null && !HeaderCache.TryGetValue(path, out headerLine))
            {
                headerLine = await ReadFieldsHeaderAsync(path, encoding, startByteOffset, cancellationToken).ConfigureAwait(false);
            }

            if (headerLine is not null)
            {
                fieldsMap = BuildFieldsMap(headerLine);
                CacheHeader(path, headerLine);
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
                fieldsMap = BuildFieldsMap(text);
                CacheHeader(path, text);
                continue;
            }

            if (text.Length == 0 || text[0] == '#' || !fieldsMap.HasFields)
            {
                continue;
            }

            var tokens = W3cLineTokenizer.Tokenize(text);
            if (tokens.Count == 0 || !IsRequestRecord(tokens, fieldsMap))
            {
                continue;
            }

            var extras = BuildExtras(tokens, fieldsMap, _options.IncludeAdditionalFields);
            LogEntry entry;
            try
            {
                entry = Map(tokens, fieldsMap, extras, text, sourceId, fileId, lineNumber);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                continue;
            }

            yield return new ParsedLogRecord(entry, line.StartOffset, line.EndOffset, line.IsComplete);
        }
    }

    private W3cFieldMap BuildFieldsMap(string headerLine) => W3cFieldMap.Build(_headerParser.Parse(headerLine).ToArray(), _ipResolver.PriorityHeaders);

    private static bool IsRequestRecord(IReadOnlyList<ReadOnlyMemory<char>> tokens, W3cFieldMap fieldsMap)
    {
        var dateIndex = fieldsMap.Date;
        if (dateIndex < 0 || dateIndex >= tokens.Count)
        {
            return true;
        }

        var raw = tokens[dateIndex].Span;
        if (raw.IsEmpty || raw.SequenceEqual("-"))
        {
            return true;
        }

        if (raw.Length != 10)
        {
            return false;
        }

        if (raw[4] != '-' || raw[7] != '-')
        {
            return false;
        }

        return DateTime.TryParseExact(raw.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static Dictionary<string, string?>? BuildExtras(IReadOnlyList<ReadOnlyMemory<char>> tokens, W3cFieldMap fieldsMap, bool includeAll)
    {
        Dictionary<string, string?>? extras = null;
        var source = includeAll && fieldsMap.HasExtraFields ? fieldsMap.ExtraIndexes : fieldsMap.HasResolverFields ? fieldsMap.ResolverIndexes : null;
        if (source is null)
        {
            return null;
        }

        foreach (var pair in source)
        {
            extras ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            extras[pair.Key] = pair.Value < tokens.Count ? NullIfMissing(tokens[pair.Value].ToString()) : null;
        }

        return extras;
    }

    private LogEntry Map(
        IReadOnlyList<ReadOnlyMemory<char>> tokens,
        W3cFieldMap fields,
        IReadOnlyDictionary<string, string?>? extras,
        string rawLine,
        long sourceId,
        long fileId,
        long lineNumber)
    {
        var timestampUtc = ParseTimestamp(At(tokens, fields.Date), At(tokens, fields.Time));
        var entry = new LogEntry
        {
            SourceId = sourceId,
            FileId = fileId,
            LineNumber = lineNumber,
            TimestampUtc = timestampUtc,
            TimestampLocal = timestampUtc?.ToLocalTime(),
            ServerIp = At(tokens, fields.ServerIp),
            Method = At(tokens, fields.Method),
            UriStem = At(tokens, fields.UriStem),
            UriQuery = At(tokens, fields.UriQuery),
            ServerPort = ParseInt(At(tokens, fields.ServerPort)),
            Username = At(tokens, fields.Username),
            ClientIp = At(tokens, fields.ClientIp),
            UserAgent = At(tokens, fields.UserAgent),
            Referer = At(tokens, fields.Referer),
            StatusCode = ParseInt(At(tokens, fields.StatusCode)),
            SubStatusCode = ParseInt(At(tokens, fields.SubStatusCode)),
            Win32Status = ParseInt(At(tokens, fields.Win32Status)),
            TimeTakenMs = ParseInt(At(tokens, fields.TimeTakenMs)),
            BytesSent = ParseLong(At(tokens, fields.BytesSent)),
            BytesReceived = ParseLong(At(tokens, fields.BytesReceived)),
            Host = At(tokens, fields.Host),
            ProtocolVersion = At(tokens, fields.ProtocolVersion),
            Cookie = At(tokens, fields.Cookie),
            ForwardedFor = At(tokens, fields.ForwardedFor),
            RealClientIp = At(tokens, fields.RealClientIp),
            RawLine = rawLine,
            AdditionalFields = extras
        };

        return entry with { ResolvedClientIp = _ipResolver.Resolve(entry) };
    }

    private static string? At(IReadOnlyList<ReadOnlyMemory<char>> tokens, int index) => index >= 0 && index < tokens.Count ? NullIfMissing(tokens[index].ToString()) : null;
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
        var lineBytes = ArrayPool<byte>.Shared.Rent(256);
        var lineCount = 0;
        var lineStart = stream.Position;
        var position = stream.Position;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (lineCount > 0)
                    {
                        yield return new RawLine(Decode(lineBytes, lineCount, encoding), lineStart, position, false);
                    }

                    yield break;
                }

                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    position++;
                    if (value == (byte)'\n')
                    {
                        if (lineCount > 0 && lineBytes[lineCount - 1] == (byte)'\r')
                        {
                            lineCount--;
                        }

                        yield return new RawLine(Decode(lineBytes, lineCount, encoding), lineStart, position, true);
                        lineCount = 0;
                        lineStart = position;
                    }
                    else
                    {
                        if (lineCount == lineBytes.Length)
                        {
                            var next = ArrayPool<byte>.Shared.Rent(lineBytes.Length * 2);
                            System.Buffer.BlockCopy(lineBytes, 0, next, 0, lineCount);
                            ArrayPool<byte>.Shared.Return(lineBytes);
                            lineBytes = next;
                        }

                        lineBytes[lineCount++] = value;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lineBytes);
        }
    }

    private static string Decode(byte[] data, int count, Encoding encoding)
    {
        try
        {
            return encoding.GetString(data, 0, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(data, 0, count);
        }
    }
    private sealed record RawLine(string Text, long StartOffset, long EndOffset, bool IsComplete);

    private static async Task<string?> ReadFieldsHeaderAsync(string path, Encoding encoding, long startByteOffset, CancellationToken cancellationToken)
    {
        var latest = (string?)null;
        await foreach (var line in ReadLinesAsync(path, encoding, 0, cancellationToken).ConfigureAwait(false))
        {
            if (line.StartOffset >= startByteOffset)
            {
                break;
            }

            var text = line.Text.TrimStart('\uFEFF');
            if (text.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                latest = text;
            }
        }

        return latest;
    }
}