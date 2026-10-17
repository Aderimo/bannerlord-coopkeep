using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.App.Localization;
using CoopKeep.Core.Images;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Beceri ekranındaki tek beceri: ikon, seviye, odak noktaları.</summary>
public sealed partial class SkillCell(string id) : ObservableObject
{
    /// <summary>Oyundaki en yüksek beceri seviyesi.</summary>
    public const int MaxValue = 330;

    /// <summary>Bir beceriye verilebilecek en çok odak puanı.</summary>
    public const int MaxFocus = 5;

    public string Id { get; } = id;

    [ObservableProperty] private string _name = id;
    [ObservableProperty] private int _value;
    [ObservableProperty] private int _focus;
    [ObservableProperty] private int _xpProgress;
    [ObservableProperty] private bool _isSelected;

    public GameImage Icon => GameImages.Get(GameImageKind.Skill, Id);

    /// <summary>Odak puanı noktaları: dolu / boş.</summary>
    public IReadOnlyList<bool> FocusDots => Enumerable.Range(0, MaxFocus).Select(i => i < Focus).ToList();

    public double ValueRatio => Math.Clamp(Value / (double)MaxValue, 0, 1);

    partial void OnFocusChanged(int value) => OnPropertyChanged(nameof(FocusDots));
    partial void OnValueChanged(int value) => OnPropertyChanged(nameof(ValueRatio));
}

/// <summary>Bir özellik ve ona bağlı üç beceri (oyundaki sütunlar).</summary>
public sealed partial class AttributeColumn(string id, IReadOnlyList<SkillCell> skills) : ObservableObject
{
    public string Id { get; } = id;
    public IReadOnlyList<SkillCell> Skills { get; } = skills;

    [ObservableProperty] private string _name = id;
    [ObservableProperty] private string _short = id;
    [ObservableProperty] private int _value;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Oyundaki "Beceriler" ekranının CoopKeep karşılığı: 6 özellik, 18 beceri, odak puanları,
/// seviye ve XP. Tıklanan beceriye seviye/odak/XP, özelliğe puan verilir.
/// </summary>
/// <remarks>
/// <para>
/// Değerler CoopKeep sunucu eklentisinden okunur (<c>coopkeep.hero_stats</c>) ve her
/// değişiklikten sonra yeniden okunur; ekrandaki sayı sunucudaki gerçek değerdir.
/// </para>
/// <para>
/// Yetenek XP'si oyunun kendi öğrenme hızıyla işler: yüksek seviyede 5.000 XP seviyeyi
/// kıpırdatmayabilir. Görünür sonuç için "seviye" düğmeleri beceriyi doğrudan değiştirir
/// (2026-10-17'de test sunucusunda 209→219, 219→214 denendi).
/// </para>
/// Aynı sınıf oyuncu profilinde ve Hileler sekmesinde kullanılır.
/// </remarks>
public sealed partial class SkillSheet : ObservableObject
{
    /// <summary>Oyundaki sıra: özellik → becerileri.</summary>
    public static readonly (string Attribute, string[] Skills)[] Layout =
    [
        ("vigor", ["OneHanded", "TwoHanded", "Polearm"]),
        ("control", ["Bow", "Crossbow", "Throwing"]),
        ("endurance", ["Riding", "Athletics", "Crafting"]),
        ("cunning", ["Scouting", "Tactics", "Roguery"]),
        ("social", ["Charm", "Leadership", "Trade"]),
        ("intelligence", ["Steward", "Medicine", "Engineering"]),
    ];

    private readonly Func<Loc> _loc;
    private readonly Func<HeroRow, string, string, Task<bool>> _run;
    private readonly Func<string, Task<IReadOnlyList<string>>> _query;

