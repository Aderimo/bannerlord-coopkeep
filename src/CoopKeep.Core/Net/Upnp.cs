using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CoopKeep.Core.Net;

/// <summary>
/// Modemde port açma (UPnP IGD). Coop doğrudan bağlantıda UDP 4200 bekliyor; en çok takılınan
/// adım modemde bu portu açmak. Modem UPnP destekliyorsa tek tıkla açılır; desteklemiyorsa
/// kullanıcı rehberdeki elle açma adımlarına ya da Steam ile katılmaya yönlendirilir.
/// </summary>
/// <remarks>
/// Akış: SSDP ile ağ geçidi aranır (239.255.255.250:1900), cihaz tanımından
/// <c>WANIPConnection</c>/<c>WANPPPConnection</c> hizmetinin denetim adresi okunur, SOAP ile
/// <c>AddPortMapping</c> ve <c>GetExternalIPAddress</c> çağrılır.
/// </remarks>
public sealed class UpnpGateway
{
    private static readonly string[] ServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    ];

    private readonly HttpClient _http;

    private UpnpGateway(HttpClient http, Uri controlUrl, string serviceType, IPAddress localAddress)
    {
        _http = http;
        ControlUrl = controlUrl;
        ServiceType = serviceType;
        LocalAddress = localAddress;
    }

    public Uri ControlUrl { get; }
    public string ServiceType { get; }

    /// <summary>Bu bilgisayarın ağ geçidine bakan yerel adresi (yönlendirmenin hedefi).</summary>
    public IPAddress LocalAddress { get; }

    /// <summary>Ağdaki UPnP ağ geçidini arar; bulamazsa <c>null</c>.</summary>
    public static async Task<UpnpGateway?> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
        var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        await udp.SendAsync(request, target, cancellationToken).ConfigureAwait(false);

        var http = FastHttp.Create(TimeSpan.FromSeconds(5));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (true)
            {
                var reply = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (ParseLocation(Encoding.ASCII.GetString(reply.Buffer)) is not { } location || !tried.Add(location.ToString())) continue;

                try
                {
                    var description = await http.GetStringAsync(location, cts.Token).ConfigureAwait(false);
                    if (FindControl(description, location) is not { } control) continue;
                    var local = LocalAddressFor(reply.RemoteEndPoint.Address) ?? IPAddress.Loopback;
                    return new UpnpGateway(http, control.Url, control.ServiceType, local);
                }
                catch (Exception e) when (e is HttpRequestException or System.Xml.XmlException or TaskCanceledException)
                {
                    // Bu cihaz olmadı; başka yanıt gelirse ona bakılır.
                }
            }
        }
        catch (OperationCanceledException)
        {
            http.Dispose();
            return null;
        }
    }

    /// <summary>SSDP yanıtındaki <c>LOCATION</c> başlığı.</summary>
    public static Uri? ParseLocation(string response)
    {
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.TryCreate(line["LOCATION:".Length..].Trim(), UriKind.Absolute, out var uri) ? uri : null;
        }

        return null;
    }

    /// <summary>Cihaz tanımından WAN bağlantı hizmetinin denetim adresi.</summary>
    public static (Uri Url, string ServiceType)? FindControl(string descriptionXml, Uri location)
    {
        var doc = XDocument.Parse(descriptionXml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var baseUrl = doc.Root?.Element(ns + "URLBase")?.Value is { Length: > 0 } b && Uri.TryCreate(b, UriKind.Absolute, out var bu) ? bu : location;

        foreach (var type in ServiceTypes)
        {
            var service = doc.Descendants(ns + "service").FirstOrDefault(s => (string?)s.Element(ns + "serviceType") == type);
            if (service?.Element(ns + "controlURL")?.Value is { Length: > 0 } control)
                return (new Uri(baseUrl, control), type);
        }

        return null;
    }

    /// <summary>Gelen UDP <paramref name="port"/> trafiğini bu bilgisayara yönlendirir.</summary>
    /// <returns>Modemin yanıtı başarılıysa <c>true</c>.</returns>
    public async Task<bool> AddPortMappingAsync(int port, string protocol, string description, CancellationToken cancellationToken = default)
    {
        var body =
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>{protocol}</NewProtocol>" +
            $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{LocalAddress}</NewInternalClient><NewEnabled>1</NewEnabled>" +
            $"<NewPortMappingDescription>{System.Security.SecurityElement.Escape(description)}</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>";
        var (ok, _) = await SoapAsync("AddPortMapping", body, cancellationToken).ConfigureAwait(false);
        return ok;
    }

    /// <summary>Modemin internetteki adresi; alınamazsa <c>null</c>.</summary>
    public async Task<string?> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        var (ok, response) = await SoapAsync("GetExternalIPAddress", "", cancellationToken).ConfigureAwait(false);
        return ok ? ParseExternalIp(response) : null;
    }

    public static string? ParseExternalIp(string soapResponse)
    {
        var m = Regex.Match(soapResponse, @"<NewExternalIPAddress>\s*([^<\s]+)\s*</NewExternalIPAddress>");
        return m.Success && IPAddress.TryParse(m.Groups[1].Value, out _) ? m.Groups[1].Value : null;
    }

    private async Task<(bool Ok, string Body)> SoapAsync(string action, string arguments, CancellationToken cancellationToken)
    {
        var envelope =
            "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            $"<s:Body><u:{action} xmlns:u=\"{ServiceType}\">{arguments}</u:{action}></s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, ControlUrl)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{ServiceType}#{action}\"");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (response.IsSuccessStatusCode, text);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, "");
        }
    }

    /// <summary>Ağ geçidine giden yolda kullanılan yerel adres.</summary>
    private static IPAddress? LocalAddressFor(IPAddress gateway)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(gateway, 1900); // UDP: paket gönderilmez, yalnızca yol seçilir
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
