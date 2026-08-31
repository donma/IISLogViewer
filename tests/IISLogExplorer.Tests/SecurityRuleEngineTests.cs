using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Tests;

public class SecurityRuleEngineTests
{
    [Fact]
    public void Matches_contains_and_regex_rules()
    {
        var engine = new IISLogExplorer.Infrastructure.Security.SecurityRuleEngine(
            new IISLogExplorer.Infrastructure.Security.SecurityRule("R1", "Test", "/.env", "contains", 15, true, "env"),
            new IISLogExplorer.Infrastructure.Security.SecurityRule("R2", "Test", "union\\s+select", "regex", 20, true, "sqli"));
        var entry = new LogEntry { UriStem = "/.env", RawLine = "GET /.env HTTP/1.1 union select 1" };
        var matched = engine.Match(entry).ToList();
        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var engine = new IISLogExplorer.Infrastructure.Security.SecurityRuleEngine(
            new IISLogExplorer.Infrastructure.Security.SecurityRule("R1", "Test", "/.env", "contains", 15, false, "env"));
        var entry = new LogEntry { UriStem = "/.env" };
        Assert.Empty(engine.Match(entry));
    }
}