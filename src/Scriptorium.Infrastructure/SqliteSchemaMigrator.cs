using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Safely upgrades databases created before the media-item schema was expanded.
/// </summary>
public static class SqliteSchemaMigrator
{
    private const string LegacyFolderId = "00000000-0000-0000-0000-000000000001";

    /// <summary>Applies the version-one media-item schema upgrade when it is required.</summary>
    public static async Task UpgradeAsync(ScriptoriumDbContext context, CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            if (!await TableExistsAsync(connection, "MediaItems", cancellationToken))
            {
                return;
            }

            var mediaItemColumns = await GetColumnNamesAsync(connection, "MediaItems", cancellationToken);
            if (IsCurrentSchema(mediaItemColumns) && !await TableExistsAsync(connection, "Favorites", cancellationToken))
            {
                await ExecuteAsync(connection, "PRAGMA user_version = 1;", cancellationToken);
                return;
            }

            await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);

            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await ExecuteAsync(
                    connection,
                    $"INSERT OR IGNORE INTO \"LibraryFolders\" (\"Id\", \"Path\", \"Name\", \"IsEnabled\") VALUES ('{LegacyFolderId}', '__legacy__', 'Legacy imported media', 1);",
                    cancellationToken,
                    transaction);
                await ExecuteAsync(connection, "DROP TABLE IF EXISTS \"MediaItems_Upgrade\";", cancellationToken, transaction);
                await ExecuteAsync(
                    connection,
                    """
                    CREATE TABLE "MediaItems_Upgrade" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_MediaItems" PRIMARY KEY,
                        "Title" TEXT NOT NULL, "Path" TEXT NOT NULL, "ThumbnailPath" TEXT NULL,
                        "DateAdded" TEXT NOT NULL, "LastPlayed" TEXT NULL, "IsFavorite" INTEGER NOT NULL DEFAULT 0,
                        "MediaType" INTEGER NOT NULL, "CategoryId" TEXT NULL, "LibraryFolderId" TEXT NOT NULL,
                        "RuntimeSeconds" INTEGER NULL, "ReleaseYear" INTEGER NULL, "Description" TEXT NULL,
                        "PlaybackPositionSeconds" INTEGER NOT NULL DEFAULT 0, "IsCompleted" INTEGER NOT NULL DEFAULT 0,
                        "FileSize" INTEGER NULL, "CreatedDate" TEXT NULL, "ModifiedDate" TEXT NULL,
                        "IsMissing" INTEGER NOT NULL DEFAULT 0, "MissingSince" TEXT NULL,
                        CONSTRAINT "FK_MediaItems_Categories_CategoryId"
                            FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE SET NULL,
                        CONSTRAINT "FK_MediaItems_LibraryFolders_LibraryFolderId"
                            FOREIGN KEY ("LibraryFolderId") REFERENCES "LibraryFolders" ("Id") ON DELETE RESTRICT
                    );
                    """,
                    cancellationToken,
                    transaction);
                await ExecuteAsync(connection, BuildCopyStatement(mediaItemColumns), cancellationToken, transaction);
                await ExecuteAsync(connection, "DROP TABLE \"MediaItems\";", cancellationToken, transaction);
                await ExecuteAsync(connection, "ALTER TABLE \"MediaItems_Upgrade\" RENAME TO \"MediaItems\";", cancellationToken, transaction);
                await ExecuteAsync(connection, "CREATE INDEX \"IX_MediaItems_Path\" ON \"MediaItems\" (\"Path\");", cancellationToken, transaction);
                await ExecuteAsync(connection, "CREATE INDEX \"IX_MediaItems_LibraryFolderId\" ON \"MediaItems\" (\"LibraryFolderId\");", cancellationToken, transaction);
                await ExecuteAsync(connection, "CREATE INDEX \"IX_MediaItems_CategoryId\" ON \"MediaItems\" (\"CategoryId\");", cancellationToken, transaction);

