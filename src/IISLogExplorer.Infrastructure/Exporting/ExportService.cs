using System.Text;
using System.Text.Json;
using IISLogExplorer.Core.Exporting;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Exporting;

public sealed class ExportService : IExportService
{
    public async Task ExportCsvAsync(IAsyncEnumerable<SearchResult> results, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        await writer.WriteLineAsync("Time,Status,Method,ResolvedClientIp,URL,TimeTakenMs,UserAgent,SourceFile,LineNumber");
        await foreach (var result in results.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var e = result.Entry;
            var values = new[] { e.TimestampLocal?.ToString("O") ?? "", e.DisplayStatus, e.Method ?? "", e.ResolvedClientIp ?? "", e.DisplayUrl, e.TimeTakenMs?.ToString() ?? "", e.UserAgent ?? "", result.SourceFile, e.LineNumber.ToString() };
            await writer.WriteLineAsync(string.Join(',', values.Select(Escape)));
        }
    }

    public async Task ExportJsonAsync(IAsyncEnumerable<SearchResult> results, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();
        await foreach (var result in results.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            JsonSerializer.Serialize(writer, new { result.Entry.TimestampLocal, Status = result.Entry.DisplayStatus, result.Entry.Method, ClientIp = result.Entry.ResolvedClientIp, Url = result.Entry.DisplayUrl, result.Entry.TimeTakenMs, result.Entry.UserAgent, result.SourceFile, result.Entry.LineNumber, result.Entry.RawLine });
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value) => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
