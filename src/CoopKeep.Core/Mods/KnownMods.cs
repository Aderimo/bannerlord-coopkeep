namespace CoopKeep.Core.Mods;

/// <summary>
/// Çok sayıda modun ihtiyaç duyduğu ortak kütüphaneler ve Steam Atölyesi numaraları.
/// </summary>
/// <remarks>
/// Bir modun gerektirdiği kütüphane bilgisayarda yoksa (ör. Better Time → Mod Configuration
/// Menu v5) atölye numarası paketten de bilinmeyebilir. Bu dördü pek çok modun ortak
/// gereksinimi; numaraları sabit, böylece "Abone ol" düğmesi her zaman gösterilebilir.
/// </remarks>
public static class KnownMods
{
    public static readonly IReadOnlyDictionary<string, (string Name, string WorkshopId)> Libraries =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bannerlord.Harmony"] = ("Harmony", "2859188632"),
            ["Bannerlord.ButterLib"] = ("ButterLib", "2859232415"),
            ["Bannerlord.UIExtenderEx"] = ("UIExtenderEx", "2859222409"),
            ["Bannerlord.MBOptionScreen"] = ("Mod Configuration Menu v5", "2859238197"),
        };

    public static string? WorkshopId(string modId) => Libraries.TryGetValue(modId, out var lib) ? lib.WorkshopId : null;

    public static string? Name(string modId) => Libraries.TryGetValue(modId, out var lib) ? lib.Name : null;
}
