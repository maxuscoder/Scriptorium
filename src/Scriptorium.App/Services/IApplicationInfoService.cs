namespace Scriptorium.App.Services;

public interface IApplicationInfoService
{
    string ApplicationName { get; }
    string Version { get; }
    string BuildInformation { get; }
}
