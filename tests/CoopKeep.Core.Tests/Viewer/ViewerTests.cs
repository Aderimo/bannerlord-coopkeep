using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CoopKeep.Core.Images;
using CoopKeep.Core.Map;
using CoopKeep.Core.Viewer;

namespace CoopKeep.Core.Tests.Viewer;

public class ViewerTests
{
    [Fact]
    public void dogru_kodla_anlik_goruntu_doner()
    {
        var server = new ViewerServer(47123, "ABCDEFGH23");
        server.Publish(new MapSnapshot { World = "dünya", Players = [new PlayerSnapshot { HeroId = "h", Name = "Ahmet", X = 1, Y = 2 }] });

        var (status, body) = server.Route("GET /snapshot?code=ABCDEFGH23 HTTP/1.1");
        Assert.Equal(200, status);

        var snap = JsonSerializer.Deserialize<MapSnapshot>(body)!;
        Assert.Equal("dünya", snap.World);
        Assert.Equal("Ahmet", snap.Players[0].Name);
    }

    [Theory]
    [InlineData("GET /snapshot?code=YANLIS HTTP/1.1", 403)]
    [InlineData("GET /snapshot HTTP/1.1", 403)]
    [InlineData("GET /kick?code=ABCDEFGH23 HTTP/1.1", 404)]
    [InlineData("POST /snapshot?code=ABCDEFGH23 HTTP/1.1", 405)]
    public void yalnizca_okuma_ve_dogru_kod(string line, int expected)
        => Assert.Equal(expected, new ViewerServer(47124, "ABCDEFGH23").Route(line).Status);

    [Fact]
    public async Task istemci_sunucudan_okur()
    {
        var port = FreePort();
        await using var server = new ViewerServer(port, "KOD2345678");
        server.Publish(new MapSnapshot { World = "w" });
        server.Start();

        using var http = new HttpClient();
        var snap = await new ViewerClient(http).FetchAsync("127.0.0.1", port, "kod2345678");
        Assert.Equal("w", snap!.World);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ViewerClient(http).FetchAsync("127.0.0.1", port, "YANLIS000"));
    }

    [Fact]
    public void paylasim_kodu_karisan_harf_icermez()
    {
        var code = ViewerServer.NewCode();
        Assert.Equal(10, code.Length);
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
    }

    [Fact]
    public void harita_donusumu_kasaba_sancaklarina_oturur()
    {
        // Varcheg (361.256, 554.313) haritadaki sancağının ortası ~ (680, 125).
        var (x, y) = MapProjection.ToImage(361.256, 554.313);
        Assert.InRange(x, 670, 690);
        Assert.InRange(y, 115, 135);

        var (gx, gy) = MapProjection.ToGame(x, y);
        Assert.Equal(361.256, gx, 3);
        Assert.Equal(554.313, gy, 3);
    }

    [Theory]
    [InlineData("Imperial Legionary", "imperial-legionary")]
    [InlineData("dey Meroc", "dey-meroc")]
    [InlineData("Banu Hulyan", "banu-hulyan")]
    public void gorsel_anahtari(string name, string slug) => Assert.Equal(slug, GameImageService.Slug(name));

    [Fact]
    public void yalnizca_gorsel_dosyalari_kabul_edilir()
    {
        Assert.True(GameImageService.LooksLikeImage([0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0, 0]));
        Assert.True(GameImageService.LooksLikeImage([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(GameImageService.LooksLikeImage(Encoding.UTF8.GetBytes("<html>404</html>")));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
