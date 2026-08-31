using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Infrastructure.Database;

namespace IISLogExplorer.Tests;

public class Phase2FixTests
{
    [Fact]
    public async Task IncrementalIndexUsesPersistedHeader()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-phase2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = new SqliteConnectionFactory(Path.Combine(root, "test.db"));
        await new DatabaseInitializer(factory).InitializeAsync();
        var parser = new IisW3cLogParser(new FieldsHeaderParser(), new ClientIpResolver());
        var scanner = new IISLogExplorer.Infrastructure.Files.LogFileScanner();
        var sources = new SourceRepository(factory);
        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        var index = new IISLogExplorer.Infrastructure.Indexing.SqliteIndexService(scanner, parser, files, entries, new IISLogExplorer.Infrastructure.Files.FileFingerprintService(), factory);

        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "u_ex260828.log");
        await File.WriteAllTextAsync(file, TestHelpers.SampleW3CLog(500, "/api/order"));
        var source = await sources.SaveAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = "logs", Path = dir });

        await index.IndexAsync(source);
        var state = (await index.GetFileStatesAsync(source)).Single();
        Assert.True(state.IsFullyIndexed);
        Assert.NotNull(state.FieldsHeader);
        Assert.StartsWith("#Fields:", state.FieldsHeader);

        IisW3cLogParser.InvalidateHeaderCache(file);
        Assert.Null(IisW3cLogParser.GetActiveHeader(file));

        var tail = string.Join('\n', Enumerable.Range(0, 10).Select(i => $"2026-08-28 11:00:{i:00} 10.0.0.1 GET /tail/{i} - 443 - 1.2.3.4 \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" - 200 0 0 0 100 200"));
        await File.AppendAllTextAsync(file, tail + '\n');
        await index.IndexAsync(source);

        Assert.Equal(510, await entries.CountAsync());
        var final = (await index.GetFileStatesAsync(source)).Single();
        Assert.True(final.IsFullyIndexed);
        Assert.NotNull(final.FieldsHeader);

        var hits = new List<SearchResult>();
        var search = new IISLogExplorer.Infrastructure.Searching.HybridSearchService(scanner, parser, files, entries, new IISLogExplorer.Infrastructure.Files.FileFingerprintService());
        await foreach (var result in search.SearchAsync(new SearchRequest { Source = source, Keyword = "/tail/", MaxResults = 100 })) hits.Add(result);
        Assert.Equal(10, hits.Count);
        Assert.Equal(Enumerable.Range(505, 10).Select(i => (long)i).ToArray(), hits.OrderBy(h => h.Entry.LineNumber).Select(h => h.Entry.LineNumber).ToArray());
    }

    [Fact]
    public async Task OldDatabaseMigratesWithoutDataLoss()
    {
        var root = Path.Combine(Path.GetTempPath(), "iislog-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = new SqliteConnectionFactory(Path.Combine(root, "test.db"));
        await using (var connection = await factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Sources (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceType INTEGER NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    IncludeSubfolders INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastUsedAt TEXT NULL,
                    UNIQUE(SourceType, Path)
                );
                CREATE TABLE LogFiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceId INTEGER NOT NULL,
                    FullPath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    LastWriteUtc TEXT NOT NULL,
                    IndexedLength INTEGER NOT NULL DEFAULT 0,
                    IndexedLineCount INTEGER NOT NULL DEFAULT 0,
                    IsFullyIndexed INTEGER NOT NULL DEFAULT 0,
                    HeaderHash TEXT NULL,
                    FileFingerprint TEXT NULL,
                    LastIndexedAt TEXT NULL,
                    UNIQUE(SourceId, FullPath)
                );
                CREATE TABLE LogEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceId INTEGER NOT NULL,
                    FileId INTEGER NOT NULL,
                    LineNumber INTEGER NOT NULL,
                    TimestampUtc TEXT NULL,
                    RawLine TEXT NULL,
                    UNIQUE(FileId, LineNumber)
                );
                INSERT INTO Sources (SourceType, DisplayName, Path, IncludeSubfolders, CreatedAt, LastUsedAt) VALUES (0, 'legacy', 'C:\legacy', 0, '2026-01-01T00:00:00.0000000+00:00', NULL);
                INSERT INTO LogFiles (SourceId, FullPath, FileName, FileSize, LastWriteUtc, IndexedLength, IndexedLineCount, IsFullyIndexed, HeaderHash, FileFingerprint, LastIndexedAt)
                VALUES (1, 'C:\legacy\u_ex.log', 'u_ex.log', 123, '2026-08-28T00:00:00.0000000+00:00', 100, 10, 1, 'hash-old', 'fingerprint-old', '2026-08-28T00:00:01.0000000+00:00');
                INSERT INTO LogEntries (SourceId, FileId, LineNumber, TimestampUtc, RawLine) VALUES (1, 1, 1, '2026-08-28T00:00:00.0000000+00:00', 'raw1'), (1, 1, 2, '2026-08-28T00:00:01.0000000+00:00', 'raw2');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new DatabaseInitializer(factory).InitializeAsync();

        var files = new LogFileRepository(factory);
        var entries = new LogEntryRepository(factory);
        Assert.Equal(1, await files.CountAsync());
        Assert.Equal(2, await entries.CountAsync());

        await using var verify = await factory.OpenAsync();
        await using var pragma = verify.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(LogFiles);";
        var columns = new List<string>();
        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("FieldsHeader", columns);

        var state = (await files.GetBySourceAsync(1)).Single();
        Assert.Equal("C:\\legacy\\u_ex.log", state.FullPath);
        Assert.Equal("hash-old", state.HeaderHash);
        Assert.Null(state.FieldsHeader);

        await files.UpdateProgressAsync(state.Id, 200, new DateTime(2026, 8, 28), 150, 20, true, "fingerprint-new", "#Fields: date time");
        var updated = (await files.GetBySourceAsync(1)).Single();
        Assert.Equal("#Fields: date time", updated.FieldsHeader);
        Assert.Equal("fingerprint-new", updated.FileFingerprint);
    }
}