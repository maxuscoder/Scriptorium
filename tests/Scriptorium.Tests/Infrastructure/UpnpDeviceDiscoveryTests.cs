using Xunit;
using System.Net;
using System.Xml;
using Scriptorium.Infrastructure.Services;

namespace Scriptorium.Tests.Infrastructure;

public class UpnpDeviceDiscoveryTests
{
    [Fact]
    public void LocationMustBeHttpAndMatchTheRespondingHost()
    {
        var sender = IPAddress.Parse("192.168.1.2");
        Assert.NotNull(UpnpDeviceDiscovery.ParseLocation("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.2:123/device.xml\r\n\r\n", sender));
        Assert.Null(UpnpDeviceDiscovery.ParseLocation("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.3/device.xml\r\n\r\n", sender));
        Assert.Null(UpnpDeviceDiscovery.ParseLocation("HTTP/1.1 200 OK\r\nLOCATION: http://example.com/device.xml\r\n\r\n", sender));
        Assert.Null(UpnpDeviceDiscovery.ParseLocation("HTTP/1.1 404 Not Found\r\nLOCATION: http://192.168.1.2/device.xml\r\n\r\n", sender));
    }

    [Fact]
    public void DescriptionFindsEmbeddedRenderersAndIgnoresOtherDevices()
    {
        const string xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0"><device><deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType><friendlyName>Server</friendlyName><UDN>uuid:server</UDN><deviceList><device><deviceType>urn:schemas-upnp-org:device:MediaRenderer:2</deviceType><friendlyName>Living room</friendlyName><manufacturer>Example</manufacturer><UDN>uuid:tv</UDN></device></deviceList></device></root>
            """;
        var devices = UpnpDeviceDiscovery.ParseDescription(xml, new Uri("http://192.168.1.2/device.xml"));
        var device = Assert.Single(devices);
        Assert.Equal("uuid:tv", device.Id);
        Assert.Equal("Living room", device.Name);
        Assert.Equal("Example", device.Manufacturer);
    }

    [Fact]
    public void DescriptionRejectsDtd()
    {
        Assert.Throws<XmlException>(() => UpnpDeviceDiscovery.ParseDescription("<!DOCTYPE root [<!ENTITY x 'test'>]><root>&x;</root>", new Uri("http://192.168.1.2/")));
    }
}

