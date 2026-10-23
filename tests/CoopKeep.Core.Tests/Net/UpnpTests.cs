using CoopKeep.Core.Net;
using Xunit;

namespace CoopKeep.Core.Tests.Net;

/// <summary>UPnP yanıtlarını okuma (ağa çıkmadan).</summary>
public class UpnpTests
{
    [Fact]
    public void ParseLocation_ReadsHeader()
    {
        const string reply = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nLocation: http://192.168.1.1:5000/rootDesc.xml\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

        Assert.Equal(new Uri("http://192.168.1.1:5000/rootDesc.xml"), UpnpGateway.ParseLocation(reply));
        Assert.Null(UpnpGateway.ParseLocation("HTTP/1.1 200 OK\r\n\r\n"));
    }

    [Fact]
    public void FindControl_PrefersWanIpConnection_RelativeToLocation()
    {
        const string xml = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <deviceList><device><deviceList><device>
                  <serviceList>
                    <service>
                      <serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType>
                      <controlURL>/ctl/PPP</controlURL>
                    </service>
                    <service>
                      <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                      <controlURL>/ctl/IPConn</controlURL>
                    </service>
                  </serviceList>
                </device></deviceList></device></deviceList>
              </device>
            </root>
            """;

        var control = UpnpGateway.FindControl(xml, new Uri("http://192.168.1.1:5000/rootDesc.xml"));

        Assert.NotNull(control);
        Assert.Equal(new Uri("http://192.168.1.1:5000/ctl/IPConn"), control.Value.Url);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", control.Value.ServiceType);
    }

    [Fact]
    public void FindControl_NoWanService_ReturnsNull() =>
        Assert.Null(UpnpGateway.FindControl("<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device/></root>", new Uri("http://10.0.0.1/")));

    [Fact]
    public void ParseExternalIp_ReadsAddress()
    {
        const string soap = "<s:Envelope><s:Body><u:GetExternalIPAddressResponse><NewExternalIPAddress>203.0.113.7</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>";

        Assert.Equal("203.0.113.7", UpnpGateway.ParseExternalIp(soap));
        Assert.Null(UpnpGateway.ParseExternalIp("<x/>"));
    }
}
