using System.Net;
using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Tests.Installation;

/// <summary>Steam Workshop açıklaması: ayrıştırma, BBCode temizliği ve önbellek.</summary>
public sealed class WorkshopDetailsTests : IDisposable
{
    private const string Yanit = """
        {"response":{"result":1,"resultcount":1,"publishedfiledetails":[{
          "publishedfileid":"2859188632","result":1,"file_size":"5447879",
          "preview_url":"https://images.steamusercontent.com/ugc/1/ABC/",
          "title":"Harmony",
          "description":"[h2]Notlar[/h2]\r\nKütüphane [url=https://github.com/pardeike/Harmony]Harmony[/url] dağıtımı.\r\n[img]https://i.imgur.com/x.png[/img]\r\n[list]\r\n[*]bir\r\n[*][b]iki[/b]\r\n[/list]\r\n\r\n\r\n\r\nSon [WIP] satır.",
          "time_updated":1784662507,"subscriptions":1618087}]}}
        """;

    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-workshop-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dizin)) Directory.Delete(_dizin, recursive: true);
    }

    [Fact]
    public void yanit_okunur()
    {
        var d = WorkshopDetails.Parse(Yanit)!;

        Assert.Equal("2859188632", d.Id);
        Assert.Equal("Harmony", d.Title);
        Assert.Equal(1618087, d.Subscriptions);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784662507), d.Updated);
        Assert.Equal("https://images.steamusercontent.com/ugc/1/ABC/", d.PreviewUrl);
    }

    [Fact]
    public void bbcode_temizlenir_satir_sonlari_korunur()
    {
        var d = WorkshopDetails.Parse(Yanit)!;

        Assert.Equal("Notlar\nKütüphane Harmony dağıtımı.\n\n• bir\n• iki\n\nSon [WIP] satır.", d.Description);
    }

    [Theory]
    [InlineData("""{"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"1","result":9}]}}""")]
    [InlineData("""{"response":{}}""")]
    [InlineData("{ bozuk")]
    public void bulunamayan_veya_bozuk_yanit_null(string json) => Assert.Null(WorkshopDetails.Parse(json));

    [Theory]
    [InlineData("2859188632", true)]
    [InlineData("..\\x", false)]
    [InlineData("", false)]
    public void kimlik_yalnizca_rakam(string id, bool gecerli) => Assert.Equal(gecerli, WorkshopDetails.IsValidId(id));

    [Fact]
    public async Task indirilen_bilgi_ve_gorsel_onbelleklenir_sonra_aga_cikilmaz()
    {
        var sahte = new SahteSteam(Yanit);
        var servis = new WorkshopDetailsService(_dizin);

        var ilk = await servis.GetAsync(new HttpClient(sahte), "2859188632");
        Assert.NotNull(ilk);
        Assert.True(File.Exists(Path.Combine(_dizin, "2859188632.json")));
        Assert.Equal(Path.Combine(_dizin, "2859188632.jpg"), ilk!.ImagePath);
        Assert.Equal(2, sahte.Istekler);

        var ikinci = await servis.GetAsync(new HttpClient(sahte), "2859188632");
        Assert.Equal("Harmony", ikinci!.Title);
        Assert.Equal(2, sahte.Istekler);
    }

    [Fact]
    public async Task cevrimdisiyken_eski_onbellek_kullanilir()
    {
        Directory.CreateDirectory(_dizin);
        var yol = Path.Combine(_dizin, "2859188632.json");
        File.WriteAllText(yol, Yanit);
        File.SetLastWriteTimeUtc(yol, DateTime.UtcNow.AddDays(-30));

        var servis = new WorkshopDetailsService(_dizin);
        var d = await servis.GetAsync(new HttpClient(new SahteSteam(null)), "2859188632");

        Assert.NotNull(d);
        Assert.True(d!.IsStale);
        Assert.Equal("Harmony", d.Title);
    }

    [Fact]
    public async Task hic_onbellek_yoksa_ve_cevrimdisiysa_null()
    {
        var servis = new WorkshopDetailsService(_dizin);
        Assert.Null(await servis.GetAsync(new HttpClient(new SahteSteam(null)), "2859188632"));
    }

    /// <summary>API'ye <paramref name="yanit"/> döner, görsel için birkaç bayt; null ise ağ yok.</summary>
    private sealed class SahteSteam(string? yanit) : HttpMessageHandler
    {
        public int Istekler { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Istekler++;
            if (yanit is null) throw new HttpRequestException("çevrimdışı");

            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal(WorkshopDetailsService.ApiUrl, request.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(yanit) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) });
        }
    }
}
