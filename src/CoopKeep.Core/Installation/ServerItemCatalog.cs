using System.Globalization;
using System.Text.Json;

namespace CoopKeep.Core.Installation;

/// <summary>Eşya ekranındaki ana gruplar.</summary>
public enum ItemGroup
{
    Sword, Dagger, Axe, Mace, Polearm, Thrown, Bow, Crossbow, Ammo, Shield,
    Head, Body, Legs, Hands, Cape,
    Horse, Harness,
    Food, Goods,
    Other,
}

/// <summary>Sunucudan okunan bir eşya (<c>coopkeep.items</c>).</summary>
public sealed record ServerItem(
    string Id, string Name, string Type, string Category, int Value, int Tier, string Culture,
    bool Civilian, bool Food, bool TradeGood, string WeaponClass, double Weight)
{
    public ItemGroup Group => ServerItemCatalog.GroupOf(this);

    /// <summary>Zırh ve kıyafetlerde: şehirde giyilen (sivil) mi?</summary>
    public bool IsClothing => Group is ItemGroup.Head or ItemGroup.Body or ItemGroup.Legs or ItemGroup.Hands or ItemGroup.Cape;
}

/// <summary>Bir eşyanın kasabalardaki alış fiyatı (<c>coopkeep.prices</c>).</summary>
public sealed record ItemPrice(string Id, int Min, string MinTown, int Max, string MaxTown, int Average);

/// <summary>
/// Oyundaki bütün eşyaların listesi: CoopKeep sunucu eklentisi oyunun kendisinden okur.
/// </summary>
/// <remarks>
/// Yiyecek, ticaret malları ve malzemeler oyunun XML dosyalarında yok; bu yüzden liste
/// sunucudan alınır ve önbelleğe yazılır (sunucu kapalıyken de eşya ekranı açılabilsin).
/// Biçim 2026-10-16'da doğrulandı (<c>Fixtures/server-tools.txt</c>): 1.285 eşya.
/// </remarks>
public static class ServerItemCatalog
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IReadOnlyList<ServerItem> ParseItems(IEnumerable<string> lines)
    {
        var result = new List<ServerItem>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("ITEM|", StringComparison.Ordinal)) continue;

            var f = line.Split('|');
            if (f.Length < 13) continue;

            result.Add(new ServerItem(f[1], f[2], f[3], f[4], Int(f[5]), Int(f[6]), f[7],
                f[8] == "1", f[9] == "1", f[10] == "1", f[11],
                double.TryParse(f[12], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0));
        }

        return result;
    }

    public static IReadOnlyList<ItemPrice> ParsePrices(IEnumerable<string> lines)
    {
        var result = new List<ItemPrice>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("PRICE|", StringComparison.Ordinal)) continue;

            var f = line.Split('|');
            if (f.Length < 7) continue;
            result.Add(new ItemPrice(f[1], Int(f[2]), f[3], Int(f[4]), f[5], Int(f[6])));
        }

        return result;
    }

    /// <summary><c>coopkeep.party_items</c>: <c>item_id: sayı</c>.</summary>
    public static IReadOnlyDictionary<string, int> ParsePartyItems(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var colon = line.LastIndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0 || line.StartsWith('[') || line.Contains(' ', StringComparison.Ordinal) && line.IndexOf(' ') < colon) continue;
            if (int.TryParse(line[(colon + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                result[line[..colon]] = n;
        }

        return result;
    }

    /// <summary><c>COOPKEEP_ITEM … old=41 new=46</c> → (41, 46).</summary>
    public static (int Old, int New)? ParseItemChange(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("COOPKEEP_ITEM ", StringComparison.Ordinal)) continue;

            int? old = null, @new = null;
            foreach (var part in line.Split(' '))
            {
                if (part.StartsWith("old=", StringComparison.Ordinal)) old = Int(part[4..]);
                if (part.StartsWith("new=", StringComparison.Ordinal)) @new = Int(part[4..]);
            }

            if (old is { } o && @new is { } n) return (o, n);
        }

        return null;
    }

    /// <summary>Eşyanın ekrandaki grubu (tür + silah sınıfına göre).</summary>
    public static ItemGroup GroupOf(ServerItem i)
    {
        if (i.Food) return ItemGroup.Food;

        return i.Type switch
        {
            "Goods" => ItemGroup.Goods,
            "HeadArmor" => ItemGroup.Head,
            "BodyArmor" => ItemGroup.Body,
            "LegArmor" => ItemGroup.Legs,
            "HandArmor" => ItemGroup.Hands,
            "Cape" => ItemGroup.Cape,
            "Horse" => ItemGroup.Horse,
            "HorseHarness" => ItemGroup.Harness,
            "Shield" => ItemGroup.Shield,
            "Bow" or "Sling" => ItemGroup.Bow,
            "Crossbow" => ItemGroup.Crossbow,
            "Arrows" or "Bolts" or "SlingStones" or "Bullets" => ItemGroup.Ammo,
            "Thrown" => ItemGroup.Thrown,
            _ => i.WeaponClass switch
            {
                "OneHandedSword" or "TwoHandedSword" => ItemGroup.Sword,
                "Dagger" => ItemGroup.Dagger,
                "OneHandedAxe" or "TwoHandedAxe" => ItemGroup.Axe,
                "Mace" or "TwoHandedMace" or "Pick" => ItemGroup.Mace,
                "OneHandedPolearm" or "TwoHandedPolearm" or "LowGripPolearm" => ItemGroup.Polearm,
                "Javelin" or "ThrowingAxe" or "ThrowingKnife" or "Stone" => ItemGroup.Thrown,
                _ => ItemGroup.Other,
            },
        };
    }

    public static void Save(string path, IReadOnlyList<ServerItem> items, IReadOnlyList<ItemPrice> prices)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Cache(items.ToList(), prices.ToList()), Json));
    }

    public static (IReadOnlyList<ServerItem> Items, IReadOnlyList<ItemPrice> Prices) Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Cache>(File.ReadAllText(path)) is { } c)
                return (c.Items, c.Prices);
        }
        catch (Exception e) when (e is JsonException or IOException) { }

        return ([], []);
    }

    private sealed record Cache(List<ServerItem> Items, List<ItemPrice> Prices);

    private static int Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
