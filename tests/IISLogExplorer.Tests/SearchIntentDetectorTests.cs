using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Searching;

namespace IISLogExplorer.Tests;

public class SearchIntentDetectorTests
{
    [Theory]
    [InlineData("1.2.3.4", SearchIntent.IpAddress)]
    [InlineData("2001:db8::1", SearchIntent.IpAddress)]
    [InlineData("404", SearchIntent.HttpStatus)]
    [InlineData("POST", SearchIntent.HttpMethod)]
    [InlineData("/api/order", SearchIntent.Uri)]
    [InlineData("Mozilla", SearchIntent.GeneralKeyword)]
    public void Detects_intent(string keyword, SearchIntent expected)
    {
        Assert.Equal(expected, new SearchIntentDetector().Detect(keyword));
    }
}
