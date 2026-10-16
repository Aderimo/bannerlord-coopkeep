using CoopKeep.Core.Installation;

namespace CoopKeep.Core.Tests.Installation;

/// <summary>
/// Mod uyumu ve "Coop profili".
/// </summary>
/// <remarks>
/// Profil, kullanıcının oyun başlatıcısı dosyasını değiştiriyor. Bu yüzden asıl önemli
/// olan: önce yedek alınması, yalnızca <c>IsSelected</c> alanlarına dokunulması ve geri
/// yüklemenin dosyayı birebir eski hâline getirmesi.
/// </remarks>
public sealed class ModuleCompatibilityTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-mods-" + Guid.NewGuid().ToString("N"));
    private readonly string _yol;

    // Gerçek LauncherData.xml'in biçimi (Bannerlord v1.4.8), kısaltılmış.
    private const string Ornek = """
        <?xml version="1.0" encoding="utf-8"?>
        <UserData xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <GameType>Singleplayer</GameType>
          <SingleplayerData>
            <ModDatas>
              <UserModData><Id>Bannerlord.Harmony</Id><LastKnownVersion>v2.4.2.248</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>Native</Id><LastKnownVersion>v1.4.8.119303</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>SandBoxCore</Id><LastKnownVersion>v1.4.8.119303</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>BirthAndDeath</Id><LastKnownVersion>v1.4.8.119303</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>Sandbox</Id><LastKnownVersion>v1.4.8.119303</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>StoryMode</Id><LastKnownVersion>v1.4.8.119303</LastKnownVersion><IsSelected>true</IsSelected></UserModData>
              <UserModData><Id>Coop</Id><LastKnownVersion>v0.1.6.0</LastKnownVersion><IsSelected>false</IsSelected></UserModData>
            </ModDatas>
          </SingleplayerData>
          <MultiplayerData>
            <ModDatas>
              <UserModData><Id>SomeMultiplayerMod</Id><IsSelected>true</IsSelected></UserModData>
            </ModDatas>
          </MultiplayerData>
        </UserData>
        """;

    public ModuleCompatibilityTests()
    {
        Directory.CreateDirectory(_dizin);
        _yol = Path.Combine(_dizin, "LauncherData.xml");
        File.WriteAllText(_yol, Ornek);
    }

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    [Fact]
    public void moduller_dogru_siniflandirilir()
    {
        var sonuc = ModuleCompatibility.Check(_yol)!;

        Assert.False(sonuc.CoopEnabled);
        Assert.Equal(["Bannerlord.Harmony", "BirthAndDeath"], sonuc.ExtraModules);
        Assert.Equal(["BirthAndDeath"], sonuc.BlockedModules);

        // Çok oyunculu bölümdeki modlar tek oyunculu Coop'u etkilemez.
        Assert.DoesNotContain(sonuc.All, m => m.Id == "SomeMultiplayerMod");
    }

    [Fact]
    public void coop_profili_sorunlu_modlari_kapatir_coopu_acar()
    {
        var kapatilan = ModuleCompatibility.ApplyCoopProfile(_yol);

        Assert.Equal(["Bannerlord.Harmony", "BirthAndDeath"], kapatilan);

        var sonuc = ModuleCompatibility.Check(_yol)!;
        Assert.True(sonuc.CoopEnabled);
        Assert.Empty(sonuc.ExtraModules);
        Assert.False(sonuc.HasProblem);
        Assert.True(ModuleCompatibility.HasProfileBackup(_yol));
    }

    [Fact]
    public void geri_yukleme_dosyayi_birebir_eski_haline_getirir()
    {
        ModuleCompatibility.ApplyCoopProfile(_yol);
        ModuleCompatibility.ApplyCoopProfile(_yol);   // iki kez: ilk yedek korunmalı

        Assert.True(ModuleCompatibility.RestoreProfile(_yol));

        Assert.Equal(Ornek, File.ReadAllText(_yol));
        Assert.False(ModuleCompatibility.HasProfileBackup(_yol));
    }

    [Fact]
    public void profil_yalnizca_secim_alanlarina_dokunur()
    {
        ModuleCompatibility.ApplyCoopProfile(_yol);

        var metin = File.ReadAllText(_yol);
        Assert.Contains("<LastKnownVersion>v2.4.2.248</LastKnownVersion>", metin);
        Assert.Contains("<Id>SomeMultiplayerMod</Id><IsSelected>true</IsSelected>", metin);
    }
}
