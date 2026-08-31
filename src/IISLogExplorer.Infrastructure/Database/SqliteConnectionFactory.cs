using Microsoft.Data.Sqlite;

namespace IISLogExplorer.Infrastructure.Database;

public sealed class SqliteConnectionFactory
{
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(AppContext.BaseDirectory, "IISLogExplorer.db");
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.DefaultTimeout = 5;
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
