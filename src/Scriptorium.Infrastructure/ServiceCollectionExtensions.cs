using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Registers the infrastructure services used by the application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the local SQLite database and its initializer.</summary>
    public static IServiceCollection AddScriptoriumInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ScriptoriumDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        return services;
    }
}
