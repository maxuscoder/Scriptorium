using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Initializes the SQLite database before the application begins using it.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    DatabaseLocation databaseLocation,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await SqliteSchemaMigrator.UpgradeAsync(context, cancellationToken);
        await SqliteSchemaMigrator.UpgradeAsync(context, cancellationToken);

        if (!await context.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Unable to connect to the local SQLite database at '{databaseLocation.FilePath}'.");
        }

        logger.LogInformation("Connected to local SQLite database at {DatabasePath}.", databaseLocation.FilePath);
    }
}
