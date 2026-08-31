using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;

namespace IISLogExplorer.Tests;

public class FinalFixTests
{
    private static IisW3cLogParser CreateParser() => new(new FieldsHeaderParser(), new ClientIpResolver());

    private static async Task<Core.Models.LogEntry> ParseSingleAsync(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "iislog-final-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = TestHelpers.WriteSampleLog(dir, content);
        var entries = await CreateParser().ParseAsync(file, 1).ToListAsync();
        return Assert.Single(entries);
    }

    [Fact]
    public async Task CustomClientIpHeaderWithCsWrapperWorks()
    {
        var entry = await ParseSingleAsync("""
            #Software: IIS
            #Fields: date time c-ip cs(cnd-src-ip) cs-method cs-uri-stem sc-status
            2026-08-31 10:00:00 10.0.0.1 198.51.100.7 GET / 200
            """);
        Assert.Equal("10.0.0.1", entry.ClientIp);
        Assert.Equal("198.51.100.7", entry.ResolvedClientIp);
    }

    [Fact]
    public async Task CfConnectingIpWithCsWrapperWorks()
    {
        var entry = await ParseSingleAsync("""
            #Software: IIS
            #Fields: date time c-ip cs(CF-Connecting-IP) cs-method cs-uri-stem sc-status
            2026-08-31 10:00:00 10.0.0.1 203.0.113.9 GET / 200
            """);
        Assert.Equal("10.0.0.1", entry.ClientIp);
        Assert.Equal("203.0.113.9", entry.ResolvedClientIp);
    }

    [Fact]
    public async Task XForwardedForWithCsWrapperWorks()
    {
        var entry = await ParseSingleAsync("""
            #Software: IIS
            #Fields: date time c-ip cs(X-Forwarded-For) cs-method cs-uri-stem sc-status
            2026-08-31 10:00:00 10.0.0.1 "203.0.113.20, 10.0.0.2" GET / 200
            """);
        Assert.Equal("10.0.0.1", entry.ClientIp);
        Assert.Equal("203.0.113.20", entry.ResolvedClientIp);
    }

    [Fact]
    public async Task XRealIpWithCsWrapperWorks()
    {
        var entry = await ParseSingleAsync("""
            #Software: IIS
            #Fields: date time c-ip cs(X-Real-IP) cs-method cs-uri-stem sc-status
            2026-08-31 10:00:00 10.0.0.1 203.0.113.77 GET / 200
            """);
        Assert.Equal("10.0.0.1", entry.ClientIp);
        Assert.Equal("203.0.113.77", entry.ResolvedClientIp);
    }
}