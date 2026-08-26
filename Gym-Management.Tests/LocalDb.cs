using Microsoft.Data.SqlClient;

namespace Gym_Management.Tests;

/// <summary>
/// Creates and drops throwaway LocalDB databases for integration tests
/// (AGENTS.md: real LocalDB per test fixture — no SQLite, no InMemory).
/// </summary>
public static class LocalDb
{
    public const string ServerConnectionString = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;Pooling=false";

    public static string ConnectionStringFor(string databaseName) =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";

    public static async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(ServerConnectionString + ";Database=master");
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"CREATE DATABASE [{databaseName}]");
    }

    public static async Task DropDatabaseAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ServerConnectionString + ";Database=master");
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await ExecuteAsync(connection, $"DROP DATABASE [{databaseName}]");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
