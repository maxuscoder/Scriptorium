using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Creates the context for Entity Framework Core design-time commands.
/// </summary>
public sealed class ScriptoriumDbContextFactory : IDesignTimeDbContextFactory<ScriptoriumDbContext>
{
    /// <inheritdoc />
    public ScriptoriumDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SCRIPTORIUM_MIGRATION_CONNECTION")
            ?? "Data Source=scriptorium.db";

        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ScriptoriumDbContext(options);
    }
}