                if (await TableExistsAsync(connection, "Favorites", cancellationToken, transaction))
                {
                    var favoriteColumns = await GetColumnNamesAsync(connection, "Favorites", cancellationToken, transaction);
                    var favoriteKey = favoriteColumns.Contains("MediaItemId") ? "MediaItemId" : "MediaId";
                    if (favoriteColumns.Contains(favoriteKey))
                    {
                        await ExecuteAsync(connection, $"UPDATE \"MediaItems\" SET \"IsFavorite\" = 1 WHERE \"Id\" IN (SELECT \"{favoriteKey}\" FROM \"Favorites\");", cancellationToken, transaction);
                    }

                    await ExecuteAsync(connection, "DROP TABLE \"Favorites\";", cancellationToken, transaction);
                }

                await ExecuteAsync(connection, "PRAGMA user_version = 1;", cancellationToken, transaction);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static bool IsCurrentSchema(IReadOnlySet<string> columns) =>
        columns.Contains("LibraryFolderId") && columns.Contains("RuntimeSeconds") &&
        columns.Contains("PlaybackPositionSeconds") && columns.Contains("IsCompleted") &&
        columns.Contains("FileSize") && columns.Contains("CreatedDate") && columns.Contains("ModifiedDate") &&
        columns.Contains("IsMissing") && columns.Contains("MissingSince") &&
        !columns.Contains("TVShow_Description") && !columns.Contains("TVShow_ReleaseYear");

    private static string BuildCopyStatement(IReadOnlySet<string> columns)
    {
        var runtimeSeconds = columns.Contains("Runtime")
            ? "CASE WHEN \"Runtime\" IS NULL THEN NULL ELSE CAST(\"Runtime\" / 10000000 AS INTEGER) END"
            : ColumnOrDefault(columns, "RuntimeSeconds", "NULL");

        return $"""
            INSERT INTO "MediaItems_Upgrade" (
                "Id", "Title", "Path", "ThumbnailPath", "DateAdded", "LastPlayed", "IsFavorite", "MediaType", "CategoryId", "LibraryFolderId", "RuntimeSeconds", "ReleaseYear", "Description", "PlaybackPositionSeconds", "IsCompleted", "FileSize", "CreatedDate", "ModifiedDate", "IsMissing", "MissingSince")
            SELECT
                {ColumnOrDefault(columns, "Id", "lower(hex(randomblob(16)))")}, {ColumnOrDefault(columns, "Title", "''")}, {ColumnOrDefault(columns, "Path", "''")}, {ColumnOrDefault(columns, "ThumbnailPath", "NULL")},
                {ColumnOrDefault(columns, "DateAdded", "CURRENT_TIMESTAMP")}, {ColumnOrDefault(columns, "LastPlayed", "NULL")}, {ColumnOrDefault(columns, "IsFavorite", "0")}, {ColumnOrDefault(columns, "MediaType", "0")},
                {ColumnOrDefault(columns, "CategoryId", "NULL")}, COALESCE({ColumnOrDefault(columns, "LibraryFolderId", "NULL")}, '{LegacyFolderId}'), {runtimeSeconds},
                {ColumnOrDefault(columns, "ReleaseYear", "NULL")}, {ColumnOrDefault(columns, "Description", "NULL")}, {ColumnOrDefault(columns, "PlaybackPositionSeconds", "0")},
                {ColumnOrDefault(columns, "IsCompleted", "0")}, {ColumnOrDefault(columns, "FileSize", "NULL")}, {ColumnOrDefault(columns, "CreatedDate", "NULL")}, {ColumnOrDefault(columns, "ModifiedDate", "NULL")},
                {ColumnOrDefault(columns, "IsMissing", "0")}, {ColumnOrDefault(columns, "MissingSince", "NULL")}
            FROM "MediaItems";
            """;
    }

    private static string ColumnOrDefault(IReadOnlySet<string> columns, string columnName, string defaultValue) =>
        columns.Contains(columnName) ? $"\"{columnName}\"" : defaultValue;

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken cancellationToken, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string tableName, CancellationToken cancellationToken, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return names;
    }

    private static async Task ExecuteAsync(DbConnection connection, string commandText, CancellationToken cancellationToken, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
