using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Analysis;

public interface IIpAnalyzer
{
    Task<IpAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, string ip, CancellationToken cancellationToken = default);
}

public interface IErrorAnalyzer
{
    Task<ErrorAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default);
}

public interface ISlowRequestAnalyzer
{
    Task<SlowRequestAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, int thresholdMs, CancellationToken cancellationToken = default);
}

public interface ITrafficAnalyzer
{
    Task<TrafficAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default);
}
