using System.Net;
using System.Text;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public class UpnpRemotePlaybackTests
{
    private const string Type = "urn:schemas-upnp-org:service:AVTransport:1";
    private static readonly Uri Description = new("http://192.168.1.5/device.xml");
    private static readonly Uri Control = new("http://192.168.1.5/control");

    [Fact]
    public async Task SendsPlaybackCommandsAndReadsState()
    {
        var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var remote = new UpnpRemotePlayback(client, Description, Control, Type);
        await remote.PlayAsync();
        await remote.PauseAsync();
        await remote.SeekAsync(TimeSpan.FromSeconds(65));
        await remote.StopAsync();
        var state = await remote.GetStateAsync();
        Assert.Equal("PLAYING", state.TransportState);
        Assert.True(state.CanSeek);
        Assert.Contains(handler.Requests, r => r.Action.Contains("#Seek") && r.Body.Contains("00:01:05") && r.Body.Contains("REL_TIME"));
        Assert.Contains(handler.Requests, r => r.Action.Contains("#Stop"));
        Assert.Contains(handler.Requests, r => r.Action.Contains("#Play"));
        Assert.Contains(handler.Requests, r => r.Action.Contains("#Pause"));
    }

    [Fact]
    public void RejectsControlUrlOnAnotherDevice() =>
        Assert.Throws<ArgumentException>(() => new UpnpRemotePlayback(new HttpClient(), Description, new Uri("http://192.168.1.6/control"), Type));

    [Fact]
    public async Task SurfacesReceiverErrors()
    {
        using var client = new HttpClient(new FakeHandler { Fail = true });
        var remote = new UpnpRemotePlayback(client, Description, Control, Type);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => remote.PlayAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public List<(string Action, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var action = request.Headers.GetValues("SOAPACTION").Single();
            Requests.Add((action, await request.Content!.ReadAsStringAsync(cancellationToken)));
            var body = action.Contains("GetTransportInfo") ? "<CurrentTransportState>PLAYING</CurrentTransportState>" :
                action.Contains("GetCurrentTransportActions") ? "<Actions>Play,Pause,Seek,Stop</Actions>" : "";
            return new HttpResponseMessage(Fail ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = new StringContent($"<root>{body}</root>", Encoding.UTF8, "text/xml") };
        }
    }
}
