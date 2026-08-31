namespace IISLogExplorer.Infrastructure.Logging;

public sealed class AppLogger
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task LogAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(directory);
        PurgeExpired(directory);
        var path = Path.Combine(directory, $"IISLogExplorer-{DateTime.Now:yyyyMMdd}.log");
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void PurgeExpired(string directory)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.EnumerateFiles(directory, "IISLogExplorer-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
