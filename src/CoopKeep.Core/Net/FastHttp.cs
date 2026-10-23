using System.Net;
using System.Net.Sockets;

namespace CoopKeep.Core.Net;

/// <summary>
/// Programın bütün internet istekleri için <see cref="HttpClient"/>: önce IPv4 adresi sorar.
/// </summary>
/// <remarks>
/// <para>
/// Kullanıcı bildirdi: "Mod bul"da arama "Aranıyor…" diye takılıp kalıyordu. Ölçünce (2026-10-23)
/// sebep DNS çıktı: .NET bir adresi çözerken IPv4 ile birlikte IPv6 kaydını da istiyor; bazı
/// ev ağlarında IPv6 sorgusu yanıtsız kalıp ~12 saniyede zaman aşımına düşüyor. Her yeni
/// bağlantı 12 saniye bekliyor, birkaç istek art arda gelince 20 saniyelik süre doluyordu.
/// </para>
/// <para>
/// Burada önce yalnızca IPv4 sorulur (milisaniyeler); IPv4 adresi olmayan yerde IPv6 denenir.
/// Proxy ayarları ve TLS olduğu gibi kalır.
/// </para>
/// </remarks>
public static class FastHttp
{
    public static HttpClient Create(TimeSpan timeout, string? userAgent = null)
    {
        var client = new HttpClient(CreateHandler()) { Timeout = timeout };
        if (userAgent is not null) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await ResolveAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
                addresses = await ResolveAsync(host, AddressFamily.InterNetworkV6, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress[]> ResolveAsync(string host, AddressFamily family, CancellationToken cancellationToken)
    {
        try { return await Dns.GetHostAddressesAsync(host, family, cancellationToken).ConfigureAwait(false); }
        catch (SocketException) { return []; }
    }
}
