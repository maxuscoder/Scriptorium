namespace Scriptorium.Core.Services;

public sealed record NetworkMediaDevice(string Id, string Name, string Manufacturer, Uri DescriptionLocation);

public interface IDeviceDiscovery
{
    Task<IReadOnlyList<NetworkMediaDevice>> DiscoverAsync(CancellationToken cancellationToken = default);
}
