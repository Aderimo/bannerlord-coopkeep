using CoopKeep.Core.Configuration;

namespace CoopKeep.Core.Tests.Configuration;

/// <summary>
/// Cerrahi JSONC düzenleyici testleri.
/// </summary>
/// <remarks>
/// Fixture, kullanıcının makinesindeki gerçek <c>server-config.json</c> dosyasından
/// kısaltılarak alındı (30.08 tarihli, güncel build'in eklediği <c>"port"</c> anahtarı
/// eksik olan sürüm). Yorum yoğunluğu bilinçli olarak korundu — asıl test edilen şey
/// bu yorumların hayatta kalması.
/// </remarks>
public class JsoncEditorTests
{
    private const string GercekConfig = """
        {
          // Bannerlord Coop dedicated server configuration — the single source of
          // server settings. Comments and trailing commas are allowed in this file.

          // Save to host, from "Game Saves" in this folder (no .sav extension).
          "saveName": "saveauto1",

          // Minutes between world autosaves; 0 disables autosaving.
          "autosaveMinutes": 5,

          // Connection password (up to 128 characters). Empty = no password.
          "password": "",

          // Write everything the server prints to logs\coop-server-*.log
          "logFile": true,

          // Advertise this server on Steam.
          "steam": true,

          // Diagnostics. Only enable when asked to capture logs for a bug report.
          "traceTick": false,
          "tracePublish": false,
          "traceBandits": false,
        }
        """;

    // --- okuma -----------------------------------------------------------

    [Fact]
    public void yorumlu_ve_trailing_commali_dosya_okunabilir()
    {
        using var doc = JsoncEditor.Parse(GercekConfig).ToJsonDocument();

        Assert.Equal("saveauto1", doc.RootElement.GetProperty("saveName").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("autosaveMinutes").GetInt32());
        Assert.True(doc.RootElement.GetProperty("steam").GetBoolean());
    }

    [Fact]
    public void var_olan_anahtar_bulunur_olmayan_bulunmaz()
    {
        var editor = JsoncEditor.Parse(GercekConfig);

        Assert.True(editor.ContainsKey("saveName"));
        Assert.True(editor.ContainsKey("traceBandits"));
        Assert.False(editor.ContainsKey("port"));      // bu dosyada gerçekten yok
        Assert.False(editor.ContainsKey("maxPlayers")); // mod bunu hiç desteklemiyor
    }

    // --- cerrahi yazma ---------------------------------------------------

    [Fact]
    public void save_degistirmek_yorumlari_ve_sirayi_bozmaz()
    {
        var editor = JsoncEditor.Parse(GercekConfig);

        editor.SetString("saveName", "yeni_dunyam");

        var sonuc = editor.Text;

        // Değer değişti
        Assert.Contains("\"saveName\": \"yeni_dunyam\"", sonuc);
        Assert.DoesNotContain("saveauto1", sonuc);

        // Yorumların TAMAMI duruyor
        Assert.Contains("// Bannerlord Coop dedicated server configuration", sonuc);
        Assert.Contains("// Minutes between world autosaves; 0 disables autosaving.", sonuc);
        Assert.Contains("// Advertise this server on Steam.", sonuc);

        // Diğer anahtarlara dokunulmadı
        Assert.Contains("\"autosaveMinutes\": 5", sonuc);
        Assert.Contains("\"traceBandits\": false", sonuc);

        // Sıra korundu
        Assert.True(sonuc.IndexOf("saveName") < sonuc.IndexOf("autosaveMinutes"));
        Assert.True(sonuc.IndexOf("autosaveMinutes") < sonuc.IndexOf("password"));
    }

    [Fact]
    public void sayi_ve_bool_degerleri_yazilir()
    {
        var editor = JsoncEditor.Parse(GercekConfig);

        editor.SetInt("autosaveMinutes", 15);
        editor.SetBool("steam", false);

        Assert.Contains("\"autosaveMinutes\": 15", editor.Text);
        Assert.Contains("\"steam\": false", editor.Text);

        using var doc = editor.ToJsonDocument();
        Assert.Equal(15, doc.RootElement.GetProperty("autosaveMinutes").GetInt32());
        Assert.False(doc.RootElement.GetProperty("steam").GetBoolean());
    }

    [Fact]
    public void sifre_iceren_ozel_karakterler_dogru_kacisla_yazilir()
    {
        var editor = JsoncEditor.Parse(GercekConfig);

        editor.SetString("password", "a\"b\\c");

        using var doc = editor.ToJsonDocument();
        Assert.Equal("a\"b\\c", doc.RootElement.GetProperty("password").GetString());
    }

    // --- eksik anahtar ekleme (gerçek senaryo) ---------------------------

