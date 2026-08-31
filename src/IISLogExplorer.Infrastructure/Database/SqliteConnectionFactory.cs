using Microsoft.Data.Sqlite;

namespace IISLogExplorer.Infrastructure.Database;

public sealed class SqliteConnectionFactory
{
    public event Action<string>? BusyObserved;

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
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsBusy(exception))
        {
            BusyObserved?.Invoke(exception.Message);
            throw;
        }

        return connection;
    }

    private static bool IsBusy(SqliteException exception) => exception.SqliteErrorCode is 5 or 6;
}