using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CoopKeep.App;
using CoopKeep.App.ViewModels;
using CoopKeep.App.Views;

// Kullanım: UiShots <çıktı klasörü> [tr|en]
// Her sayfayı 1440x900 pencerede çizip <klasör>\<sayfa>.png olarak kaydeder.
var outDir = args.Length > 0 ? args[0] : "shots";
if (args.Length > 1) CoopKeep.App.Localization.Loc.Current.Language = args[1];
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

// Tema: kullanıcının kayıtlı teması (CK_THEME=Night:Blue gibi verilirse o; kayda yazılmaz).
var themeArg = Environment.GetEnvironmentVariable("CK_THEME");
if (themeArg is { Length: > 0 })
{
    var parts = themeArg.Split(':');
    CoopKeep.App.Theming.ThemeManager.Apply(parts[0],
        CoopKeep.App.Theming.ThemeManager.ParseAccent(parts.Length > 1 ? parts[1] : "Gold"));
}
else
{
    var saved = CoopKeep.App.Settings.UiSettings.Load();
    CoopKeep.App.Theming.ThemeManager.Apply(saved.ThemeBase,
        CoopKeep.App.Theming.ThemeManager.ParseAccent(saved.ThemeAccent));
}

var vm = new MainViewModel();
// Program kayıtlı dil tercihini yükler; istenen dil ondan sonra verilir.
// Dil, programın kendi düğmesiyle (kodda hesaplanan metinler de yenilensin); düğme tercihi
// kaydettiği için iş bitince eski dile geri çevrilir.
var toggled = args.Length > 1 && CoopKeep.App.Localization.Loc.Current.Language != args[1];
if (toggled) vm.ToggleLanguageCommand.Execute(null);
var toolsWere = vm.ToolsExpanded; // sayfalar dolaşılırken açılır; kullanıcının tercihi sonda geri verilir
var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
window.Show();

