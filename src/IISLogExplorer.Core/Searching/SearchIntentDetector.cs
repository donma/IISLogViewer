using System.Net;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Searching;

public sealed class SearchIntentDetector
{
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT", "PROPFIND", "TRACK" };

    public SearchIntent Detect(string? keyword)
    {
        if (keyword is not null && (keyword.Contains('.', StringComparison.Ordinal) || keyword.Contains(':', StringComparison.Ordinal)) && IPAddress.TryParse(keyword, out _))
        {
            return SearchIntent.IpAddress;
        }

        if (keyword is not null && !keyword.Contains('.', StringComparison.Ordinal) && int.TryParse(keyword, out var status) && status is >= 100 and <= 599)
        {
            return SearchIntent.HttpStatus;
        }

        if (keyword is not null && Methods.Contains(keyword.Trim()))
        {
            return SearchIntent.HttpMethod;
        }

        if (keyword?.Contains('/', StringComparison.Ordinal) == true || keyword?.Contains('?', StringComparison.Ordinal) == true)
        {
            return SearchIntent.Uri;
        }

        return SearchIntent.GeneralKeyword;
    }
}