    [Fact]
    public void eksik_port_anahtari_eklenir_ve_dosya_gecerli_kalir()
    {
        // Kullanıcının gerçek dosyası bu durumda: "port" anahtarı yok, 4200 varsayılanına düşüyor.
        var editor = JsoncEditor.Parse(GercekConfig);
        Assert.False(editor.ContainsKey("port"));

        editor.SetInt("port", 4210);

        Assert.True(editor.ContainsKey("port"));
        using var doc = editor.ToJsonDocument();
        Assert.Equal(4210, doc.RootElement.GetProperty("port").GetInt32());

        // Eklerken mevcut içerik bozulmadı
        Assert.Contains("\"saveName\": \"saveauto1\"", editor.Text);
        Assert.Contains("// Advertise this server on Steam.", editor.Text);
    }

    [Fact]
    public void eklenen_anahtar_sonradan_guncellenebilir()
    {
        var editor = JsoncEditor.Parse(GercekConfig);

        editor.SetInt("port", 4210);
        editor.SetInt("port", 4300);

        using var doc = editor.ToJsonDocument();
        Assert.Equal(4300, doc.RootElement.GetProperty("port").GetInt32());
        // İki kez eklenmediğinden emin ol
        Assert.Equal(1, editor.Text.Split("\"port\"").Length - 1);
    }

    // --- tuzaklar --------------------------------------------------------

    [Fact]
    public void yorum_icindeki_anahtar_esleşme_sayilmaz()
    {
        // Server'ın kendi migration mekanizması ayarları yorum satırı olarak bırakabiliyor.
        // Yorumdaki bir anahtarı gerçek ayar sanmak, yanlış yere yazmak demektir.
        var metin = """
            {
              // "saveName": "yorumdaki_deger",
              "saveName": "gercek_deger"
            }
            """;

        var editor = JsoncEditor.Parse(metin);
        editor.SetString("saveName", "yeni");

        Assert.Contains("// \"saveName\": \"yorumdaki_deger\"", editor.Text); // yorum dokunulmadan durdu
        Assert.Contains("\"saveName\": \"yeni\"", editor.Text);
        Assert.DoesNotContain("gercek_deger", editor.Text);
    }

    [Fact]
    public void tanimadigimiz_anahtarlar_korunur()
    {
        // Upstream günlük commit atıyor. Bilmediğimiz bir ayarı silmek,
        // kullanıcının yapılandırmasını sessizce yok etmek olurdu.
        var metin = """
            {
              "saveName": "x",
              "gelecektekiYeniAyar": { "ic": [1, 2, 3] },
              "digerBilinmeyen": "korunmali"
            }
            """;

        var editor = JsoncEditor.Parse(metin);
        editor.SetString("saveName", "y");

        Assert.Contains("gelecektekiYeniAyar", editor.Text);
        Assert.Contains("[1, 2, 3]", editor.Text);
        Assert.Contains("\"digerBilinmeyen\": \"korunmali\"", editor.Text);
    }

    [Fact]
    public void ic_ice_nesnedeki_ayni_isimli_anahtar_kok_seviyeyi_bozmaz()
    {
        // mod-config.json'da "difficulty" ve "modOptions" blokları var; iç içe
        // yapılarda aynı ada rastlamak mümkün.
        var metin = """
            {
              "difficulty": { "battleDeath": "VeryEasy" },
              "battleDeath": "kokSeviye"
            }
            """;

        var editor = JsoncEditor.Parse(metin);
        editor.SetString("battleDeath", "degisti");

        // İlk eşleşme iç içe olandır; bu testin amacı davranışı SABİTLEMEK.
        using var doc = editor.ToJsonDocument();
        Assert.Equal("degisti", doc.RootElement.GetProperty("difficulty").GetProperty("battleDeath").GetString());
        Assert.Equal("kokSeviye", doc.RootElement.GetProperty("battleDeath").GetString());
    }

    // --- dosyaya yazma ---------------------------------------------------

    [Fact]
    public void kaydetmek_yedek_birakir_ve_iceriği_korur()
    {
        var dizin = Path.Combine(Path.GetTempPath(), "coopkeep-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dizin);
        try
        {
            var yol = Path.Combine(dizin, "server-config.json");
            File.WriteAllText(yol, GercekConfig);

            var editor = JsoncEditor.Load(yol);
            editor.SetString("saveName", "kaydedildi");
            editor.Save(yol);

            Assert.True(File.Exists(yol + ".bak"), "yedek dosya oluşmalıydı");
            Assert.Contains("saveauto1", File.ReadAllText(yol + ".bak"));   // yedekte eski değer
            Assert.Contains("kaydedildi", File.ReadAllText(yol));           // asıl dosyada yeni değer
            Assert.False(File.Exists(yol + ".tmp"), "geçici dosya temizlenmeliydi");
        }
        finally
        {
            Directory.Delete(dizin, recursive: true);
        }
    }
}
