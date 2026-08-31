using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;

namespace IISLogExplorer.Tests;

public class IisW3cLogParserTests
{
    private static IisW3cLogParser CreateParser() => new(new FieldsHeaderParser(), new ClientIpResolver());

    [Fact]
    public async Task Parses_standard_w3c_log()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, TestHelpers.SampleW3CLog(10));
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        Assert.Equal(10, entries.Count);
        Assert.Equal("GET", entries[0].Method);
        Assert.Equal(1, entries[0].SourceId);
        Assert.Equal(5, entries[0].LineNumber);
        Assert.NotNull(entries[0].TimestampUtc);
        Assert.Equal("1.2.3.4", entries[0].ResolvedClientIp);
        Assert.Equal(404, entries[0].StatusCode);
        Assert.Contains(entries, x => x.StatusCode == 200);
    }

    [Fact]
    public async Task Handles_different_fields_order()
    {
        var content = """
            #Software: IIS
            #Fields: cs-uri-stem sc-status time-taken cs-method c-ip date time
            /admin 500 12 POST 9.9.9.9 2026-08-28 10:00:01
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("/admin", entry.UriStem);
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal(12, entry.TimeTakenMs);
        Assert.Equal("POST", entry.Method);
        Assert.Equal("9.9.9.9", entry.ClientIp);
    }

    [Fact]
    public async Task Missing_fields_are_null_not_dash()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem cs-username
            2026-08-28 10:00:01 1.2.3.4 GET /home -
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.Username);
        Assert.Equal(10, entry.TimestampUtc?.Hour);
    }

    [Fact]
    public async Task Malformed_line_does_not_stop_file()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem sc-status
            2026-08-28 10:00:01 1.2.3.4 GET /ok 200
            THIS IS GARBAGE THAT SHOULD NOT CRASH
            2026-08-28 10:00:02 5.6.7.8 GET /bad 404
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Header_change_mid_file_rebuilds_mapping()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem sc-status
            2026-08-28 10:00:01 1.2.3.4 GET /first 200
            #Fields: date time cs-method cs-uri-stem c-ip sc-status
            2026-08-28 10:00:02 POST /second 5.6.7.8 201
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("/second", entries[1].UriStem);
        Assert.Equal("5.6.7.8", entries[1].ClientIp);
        Assert.Equal(201, entries[1].StatusCode);
    }

    [Fact]
    public async Task Quoted_fields_with_spaces_preserved()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem cs(User-Agent) sc-status
            2026-08-28 10:00:01 1.2.3.4 GET /ok "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120" 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Contains("Chrome/120", entry.UserAgent);
        Assert.StartsWith("Mozilla/5.0", entry.UserAgent);
    }

    [Fact]
    public async Task Encoded_and_unicode_url_preserved_raw()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem cs-uri-query sc-status
            2026-08-28 10:00:01 1.2.3.4 GET /路徑/%E4%B8%AD%E6%96%87 id=1 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Contains("路徑", entry.UriStem);
        Assert.Contains("%E4%B8%AD%E6%96%87", entry.UriStem);
    }

    [Fact]
    public async Task Resolves_forwarded_for_ip()
    {
        var content = """
            #Fields: date time c-ip cs-method cs-uri-stem X-Forwarded-For sc-status
            2026-08-28 10:00:01 10.0.0.1 GET /ok 8.8.8.8, 10.0.0.9 200
            """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("8.8.8.8", entry.ResolvedClientIp);
        Assert.Equal("10.0.0.1", entry.ClientIp);
    }

    [Fact]
    public async Task Incremental_offset_read_returns_only_new_lines()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, TestHelpers.SampleW3CLog(50));
        var parser = CreateParser();
        var full = await parser.ParseRecordsAsync(file, 1).ToListAsync();
        Assert.Equal(50, full.Count);
        var offset = full[^1].EndByteOffset;
        var lines = full.Count;
        File.AppendAllText(file, "2026-08-28 11:00:01 10.0.0.1 GET /appended 443 - 1.2.3.4 Mozilla/5.0 - 200 0 0 10" + Environment.NewLine);
        var appended = await parser.ParseRecordsAsync(file, 1, 0, offset, lines).ToListAsync();
        Assert.Single(appended);
        Assert.Equal("/appended", appended[0].Entry.UriStem);
    }

    [Fact]
    public async Task Long_user_agent_handled()
    {
        var longAgent = "Mozilla/5.0 " + new string('X', 5000);
        var content = $"#Fields: date time c-ip cs-method cs-uri-stem cs(User-Agent) sc-status\n2026-08-28 10:00:01 1.2.3.4 GET /ok \"{longAgent}\" 200";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = TestHelpers.WriteSampleLog(dir, content);
        var parser = CreateParser();
        var entries = await parser.ParseAsync(file, 1).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(longAgent, entry.UserAgent);
    }
}
