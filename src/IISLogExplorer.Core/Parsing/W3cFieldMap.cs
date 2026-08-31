using System.Collections.ObjectModel;

namespace IISLogExplorer.Core.Parsing;

public sealed class W3cFieldMap
{
    private readonly Dictionary<string, int> _extra = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _resolver = new(StringComparer.OrdinalIgnoreCase);
    public int Date { get; private set; } = -1;
    public int Time { get; private set; } = -1;
    public int ServerIp { get; private set; } = -1;
    public int Method { get; private set; } = -1;
    public int UriStem { get; private set; } = -1;
    public int UriQuery { get; private set; } = -1;
    public int ServerPort { get; private set; } = -1;
    public int Username { get; private set; } = -1;
    public int ClientIp { get; private set; } = -1;
    public int UserAgent { get; private set; } = -1;
    public int Referer { get; private set; } = -1;
    public int StatusCode { get; private set; } = -1;
    public int SubStatusCode { get; private set; } = -1;
    public int Win32Status { get; private set; } = -1;
    public int TimeTakenMs { get; private set; } = -1;
    public int BytesSent { get; private set; } = -1;
    public int BytesReceived { get; private set; } = -1;
    public int Host { get; private set; } = -1;
    public int ProtocolVersion { get; private set; } = -1;
    public int Cookie { get; private set; } = -1;
    public int ForwardedFor { get; private set; } = -1;
    public int RealClientIp { get; private set; } = -1;

    public int FieldCount { get; private set; }
    public bool HasFields => FieldCount > 0;

    public bool HasExtraFields => _extra.Count > 0;
    public bool HasResolverFields => _resolver.Count > 0;

    public static W3cFieldMap Build(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<string>? resolverHeaders = null)
    {
        var map = new W3cFieldMap { FieldCount = fields.Count };
        var resolverNames = resolverHeaders is { Count: > 0 } ? resolverHeaders.Select(Normalize).ToHashSet(StringComparer.Ordinal) : null;
        foreach (var field in fields)
        {
            var index = field.Index;
            var normalized = Normalize(field.Name);
            if (normalized == "date") map.Date = index;
            else if (normalized == "time") map.Time = index;
            else if (normalized == "sip") map.ServerIp = index;
            else if (normalized == "csmethod") map.Method = index;
            else if (normalized == "csuristem") map.UriStem = index;
            else if (normalized == "csuriquery") map.UriQuery = index;
            else if (normalized == "sport") map.ServerPort = index;
            else if (normalized == "csusername") map.Username = index;
            else if (normalized == "cip") map.ClientIp = index;
            else if (normalized == "cs(useragent)" || normalized == "csuseragent") map.UserAgent = index;
            else if (normalized == "cs(referer)" || normalized == "csreferer") map.Referer = index;
            else if (normalized == "scstatus") map.StatusCode = index;
            else if (normalized == "scsubstatus") map.SubStatusCode = index;
            else if (normalized == "scwin32status") map.Win32Status = index;
            else if (normalized == "timetaken") map.TimeTakenMs = index;
            else if (normalized == "scbytes") map.BytesSent = index;
            else if (normalized == "csbytes") map.BytesReceived = index;
            else if (normalized == "shost" || normalized == "cshost") map.Host = index;
            else if (normalized == "csversion") map.ProtocolVersion = index;
            else if (normalized == "cs(cookie)" || normalized == "cscookie") map.Cookie = index;
            else if (normalized == "xforwardedfor" || normalized == "forwardedfor") map.ForwardedFor = index;
            else if (normalized == "xrealip" || normalized == "realip") map.RealClientIp = index;
            else
            {
                map._extra[field.Name] = index;
                if (resolverNames is not null && resolverNames.Contains(normalized))
                {
                    map._resolver[field.Name] = index;
                }
            }
        }

        return map;
    }

    public IReadOnlyDictionary<string, int> ExtraIndexes => new ReadOnlyDictionary<string, int>(_extra);
    public IReadOnlyDictionary<string, int> ResolverIndexes => new ReadOnlyDictionary<string, int>(_resolver);

    private static string Normalize(string value) => value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}