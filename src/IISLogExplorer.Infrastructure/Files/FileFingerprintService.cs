using System.Security.Cryptography;
using System.Text;

namespace IISLogExplorer.Infrastructure.Files;

public sealed class FileFingerprintService
{
    public async Task<string> ComputeAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[Math.Min(4096, Math.Max(1, file.Length))];
        var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        var prefixHash = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        return $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{prefixHash}";
    }
}
