using IISLogExplorer.Core.Files;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Files;

public sealed class LogFileScanner : ILogFileScanner
{
    public Task<IReadOnlyList<FileInfo>> ScanFilesAsync(LogSource source, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<FileInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.SourceType == LogSourceType.File)
            {
                return File.Exists(source.Path) ? [new FileInfo(source.Path)] : [];
            }

            if (!Directory.Exists(source.Path))
            {
                return [];
            }

            var option = source.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                return Directory.EnumerateFiles(source.Path, "*.log", option)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
            catch (DirectoryNotFoundException)
            {
                return [];
            }
        }, cancellationToken);
    }
}
