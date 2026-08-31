using IISLogExplorer.Core.Analysis;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Analysis;

public sealed class IpAnalyzer : IIpAnalyzer
{
    public async Task<IpAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, string ip, CancellationToken cancellationToken = default)
    {
        var uniqueUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var topUrls = new BoundedCounter(50000);
        var userAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methods = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var statuses = new Dictionary<int, long>();
        var timeline = new Queue<IpTimelineItem>();
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        long requests = 0;
        long notFound = 0;
        long serverErrors = 0;
        long timedRequests = 0;
        double durationTotal = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(entry.ResolvedClientIp, ip, StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.ClientIp, ip, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            requests++;
            var url = entry.DisplayUrl;
            if (url.Length > 0)
            {
                uniqueUrls.Add(url);
                topUrls.Add(url);
            }

            if (!string.IsNullOrWhiteSpace(entry.UserAgent)) userAgents.Add(entry.UserAgent);
            if (!string.IsNullOrWhiteSpace(entry.Method)) methods[entry.Method] = methods.GetValueOrDefault(entry.Method) + 1;
            if (entry.StatusCode is not null) statuses[entry.StatusCode.Value] = statuses.GetValueOrDefault(entry.StatusCode.Value) + 1;
            if (entry.StatusCode == 404) notFound++;
            if (entry.StatusCode is >= 500 and <= 599) serverErrors++;
            if (entry.TimeTakenMs is not null) { timedRequests++; durationTotal += entry.TimeTakenMs.Value; }
            if (entry.TimestampUtc is not null)
            {
                first = first is null || entry.TimestampUtc < first ? entry.TimestampUtc : first;
                last = last is null || entry.TimestampUtc > last ? entry.TimestampUtc : last;
            }

            timeline.Enqueue(new IpTimelineItem { Timestamp = entry.TimestampUtc, Method = entry.Method, Url = url, Status = entry.DisplayStatus });
            while (timeline.Count > 500) timeline.Dequeue();
        }

        return new IpAnalysisResult
        {
            Ip = ip,
            FirstSeen = first,
            LastSeen = last,
            RequestCount = requests,
            UniqueUrls = uniqueUrls.Count,
            NotFoundCount = notFound,
            ServerErrorCount = serverErrors,
            AverageTimeTakenMs = timedRequests == 0 ? 0 : durationTotal / timedRequests,
            UserAgents = userAgents.Take(20).ToArray(),
            TopUrls = topUrls.Ordered().Take(20).Select(x => x.Key).ToArray(),
            Methods = methods.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
            StatusDistribution = statuses.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            Timeline = timeline.OrderBy(x => x.Timestamp).ToArray()
        };
    }
}

public sealed class ErrorAnalyzer : IErrorAnalyzer
{
    public async Task<ErrorAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
    {
        var topUrls = new BoundedCounter(50000);
        var topIps = new BoundedCounter(50000);
        var statuses = new Dictionary<int, long>();
        var timeline = new Queue<LogEntry>();
        long total = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (entry.StatusCode is null or < 400 or > 599)
            {
                continue;
            }

            total++;
            topUrls.Add(entry.DisplayUrl);
            var ip = entry.ResolvedClientIp ?? entry.ClientIp;
            if (!string.IsNullOrWhiteSpace(ip)) topIps.Add(ip);
            if (entry.StatusCode is not null) statuses[entry.StatusCode.Value] = statuses.GetValueOrDefault(entry.StatusCode.Value) + 1;
            timeline.Enqueue(entry);
            while (timeline.Count > 500) timeline.Dequeue();
        }

        return new ErrorAnalysisResult
        {
            TotalErrors = total,
            TopErrorUrls = topUrls.Ordered().Take(20).Select(x => (x.Key, x.Value)).ToArray(),
            TopErrorIps = topIps.Ordered().Take(20).Select(x => (x.Key, x.Value)).ToArray(),
            StatusDistribution = statuses.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            Timeline = timeline.OrderByDescending(x => x.TimestampUtc).ToArray()
        };
    }
}

