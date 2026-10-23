namespace CoopKeep.Core.Mods;

/// <summary>Bir modun sunucuya nasıl kurulacağı.</summary>
public enum ModInstallMode
{
    /// <summary>CoopKeep seçer (bkz. <see cref="ModPlanner.ResolveMode"/>).</summary>
    Auto,

    /// <summary>Mod olduğu gibi; sunucu için derlenmiş kodu var.</summary>
    Full,

    /// <summary>Verileri (eşya, birlik, yerleşim XML'leri) sunucuda; kodu yalnızca oyuncularda çalışır.</summary>
    DataOnly,

    /// <summary>
    /// Sunucuda yalnızca kimliği bulunur: Coop'un modül doğrulaması geçer, sunucu modun
    /// ne kodunu ne verisini yükler. Arayüz kütüphaneleri (Harmony, MCM…) için.
    /// </summary>
    IdOnly,

    /// <summary>Deneysel: istemci kodu sunucuda da çalıştırılır. Hata çıkarsa CoopKeep bildirir.</summary>
    RunCode,
}

/// <summary>Dünyaya eklenmiş bir mod ve kurulum kipi.</summary>
public sealed record WorldMod(string Id, ModInstallMode Mode = ModInstallMode.Auto);

/// <summary>Planın bir modu: hangi kipte kurulacağı ve neden eklendiği.</summary>
/// <param name="AddedAsDependency">Kullanıcı seçmedi; seçilen bir modun bağımlılığı olduğu için eklendi.</param>
public sealed record PlannedMod(InstalledModule Module, ModInstallMode Mode, bool AddedAsDependency);

/// <summary>Sunucunun hangi modlarla, hangi sırayla açılacağı.</summary>
public sealed record ModPlan
{
    /// <summary>Kurulacak modlar, yüklenme sırasıyla.</summary>
    public IReadOnlyList<PlannedMod> Mods { get; init; } = [];

    /// <summary>
    /// Motora verilecek tam modül listesi: resmî modüller + modlar + Coop
    /// (<c>_MODULES_*…*_MODULES_</c> içeriği).
    /// </summary>
    public IReadOnlyList<string> ModuleOrder { get; init; } = [];

    /// <summary>Bilgisayarda bulunmayan, seçili modlar.</summary>
    public IReadOnlyList<string> MissingMods { get; init; } = [];

    /// <summary>Bulunamayan zorunlu bağımlılıklar: "ButterLib → Bannerlord.Harmony".</summary>
    public IReadOnlyList<(string Mod, string Dependency)> MissingDependencies { get; init; } = [];

    /// <summary>Sıralama çelişkisi var mıydı (döngü)? Varsa seçim sırası kullanıldı.</summary>
    public bool OrderConflict { get; init; }

    /// <summary>Sunucu açılabilir mi? Eksik mod ya da bağımlılık varsa hayır.</summary>
    public bool CanLaunch => MissingMods.Count == 0 && MissingDependencies.Count == 0;
}
