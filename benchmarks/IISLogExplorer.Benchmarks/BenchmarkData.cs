using System.Text;

namespace IISLogExplorer.Benchmarks;

public static class BenchmarkData
{
    private static readonly object Lock = new();
    private static string? _samplePath;

    public static string SampleLogPath(int records = 100_000)
    {
        lock (Lock)
        {
            if (_samplePath is not null)
            {
                return _samplePath;
            }

            var path = Path.Combine(Path.GetTempPath(), $"iislog-bench-{records}.log");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, SampleW3CLog(records));
            }

            _samplePath = path;
            return path;
        }
    }

    public static string SampleLine(int index = 0)
    {
        var uri = index % 10 == 0 ? "/api/order" : $"/page/{index}";
        return $"2026-08-28 10:{(index / 60) % 60:00}:{index % 60:00} 10.0.0.1 {(index % 2 == 0 ? "GET" : "POST")} {uri} {(index % 3 == 0 ? "id=1" : "-")} 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - {(index % 100 == 0 ? 404 : 200)} 0 0 {index % 5000} 100 200";
    }

    private static string SampleW3CLog(int records)
    {
        var builder = new StringBuilder(records * 160);
        builder.AppendLine("#Software: Microsoft Internet Information Services 10.0");
        builder.AppendLine("#Version: 1.0");
        builder.AppendLine("#Date: 2026-08-28 00:00:00");
        builder.AppendLine("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken sc-bytes cs-bytes");
        for (var index = 0; index < records; index++)
        {
            builder.AppendLine(SampleLine(index));
        }

        return builder.ToString();
    }
}