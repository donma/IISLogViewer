using System.Net;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;

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

    private static string? GetValue(LogEntry entry, string name)
    {
        if (name.Equals("c-ip", StringComparison.OrdinalIgnoreCase))
        {
            return entry.ClientIp;
        }

        if (name.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase))
        {
            return entry.ForwardedFor ?? entry.AdditionalFields.FirstOrDefault(x => x.Key.Contains("forwarded", StringComparison.OrdinalIgnoreCase)).Value;
        }

        if (name.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase))
        {
            return entry.RealClientIp ?? entry.AdditionalFields.FirstOrDefault(x => x.Key.Contains("real-ip", StringComparison.OrdinalIgnoreCase)).Value;
        }

        var field = entry.AdditionalFields.FirstOrDefault(x => Normalize(x.Key) == Normalize(name));
        return field.Value;
    }

    private static string Normalize(string value) => value.Replace("cs(", "", StringComparison.OrdinalIgnoreCase).Replace("sc(", "", StringComparison.OrdinalIgnoreCase).Replace(")", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
