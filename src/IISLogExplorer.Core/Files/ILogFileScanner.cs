using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Files;

public interface ILogFileScanner
{
    Task<IReadOnlyList<FileInfo>> ScanFilesAsync(LogSource source, CancellationToken cancellationToken = default);
}
