using System.Net;
using System.Net.Http.Headers;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public class LocalMediaStreamTests
{
    [Fact]
    public async Task ServesOriginalBytesAndRangesWithoutNetworkAccess()
    {
        var path = Path.GetTempFileName();
        try
        {
            var data = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
            await File.WriteAllBytesAsync(path, data);
            await using var server = new LocalMediaStream(IPAddress.Loopback, IPAddress.Loopback, path, "video/mp4");
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            Assert.Equal(data, await client.GetByteArrayAsync(server.Uri));
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Uri);
            request.Headers.Range = new RangeHeaderValue(10, 19);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(data[10..20], await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("bytes 10-19/128", response.Content.Headers.ContentRange!.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(server.Uri, "/wrong"))).StatusCode);
            using var invalid = new HttpRequestMessage(HttpMethod.Get, server.Uri);
            invalid.Headers.Range = new RangeHeaderValue(200, null);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await client.SendAsync(invalid)).StatusCode);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("bytes=-5", 100, 95, 99)]
    [InlineData("bytes=5-", 100, 5, 99)]
    [InlineData("bytes=5-200", 100, 5, 99)]
    public void ParsesRanges(string value, long length, long expectedStart, long expectedEnd)
    {
        Assert.True(LocalMediaStream.TryRange(value, length, out var start, out var end));
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }
}
