using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Indexing;

public sealed class IndexPlanner
{
    public IReadOnlyList<FileInfo> Order(IReadOnlyList<FileInfo> files, SearchRequest? priorityRequest = null)
    {
        if (files.Count < 2)
        {
            return files;
        }

        var ordered = files.OrderByDescending(file => FileDateScore(file, priorityRequest)).ThenByDescending(file => file.LastWriteTimeUtc).ToArray();
        return ordered;
    }

    private static DateTime FileDateScore(FileInfo file, SearchRequest? request)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        if (name.StartsWith("u_ex", StringComparison.OrdinalIgnoreCase) && name.Length >= 10 && DateTime.TryParseExact(name[4..10], "yyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
        {
            if (request?.From is not null)
            {
                var distance = Math.Abs((date.Date - request.From.Value.LocalDateTime.Date).TotalDays);
                return DateTime.MaxValue.AddDays(-distance);
            }

            return date;
        }

        return file.LastWriteTimeUtc;
    }
}
