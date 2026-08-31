using System.Text.Json;
using System.Text.RegularExpressions;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Security;

namespace IISLogExplorer.Infrastructure.Security;

public sealed record SecurityRule(string Id, string Category, string Pattern, string Match, int Score, bool Enabled, string Title);

public sealed class SecurityRuleEngine
{
    private readonly IReadOnlyList<SecurityRule> _rules;

    public SecurityRuleEngine(params SecurityRule[] rules)
    {
        if (rules.Length > 0)
        {
            _rules = rules;
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "security-rules.json");
        if (!File.Exists(path))
        {
            _rules = [];
            return;
        }

        try
        {
            _rules = JsonSerializer.Deserialize<List<SecurityRule>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            _rules = [];
        }
    }

    public IReadOnlyList<SecurityRule> Rules => _rules;

    public IEnumerable<SecurityRule> Match(LogEntry entry)
    {
        var value = $"{entry.DisplayUrl} {entry.UriQuery} {entry.RawLine}";
        foreach (var rule in _rules.Where(x => x.Enabled))
        {
            var matched = rule.Match.Equals("regex", StringComparison.OrdinalIgnoreCase)
                ? Regex.IsMatch(value, rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                : rule.Match.Equals("method", StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(entry.Method, rule.Pattern, StringComparison.OrdinalIgnoreCase)
                    : value.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
            if (matched)
            {
                yield return rule;
            }
        }
    }
}

public sealed class SecurityAnalyzer : ISecurityAnalyzer
{
    private const int MaxFindings = 5000;
    private readonly SecurityRuleEngine _engine;

    public SecurityAnalyzer(SecurityRuleEngine engine)
    {
        _engine = engine;
    }

    public async Task<SecurityAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
    {
        var findings = new List<SecurityFinding>(MaxFindings);
        var matchedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ipAggregates = new Dictionary<string, IpAggregate>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        long notFound = 0;
        long normalSuccesses = 0;
        long sensitiveMatchCount = 0;
        var sensitiveRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownBrowser = false;

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            total++;
            if (entry.StatusCode == 404) notFound++;
            if (entry.StatusCode is >= 200 and < 400)
            {
                normalSuccesses++;
            }

            if (IsKnownBrowser(entry.UserAgent))
            {
                knownBrowser = true;
            }

            var ip = entry.ResolvedClientIp ?? entry.ClientIp;
            IpAggregate? aggregate = null;
            if (!string.IsNullOrWhiteSpace(ip))
            {
                if (!ipAggregates.TryGetValue(ip, out aggregate))
                {
                    aggregate = new IpAggregate();
                    ipAggregates[ip] = aggregate;
                }

                aggregate.Requests++;
                if (entry.StatusCode == 404) aggregate.NotFound++;
                aggregate.AddUrl(entry.DisplayUrl);
                if (entry.TimestampUtc is not null)
                {
                    aggregate.FirstSeen = aggregate.FirstSeen is null || entry.TimestampUtc < aggregate.FirstSeen ? entry.TimestampUtc : aggregate.FirstSeen;
                    aggregate.LastSeen = aggregate.LastSeen is null || entry.TimestampUtc > aggregate.LastSeen ? entry.TimestampUtc : aggregate.LastSeen;
                }
            }

            foreach (var rule in _engine.Match(entry))
            {
                matchedRuleIds.Add(rule.Id);
                if (IsSensitiveCategory(rule.Category))
                {
                    sensitiveMatchCount++;
                    sensitiveRuleIds.Add(rule.Id);
                    if (aggregate is not null) aggregate.SensitiveHits++;
                }
                if (findings.Count < MaxFindings)
                {
                    findings.Add(new SecurityFinding
                    {
                        RuleId = rule.Id,
                        Title = string.IsNullOrWhiteSpace(rule.Title) ? rule.Category : rule.Title,
                        Severity = SeverityFor(rule.Category),
                        Reason = $"命中規則 {rule.Pattern}。此為 Heuristic 指標，不代表攻擊成功。",
                        ClientIp = ip,
                        Uri = entry.DisplayUrl,
                        Timestamp = entry.TimestampUtc,
                        LogEntryId = entry.Id
                    });
                }
            }
        }

        var reasons = new List<string> { "Heuristic：結果僅代表 Log 中的可疑指標，不代表攻擊成功。" };
        var score = 0;
        var sensitiveHits = sensitiveMatchCount;
        var hasTraversal = matchedRuleIds.Any(x => x.Contains("TRAVERSAL", StringComparison.OrdinalIgnoreCase));
        var hasSql = matchedRuleIds.Any(x => x.Contains("SQL", StringComparison.OrdinalIgnoreCase));
        var hasXss = matchedRuleIds.Any(x => x.Contains("XSS", StringComparison.OrdinalIgnoreCase));
        var hasSuspiciousMethod = matchedRuleIds.Any(x => x.Contains("METHOD", StringComparison.OrdinalIgnoreCase));
        var scanner = ipAggregates.Values.Any(x => x.HasManyUrls && x.Requests > 0 && x.NotFound / (double)x.Requests > .8 && x.SensitiveHits > 0 && x.LastSeen - x.FirstSeen <= TimeSpan.FromMinutes(5));

        if (sensitiveHits > 0)
        {
            score += 15;
            reasons.Add("命中敏感檔案或可疑請求路徑");
        }

        if (ipAggregates.Values.Any(x => x.SensitiveHits > 1) || sensitiveRuleIds.Count > 1)
        {
            score += 20;
            reasons.Add("命中多個敏感路徑");
        }

        if (total > 0 && notFound / (double)total > .8)
        {
            score += 15;
            reasons.Add("極高 404 比例");
        }

        if (scanner)
        {
            score += 20;
            reasons.Add("疑似掃描行為：大量不同 URL、極高 404 比例、短時間集中請求、命中敏感路徑");
        }

        if (hasTraversal)
        {
            score += 25;
            reasons.Add("Potential Path Traversal");
        }

        if (hasSql)
        {
            score += 20;
            reasons.Add("SQL Injection Indicator");
        }

        if (hasXss)
        {
            score += 20;
            reasons.Add("XSS Indicator");
        }

        if (hasSuspiciousMethod)
        {
            score += 5;
            reasons.Add("使用需要進一步檢視的 HTTP method");
        }

        if (knownBrowser)
        {
            score -= 5;
        }

        if (normalSuccesses > 0)
        {
            score -= 10;
        }

        score = Math.Clamp(score, 0, 100);
        return new SecurityAnalysisResult
        {
            Score = score,
            Severity = ToSeverity(score),
            LikelyScanner = scanner,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Findings = findings
        };
    }

    private static bool IsKnownBrowser(string? userAgent) => userAgent?.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) == true || userAgent?.Contains("Chrome", StringComparison.OrdinalIgnoreCase) == true || userAgent?.Contains("Firefox", StringComparison.OrdinalIgnoreCase) == true || userAgent?.Contains("Safari", StringComparison.OrdinalIgnoreCase) == true || userAgent?.Contains("Edg/", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsSensitiveCategory(string category) => category.Contains("Sensitive", StringComparison.OrdinalIgnoreCase);
    private static bool IsSensitiveRule(string ruleId) => ruleId.Contains("SENSITIVE", StringComparison.OrdinalIgnoreCase);
    private static SecuritySeverity SeverityFor(string category) => category.Contains("Traversal", StringComparison.OrdinalIgnoreCase) || category.Contains("Injection", StringComparison.OrdinalIgnoreCase) || category.Contains("Xss", StringComparison.OrdinalIgnoreCase) ? SecuritySeverity.High : SecuritySeverity.Medium;
    private static SecuritySeverity ToSeverity(int score) => score switch { >= 75 => SecuritySeverity.Critical, >= 50 => SecuritySeverity.High, >= 25 => SecuritySeverity.Medium, _ => SecuritySeverity.Low };

    private sealed class IpAggregate
    {
        public long Requests;
        public long NotFound;
        public int SensitiveHits;
        public DateTimeOffset? FirstSeen;
        public DateTimeOffset? LastSeen;
        public HashSet<string> Urls { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasManyUrls { get; private set; }

        public void AddUrl(string url)
        {
            if (Urls.Count < 101)
            {
                Urls.Add(url);
                HasManyUrls = Urls.Count > 100;
            }
        }
    }
}
