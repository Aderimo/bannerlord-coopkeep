using System;
using System.Collections.Generic;

namespace CoopKeep.App.Localization;

/// <summary>
/// Modlar sayfası: sayfalama ve mod bilgi penceresi.
/// </summary>
public sealed partial class Loc
{
    private static readonly Dictionary<string, string> TurkishMods = new(StringComparer.Ordinal)
    {
        // ---- sayfalama ----
        ["mods.page.range"] = "{0}–{1} / {2}",
        ["mods.page.prev"] = "« Önceki",
        ["mods.page.next"] = "Sonraki »",
        ["mods.rowTip"] = "Ayrıntılar için tıkla",

        // ---- bilgi penceresi ----
        ["modinfo.eyebrow"] = "Mod bilgisi",
        ["modinfo.id"] = "Kimlik",
        ["modinfo.idTip"] = "SubModule.xml'deki Module Id. Bazı topluluk kayıtlarında gerçek kimlik bilinmiyor; orada görünen ad yazılı.",
        ["modinfo.community"] = "Topluluk",
        ["modinfo.communityTip"] = "Oyuncuların Coop ile denediği sonuç (GitHub'daki compat/mods.json). ✓ çalışıyor, ✗ çalışmıyor bildirimleri.",
        ["modinfo.note"] = "Not",
        ["modinfo.server"] = "Bu bilgisayarda",
        ["modinfo.installed"] = "Sürüm {0} · {1}",
        ["modinfo.enabled"] = "başlatıcıda açık",
        ["modinfo.disabled"] = "başlatıcıda kapalı",
        ["modinfo.description"] = "Workshop açıklaması",
        ["modinfo.loading"] = "Steam'den alınıyor…",
        ["modinfo.descFailed"] = "Açıklama alınamadı. Workshop sayfasına bakabilirsin.",
        ["modinfo.noWorkshop"] = "Bu modun Workshop sayfası yok.",
        ["modinfo.stats"] = "{0} abone · güncelleme {1}",
        ["modinfo.stale"] = "Çevrimdışı · kayıtlı kopya",
        ["modinfo.staleTip"] = "Steam'e ulaşılamadı; en son indirilen açıklama gösteriliyor.",
        ["modinfo.workshop"] = "Workshop",
        ["modinfo.nexus"] = "Nexus",
        ["modinfo.reportWorks"] = "Çalışıyor bildir",
        ["modinfo.reportBroken"] = "Çalışmıyor bildir",
        ["modinfo.reportTip"] = "GitHub'da hazır doldurulmuş bir form açılır; göndermeden önce görürsün.",
        ["modinfo.close"] = "Kapat",
    };

    private static readonly Dictionary<string, string> EnglishMods = new(StringComparer.Ordinal)
    {
        ["mods.page.range"] = "{0}–{1} of {2}",
        ["mods.page.prev"] = "« Previous",
        ["mods.page.next"] = "Next »",
        ["mods.rowTip"] = "Click for details",

        ["modinfo.eyebrow"] = "Mod info",
        ["modinfo.id"] = "Id",
        ["modinfo.idTip"] = "The Module Id from SubModule.xml. Some community entries don't know the real id yet; those show the display name.",
        ["modinfo.community"] = "Community",
        ["modinfo.communityTip"] = "What players saw when using it with Coop (compat/mods.json on GitHub). ✓ works, ✗ broken reports.",
        ["modinfo.note"] = "Note",
        ["modinfo.server"] = "On this PC",
        ["modinfo.installed"] = "Version {0} · {1}",
        ["modinfo.enabled"] = "enabled in launcher",
        ["modinfo.disabled"] = "disabled in launcher",
        ["modinfo.description"] = "Workshop description",
        ["modinfo.loading"] = "Fetching from Steam…",
        ["modinfo.descFailed"] = "Couldn't get the description. Check the Workshop page.",
        ["modinfo.noWorkshop"] = "This mod has no Workshop page.",
        ["modinfo.stats"] = "{0} subscribers · updated {1}",
        ["modinfo.stale"] = "Offline · saved copy",
        ["modinfo.staleTip"] = "Steam couldn't be reached; showing the last downloaded description.",
        ["modinfo.workshop"] = "Workshop",
        ["modinfo.nexus"] = "Nexus",
        ["modinfo.reportWorks"] = "Report working",
        ["modinfo.reportBroken"] = "Report broken",
        ["modinfo.reportTip"] = "Opens a pre-filled GitHub form; you see it before sending.",
        ["modinfo.close"] = "Close",
    };
}
