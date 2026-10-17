using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopKeep.ServerTools;

/// <summary>
/// Kahraman gelişimi ve dünya durumu komutları (v2).
/// </summary>
/// <remarks>
/// <para>
/// Beceri, özellik ve odak oyunun kendi <c>HeroDeveloper</c>'ı üzerinden değiştirilir;
/// Coop bu sınıfı yamalıyor (<c>SetSkillXpPatch</c>, <c>HeroDeveloperSync</c>) ve
/// değişikliği oyunculara iletir.
/// </para>
/// <para>
/// Toplu okuma komutları (<c>parties</c>, <c>owners</c>) haritanın yüzlerce ayrı sorgusunun
/// yerini alır. Hepsinin çıktısı satır başına bir kayıt, alanlar <c>|</c> ile ayrılmış,
/// sonda <c>COOPKEEP_END</c>.
/// </para>
/// </remarks>
internal static class HeroAndWorldCommands
{
    public const string End = "COOPKEEP_END";

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string C(string? s) => (s ?? "").Replace('|', '/').Replace('\n', ' ').Trim();

    /// <summary>
    /// Kahramanı oyunun kimliğiyle ya da Coop'un kayıt kimliğiyle bulur.
    /// </summary>
    /// <remarks>
    /// Coop'un kayıt kimliği türün adı + oyunun kimliği: <c>Hero_dead_lord_2_1</c> →
    /// <c>dead_lord_2_1</c> (<c>coop.debug.hero.list</c>). Save'in eş JSON'undaki oyuncu
    /// kimlikleri (<c>Hero_Player2863</c>) bu biçimde.
    /// </remarks>
    internal static Hero? FindHero(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var hero = Hero.Find(id) ?? Hero.FindFirst(h => h.StringId == id);
        if (hero is null && id.StartsWith("Hero_", StringComparison.Ordinal))
        {
            var inner = id.Substring(5);
            hero = Hero.Find(inner) ?? Hero.FindFirst(h => h.StringId == inner);
        }

        return hero ?? Hero.FindFirst(h => string.Equals(h.StringId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<SkillObject> Skills => MBObjectManager.Instance.GetObjectTypeList<SkillObject>();
    private static IEnumerable<CharacterAttribute> Attributes => MBObjectManager.Instance.GetObjectTypeList<CharacterAttribute>();

    /// <summary>
    /// <c>HERO|id|ad|seviye|toplamXp|harcanmamış özellik|harcanmamış odak|can|azami can|altın|yaş</c>,
    /// <c>ATTR|id|ad|değer</c>, <c>SKILL|id|ad|değer|odak|xp ilerleme|özellik id</c>.
    /// </summary>
    public static string HeroStats(List<string> args)
    {
        if (args.Count < 1) return "Usage: coopkeep.hero_stats <hero_id>";
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";
        var hero = FindHero(args[0]);
        if (hero is null) return $"COOPKEEP_ERROR hero '{args[0]}' not found.";

        var dev = hero.HeroDeveloper;
        var lines = new List<string>
        {
            string.Join("|", "HERO", hero.StringId, C(hero.Name?.ToString()), I(hero.Level), I(dev.TotalXp),
                I(dev.UnspentAttributePoints), I(dev.UnspentFocusPoints), I(hero.HitPoints), I(hero.MaxHitPoints),
                I(hero.Gold), F(hero.Age)),
        };

        foreach (var a in Attributes)
            lines.Add(string.Join("|", "ATTR", a.StringId, C(a.Name?.ToString()), I(hero.GetAttributeValue(a))));

        foreach (var s in Skills)
        {
            int focus = 0, progress = 0;
            try { focus = dev.GetFocus(s); progress = dev.GetSkillXpProgress(s); } catch (Exception) { }
            lines.Add(string.Join("|", "SKILL", s.StringId, C(s.Name?.ToString()), I(hero.GetSkillValue(s)), I(focus), I(progress),
                (s.Attributes is { Length: > 0 } at ? at[0].StringId : "")));
        }

        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary><c>coopkeep.add_skill_xp &lt;hero&gt; &lt;skill&gt; &lt;xp&gt;</c> → <c>COOPKEEP_DEV …</c></summary>
    public static string AddSkillXp(List<string> args)
    {
        if (args.Count < 3) return "Usage: coopkeep.add_skill_xp <hero_id> <skill_id> <xp>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        var skill = Skills.FirstOrDefault(s => string.Equals(s.StringId, args[1], StringComparison.OrdinalIgnoreCase));
        if (skill is null) return $"COOPKEEP_ERROR skill '{args[1]}' not found.";
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var xp) || xp <= 0 || xp > 1_000_000)
            return "COOPKEEP_ERROR xp must be between 1 and 1000000";

        var before = hero!.GetSkillValue(skill);
        hero.HeroDeveloper.AddSkillXp(skill, xp, false, true);
        return $"COOPKEEP_DEV hero={hero.StringId} what=skill:{skill.StringId} old={before} new={hero.GetSkillValue(skill)}";
    }

    /// <summary>
    /// <c>coopkeep.add_skill &lt;hero&gt; &lt;skill&gt; &lt;n&gt;</c>: beceri seviyesini doğrudan değiştirir (eksi: düşürür).
    /// </summary>
    /// <remarks>Yetenek XP'si yüksek seviyelerde seviyeyi neredeyse hiç kıpırdatmıyor; görünür sonuç için bu kullanılır.</remarks>
    public static string AddSkill(List<string> args)
    {
        if (args.Count < 3) return "Usage: coopkeep.add_skill <hero_id> <skill_id> <count>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        var skill = Skills.FirstOrDefault(s => string.Equals(s.StringId, args[1], StringComparison.OrdinalIgnoreCase));
        if (skill is null) return $"COOPKEEP_ERROR skill '{args[1]}' not found.";
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0 || Math.Abs(n) > 330)
            return "COOPKEEP_ERROR count must be between -330 and 330";

        var before = hero!.GetSkillValue(skill);
        var target = Math.Max(0, Math.Min(330, before + n));
        // ChangeSkillLevel oyunun olaylarını tetikler ama eksiyi ve elle düşürülmüş beceriyi yok sayıyor;
        // o durumlarda değer doğrudan yazılır.
        if (target > before) hero.HeroDeveloper.ChangeSkillLevel(skill, target - before, false);
        if (hero.GetSkillValue(skill) != target) hero.SetSkillValue(skill, target);
        return $"COOPKEEP_DEV hero={hero.StringId} what=skill:{skill.StringId} old={before} new={hero.GetSkillValue(skill)}";
    }

    /// <summary><c>coopkeep.add_focus &lt;hero&gt; &lt;skill&gt; &lt;n&gt;</c> (eksi: geri alır).</summary>
    public static string AddFocus(List<string> args)
    {
        if (args.Count < 3) return "Usage: coopkeep.add_focus <hero_id> <skill_id> <count>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        var skill = Skills.FirstOrDefault(s => string.Equals(s.StringId, args[1], StringComparison.OrdinalIgnoreCase));
        if (skill is null) return $"COOPKEEP_ERROR skill '{args[1]}' not found.";
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0 || Math.Abs(n) > 10)
            return "COOPKEEP_ERROR count must be between -10 and 10";

        var dev = hero!.HeroDeveloper;
        var before = dev.GetFocus(skill);
        var max = Campaign.Current.Models.CharacterDevelopmentModel.MaxFocusPerSkill;
        var target = Math.Max(0, Math.Min(max, before + n));
        if (target != before) dev.AddFocus(skill, target - before, false);
        return $"COOPKEEP_DEV hero={hero.StringId} what=focus:{skill.StringId} old={before} new={dev.GetFocus(skill)}";
    }

