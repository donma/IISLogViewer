using System.Net;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Parsing;

namespace IISLogExplorer.Core.Networking;

public sealed class ClientIpResolver
{
    private static readonly string[] DefaultPriority = ["CF-Connecting-IP", "True-Client-IP", "X-Forwarded-For", "X-Real-IP", "cnd-src-ip", "c-ip"];
    private readonly ISettingsService? _settings;

    public ClientIpResolver(ISettingsService? settings = null)
    {
        _settings = settings;
    }

    public string? Resolve(LogEntry entry, IReadOnlyList<string>? priority = null)
    {
        var configuredPriority = _settings?.Current.ClientIpHeaderPriority is { Count: > 0 } configured ? configured : DefaultPriority;
        foreach (var name in priority is { Count: > 0 } ? priority : configuredPriority)
        {
            var value = GetValue(entry, name);
            var ip = FirstValidIp(value);
            if (ip is not null)
            {
                return ip;
            }
        }

        return FirstValidIp(entry.ClientIp);
    }

    public static string? FirstValidIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return null;
        }

        foreach (var candidate in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(candidate, out _))
            {
                return candidate;
            }
        }

        return null;
    }

    public IReadOnlyList<string> PriorityHeaders => _settings?.Current.ClientIpHeaderPriority is { Count: > 0 } configured ? configured : DefaultPriority;

    private static string? GetValue(LogEntry entry, string name)
    {
        if (name.Equals("c-ip", StringComparison.OrdinalIgnoreCase))
        {
            return entry.ClientIp;
        }

        if (name.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase))
        {
            return entry.ForwardedFor ?? FirstMatch(entry, x => HeaderNameNormalizer.Normalize(x).Contains("forwarded", StringComparison.Ordinal));
        }

        if (name.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase))
        {
            return entry.RealClientIp ?? FirstMatch(entry, x => HeaderNameNormalizer.Normalize(x).Contains("realip", StringComparison.Ordinal));
        }

        var normalized = HeaderNameNormalizer.Normalize(name);
        return FirstMatch(entry, x => HeaderNameNormalizer.Normalize(x) == normalized);
    }

    private static string? FirstMatch(LogEntry entry, Func<string, bool> predicate)
    {
        if (entry.AdditionalFields is null)
        {
            return null;
        }

        foreach (var pair in entry.AdditionalFields)
        {
            if (predicate(pair.Key))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
