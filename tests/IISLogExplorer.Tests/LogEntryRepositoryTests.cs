using IISLogExplorer.Core.Models;
using IISLogExplorer.Infrastructure.Database;

namespace IISLogExplorer.Tests;

public class LogEntryRepositoryTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "iislog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Insert_or_ignore_prevents_duplicates()
    {
        var tempDir = CreateTempDir();
        var factory = new SqliteConnectionFactory(Path.Combine(tempDir, "test.db"));
        var init = new DatabaseInitializer(factory);
        await init.InitializeAsync();
        var repo = new LogEntryRepository(factory);
        var entry = new LogEntry { SourceId = 1, FileId = 1, LineNumber = 1, Method = "GET", UriStem = "/", StatusCode = 200, TimestampUtc = DateTimeOffset.UtcNow, RawLine = "raw" };
        await repo.InsertBatchAsync([entry]);
        await repo.InsertBatchAsync([entry]);
        var count = await repo.CountAsync();
        Assert.Equal(1, count);
    }
}
