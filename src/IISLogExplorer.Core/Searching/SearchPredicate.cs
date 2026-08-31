using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Searching;

public static class SearchPredicate
{
    public static bool Matches(LogEntry entry, SearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Keyword) && !Contains(entry.RawLine, request.Keyword) && !Contains(entry.ClientIp, request.Keyword) && !Contains(entry.ResolvedClientIp, request.Keyword) && !Contains(entry.UriStem, request.Keyword) && !Contains(entry.UriQuery, request.Keyword) && !Contains(entry.UserAgent, request.Keyword) && !Contains(entry.Method, request.Keyword) && entry.StatusCode != (int.TryParse(request.Keyword, out var status) ? status : -1)) return false;
        if (request.From is not null && (entry.TimestampUtc is null || entry.TimestampUtc < request.From)) return false;
        if (request.To is not null && (entry.TimestampUtc is null || entry.TimestampUtc > request.To)) return false;
        if (request.TimeFrom is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay < request.TimeFrom)) return false;
        if (request.TimeTo is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay > request.TimeTo)) return false;
        if (!string.IsNullOrWhiteSpace(request.Method) && !string.Equals(entry.Method, request.Method, StringComparison.OrdinalIgnoreCase)) return false;
        if (request.StatusCode is not null && entry.StatusCode != request.StatusCode) return false;
        if (!string.IsNullOrWhiteSpace(request.ClientIp) && !Contains(entry.ClientIp, request.ClientIp) && !Contains(entry.ResolvedClientIp, request.ClientIp)) return false;
        if (!string.IsNullOrWhiteSpace(request.UrlContains) && !Contains(entry.DisplayUrl, request.UrlContains)) return false;
        if (!string.IsNullOrWhiteSpace(request.UserAgentContains) && !Contains(entry.UserAgent, request.UserAgentContains)) return false;
        if (request.MinTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs < request.MinTimeTakenMs)) return false;
        if (request.MaxTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs > request.MaxTimeTakenMs)) return false;
        if (!string.IsNullOrWhiteSpace(request.Username) && !Contains(entry.Username, request.Username)) return false;
        return true;
    }

    public static bool Contains(string? value, string? search) => value?.Contains(search ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;
}