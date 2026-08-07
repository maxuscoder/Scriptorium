using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Scriptorium.App.Services;

/// <summary>
/// Provides application-level values sourced from configuration.
/// </summary>
public sealed class ApplicationInfoService : IApplicationInfoService
{
    public ApplicationInfoService(
        IConfiguration configuration,
        ILogger<ApplicationInfoService> logger)
    {
        ApplicationName = configuration["Application:Name"] ?? "Scriptorium";
        logger.LogInformation("Application information loaded for {ApplicationName}.", ApplicationName);
    }

    public string ApplicationName { get; }
}
