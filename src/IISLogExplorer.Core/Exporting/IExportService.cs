using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Exporting;

public interface IExportService
{
    Task ExportCsvAsync(IAsyncEnumerable<SearchResult> results, string path, CancellationToken cancellationToken = default);
    Task ExportJsonAsync(IAsyncEnumerable<SearchResult> results, string path, CancellationToken cancellationToken = default);
}
