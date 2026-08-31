using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Security;

namespace IISLogExplorer.Tests;

public class RealWorldSampleLogTests
{
    private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
    private static IisW3cLogParser CreateParser() => new(new FieldsHeaderParser(), new ClientIpResolver());

    [Fact]
    public async Task Real_sample_with_custom_fields_and_forwarded_for()
    {
        var entries = await CreateParser().ParseAsync(DataPath("sample.log"), 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("2024-01-01", entry.TimestampUtc?.ToString("yyyy-MM-dd"));
        Assert.Equal("127.0.0.1", entry.ServerIp);
        Assert.Null(entry.ClientIp);
        Assert.Equal("192.168.0.1", entry.ResolvedClientIp);
        Assert.Equal("GET", entry.Method);
        Assert.Equal("/index.html", entry.UriStem);
        Assert.Equal(200, entry.StatusCode);
        Assert.NotNull(entry.AdditionalFields["x(My-Field)"]);
    }

    [Fact]
    public async Task Real_sample_multi_sequential_records()
    {
        var entries = await CreateParser().ParseAsync(DataPath("multi.log"), 1).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal("/index0.html", entries[0].UriStem);
        Assert.Equal("/index2.html", entries[2].UriStem);
    }

    [Fact]
    public async Task Real_sample_large_bytes_values_do_not_overflow()
    {
        var entries = await CreateParser().ParseAsync(DataPath("large_values.log"), 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(3_000_000_000L, entry.BytesSent);
        Assert.Equal(4_000_000_000L, entry.BytesReceived);
    }

    [Fact]
    public async Task Real_sample_malformed_datetime_keeps_entry_with_null_timestamp()
    {
        var entries = await CreateParser().ParseAsync(DataPath("malformed_datetime.log"), 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.TimestampUtc);
        Assert.Equal("/index.html", entry.UriStem);
        Assert.Equal(200, entry.StatusCode);
    }

    [Fact]
    public async Task Real_sample_missing_datetime_keeps_entry_with_null_timestamp()
    {
        var entries = await CreateParser().ParseAsync(DataPath("missing_datetime.log"), 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.TimestampUtc);
        Assert.Equal("127.0.0.1", entry.ServerIp);
        Assert.Null(entry.ClientIp);
        Assert.Equal("/index.html", entry.UriStem);
    }

    [Fact]
    public async Task Real_sample_short_line_missing_optional_field()
    {
        var entries = await CreateParser().ParseAsync(DataPath("short_line.log"), 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.ForwardedFor);
        Assert.Equal("/index.html", entry.UriStem);
        Assert.Equal(200, entry.StatusCode);
    }

    [Fact]
    public async Task Real_anomaly_sample_matches_production_security_rules()
    {
        var entries = await CreateParser().ParseAsync(DataPath("anomaly_iis_sample.log"), 1).ToListAsync();
        Assert.Equal(5, entries.Count);

        var analyzer = new SecurityAnalyzer(new SecurityRuleEngine());
        var result = await analyzer.AnalyzeAsync(ToAsync(entries));

        Assert.Contains(result.Findings, finding => finding.RuleId == "SQL_BOOLEAN");
        Assert.Contains(result.Findings, finding => finding.RuleId == "XSS_SCRIPT");
        Assert.Contains(result.Findings, finding => finding.RuleId == "TRAVERSAL");
        Assert.True(result.Score > 0);
    }

    private static async IAsyncEnumerable<IISLogExplorer.Core.Models.LogEntry> ToAsync(IEnumerable<IISLogExplorer.Core.Models.LogEntry> entries)
    {
        await Task.CompletedTask;
        foreach (var entry in entries) yield return entry;
    }
}