    /// <param name="loc">Geçerli dil.</param>
    /// <param name="run">Değiştiren komutu gönderir ve denetim kaydına yazar: (kahraman, komut, kayıt metni) → başarılı mı.</param>
    /// <param name="query">Okuma komutu gönderir, satırları döndürür.</param>
    public SkillSheet(Func<Loc> loc, Func<HeroRow, string, string, Task<bool>> run, Func<string, Task<IReadOnlyList<string>>> query)
    {
        _loc = loc;
        _run = run;
        _query = query;
        Attributes = Layout.Select(c => new AttributeColumn(c.Attribute, c.Skills.Select(s => new SkillCell(s)).ToList())).ToList();
        ApplyNames();
    }

    private Loc L => _loc();

    public IReadOnlyList<AttributeColumn> Attributes { get; }

    private IEnumerable<SkillCell> AllSkills => Attributes.SelectMany(a => a.Skills);

    [ObservableProperty] private HeroRow? _hero;
    [ObservableProperty] private HeroStats? _stats;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private SkillCell? _selectedSkill;
    [ObservableProperty] private AttributeColumn? _selectedAttribute;
    [ObservableProperty] private bool _busy;

    /// <summary>"XP ver" kutusundaki miktar.</summary>
    [ObservableProperty] private decimal? _xpAmount = 1000;

    public bool HasStats => Stats is not null;
    public bool HasError => Error.Length > 0;
    public bool HasSelectedSkill => SelectedSkill is not null;
    public bool HasSelectedAttribute => SelectedAttribute is not null;
    public bool HasSelection => HasSelectedSkill || HasSelectedAttribute;

    public string LevelText => Stats is { } s ? s.Level.ToString(CultureInfo.CurrentCulture) : "—";
    public string TotalXpText => Stats is { } s ? s.TotalXp.ToString("N0", CultureInfo.CurrentCulture) : "—";
    public string UnspentAttributeText => Stats?.UnspentAttributePoints.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string UnspentFocusText => Stats?.UnspentFocusPoints.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string AgeText => Stats is { } s ? ((int)s.Age).ToString(CultureInfo.CurrentCulture) : "—";
    public string HealthText => Stats is { } s ? $"{s.HitPoints} / {s.MaxHitPoints}" : "—";
    public string GoldText => Stats is { } s ? s.Gold.ToString("N0", CultureInfo.CurrentCulture) : "—";

    /// <summary>Seçili becerinin açıklaması: "Liderlik · 209 · odak 2/5 · XP 5.000".</summary>
    public string SelectionText => SelectedSkill is { } k
        ? string.Format(L["skills.selSkill"], k.Name, k.Value, k.Focus, SkillCell.MaxFocus, k.XpProgress.ToString("N0", CultureInfo.CurrentCulture))
        : SelectedAttribute is { } a
            ? string.Format(L["skills.selAttr"], a.Name, a.Value)
            : L["skills.pick"];

