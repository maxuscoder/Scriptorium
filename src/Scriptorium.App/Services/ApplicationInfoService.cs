using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Scriptorium.App.Services;

/// <summary>
/// Provides application identity and build metadata.
/// </summary>
public sealed class ApplicationInfoService : IApplicationInfoService
{
    public ApplicationInfoService(
        IConfiguration configuration,
        ILogger<ApplicationInfoService> logger)
    {
        ApplicationName = configuration["Application:Name"] ?? "Scriptorium";
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationInfoService).Assembly;
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "Unknown";
#if DEBUG
        const string buildConfiguration = "Debug";
#else
        const string buildConfiguration = "Release";
#endif
        BuildInformation = $"{buildConfiguration} | {RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";
        logger.LogInformation(
            "Application information loaded for {ApplicationName} version {Version} ({BuildInformation}).",
            ApplicationName,
            Version,
            BuildInformation);
    }

    public string ApplicationName { get; }
    public string Version { get; }
    public string BuildInformation { get; }
}
