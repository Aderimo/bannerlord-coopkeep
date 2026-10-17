using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// CoopKeep sunucu eklentisi: Coop'ta olmayan eşya ver/al komutları, eşya listesi ve
/// kasaba fiyatları.
/// </summary>
/// <remarks>
/// <para>
/// Eklenti, kullanıcının kendi Coop kurulumundaki Coop modülüne alt modül olarak eklenir
/// (<see cref="ServerToolsInstaller"/>). Kullanıcı Ayarlar'dan ya da Eşyalar ekranından
/// açmadıkça hiçbir dosyaya dokunulmaz. Açıksa her başlatmada kontrol edilir: Steam
/// Coop'u güncelleyip dosyaları sıfırladıysa yeniden kurulur.
/// </para>
/// <para>
/// 2026-10-16'da Coop v0.1.6'nın bir test kopyasında denendi: sunucu eklentiyi reddetmedi,
/// eşyalar partinin envanterine eklendi ve Coop'un kendi envanter sayımı değişikliği gördü.
/// Bağlı bir oyuncunun ekranına yansıması canlı testle doğrulanacak.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    [ObservableProperty] private bool _serverToolsEnabled;

    /// <summary>Çalışan sunucuda eklenti yanıt veriyor mu (<c>coopkeep.ping</c>)?</summary>
    [ObservableProperty] private bool _serverToolsAvailable;

    private string ServerToolsCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoopKeep", "server-tools");

    private string ItemCatalogPath => Path.Combine(ServerToolsCacheDir, "items.json");

    private string? DedicatedServerDir => _coop is null ? null : Path.GetDirectoryName(_coop.DedicatedServerExe);

    partial void OnServerToolsEnabledChanged(bool value)
    {
        _ui.ServerTools = value;
        _ui.Save();
        if (!IsRunning) EnsureServerTools();
        else if (value) ShowToast(L["tools.restartNeeded"], ToastKind.Info);
    }

    /// <summary>Uygulamanın içindeki eklenti DLL'ini diske çıkarır.</summary>
    private string ExtractServerTools()
    {
        Directory.CreateDirectory(ServerToolsCacheDir);
        var path = Path.Combine(ServerToolsCacheDir, ServerToolsInstaller.DllName);

        using var source = AssetLoader.Open(new Uri("avares://CoopKeep/Assets/ServerTools/" + ServerToolsInstaller.DllName));
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        var bytes = memory.ToArray();

        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Sunucu kapalıyken çağrılır: ayar açıksa kurar, kapalıysa kaldırır.</summary>
    private void EnsureServerTools()
    {
        if (DedicatedServerDir is not { } ds) return;

        try
        {
            if (ServerToolsEnabled)
            {
                var dll = ExtractServerTools();
                if (!ServerToolsInstaller.IsInstalled(ds, dll))
                {
                    ServerToolsInstaller.Install(ds, dll);
                    AppendConsole("— " + L["tools.installed"]);
                }
            }
            else if (ServerToolsInstaller.IsInstalled(ds))
            {
                ServerToolsInstaller.Uninstall(ds);
                AppendConsole("— " + L["tools.removed"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowToast(string.Format(L["tools.installFailed"], ex.Message), ToastKind.Error);
        }
    }

    /// <summary>Eşya listesi ve fiyatlar bu kadar eskiyse yeniden alınır.</summary>
    private static readonly TimeSpan ItemCatalogMaxAge = TimeSpan.FromDays(1);

    private bool _itemRefreshRunning;

    /// <summary>Sunucu yayına geçince yalnızca eklentinin yanıt verip vermediği yoklanır.</summary>
    /// <remarks>
    /// 1.285 eşyanın listesi ve bütün kasabalardaki fiyatlar sunucuyu bir süre meşgul ediyor.
    /// Önbellek bir günden yeniyse hiç sorulmaz; eskiyse yayına geçişten bir dakika sonra,
    /// ya da eşya penceresi açılınca alınır.
    /// </remarks>
    private async Task DetectServerToolsAsync()
    {
        ServerToolsAvailable = false;
        if (!ServerToolsEnabled) return;

        try
        {
            var ping = await _supervisor.SendAndCollectAsync(AdminCommand.ToolsPing(),
                l => l.StartsWith("COOPKEEP_TOOLS", StringComparison.Ordinal) || l.Contains("no game console command", StringComparison.Ordinal),
                TimeSpan.FromSeconds(3));
            ServerToolsAvailable = System.Linq.Enumerable.Any(ping, l => l.StartsWith("COOPKEEP_TOOLS", StringComparison.Ordinal));
            if (!ServerToolsAvailable || !ItemCatalogStale()) return;

            await Task.Delay(TimeSpan.FromMinutes(1));
            if (IsServing) await RefreshServerItemsAsync();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
    }

    private bool ItemCatalogStale() =>
        !File.Exists(ItemCatalogPath) || !File.Exists(ItemStatsPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(ItemCatalogPath) > ItemCatalogMaxAge;

    /// <summary>Eşya listesini ve kasaba fiyatlarını sunucudan alıp önbelleğe yazar.</summary>
    private async Task RefreshServerItemsAsync()
    {
        if (_itemRefreshRunning || !ServerToolsAvailable || !IsServing) return;
        _itemRefreshRunning = true;

        try
        {
            var items = ServerItemCatalog.ParseItems(await _supervisor.SendAndCollectAsync(AdminCommand.ItemList(),
                l => l.StartsWith("COOPKEEP_ITEMS_END", StringComparison.Ordinal), TimeSpan.FromSeconds(15)));
            var prices = ServerItemCatalog.ParsePrices(await _supervisor.SendAndCollectAsync(AdminCommand.ItemPrices(),
                l => l.StartsWith("COOPKEEP_PRICES_END", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));

            if (items.Count > 0)
            {
                ServerItemCatalog.Save(ItemCatalogPath, items, prices);
                SetServerItems(items, prices);
            }

            // Silah/zırh değerleri: ansiklopedi ve "en iyi eşyalar" rehberi için.
            await RefreshItemStatsAsync();
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
        }
        finally
        {
            _itemRefreshRunning = false;
        }
    }
}
