using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;

namespace CoopKeep.Core.Encyclopedia;

/// <summary>Bir rehberdeki sıra: kayıt, sayı ve kısa açıklama.</summary>
public sealed record GuideRow(string Id, string Name, double Score, string Detail);

/// <summary>
/// Oyun verilerinden hesaplanan rehberler: en güçlü askerler, en zengin kasabalar, en güçlü
/// klanlar, en iyi kale konumları, en iyi eşyalar.
/// </summary>
/// <remarks>
/// Hiçbiri tahmin değil, oyunun kendi sayılarından sıralama: askerlerde beceri toplamı,
/// kasabalarda refah (sunucu açıksa canlı), klanlarda kademe ve lord sayısı, eşyalarda
/// eklentinin okuduğu hasar/zırh değerleri.
/// </remarks>
public static class EncyclopediaGuides
{
    /// <summary>Her kademenin en güçlü askerleri (beceri toplamı).</summary>
    public static IReadOnlyList<GuideRow> StrongestTroops(IEnumerable<EncTroop> troops, int tier, int take = 10) =>
        troops.Where(t => t.Tier == tier && t.Name.Length > 0)
            .OrderByDescending(t => t.SkillTotal).ThenByDescending(t => t.Level)
            .Take(take)
            .Select(t => new GuideRow(t.Id, t.Name, t.SkillTotal, t.Class))
            .ToList();

    /// <summary>Refaha göre kasabalar. <paramref name="live"/>: sunucudan okunan güncel refah.</summary>
    public static IReadOnlyList<GuideRow> RichestTowns(IEnumerable<EncSettlement> settlements,
        IReadOnlyDictionary<string, double>? live = null, int take = 15) =>
        settlements.Where(s => s.Kind == SettlementKind.Town)
            .Select(s => (s, p: live is not null && live.TryGetValue(s.Id, out var v) ? v : s.Prosperity))
            .OrderByDescending(x => x.p)
            .Take(take)
            .Select(x => new GuideRow(x.s.Id, x.s.Name, x.p, x.s.Culture))
            .ToList();

    /// <summary>Kademe ve lord sayısına göre soylu klanlar.</summary>
    public static IReadOnlyList<GuideRow> StrongestClans(IEnumerable<EncClan> clans, IEnumerable<EncLord> lords, int take = 15)
    {
        var members = lords.Where(l => l.ClanId is not null).GroupBy(l => l.ClanId!).ToDictionary(g => g.Key, g => g.Count());
        return clans.Where(c => !c.IsBandit && !c.IsMinor && c.Name.Length > 0)
            .Select(c => (c, n: members.GetValueOrDefault(c.Id)))
            .OrderByDescending(x => x.c.Tier).ThenByDescending(x => x.n)
            .Take(take)
            .Select(x => new GuideRow(x.c.Id, x.c.Name, x.c.Tier, x.n.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>
    /// Ticarete en yakın kaleler: yakınındaki kasaba sayısı ve en yakın kasabaya uzaklık.
    /// </summary>
    /// <remarks>
    /// Oyunda ticaret kervanları kasabalar arasında gidip gelir; çevresinde birkaç kasaba olan
    /// kale bu yolların üstündedir. <paramref name="radius"/> oyunun harita birimiyle.
    /// </remarks>
    public static IReadOnlyList<GuideRow> BestCastleSpots(IEnumerable<EncSettlement> settlements, double radius = 70, int take = 12)
    {
        var all = settlements.ToList();
        var towns = all.Where(s => s.Kind == SettlementKind.Town).ToList();
        if (towns.Count == 0) return [];

        return all.Where(s => s.Kind == SettlementKind.Castle)
            .Select(c =>
            {
                var near = towns.Select(t => (t, d: Math.Sqrt((t.X - c.X) * (t.X - c.X) + (t.Y - c.Y) * (t.Y - c.Y))))
                    .OrderBy(x => x.d).ToList();
                var count = near.Count(x => x.d <= radius);
                var score = count * 10 + Math.Max(0, radius - near[0].d) / radius * 10;
                return new GuideRow(c.Id, c.Name, Math.Round(score, 1), string.Join(", ", near.Take(3).Select(x => x.t.Name)));
            })
            .OrderByDescending(r => r.Score)
            .Take(take)
            .ToList();
    }

    /// <summary>Eşya türüne göre en iyiler (eklentinin <c>coopkeep.item_stats</c> değerleriyle).</summary>
    public static IReadOnlyList<GuideRow> BestItems(IEnumerable<ServerItem> items, IReadOnlyDictionary<string, ItemStats> stats,
        Func<ServerItem, bool> filter, Func<ItemStats, double> score, Func<ItemStats, string> detail, int take = 10) =>
        items.Where(i => i.Name.Length > 0 && filter(i) && stats.ContainsKey(i.Id))
            .Select(i => (i, s: stats[i.Id]))
            .OrderByDescending(x => score(x.s)).ThenBy(x => x.i.Value)
            .Take(take)
            .Select(x => new GuideRow(x.i.Id, x.i.Name, score(x.s), detail(x.s)))
            .ToList();
}
