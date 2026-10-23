using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.Core.Mods;
using CoopKeep.Core.Installation;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Oyuna girmek: kendi sunucuna ya da bir arkadaşının Coop sunucusuna.
/// </summary>
/// <remarks>
/// <para>
/// Coop'ta oyunu bir adrese doğrudan bağlayan bir açılış argümanı yok; bağlanma oyunun içinde,
/// Coop menüsünden yapılır (Steam'de sunucunun barındırıcısının adıyla arama ya da IP). Program
/// oyunu doğru modlarla açar ve adresi panoya kopyalar; oyuncunun yapacağı tek şey adresi
/// yapıştırmaktır.
/// </para>
/// <para>
/// Coop, oyuncunun modlarını sunucununkilerle iki yönde birebir karşılaştırır. Kendi sunucuna
/// girerken oyun dünyanın modlarıyla açılır; arkadaşına girerken başlatıcıda seçili modlarla
/// (arkadaşın mod paketini Modlar sayfasından uygulayınca birebir aynı olur).
/// </para>
/// </remarks>
public partial class MainViewModel
{
    [ObservableProperty] private string _coopStatusText = "";

    [ObservableProperty] private string _coopJoinAddress = "";

    [ObservableProperty] private bool? _coopFirewallOk;

    public bool CoopModReady => _coop is not null && _game is not null;
    public bool CoopFirewallMissing => CoopFirewallOk == false;

    /// <summary>Kurulum bilgileri yenilenince: Coop ve oyunun durumu, güvenlik duvarı.</summary>
    private void OnCoopPageOpened()
    {
        CoopStatusText = (_game, _coop) switch
        {
            (null, _) => L["coop.status.noGame"],
            (_, null) => L["coop.status.noMod"],
            var (g, c) => string.Format(L["coop.status.ok"], c.ModVersion ?? "?", g.GameVersion ?? "?", c.WorkshopItemId ?? "?"),
        };

        if (CoopJoinAddress.Length == 0) CoopJoinAddress = _ui.LastJoinAddress ?? "";
        _ = CheckFirewallAsync();
        NotifyCoop();
    }

    private void NotifyCoop()
    {
        OnPropertyChanged(nameof(CoopModReady));
        OnPropertyChanged(nameof(CoopFirewallMissing));
        JoinCoopCommand.NotifyCanExecuteChanged();
        BrowseCoopCommand.NotifyCanExecuteChanged();
        LaunchCoopCommand.NotifyCanExecuteChanged();
    }

    partial void OnCoopFirewallOkChanged(bool? value) => OnPropertyChanged(nameof(CoopFirewallMissing));
    partial void OnCoopJoinAddressChanged(string value) => JoinCoopCommand.NotifyCanExecuteChanged();

    // ------------------------------------------------------------------
    // Oyunu açma
    // ------------------------------------------------------------------

    private bool CanLaunchCoop() => CoopModReady;

    private bool CanJoinCoop() => CoopModReady && CoopJoinAddress.Trim().Length > 0;

    /// <summary>Arkadaşının sunucusu: oyun açılır, adres panoya kopyalanır.</summary>
    [RelayCommand(CanExecute = nameof(CanJoinCoop))]
    private async Task JoinCoopAsync()
    {
        var address = CoopJoinAddress.Trim();
        _ui.LastJoinAddress = address;
        _ui.Save();

        await CopyToClipboardAsync(address);
        LaunchCoopGame(LauncherMods(), string.Format(L["coop.join.steps"], address));
    }

    /// <summary>Arkadaşının sunucusunu Steam'de aramak için oyunu açar.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunchCoop))]
    private void BrowseCoop() => LaunchCoopGame(LauncherMods(), L["coop.browse.steps"]);

    /// <summary>Oyunu Coop ile açar (başlatıcıda seçili modlarla).</summary>
    [RelayCommand(CanExecute = nameof(CanLaunchCoop))]
    private void LaunchCoop() => LaunchCoopGame(LauncherMods(), L["coop.launched"]);

