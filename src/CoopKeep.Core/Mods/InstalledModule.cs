namespace CoopKeep.Core.Mods;

/// <summary>Modun bilgisayarda nereden geldiği.</summary>
public enum ModuleSource
{
    /// <summary>Oyunun kendi <c>Modules</c> klasörü (elle kurulmuş ya da Nexus).</summary>
    GameFolder,

    /// <summary>Steam Atölye (<c>steamapps\workshop\content\261550\&lt;id&gt;</c>).</summary>
    Workshop,
}

/// <summary>Bir modun bağımlılığı (<c>SubModule.xml</c>).</summary>
/// <param name="Id">Bağımlı olunan modül.</param>
/// <param name="Version">İstenen sürüm (ör. <c>v1.2.12</c>); yoksa boş.</param>
/// <param name="Optional">İsteğe bağlı mı?</param>
public sealed record ModuleDependency(string Id, string? Version, bool Optional);

/// <summary>
/// Diskte bulunmuş bir Bannerlord modu (<c>SubModule.xml</c> okunarak).
/// </summary>
public sealed record InstalledModule
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary><c>SubModule.xml</c>'deki sürüm (ör. <c>v2.4.2.248</c>). Coop sürümü birebir karşılaştırır.</summary>
    public string? Version { get; init; }

    /// <summary>Modun klasörü.</summary>
    public required string Folder { get; init; }

    public ModuleSource Source { get; init; }

    /// <summary>Atölye öğe kimliği (Atölye modlarında klasör adı).</summary>
    public string? WorkshopId { get; init; }

    /// <summary>TaleWorlds'ün kendi modülü mü (Native, SandBox, DLC'ler…)?</summary>
    public bool IsOfficial { get; init; }

    /// <summary>Çok oyunculu moda ait mi? Coop tek oyunculu oyunda çalışır.</summary>
    public bool IsMultiplayer { get; init; }

    /// <summary>Kesin bağımlılıklar (<c>DependedModules</c>, <c>DependedModuleMetadatas</c>).</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];

    /// <summary>Bu moddan <b>önce</b> yüklenmesi gerekenler (bağımlılıklar dahil).</summary>
    public IReadOnlyList<string> LoadAfter { get; init; } = [];

    /// <summary>Bu moddan <b>sonra</b> yüklenmesi gerekenler (<c>ModulesToLoadAfterThis</c>).</summary>
    public IReadOnlyList<string> LoadBefore { get; init; } = [];

    /// <summary>Modun kodu var mı (<c>SubModules</c> içinde DLL)? Yoksa yalnızca veri modudur.</summary>
    public bool HasCode { get; init; }

    /// <summary>Sunucu için derlenmiş kod (<c>bin\Win64_Shipping_Server</c>).</summary>
    public bool HasServerBinaries { get; init; }

    /// <summary>
    /// .NET Core istemci derlemesi (<c>bin\Gaming.Desktop.x64_Shipping_Client</c>, Game Pass sürümü).
    /// Sunucu motoru da .NET Core'da çalıştığı için deneysel kipte ilk tercih budur.
    /// </summary>
    public bool HasDotNetCoreBinaries { get; init; }

    /// <summary>Steam istemcisi derlemesi (<c>bin\Win64_Shipping_Client</c>, .NET Framework).</summary>
    public bool HasClientBinaries { get; init; }

    /// <summary>Modun yapıldığı oyun sürümü (Native bağımlılığının sürümü); bilinmiyorsa boş.</summary>
    public string? GameVersion { get; init; }

    public bool IsCoop => Id.Equals("Coop", StringComparison.OrdinalIgnoreCase) ||
                          Id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kullanıcının dünyasına eklenebilecek bir mod mu (resmî, çok oyunculu ya da Coop değil)?</summary>
    public bool IsSelectable => !IsOfficial && !IsMultiplayer && !IsCoop;
}
