using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Security;

public interface ISecurityAnalyzer
{
    Task<SecurityAnalysisResult> AnalyzeAsync(IAsyncEnumerable<LogEntry> entries, CancellationToken cancellationToken = default);
}
