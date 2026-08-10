using Microsoft.Data.Sqlite;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Defines the per-user storage location and connection string for the local database.
/// </summary>
public sealed class DatabaseLocation
{
    private DatabaseLocation(string filePath)
    {
        FilePath = filePath;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    /// <summary>Gets the full path to the SQLite database file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the SQLite connection string for the database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates the default database location in the current user's local app-data directory.</summary>
    public static DatabaseLocation CreateDefault(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("The database file name must not include a directory path.", nameof(fileName));
        }

        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scriptorium");

        Directory.CreateDirectory(directoryPath);
        return new DatabaseLocation(Path.Combine(directoryPath, fileName));
    }
}
