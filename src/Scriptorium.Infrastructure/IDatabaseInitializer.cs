namespace Scriptorium.Infrastructure;

/// <summary>
/// Creates and verifies the application's local database.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>Creates the database when needed and verifies that it can be reached.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
