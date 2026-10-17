using System.Globalization;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopKeep.ServerTools;

/// <summary>
/// CoopKeep'in sunucuya eklediği komutlar.
/// </summary>
/// <remarks>
/// <para>
/// Coop'ta oyuncuya belirli bir eşya veren komut yok ve oyunun kendi hileleri sunucuda
/// kapalı. Bu alt modül yalnızca şu komutları ekler; hepsi sunucu konsolundan (CoopKeep)
/// çalışır, oyuncular kullanamaz:
/// </para>
/// <code>
/// coopkeep.ping                                   → COOPKEEP_TOOLS v2
/// coopkeep.give_item &lt;hero_id&gt; &lt;item_id&gt; &lt;count&gt;  → COOPKEEP_ITEM hero=… item=… old=… new=…
/// coopkeep.take_item &lt;hero_id&gt; &lt;item_id&gt; &lt;count&gt;  → COOPKEEP_ITEM hero=… item=… old=… new=…
/// coopkeep.party_items &lt;hero_id&gt;                  → item_id: count (satır satır), COOPKEEP_ITEMS_END
/// coopkeep.items                                  → ITEM|… (bütün eşyalar), COOPKEEP_ITEMS_END
/// coopkeep.prices [item_id]                       → PRICE|… (kasaba fiyatları), COOPKEEP_PRICES_END
/// coopkeep.hero_stats / add_skill / add_skill_xp / add_focus / add_attribute / add_unspent
/// coopkeep.hero_equipment / roster / parties / owners / settlement_stats / item_stats  (bkz. HeroAndWorldCommands)
/// </code>
/// <para>
/// Eşya, kahramanın partisinin envanterine (<c>ItemRoster</c>) eklenir. Coop envanter
/// değişikliklerini istemcilere kendisi eşitler.
/// </para>
/// </remarks>
public sealed class ServerToolsSubModule : MBSubModuleBase
{
    public const string Version = "2";
    private const int MaxCount = 10_000;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        Register();
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        Register();
    }

    /// <summary>
    /// Komutları oyunun komut tablosuna ekler. Tablo, modüllerimiz yüklenmeden önce
    /// toplanmış olabilir; bu yüzden eksikse doğrudan eklenir.
    /// </summary>
    private static void Register()
    {
        // Son eklenen komut kayıtlıysa hepsi kayıtlıdır.
        if (CommandLineFunctionality.HasFunctionForCommand("coopkeep.add_skill")) return;

        try { CommandLineFunctionality.CollectCommandLineFunctions(); }
        catch (Exception) { /* zaten toplanmışsa yinelenen anahtar hatası verebilir */ }

        var field = typeof(CommandLineFunctionality).GetField("AllFunctions", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var functionType = typeof(CommandLineFunctionality).GetNestedType("CommandLineFunction", BindingFlags.NonPublic | BindingFlags.Public);
        if (field?.GetValue(null) is not System.Collections.IDictionary all || functionType is null) return;

        void Add(string name, Func<List<string>, string> f)
        {
            if (all.Contains(name)) return;
            all[name] = Activator.CreateInstance(functionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { f }, CultureInfo.InvariantCulture);
        }

        Add("coopkeep.ping", Ping);
        Add("coopkeep.give_item", GiveItem);
        Add("coopkeep.take_item", TakeItem);
        Add("coopkeep.party_items", PartyItems);
        Add("coopkeep.items", Items);
        Add("coopkeep.prices", Prices);
        Add("coopkeep.hero_stats", HeroAndWorldCommands.HeroStats);
        Add("coopkeep.add_skill_xp", HeroAndWorldCommands.AddSkillXp);
        Add("coopkeep.add_skill", HeroAndWorldCommands.AddSkill);
        Add("coopkeep.add_focus", HeroAndWorldCommands.AddFocus);
        Add("coopkeep.add_attribute", HeroAndWorldCommands.AddAttribute);
        Add("coopkeep.add_unspent", HeroAndWorldCommands.AddUnspent);
        Add("coopkeep.hero_equipment", HeroAndWorldCommands.HeroEquipment);
        Add("coopkeep.roster", HeroAndWorldCommands.Roster);
        Add("coopkeep.parties", HeroAndWorldCommands.Parties);
        Add("coopkeep.owners", HeroAndWorldCommands.Owners);
        Add("coopkeep.settlement_stats", HeroAndWorldCommands.SettlementStats);
        Add("coopkeep.item_stats", HeroAndWorldCommands.ItemStats);
    }

    /// <summary>
    /// Oyundaki bütün eşyalar: <c>ITEM|id|ad|tür|kategori|değer|kademe|kültür|sivil|yiyecek|ticaret|silah sınıfı|ağırlık</c>.
    /// </summary>
    /// <remarks>Yiyecek ve ticaret malları XML'de yok; tam liste ancak oyunun kendisinden alınabiliyor.</remarks>
    [CommandLineFunctionality.CommandLineArgumentFunction("items", "coopkeep")]
    public static string Items(List<string> args)
    {
        var lines = new List<string>();
        foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
        {
            if (item is null || string.IsNullOrEmpty(item.StringId)) continue;
            lines.Add(string.Join("|",
                "ITEM",
                item.StringId,
                Clean(item.Name?.ToString()),
                item.ItemType.ToString(),
                item.ItemCategory?.StringId ?? "",
                item.Value.ToString(CultureInfo.InvariantCulture),
                ((int)item.Tier).ToString(CultureInfo.InvariantCulture),
                item.Culture?.StringId ?? "",
                item.IsCivilian ? "1" : "0",
                item.IsFood ? "1" : "0",
                item.IsTradeGood ? "1" : "0",
                item.PrimaryWeapon?.WeaponClass.ToString() ?? "",
                item.Weight.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        lines.Add("COOPKEEP_ITEMS_END");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Kasabalardaki gerçek alış fiyatları: <c>PRICE|id|en düşük|kasaba|en yüksek|kasaba|ortalama</c>.
    /// Kimlik verilmezse bütün eşyalar.
    /// </summary>
    [CommandLineFunctionality.CommandLineArgumentFunction("prices", "coopkeep")]
    public static string Prices(List<string> args)
    {
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";

        var items = args.Count > 0
            ? new[] { MBObjectManager.Instance.GetObject<ItemObject>(args[0]) }.Where(i => i is not null).ToList()
            : MBObjectManager.Instance.GetObjectTypeList<ItemObject>().ToList();

        var towns = TaleWorlds.CampaignSystem.Settlements.Town.AllTowns.Where(t => !t.IsCastle).ToList();
        var lines = new List<string>();
        foreach (var item in items)
        {
            int min = int.MaxValue, max = 0;
            string minTown = "", maxTown = "";
            long sum = 0;
            var n = 0;
            foreach (var town in towns)
            {
                int price;
                try { price = town.GetItemPrice(item!, null, false); }
                catch (Exception) { continue; }
                if (price <= 0) continue;

                if (price < min) { min = price; minTown = town.Name.ToString(); }
                if (price > max) { max = price; maxTown = town.Name.ToString(); }
                sum += price;
                n++;
            }

            if (n > 0)
                lines.Add(string.Join("|", "PRICE", item!.StringId, min, Clean(minTown), max, Clean(maxTown), sum / n));
        }

        lines.Add("COOPKEEP_PRICES_END");
        return string.Join("\n", lines);
    }

    private static string Clean(string? s) => (s ?? "").Replace('|', '/').Replace('\n', ' ').Trim();

    [CommandLineFunctionality.CommandLineArgumentFunction("ping", "coopkeep")]
    public static string Ping(List<string> args) => "COOPKEEP_TOOLS v" + Version;

    [CommandLineFunctionality.CommandLineArgumentFunction("give_item", "coopkeep")]
    public static string GiveItem(List<string> args) => ChangeItem(args, +1);

    [CommandLineFunctionality.CommandLineArgumentFunction("take_item", "coopkeep")]
    public static string TakeItem(List<string> args) => ChangeItem(args, -1);

    [CommandLineFunctionality.CommandLineArgumentFunction("party_items", "coopkeep")]
    public static string PartyItems(List<string> args)
    {
        if (args.Count < 1) return "Usage: coopkeep.party_items <hero_id>";
        if (FindRoster(args[0], out var roster) is { } error) return error;

        var lines = new List<string>();
        for (var i = 0; i < roster!.Count; i++)
        {
            var element = roster.GetElementCopyAtIndex(i);
            if (element.EquipmentElement.Item is { } item && element.Amount > 0)
                lines.Add($"{item.StringId}: {element.Amount}");
        }

        lines.Add("COOPKEEP_ITEMS_END");
        return string.Join("\n", lines);
    }

    private static string ChangeItem(List<string> args, int sign)
    {
        if (args.Count < 3)
            return $"Usage: coopkeep.{(sign > 0 ? "give_item" : "take_item")} <hero_id> <item_id> <count>";
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0 || count > MaxCount)
            return "COOPKEEP_ERROR count must be between 1 and " + MaxCount;
        if (FindRoster(args[0], out var roster) is { } error) return error;

        var item = MBObjectManager.Instance.GetObject<ItemObject>(args[1]);
        if (item is null) return $"COOPKEEP_ERROR item '{args[1]}' not found.";

        var old = roster!.GetItemNumber(item);
        var delta = sign > 0 ? count : -Math.Min(count, old);
        if (delta != 0) roster.AddToCounts(item, delta);

        return $"COOPKEEP_ITEM hero={args[0]} item={item.StringId} old={old} new={roster.GetItemNumber(item)}";
    }

    private static string? FindRoster(string heroId, out ItemRoster? roster)
    {
        roster = null;
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";

        var hero = HeroAndWorldCommands.FindHero(heroId);
        if (hero is null) return $"COOPKEEP_ERROR hero '{heroId}' not found.";
        if (hero.PartyBelongedTo is not { } party) return $"COOPKEEP_ERROR hero '{heroId}' has no party.";

        roster = party.ItemRoster;
        return null;
    }
}