    partial void OnStatsChanged(HeroStats? value)
    {
        foreach (var name in new[] { nameof(HasStats), nameof(LevelText), nameof(TotalXpText), nameof(UnspentAttributeText),
                     nameof(UnspentFocusText), nameof(AgeText), nameof(HealthText), nameof(GoldText), nameof(SelectionText) })
            OnPropertyChanged(name);
    }

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedSkillChanged(SkillCell? value)
    {
        foreach (var s in AllSkills) s.IsSelected = s == value;
        OnPropertyChanged(nameof(HasSelectedSkill));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    partial void OnSelectedAttributeChanged(AttributeColumn? value)
    {
        foreach (var a in Attributes) a.IsSelected = a == value;
        OnPropertyChanged(nameof(HasSelectedAttribute));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    partial void OnHeroChanged(HeroRow? value)
    {
        Stats = null;
        Error = "";
        SelectedSkill = null;
        SelectedAttribute = null;
    }

    /// <summary>Dil değişince adları yeniler.</summary>
    public void ApplyNames()
    {
        foreach (var a in Attributes)
        {
            a.Name = L["attr." + a.Id];
            a.Short = L["attr.short." + a.Id];
            foreach (var s in a.Skills) s.Name = L["skill." + s.Id];
        }

        OnPropertyChanged(nameof(SelectionText));
    }

    /// <summary>Kahramanın gelişimini sunucudan okur.</summary>
    public async Task LoadAsync()
    {
        if (Hero is not { } hero) return;

        Loading = true;
        try
        {
            var lines = await _query(AdminCommand.ToolsHeroStats(hero.HeroId));
            if (Hero != hero) return;

            if (ServerToolsParser.ParseHeroStats(lines) is { } stats)
            {
                Apply(stats);
                Error = "";
            }
            else
            {
                Error = lines.FirstOrDefault(l => l.StartsWith("COOPKEEP_ERROR", StringComparison.Ordinal)) is { } err
                    ? err["COOPKEEP_ERROR".Length..].Trim()
                    : L["skills.unavailable"];
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    private void Apply(HeroStats stats)
    {
        foreach (var a in Attributes)
            a.Value = stats.Attributes.FirstOrDefault(x => x.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

        foreach (var s in AllSkills)
        {
            var src = stats.Skills.FirstOrDefault(x => x.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
            s.Value = src?.Value ?? 0;
            s.Focus = src?.Focus ?? 0;
            s.XpProgress = src?.XpProgress ?? 0;
        }

        Stats = stats;
    }

    // --- seçim -------------------------------------------------------------

    [RelayCommand]
    private void SelectSkill(SkillCell? skill)
    {
        SelectedAttribute = null;
        SelectedSkill = SelectedSkill == skill ? null : skill;
    }

    [RelayCommand]
    private void SelectAttribute(AttributeColumn? attribute)
    {
        SelectedSkill = null;
        SelectedAttribute = SelectedAttribute == attribute ? null : attribute;
    }

    // --- değişiklikler -------------------------------------------------------

    /// <summary>Seçili becerinin seviyesi: "+10", "-1" …</summary>
    [RelayCommand]
    private Task ChangeSkill(string? delta) =>
        SelectedSkill is { } s && Parse(delta) is { } n
            ? ApplyAsync(h => AdminCommand.ToolsAddSkill(h.HeroId, s.Id, n), string.Format(L["audit.skill"], s.Name, Signed(n)))
            : Task.CompletedTask;

    [RelayCommand]
    private Task ChangeFocus(string? delta) =>
        SelectedSkill is { } s && Parse(delta) is { } n
            ? ApplyAsync(h => AdminCommand.ToolsAddFocus(h.HeroId, s.Id, n), string.Format(L["audit.focus"], s.Name, Signed(n)))
            : Task.CompletedTask;

    [RelayCommand]
    private Task GiveXp()
    {
        var xp = (int)(XpAmount ?? 0);
        return SelectedSkill is { } s && xp > 0
            ? ApplyAsync(h => AdminCommand.ToolsAddSkillXp(h.HeroId, s.Id, xp), string.Format(L["audit.skillXp"], s.Name, xp))
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task ChangeAttribute(string? delta) =>
        SelectedAttribute is { } a && Parse(delta) is { } n
            ? ApplyAsync(h => AdminCommand.ToolsAddAttribute(h.HeroId, a.Id, n), string.Format(L["audit.attr"], a.Name, Signed(n)))
            : Task.CompletedTask;

    [RelayCommand]
    private Task ChangeUnspentAttribute(string? delta) =>
        Parse(delta) is { } n
            ? ApplyAsync(h => AdminCommand.ToolsAddUnspent(h.HeroId, true, n), string.Format(L["audit.unspentAttr"], Signed(n)))
            : Task.CompletedTask;

    [RelayCommand]
    private Task ChangeUnspentFocus(string? delta) =>
        Parse(delta) is { } n
            ? ApplyAsync(h => AdminCommand.ToolsAddUnspent(h.HeroId, false, n), string.Format(L["audit.unspentFocus"], Signed(n)))
            : Task.CompletedTask;

    private async Task ApplyAsync(Func<HeroRow, string> command, string audit)
    {
        if (Hero is not { } hero || Busy) return;

        Busy = true;
        try
        {
            if (await _run(hero, command(hero), audit)) await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private static int? Parse(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0 ? n : null;

    private static string Signed(int n) => n > 0 ? "+" + n : n.ToString(CultureInfo.InvariantCulture);
}
