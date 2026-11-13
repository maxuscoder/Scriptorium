using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Discovers UPnP AV MediaRenderers through IPv4 SSDP.</summary>
public sealed class UpnpDeviceDiscovery : IDeviceDiscovery
{
    private const string Search = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 256 * 1024 };

    public async Task<IReadOnlyList<NetworkMediaDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var request = Encoding.ASCII.GetBytes(Search);
        await udp.SendAsync(request, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var locations = new HashSet<Uri>();
        try
        {
            while (true)
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                var location = ParseLocation(Encoding.UTF8.GetString(response.Buffer), response.RemoteEndPoint.Address);
                if (location is not null) locations.Add(location);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        var results = await Task.WhenAll(locations.Select(location => ReadDescriptionAsync(location, cancellationToken)));
        return results.SelectMany(x => x).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static Uri? ParseLocation(string response, IPAddress sender)
    {
        var lines = response.Split('\n');
        if (lines.Length == 0 || !lines[0].Trim().StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(line[(separator + 1)..].Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp && string.IsNullOrEmpty(uri.UserInfo)
                && IPAddress.TryParse(uri.Host, out var address) && address.Equals(sender)) return uri;
        }
        return null;
    }

    public static IReadOnlyList<NetworkMediaDevice> ParseDescription(string xml, Uri location)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 256 * 1024 });
        var document = XDocument.Load(reader);
        return document.Descendants().Where(e => e.Name.LocalName == "device")
            .Select(e => new { Type = Value(e, "deviceType"), Id = Value(e, "UDN"), Name = Value(e, "friendlyName"), Manufacturer = Value(e, "manufacturer") })
            .Where(d => d.Type.StartsWith("urn:schemas-upnp-org:device:MediaRenderer:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new NetworkMediaDevice(d.Id, d.Name, d.Manufacturer, location)).ToArray();
    }

    private static string Value(XElement element, string name) => element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

    private static async Task<IReadOnlyList<NetworkMediaDevice>> ReadDescriptionAsync(Uri location, CancellationToken token)
    {
        try
        {
            var xml = await Client.GetStringAsync(location, token);
            return ParseDescription(xml, location);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or XmlException or TaskCanceledException or InvalidOperationException)
        {
            token.ThrowIfCancellationRequested();
            return Array.Empty<NetworkMediaDevice>();
        }
    }
}