public sealed class SlowRequestAnalyzer : ISlowRequestAnalyzer
{
    public async Task<SlowRequestAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, int thresholdMs, CancellationToken cancellationToken = default)
    {
        var durations = new List<long>();
        var urlStats = new Dictionary<string, (double Total, long Count)>(StringComparer.OrdinalIgnoreCase);
        long max = 0;
        double total = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (entry.TimeTakenMs is null || entry.TimeTakenMs < thresholdMs)
            {
                continue;
            }

            var duration = entry.TimeTakenMs.Value;
            durations.Add(duration);
            total += duration;
            max = Math.Max(max, duration);
            var url = entry.DisplayUrl;
            var stats = urlStats.GetValueOrDefault(url);
            urlStats[url] = (stats.Total + duration, stats.Count + 1);
        }

        durations.Sort();
        return new SlowRequestAnalysisResult
        {
            ThresholdMs = thresholdMs,
            RequestCount = durations.Count,
            AverageDurationMs = durations.Count == 0 ? 0 : total / durations.Count,
            P95 = Percentile(durations, .95),
            P99 = Percentile(durations, .99),
            MaxDurationMs = max,
            TopSlowUrls = urlStats.OrderByDescending(x => x.Value.Total / x.Value.Count).Take(20).Select(x => (x.Key, x.Value.Total / x.Value.Count, x.Value.Count)).ToArray()
        };
    }

    private static double Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var index = Math.Min(values.Count - 1, Math.Max(0, (int)Math.Ceiling(values.Count * percentile) - 1));
        return values[index];
    }
}

public sealed class TrafficAnalyzer : ITrafficAnalyzer
{
    public async Task<TrafficAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
    {
        var uniqueIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var topUrls = new BoundedCounter(50000);
        var topIps = new BoundedCounter(50000);
        var topAgents = new BoundedCounter(10000);
        var statuses = new Dictionary<int, long>();
        var minuteBuckets = new Dictionary<DateTimeOffset, long>();
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        long total = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            total++;
            if (!string.IsNullOrWhiteSpace(entry.DisplayUrl)) topUrls.Add(entry.DisplayUrl);
            var ip = entry.ResolvedClientIp ?? entry.ClientIp;
            if (!string.IsNullOrWhiteSpace(ip)) { uniqueIps.Add(ip); topIps.Add(ip); }
            if (!string.IsNullOrWhiteSpace(entry.UserAgent)) topAgents.Add(entry.UserAgent);
            if (entry.StatusCode is not null) statuses[entry.StatusCode.Value] = statuses.GetValueOrDefault(entry.StatusCode.Value) + 1;
            if (entry.TimestampUtc is not null)
            {
                first = first is null || entry.TimestampUtc < first ? entry.TimestampUtc : first;
                last = last is null || entry.TimestampUtc > last ? entry.TimestampUtc : last;
                var minute = new DateTimeOffset(entry.TimestampUtc.Value.Year, entry.TimestampUtc.Value.Month, entry.TimestampUtc.Value.Day, entry.TimestampUtc.Value.Hour, entry.TimestampUtc.Value.Minute, 0, TimeSpan.Zero);
                minuteBuckets[minute] = minuteBuckets.GetValueOrDefault(minute) + 1;
            }
        }

        var span = first is null || last is null ? TimeSpan.Zero : last.Value - first.Value;
        var trend = minuteBuckets.GroupBy(x => TrendBucket(x.Key, span)).GroupBy(x => x.Key).OrderBy(x => x.Key).Select(x => (x.Key, x.Sum(y => y.Sum(z => z.Value)))).ToArray();
        var minutes = first is not null && last is not null ? Math.Max(1, span.TotalMinutes) : 1;
        return new TrafficAnalysisResult
        {
            TotalRequests = total,
            UniqueIps = uniqueIps.Count,
            RequestsPerMinute = total / minutes,
            TopUrls = topUrls.Ordered().Take(20).Select(x => (x.Key, x.Value)).ToArray(),
            TopIps = topIps.Ordered().Take(20).Select(x => (x.Key, x.Value)).ToArray(),
            TopUserAgents = topAgents.Ordered().Take(20).Select(x => (x.Key, x.Value)).ToArray(),
            StatusDistribution = statuses.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            Trend = trend
        };
    }

    private static DateTimeOffset TrendBucket(DateTimeOffset value, TimeSpan span)
    {
        if (span.TotalDays >= 60) return new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero);
        if (span.TotalDays >= 2) return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero);
        return value;
    }
}

internal sealed class BoundedCounter
{
    private readonly int _capacity;
    private readonly Dictionary<string, long> _values = new(StringComparer.OrdinalIgnoreCase);

    public BoundedCounter(int capacity)
    {
        _capacity = capacity;
    }

    public void Add(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_values.TryGetValue(key, out var count))
        {
            _values[key] = count + 1;
            return;
        }

        if (_values.Count >= _capacity)
        {
            var minimum = _values.MinBy(x => x.Value);
            if (minimum.Value >= 1) return;
            _values.Remove(minimum.Key);
        }

        _values[key] = 1;
    }

    public IEnumerable<KeyValuePair<string, long>> Ordered() => _values.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
}
