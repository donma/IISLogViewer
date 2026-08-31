using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Searching;

public interface ISearchService
{
    IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
    Task<SearchStatistics> GetStatisticsAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
