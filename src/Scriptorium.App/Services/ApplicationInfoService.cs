using Microsoft.Extensions.Configuration;

namespace Scriptorium.App.Services;

/// <summary>
/// Provides application-level values sourced from configuration.
/// </summary>
public sealed class ApplicationInfoService : IApplicationInfoService
{
    public ApplicationInfoService(IConfiguration configuration)
    {
        ApplicationName = configuration["Application:Name"] ?? "Scriptorium";
    }

    public string ApplicationName { get; }
}
