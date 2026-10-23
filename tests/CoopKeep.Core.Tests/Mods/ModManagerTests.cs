using System.IO.Compression;
using CoopKeep.Core.Mods;

namespace CoopKeep.Core.Tests.Mods;

/// <summary>Mod yöneticisi: başlatıcı listesi, sıralama, zip'ten kurulum, paketler, arama.</summary>
public sealed class ModManagerTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "ck-mods-" + Guid.NewGuid().ToString("N"));

    public ModManagerTests() => Directory.CreateDirectory(_dizin);

    public void Dispose()
    {
        if (Directory.Exists(_dizin)) Directory.Delete(_dizin, recursive: true);
    }

    private static string Fikstur(string ad) => Path.Combine(AppContext.BaseDirectory, "Fixtures", ad);

    /// <summary>Diskte gerçek bir mod klasörü oluşturur.</summary>
    private string Mod(string ust, string id, string surum = "v1.0.0", string? bagimlilik = null, string? sonrakiler = null)
    {
        var klasor = Path.Combine(ust, id);
        Directory.CreateDirectory(klasor);
        var dep = bagimlilik is null ? "" : $"<DependedModules><DependedModule Id=\"{bagimlilik}\" /></DependedModules>";
        var after = sonrakiler is null ? "" : $"<ModulesToLoadAfterThis><Module Id=\"{sonrakiler}\" /></ModulesToLoadAfterThis>";
        File.WriteAllText(Path.Combine(klasor, "SubModule.xml"),
            $"<Module><Name value=\"{id} Mod\" /><Id value=\"{id}\" /><Version value=\"{surum}\" />{dep}{after}</Module>");
        Directory.CreateDirectory(Path.Combine(klasor, "bin"));
        File.WriteAllText(Path.Combine(klasor, "bin", "dosya.txt"), "veri");
        return klasor;
    }

    // ------------------------------------------------------------------
    // Başlatıcı listesi
    // ------------------------------------------------------------------

    private const string LauncherXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <UserData>
          <GameType>Singleplayer</GameType>
          <SingleplayerData><ModDatas>
            <UserModData><Id>Native</Id><LastKnownVersion>v1.4.8</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
            <UserModData><Id>Bannerlord.Harmony</Id><LastKnownVersion>v2.4</LastKnownVersion><IsSelected>false</IsSelected></UserModData>
            <UserModData><Id>Eski</Id><LastKnownVersion>v1</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
          </ModDatas></SingleplayerData>
          <MultiplayerData><ModDatas>
            <UserModData><Id>Multiplayer</Id><LastKnownVersion>v1</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
          </ModDatas></MultiplayerData>
        </UserData>
        """;

    [Fact]
    public void baslatici_listesi_sirasiyla_okunur()
    {
        var yol = Path.Combine(_dizin, "LauncherData.xml");
        File.WriteAllText(yol, LauncherXml);

        var liste = LauncherProfile.Read(yol);

        Assert.Equal(["Native", "Bannerlord.Harmony", "Eski"], liste.Select(m => m.Id));
        Assert.True(liste[0].IsSelected);
        Assert.False(liste[1].IsSelected);
    }

    [Fact]
    public void baslatici_listesi_yeni_sira_ve_secimle_yazilir()
    {
        var yol = Path.Combine(_dizin, "LauncherData.xml");
        File.WriteAllText(yol, LauncherXml);

        LauncherProfile.Write([new("Bannerlord.Harmony", true), new("Native", true), new("CoopKeep", true)], yol);
        var liste = LauncherProfile.Read(yol);

        // Listede olmayan "Eski" silinmez, sona kapalı olarak gider; yeni mod eklenir.
        Assert.Equal(["Bannerlord.Harmony", "Native", "CoopKeep", "Eski"], liste.Select(m => m.Id));
        Assert.Equal([true, true, true, false], liste.Select(m => m.IsSelected));
        Assert.Contains("<Id>Multiplayer</Id>", File.ReadAllText(yol)); // çok oyunculu bölüm aynı
        Assert.True(File.Exists(LauncherProfile.LastBackupPath(yol)));
    }

    [Fact]
    public void baslatici_dosyasi_yoksa_olusturulur()
    {
        var yol = Path.Combine(_dizin, "Configs", "LauncherData.xml");
        LauncherProfile.Write([new("Native", true)], yol);
        Assert.Equal([new LauncherMod("Native", true)], LauncherProfile.Read(yol));
    }

    // ------------------------------------------------------------------
    // Yükleme sırası
    // ------------------------------------------------------------------

    [Fact]
    public void resmi_moduller_basa_bagimlilik_once_coopkeep_sona()
    {
        var ust = Path.Combine(_dizin, "Modules");
        var kurulu = new[]
        {
            ModuleScanner.TryRead(Mod(ust, "B", bagimlilik: "A"), ModuleSource.GameFolder)!,
            ModuleScanner.TryRead(Mod(ust, "A"), ModuleSource.GameFolder)!,
        };

        var sira = LoadOrder.Sort(["CoopKeep", "B", "Sandbox", "A", "Native"], kurulu, out var cakisma);

        Assert.False(cakisma);
        Assert.Equal(["Native", "Sandbox", "A", "B", "CoopKeep"], sira);
    }

    [Fact]
    public void dongu_varsa_cakisma_bildirilir_ama_hepsi_kalir()
    {
        var ust = Path.Combine(_dizin, "Modules");
        var kurulu = new[]
        {
            ModuleScanner.TryRead(Mod(ust, "A", bagimlilik: "B"), ModuleSource.GameFolder)!,
            ModuleScanner.TryRead(Mod(ust, "B", bagimlilik: "A"), ModuleSource.GameFolder)!,
        };

        var sira = LoadOrder.Sort(["A", "B"], kurulu, out var cakisma);

        Assert.True(cakisma);
        Assert.Equal(2, sira.Count);
    }

    // ------------------------------------------------------------------
    // Zip'ten kurulum
    // ------------------------------------------------------------------

    [Fact]
    public void zipten_ic_ice_klasorlu_mod_kurulur_eskisi_yedeklenir()
    {
        var kaynak = Path.Combine(_dizin, "kaynak");
        Mod(Path.Combine(kaynak, "indirilen", "Modules"), "ModX", "v2.0.0");
        var zip = Path.Combine(_dizin, "modx.zip");
        ZipFile.CreateFromDirectory(kaynak, zip);

        var oyun = Path.Combine(_dizin, "oyun", "Modules");
        Mod(oyun, "ModX", "v1.0.0");
        var yedek = Path.Combine(_dizin, "yedek");

        var kurulan = ModArchiveInstaller.InstallZip(zip, oyun, yedek);

        Assert.Single(kurulan);
        Assert.Equal("v2.0.0", ModuleScanner.TryRead(Path.Combine(oyun, "ModX"), ModuleSource.GameFolder)!.Version);
        Assert.True(File.Exists(Path.Combine(oyun, "ModX", "bin", "dosya.txt")));
        Assert.Single(Directory.GetDirectories(yedek)); // eski sürüm silinmedi
    }

    [Fact]
    public void mod_icermeyen_zip_reddedilir()
    {
        var kaynak = Path.Combine(_dizin, "bos");
        Directory.CreateDirectory(kaynak);
        File.WriteAllText(Path.Combine(kaynak, "okubeni.txt"), "x");
        var zip = Path.Combine(_dizin, "bos.zip");
        ZipFile.CreateFromDirectory(kaynak, zip);

        Assert.Throws<InvalidDataException>(() => ModArchiveInstaller.InstallZip(zip, Path.Combine(_dizin, "oyun"), Path.Combine(_dizin, "yedek")));
    }

    [Fact]
    public void klasor_disina_cikan_zip_girdisi_reddedilir()
    {
        var zip = Path.Combine(_dizin, "kotu.zip");
        using (var arsiv = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(arsiv.CreateEntry("../../disari.txt").Open());
            w.Write("x");
        }

        Assert.Throws<InvalidDataException>(() => ModArchiveInstaller.ExtractSafely(zip, Path.Combine(_dizin, "acilan")));
        Assert.False(File.Exists(Path.Combine(_dizin, "..", "disari.txt")));
    }

    // ------------------------------------------------------------------
    // Mod paketleri
    // ------------------------------------------------------------------

    private IReadOnlyList<InstalledModule> KuruluModlar(string ust)
    {
        var yerel = ModuleScanner.TryRead(Mod(ust, "Yerel", "v1.1"), ModuleSource.GameFolder)!;
        var atolye = ModuleScanner.TryRead(Mod(Path.Combine(_dizin, "workshop", "123456789"), "Atolye", "v3"), ModuleSource.Workshop)! with { WorkshopId = "123456789" };
        return [yerel, atolye];
    }

    [Fact]
    public void paket_secili_modlardan_olusur_ve_json_olarak_geri_okunur()
    {
        var kurulu = KuruluModlar(Path.Combine(_dizin, "Modules"));
        var paket = ModPackFile.Create("Arkadaş paketi", ["Native", "Yerel", "Atolye"], kurulu, "v1.4.8");

        var yol = Path.Combine(_dizin, "paket" + ModPackFile.ListExtension);
        ModPackFile.SaveList(paket, yol);
        var okunan = ModPackFile.LoadList(yol);

        Assert.Equal("Arkadaş paketi", okunan.Name);
        Assert.Equal(["Native", "Yerel", "Atolye"], okunan.Mods.Select(m => m.Id));
        Assert.Equal(ModPackSource.Workshop, okunan.Mods[2].Source);
        Assert.Equal("123456789", okunan.Mods[2].WorkshopId);
        Assert.Equal(ModPackSource.Local, okunan.Mods[1].Source);
    }

    [Fact]
    public void paket_zipi_yerel_modu_icerir_atolye_modunu_icermez_ve_kurulabilir()
    {
        var kurulu = KuruluModlar(Path.Combine(_dizin, "Modules"));
        var paket = ModPackFile.Create("P", ["Yerel", "Atolye"], kurulu, "v1.4.8");
        var zip = Path.Combine(_dizin, "p.zip");

        var son = ModPackFile.ExportZip(paket, kurulu, zip, includeWorkshop: false);

        Assert.True(son.Mods.Single(m => m.Id == "Yerel").Bundled);
        Assert.False(son.Mods.Single(m => m.Id == "Atolye").Bundled);
        using (var arsiv = ZipFile.OpenRead(zip))
        {
            Assert.Contains(arsiv.Entries, e => e.FullName == "Modules/Yerel/SubModule.xml");
            Assert.DoesNotContain(arsiv.Entries, e => e.FullName.StartsWith("Modules/Atolye/"));
            Assert.Contains(arsiv.Entries, e => e.FullName == "NASIL KURULUR.txt");
        }

        var (acilan, klasor) = ModPackFile.Open(zip);
        Assert.Equal("P", acilan.Name);
        Assert.NotNull(klasor);

        // Boş bir bilgisayarda: yerel mod paketten kurulabilir, Atölye modu abone olunmalı.
        var durum = ModPackFile.Compare(acilan, []);
        Assert.Equal(ModPackItemState.MissingBundled, durum.Single(d => d.Entry.Id == "Yerel").State);
        Assert.Equal(ModPackItemState.MissingWorkshop, durum.Single(d => d.Entry.Id == "Atolye").State);

        var kurulan = ModArchiveInstaller.InstallFolder(klasor!, Path.Combine(_dizin, "yeni-oyun"), Path.Combine(_dizin, "yedek"));
        Assert.Equal(["Yerel"], kurulan.Select(m => m.Id));
    }

    [Fact]
    public void farkli_surum_bildirilir()
    {
        var kurulu = KuruluModlar(Path.Combine(_dizin, "Modules"));
        var paket = new ModPack { Name = "P", Mods = [new ModPackEntry("Yerel", "Yerel", "v9.9", ModPackSource.Local)] };

        var durum = ModPackFile.Compare(paket, kurulu).Single();

        Assert.Equal(ModPackItemState.WrongVersion, durum.State);
        Assert.Equal("v1.1", durum.InstalledVersion);
    }

    [Fact]
    public void paket_baslaticiya_sirali_ve_acik_yazilir_digerleri_kapanir()
    {
        var paket = new ModPack { Name = "P", Mods = [new("Native", "Native", null, ModPackSource.Official), new("Yerel", "Yerel", null, ModPackSource.Local)] };
        var liste = ModPackFile.LauncherList(paket, [new("Baska", true), new("Native", true)]);

        Assert.Equal([new LauncherMod("Native", true), new LauncherMod("Yerel", true), new LauncherMod("Baska", false)], liste);
    }

    [Fact]
    public void dunya_paketi_uygulaninca_oyunun_modulleri_ve_coop_acik_kalir_coop_en_sonda()
    {
        var paket = new ModPack { Name = "Dünya", Mods = [new("Harmony", "Harmony", null, ModPackSource.Workshop)] };
        var simdiki = new List<LauncherMod> { new("Coop", true), new("Native", true), new("NavalDLC", true), new("Sandbox", true), new("Eski", true) };

        var liste = ModPackFile.LauncherList(paket, simdiki);

        Assert.Equal(
            [new LauncherMod("Native", true), new LauncherMod("Sandbox", true), new LauncherMod("Harmony", true), new LauncherMod("Coop", true),
             new LauncherMod("NavalDLC", false), new LauncherMod("Eski", false)],
            liste);
    }

    // ------------------------------------------------------------------
    // Arama
    // ------------------------------------------------------------------

    [Fact]
    public void atolye_arama_sayfasindan_ogeler_sirayla_ve_tekrarsiz_okunur()
    {
        var ids = WorkshopSearch.ParseIds(File.ReadAllText(Fikstur("workshop-search.html")));
        Assert.Equal(["2859188632", "2859222409", "2875237021"], ids);
    }

    [Fact]
    public void atolye_arama_adresi_sorguyu_kodlar()
    {
        Assert.Contains("searchtext=mod%20ve%20%26", WorkshopSearch.SearchUrl("mod ve &", 2));
        Assert.Contains("browsesort=trend", WorkshopSearch.SearchUrl("", 1));
    }

    [Fact]
    public void nexus_yaniti_okunur_bozuk_satir_atlanir()
    {
        var sonuc = NexusSearch.Parse(File.ReadAllText(Fikstur("nexus-search.json")));

        var mod = Assert.Single(sonuc);
        Assert.Equal("4144", mod.Id);
        Assert.Equal("Replace original fonts & more.", mod.Summary);
        Assert.Equal(8237, mod.Popularity);
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/4144", mod.PageUrl);
        Assert.Empty(NexusSearch.Parse("{\"errors\":[{\"message\":\"x\"}]}"));
        Assert.Empty(NexusSearch.Parse("bozuk"));
    }

    [Fact]
    public void nexus_istegi_oyun_ve_ad_suzgeci_tasir()
    {
        var govde = NexusSearch.BuildRequest("harmony", 30, 30);
        Assert.Contains("mountandblade2bannerlord", govde);
        Assert.Contains("nameStemmed", govde);
        Assert.Contains("\"offset\":30", govde);
        Assert.DoesNotContain("nameStemmed", NexusSearch.BuildRequest("", 30, 0));
    }
}