void Pump(int ms)
{
    var until = DateTime.UtcNow.AddMilliseconds(ms);
    while (DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
}

void Shot(string name)
{
    Pump(1200);
    var frame = window.CaptureRenderedFrame();
    frame?.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine(name);
}

// UPnP arama denemesi (CK_UPNP=1): modem bulunuyor mu? Port açmaz, adres yazmaz.
if (Environment.GetEnvironmentVariable("CK_UPNP") is not null)
{
    // Arayüz döngüsü dönmediği için iş arka planda yürütülüp beklenir.
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var gw = Task.Run(() => CoopKeep.Core.Net.UpnpGateway.DiscoverAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
    Console.WriteLine((gw is null ? "upnp: not found" : $"upnp: found {gw.ServiceType} control={gw.ControlUrl.Host}:{gw.ControlUrl.Port}") + $" ({sw.ElapsedMilliseconds} ms)");
    if (gw is not null) Console.WriteLine("external ip readable: " + (Task.Run(() => gw.GetExternalIpAsync()).GetAwaiter().GetResult() is not null));
    return;
}

// Arama denemesi (CK_SEARCH="mod adı"): sekme açılınca başlayan aramanın hemen ardından
// kullanıcının yazıp Enter'a basması. Sonucu yazar ve çıkar.
if (Environment.GetEnvironmentVariable("CK_SEARCH") is { Length: > 0 } query)
{
    vm.CurrentPage = AppPage.Mods;
    vm.ModsTab = ModsTab.Browse;
    Pump(300);
    vm.BrowseQuery = query;
    Console.WriteLine($"enter: canExecute={vm.SearchModsCommand.CanExecute(null)}");
    vm.SearchModsCommand.Execute(null);
    for (var i = 0; i < 30 && vm.BrowseBusy; i++) Pump(1000);
    Console.WriteLine($"search '{query}': busy={vm.BrowseBusy} cards={vm.BrowseCards.Count} msg='{vm.BrowseMessage}' first='{vm.BrowseCards.FirstOrDefault()?.Title}'");
    vm.BrowseSite = CoopKeep.Core.Mods.ModSite.Nexus;
    Pump(300);
    vm.SearchModsCommand.Execute(null);
    for (var i = 0; i < 30 && vm.BrowseBusy; i++) Pump(1000);
    Console.WriteLine($"nexus '{query}': busy={vm.BrowseBusy} cards={vm.BrowseCards.Count} msg='{vm.BrowseMessage}' first='{vm.BrowseCards.FirstOrDefault()?.Title}'");
    return;
}

Pump(2500);
// Bellek ölçümü (CK_MEM=1): her sayfa açıldıktan sonra çalışma kümesi ve yönetilen yığın.
if (Environment.GetEnvironmentVariable("CK_MEM") is not null)
{
    void Mem(string label)
    {
        Pump(1500);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"{label,-14} ws={self.WorkingSet64 / 1048576,5} MB  managed={GC.GetTotalMemory(false) / 1048576,5} MB  private={self.PrivateMemorySize64 / 1048576,5} MB");
    }
    Mem("start");
    foreach (var page in Enum.GetValues<AppPage>())
    {
        vm.CurrentPage = page;
        Mem(page.ToString());
    }
    vm.CurrentPage = AppPage.Home;
    Mem("back-home");
    vm.ToolsExpanded = toolsWere;
    return;
}
foreach (var page in Enum.GetValues<AppPage>())
{
    vm.CurrentPage = page;
    Shot(page.ToString());
}

// Modlar sayfasının tamamı (gruplar aşağıda): uzun pencere.
vm.CurrentPage = AppPage.Mods;
Pump(4000); // Atölye açıklamaları arka planda gelir
window.Height = 2300;
Shot("Mods-full");
// Sorunlu hâl: Coop'un reddettiği bir modül açık + kaydedilmemiş değişiklik (yalnızca bellekte; yazılmaz).
if (vm.IncompatibleMods.FirstOrDefault() is { } bad && vm.CoopRow is { } coop)
{
    bad.IsSelected = true;
    coop.IsSelected = true;
    // "Bu sunucuda" görünümü için iki mod yalnızca bellekte dünyaya işaretlenir (profil yazılmaz).
    foreach (var m in vm.VisibleManagedMods.Take(2)) { m.IsSelected = true; m.InWorld = true; }
    Pump(800);
    Shot("Mods-problems");
    vm.RevertModChangesCommand.Execute(null);
}
window.Height = 900;

// Kompakt görünüm (kullanıcının tercihi sonda geri verilir).
var compactWas = vm.CompactMods;
vm.CompactMods = true;
vm.CurrentPage = AppPage.Mods;
Shot("Mods-compact");
vm.CompactMods = compactWas;

// Mod yöneticisi sekmeleri (Mod bul ağdan arar: biraz beklenir).
vm.CurrentPage = AppPage.Mods;
vm.ModsTab = ModsTab.Browse;
for (var i = 0; i < 30 && vm.BrowseBusy; i++) Pump(1000);
Console.WriteLine($"browse: busy={vm.BrowseBusy} cards={vm.BrowseCards.Count} msg={vm.BrowseMessage}");
Pump(4000);
Shot("Mods-Browse");
vm.ModsTab = ModsTab.Packs;
// İlk paket seçilir ve "hangi dünyalara kuralım" sorusu açılır (yalnızca ekranda; bir şey yazılmaz).
if (vm.Packs.FirstOrDefault() is { } firstPack)
{
    vm.SelectedPack = firstPack;
    vm.OpenPackWorldPromptCommand.Execute(null);
}
window.Height = 1300;
Shot("Mods-Packs");
vm.ClosePackWorldPromptCommand.Execute(null);
window.Height = 900;
vm.ModsTab = ModsTab.Installed;

if (vm.OpenWizardCommand.CanExecute(null))
{
    vm.CurrentPage = AppPage.Home;
    vm.OpenWizardCommand.Execute(null);
    for (var i = 0; i <= MainViewModel.WizardLastStep; i++)
    {
        vm.WizardStep = i;
        Shot("Wizard" + i);
    }
}

vm.ToolsExpanded = toolsWere;
if (toggled) vm.ToggleLanguageCommand.Execute(null);