    /// <summary><c>coopkeep.add_attribute &lt;hero&gt; &lt;attribute&gt; &lt;n&gt;</c> (eksi: geri alır).</summary>
    public static string AddAttribute(List<string> args)
    {
        if (args.Count < 3) return "Usage: coopkeep.add_attribute <hero_id> <attribute_id> <count>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        var attr = Attributes.FirstOrDefault(a => string.Equals(a.StringId, args[1], StringComparison.OrdinalIgnoreCase));
        if (attr is null) return $"COOPKEEP_ERROR attribute '{args[1]}' not found.";
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0 || Math.Abs(n) > 10)
            return "COOPKEEP_ERROR count must be between -10 and 10";

        var before = hero!.GetAttributeValue(attr);
        var target = Math.Max(1, Math.Min(10, before + n));
        if (target != before) hero.HeroDeveloper.AddAttribute(attr, target - before, false);
        return $"COOPKEEP_DEV hero={hero.StringId} what=attribute:{attr.StringId} old={before} new={hero.GetAttributeValue(attr)}";
    }

    /// <summary><c>coopkeep.add_unspent &lt;hero&gt; &lt;attribute|focus&gt; &lt;n&gt;</c>: harcanmamış puan ver/al.</summary>
    public static string AddUnspent(List<string> args)
    {
        if (args.Count < 3) return "Usage: coopkeep.add_unspent <hero_id> <attribute|focus> <count>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n == 0 || Math.Abs(n) > 50)
            return "COOPKEEP_ERROR count must be between -50 and 50";

        var dev = hero!.HeroDeveloper;
        if (args[1] == "attribute")
        {
            var before = dev.UnspentAttributePoints;
            dev.UnspentAttributePoints = Math.Max(0, before + n);
            return $"COOPKEEP_DEV hero={hero.StringId} what=unspent:attribute old={before} new={dev.UnspentAttributePoints}";
        }

        if (args[1] == "focus")
        {
            var before = dev.UnspentFocusPoints;
            dev.UnspentFocusPoints = Math.Max(0, before + n);
            return $"COOPKEEP_DEV hero={hero.StringId} what=unspent:focus old={before} new={dev.UnspentFocusPoints}";
        }

        return "COOPKEEP_ERROR kind must be attribute or focus";
    }

    // EquipmentIndex'te aynı değere birden çok ad düşüyor (Head = NumAllWeaponSlots); adlar elle verilir.
    private static readonly string[] SlotNames =
        { "Weapon0", "Weapon1", "Weapon2", "Weapon3", "Banner", "Head", "Body", "Leg", "Gloves", "Cape", "Horse", "HorseHarness" };

    /// <summary><c>EQ|battle|slot|item id|ad</c> ve <c>EQ|civilian|…</c>.</summary>
    public static string HeroEquipment(List<string> args)
    {
        if (args.Count < 1) return "Usage: coopkeep.hero_equipment <hero_id>";
        if (!TryHero(args[0], out var hero, out var error)) return error;

        var lines = new List<string>();
        void Dump(string set, Equipment? eq)
        {
            if (eq is null) return;
            for (var i = 0; i < SlotNames.Length; i++)
            {
                var item = eq[i].Item;
                if (item is null) continue;
                lines.Add(string.Join("|", "EQ", set, SlotNames[i], item.StringId, C(item.Name?.ToString())));
            }
        }

        Dump("battle", hero!.BattleEquipment);
        Dump("civilian", hero.CivilianEquipment);
        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary><c>TROOP|members|asker id|ad|sayı|yaralı|xp|kahraman mı</c> (esirler: <c>prisoners</c>).</summary>
    public static string Roster(List<string> args)
    {
        if (args.Count < 1) return "Usage: coopkeep.roster <hero_id>";
        if (!TryHero(args[0], out var hero, out var error)) return error;
        if (hero!.PartyBelongedTo is not { } party) return $"COOPKEEP_ERROR hero '{args[0]}' has no party.";

        var lines = new List<string> { string.Join("|", "PARTY", party.StringId, I(party.MemberRoster.TotalManCount), I(party.MemberRoster.TotalWounded)) };
        void Dump(string kind, TaleWorlds.CampaignSystem.Roster.TroopRoster roster)
        {
            foreach (var e in roster.GetTroopRoster())
            {
                if (e.Character is null) continue;
                lines.Add(string.Join("|", "TROOP", kind, e.Character.StringId, C(e.Character.Name?.ToString()), I(e.Number),
                    I(e.WoundedNumber), I(e.Xp), e.Character.IsHero ? "1" : "0"));
            }
        }

        Dump("members", party.MemberRoster);
        Dump("prisoners", party.PrisonRoster);
        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Lideri olan bütün partiler: <c>MP|parti|lider id|lider adı|klan|krallık|x|y|asker|yaralı|yerleşim|davranış|ordu|savaşta|oyuncu mu|hedef yerleşim|kuşatıyor mu</c>.
    /// </summary>
    public static string Parties(List<string> args)
    {
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";

        var lines = new List<string>();
        foreach (var p in MobileParty.All)
        {
            var leader = p?.LeaderHero;
            if (p is null || leader is null) continue;
            if (!(leader.IsLord || leader.IsMinorFactionHero || leader.Clan?.StringId == "player_faction" || p.IsMainParty || leader.IsHumanPlayerCharacter)) continue;

            var pos = p.Position;
            lines.Add(string.Join("|", "MP", p.StringId, leader.StringId, C(leader.Name?.ToString()), p.ActualClan?.StringId ?? "",
                p.ActualClan?.Kingdom?.StringId ?? "", F(pos.X), F(pos.Y), I(p.MemberRoster.TotalManCount), I(p.MemberRoster.TotalWounded),
                p.CurrentSettlement?.StringId ?? "", p.DefaultBehavior.ToString(), p.Army is null ? "0" : "1", p.MapEvent is null ? "0" : "1",
                leader.IsHumanPlayerCharacter ? "1" : "0", p.TargetSettlement?.StringId ?? "", p.BesiegedSettlement is null ? "0" : "1"));
        }

        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary>Bütün kasaba, kale ve köylerin sahibi: <c>OWN|yerleşim|klan</c>.</summary>
    public static string Owners(List<string> args)
    {
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";

        var lines = Settlement.All
            .Where(s => s is not null && (s.IsFortification || s.IsVillage))
            .Select(s => string.Join("|", "OWN", s.StringId, s.OwnerClan?.StringId ?? ""))
            .ToList();
        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary><c>SET|id|refah|güvenlik|sadakat|milis|sahip klan</c> (kasaba ve kaleler).</summary>
    public static string SettlementStats(List<string> args)
    {
        if (Campaign.Current is null) return "COOPKEEP_ERROR no campaign is running.";

        var lines = new List<string>();
        foreach (var s in Settlement.All.Where(s => s is not null && s.IsFortification))
        {
            var t = s.Town;
            lines.Add(string.Join("|", "SET", s.StringId, F(t?.Prosperity ?? 0), F(t?.Security ?? 0), F(t?.Loyalty ?? 0),
                F(s.Militia), s.OwnerClan?.StringId ?? ""));
        }

        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Silah, zırh ve at değerleri:
    /// <c>STAT|id|savurma hasarı|saplama hasarı|savurma hızı|saplama hızı|uzunluk|isabet|baş|gövde|kol|bacak|at hızı|manevra|hücum|at canı</c>.
    /// </summary>
    public static string ItemStats(List<string> args)
    {
        var lines = new List<string>();
        foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
        {
            if (item is null) continue;
            var w = item.PrimaryWeapon;
            var a = item.ArmorComponent;
            var h = item.HorseComponent;
            if (w is null && a is null && h is null) continue;

            lines.Add(string.Join("|", "STAT", item.StringId,
                I(w?.SwingDamage ?? 0), I(w?.ThrustDamage ?? 0), I(w?.SwingSpeed ?? 0), I(w?.ThrustSpeed ?? 0),
                I(w?.WeaponLength ?? 0), I(w?.Accuracy ?? 0),
                I(a?.HeadArmor ?? 0), I(a?.BodyArmor ?? 0), I(a?.ArmArmor ?? 0), I(a?.LegArmor ?? 0),
                I(h?.Speed ?? 0), I(h?.Maneuver ?? 0), I(h?.ChargeDamage ?? 0), I(h?.HitPoints ?? 0)));
        }

        lines.Add(End);
        return string.Join("\n", lines);
    }

    private static bool TryHero(string id, out Hero? hero, out string error)
    {
        hero = null;
        error = "";
        if (Campaign.Current is null) { error = "COOPKEEP_ERROR no campaign is running."; return false; }
        hero = FindHero(id);
        if (hero is null) { error = $"COOPKEEP_ERROR hero '{id}' not found."; return false; }
        return true;
    }
}
