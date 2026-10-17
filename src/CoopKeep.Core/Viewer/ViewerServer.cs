using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoopKeep.Core.Viewer;

/// <summary>
/// Ev sahibinin haritasını diğer oyunculara salt okunur olarak paylaşır.
/// </summary>
/// <remarks>
/// <para>
/// Tek bir uç nokta var: <c>GET /snapshot?code=XXXX</c> → <see cref="MapSnapshot"/> JSON.
/// Başka hiçbir istek kabul edilmez; komut gönderme yolu yoktur.
/// </para>
/// <para>
/// <c>HttpListener</c> yerine <c>TcpListener</c>: Windows'ta <c>HttpListener</c> dış ağa
/// açılmak için yönetici izni (URL ACL) istiyor. İstekler küçük ve tek satırlık; tam bir
/// HTTP sunucusuna gerek yok. Paylaşım kodu tahmin edilemeyecek kadar uzun ve sabit
/// zamanlı karşılaştırılıyor.
/// </para>
/// </remarks>
public sealed class ViewerServer : IAsyncDisposable
{
    private const int MaxRequestBytes = 4096;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _code;
    private volatile byte[] _payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new MapSnapshot(), Json));
    private Task? _loop;

    public ViewerServer(int port, string code)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (code.Length < 6) throw new ArgumentException("Paylaşım kodu çok kısa.", nameof(code));

        Port = port;
        _code = code;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public int Port { get; }

    /// <summary>Son 1 dakikada istek yapan farklı adres sayısı (arayüzde "2 izleyici").</summary>
    public int RecentViewers
    {
        get
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            lock (_seen) return _seen.Count(kv => kv.Value > cutoff);
        }
    }

    private readonly Dictionary<string, DateTime> _seen = [];

    /// <summary>Okunması kolay rastgele kod (karışan harfler yok).</summary>
    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> chars = stackalloc char[10];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>Gönderilecek anlık görüntüyü günceller.</summary>
    public void Publish(MapSnapshot snapshot) =>
        _payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, Json));

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;

        try
        {
            var stream = client.GetStream();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var buffer = new byte[MaxRequestBytes];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), timeout.Token).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
                if (buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) >= 0) break;
            }

            var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n", 2)[0];
            var (status, body) = Route(requestLine);

            if (status == 200 && client.Client.RemoteEndPoint is IPEndPoint ep)
                lock (_seen) _seen[ep.Address.ToString()] = DateTime.UtcNow;

            var header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\n" +
                         "Content-Type: application/json; charset=utf-8\r\n" +
                         $"Content-Length: {body.Length}\r\n" +
                         "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            // Bağlantı koptu ya da zaman aşımı: önemsiz.
        }
    }

    /// <summary>İstek satırına göre yanıt. Test edilebilsin diye ayrı.</summary>
    public (int Status, byte[] Body) Route(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return (405, "{}"u8.ToArray());

        var target = parts[1];
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;
        if (path != "/snapshot") return (404, "{}"u8.ToArray());

        var code = q >= 0
            ? target[(q + 1)..].Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == "code")
                .Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault()
            : null;

        if (code is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(_code)))
            return (403, "{}"u8.ToArray());

        return (200, _payload);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }
}

/// <summary>İzleyici tarafı: ev sahibinden anlık görüntü çeker.</summary>
public sealed class ViewerClient(HttpClient http)
{
    public async Task<MapSnapshot?> FetchAsync(string host, int port, string code, CancellationToken ct = default)
    {
        var url = $"http://{host.Trim()}:{port}/snapshot?code={Uri.EscapeDataString(code.Trim().ToUpperInvariant())}";
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<MapSnapshot>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }
}
