using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Controls the AVTransport service of a UPnP receiver.</summary>
public sealed class UpnpRemotePlayback
{
    private readonly HttpClient _client;
    private readonly Uri _controlUri;
    private readonly string _serviceType;

    public UpnpRemotePlayback(HttpClient client, Uri descriptionUri, Uri controlUri, string serviceType)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (descriptionUri.Scheme != Uri.UriSchemeHttp || controlUri.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(descriptionUri.Host, controlUri.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(controlUri.UserInfo))
            throw new ArgumentException("The receiver control URL must use HTTP on the discovered device.");
        if (!serviceType.StartsWith("urn:schemas-upnp-org:service:AVTransport:", StringComparison.Ordinal) ||
            !int.TryParse(serviceType["urn:schemas-upnp-org:service:AVTransport:".Length..], out var version) || version < 1)
            throw new ArgumentException("Invalid AVTransport service type.", nameof(serviceType));
        _client = client;
        _controlUri = controlUri;
        _serviceType = serviceType;
    }

    public Task PlayAsync(CancellationToken token = default) => InvokeAsync("Play", [("Speed", "1")], token);
    public Task PauseAsync(CancellationToken token = default) => InvokeAsync("Pause", [], token);
    public Task StopAsync(CancellationToken token = default) => InvokeAsync("Stop", [], token);
    public Task SeekAsync(TimeSpan position, CancellationToken token = default)
    {
        if (position < TimeSpan.Zero || position.TotalHours >= 1000) throw new ArgumentOutOfRangeException(nameof(position));
        var time = $"{(long)position.TotalHours:00}:{position.Minutes:00}:{position.Seconds:00}";
        return InvokeAsync("Seek", [("Unit", "REL_TIME"), ("Target", time)], token);
    }

    public async Task<RemotePlaybackState> GetStateAsync(CancellationToken token = default)
    {
        var transport = await InvokeAsync("GetTransportInfo", [], token);
        var actions = await InvokeAsync("GetCurrentTransportActions", [], token);
        var state = Value(transport, "CurrentTransportState");
        var supported = Value(actions, "Actions").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new RemotePlaybackState(state, supported.Contains("Seek", StringComparer.OrdinalIgnoreCase));
    }

    private async Task<XDocument> InvokeAsync(string action, (string Name, string Value)[] arguments, CancellationToken token)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = _serviceType;
        var body = new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap),
            new XAttribute(soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(soap + "Body", new XElement(service + action,
                new XElement("InstanceID", "0"), arguments.Select(arg => new XElement(arg.Name, arg.Value))))));
        using var request = new HttpRequestMessage(HttpMethod.Post, _controlUri);
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{_serviceType}#{action}\"");
        request.Content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The receiver rejected {action} (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > 256 * 1024) throw new InvalidDataException("The receiver response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 256 * 1024 });
        return await XDocument.LoadAsync(reader, LoadOptions.None, token);
    }

    private static string Value(XDocument document, string name) => document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
}

public sealed record RemotePlaybackState(string TransportState, bool CanSeek);
