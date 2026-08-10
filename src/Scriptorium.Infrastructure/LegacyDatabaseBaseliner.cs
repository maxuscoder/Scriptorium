using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Marks pre-migration SQLite databases as having the initial schema after their safe upgrade.
/// </summary>
public static class LegacyDatabaseBaseliner
{
    /// <summary>Determines whether an existing application database has no EF migration history.</summary>
    public static async Task<bool> RequiresBaselineAsync(
        ScriptoriumDbContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            return await TableExistsAsync(connection, "MediaItems", cancellationToken) &&
                   !await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>Creates the EF history table and records the initial migration without recreating existing data.</summary>
    public static async Task BaselineAsync(
        ScriptoriumDbContext context,
        string initialMigrationId,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """,
                cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($migrationId, $productVersion);";
            AddParameter(command, "$migrationId", initialMigrationId);
            AddParameter(command, "$productVersion", "9.0.0");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        AddParameter(command, "$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task ExecuteAsync(DbConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