    /// <summary>
    /// Sunucuyu açan kişi aynı bilgisayardan kendi sunucusuna girer: oyun dünyanın modlarıyla
    /// açılır, yerel adres panoya kopyalanır.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanJoinOwnServer))]
    private async Task JoinOwnServerAsync()
    {
        if (_config is null) return;
        var address = OwnServerAddress();

        // Oyun zaten açıksa (ör. "Başlat ve oyna" önce oyunu açtı) yeniden açılmaz: adres yeter.
        if (ModuleCompatibility.IsGameRunning(_supervisor.ProcessId))
        {
            await CopyToClipboardAsync(address);
            ShowToast(string.Format(L["coop.joinOwn.gameOpen"], address), ToastKind.Success);
            return;
        }

        if (ActiveSaveName is { Length: > 0 } world) LaunchGameForWorld(world, string.Format(L["coop.joinOwn.steps"], address));
        await CopyToClipboardAsync(address);
    }

    /// <summary>Bu bilgisayardaki sunucunun adresi (aynı bilgisayardan bağlanmak için).</summary>
    private string OwnServerAddress() => "127.0.0.1:" + (_config?.Read().Port ?? Core.Configuration.ServerConfig.DefaultPort);

    /// <summary>
    /// Oyunu dünyanın modlarıyla (gerektirdikleri dahil) açar. Oyunun göremediği mod varsa
    /// açmaz: oyun "modül bulunamadı" deyip kapanırdı.
    /// </summary>
    /// <returns>Oyun açıldıysa <c>true</c>.</returns>
    private bool LaunchGameForWorld(string world, string message)
    {
        var mods = WorldModIds(world).ToList();
        var visible = GameModules.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = mods.Where(id => !visible.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            ShowToast(string.Format(L["coop.joinOwn.missing"], DescribeMissing(missing)), ToastKind.Warning);
            return false;
        }

        return LaunchCoopGame(mods, message);
    }

    private bool CanJoinOwnServer() => IsServing && CoopModReady;

    /// <summary>"Oyuna gir" yalnızca sunucu yayındayken.</summary>
    public bool ShowJoinOwn => IsServing;

    /// <summary>Başlatıcıda seçili modlar (Coop ve oyunun kendi modülleri hariç).</summary>
    private static IReadOnlyList<string> LauncherMods() =>
        LauncherProfile.Read()
            .Where(m => m.IsSelected && !CoopClientLauncher.BaseModules.Contains(m.Id, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();

    private bool LaunchCoopGame(IReadOnlyList<string> mods, string message)
    {
        if (_game is null || _coop is null) return false;

        try
        {
            // Modların kendi sıralama istekleri (kütüphaneler resmî modüllerden önce).
            var modules = CoopClientLauncher.ModuleList(mods, _coop.ModuleId, GameModules);
            CoopClientLauncher.Start(_game, CoopClientLauncher.Arguments(modules));
            ShowToast(message, ToastKind.Success);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            ShowToast(ModuleCompatibility.IsGameRunning(null) ? L["coop.gameRunning"] : ex.Message, ToastKind.Error);
            return false;
        }
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;
        if (clipboard is null) return;
        try { await clipboard.SetTextAsync(text); }
        catch (Exception) { /* pano meşgul: kullanıcı adresi ekrandan okur */ }
    }

    // ------------------------------------------------------------------
    // Güvenlik duvarı
    // ------------------------------------------------------------------

    /// <summary>
    /// Oyunun ve Coop sunucusunun gelen bağlantı kuralı var mı? Windows izin penceresi oyunun
    /// ya da sunucunun arkasında kalınca fark edilmiyor ve arkadaşlar bağlanamıyordu.
    /// </summary>
    private async Task CheckFirewallAsync()
    {
        CoopFirewallOk = await Task.Run(() => GameFirewall.HasRule() && (_coop is null || GameFirewall.HasRule(GameFirewall.EngineRuleName)));
    }

    [RelayCommand]
    private async Task AddCoopFirewallRuleAsync()
    {
        if (_game is null) return;
        var rules = new List<(string, string)> { (GameFirewall.RuleName, Path.Combine(_game.ClientBinDirectory, "Bannerlord.exe")) };

        // Coop sunucusunun motoru (modsuz açılışta da sarmalayıcı motoru bununla çalıştırır).
        if (DedicatedServerDir is { } ds)
            rules.Add((GameFirewall.EngineRuleName, Path.Combine(ds, "engine", "dotnet", "dotnet.exe")));

        var ok = await Task.Run(() => GameFirewall.AddRules(rules));
        ShowToast(L[ok ? "coop.firewall.added" : "coop.firewall.failed"], ok ? ToastKind.Success : ToastKind.Warning);
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private void OpenCoopFolder()
    {
        if (_coop is not null) OpenPath(_coop.ModRoot);
    }
}
