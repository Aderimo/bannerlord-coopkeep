using CoopKeep.Core.Configuration;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Saves;
using CoopKeep.Core.Server;

// CoopKeep CLI — Bannerlord Coop dedicated server'ını tamamen dışarıdan yönetir.
//
// Bu araç, oyuna hiç girmeden şunları yapar:
//   - yeni bir sandbox dünyası yaratmak
//   - kampanyaları listelemek (oyuncularıyla birlikte)
//   - aktif kampanyayı değiştirmek
//   - server'ı çalıştırmak ve konsolunu sürmek
//
// Mod'un kendi kurulum talimatındaki "önce oyunda tek oyunculu bir save yarat"
// adımı bu sayede gereksiz hâle geliyor.

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var rest = args.Skip(1).ToArray();

// Testler için: --data-dir ile izole bir dizin verilebilir.
var dataDirIndex = Array.FindIndex(args, a => a == "--data-dir");
var dataDir = dataDirIndex >= 0 && dataDirIndex + 1 < args.Length
    ? args[dataDirIndex + 1]
    : CoopInstallation.DefaultDataDirectory;

try
{
    return command switch
    {
        "doctor" => Doctor(dataDir),
        "saves" => ListSaves(dataDir),
        "config" => ShowConfig(dataDir),
        "use" => UseSave(dataDir, rest),
        "new" => await NewWorldAsync(dataDir, rest),
        "run" => await RunAsync(dataDir),
        _ => Help(),
    };
}
catch (Exception ex)
{
    Error(ex.Message);
    return 1;
}

// ----------------------------------------------------------------------

static int Help()
{
    Console.WriteLine("""
        CoopKeep — Bannerlord Coop Server Manager

        Kullanım:
          coopkeep doctor          kurulumu bul ve uyumluluğu denetle
          coopkeep saves           kampanyaları listele (oyuncularıyla)
          coopkeep config          aktif server ayarlarını göster
          coopkeep use <ad>        aktif kampanyayı değiştir
          coopkeep new <ad>        yeni sandbox dünyası yarat (oyuna girmeden)
          coopkeep run             server'ı başlat ve konsolu sür

        Seçenekler:
          --data-dir <yol>         server veri dizinini geçersiz kıl
        """);
    return 0;
}

static int Doctor(string dataDir)
{
    Head("Kurulum denetimi");

    var game = GameLocator.FindBannerlord();
    var coop = GameLocator.FindCoop();

    Field("Steam", GameLocator.FindSteamRoot() ?? "bulunamadı");
    Field("Bannerlord", game?.InstallDirectory ?? "bulunamadı");

    // package_info.txt'teki sürüm bayat; Modules\Native\SubModule.xml doğru kaynak.
    Field("Oyun sürümü", game?.GameVersion ?? "okunamadı");

    Field("Coop modu", coop?.ModRoot ?? "bulunamadı");
    Field("Mod sürümü", coop?.ModVersion ?? "okunamadı");
    Field("Gerektirdiği oyun sürümü", coop?.RequiredGameVersion ?? "okunamadı");
    Field("Workshop öğesi", coop?.WorkshopItemId ?? "-");
    Field("Server exe", coop?.DedicatedServerExe ?? "bulunamadı");
    Field("Veri dizini", dataDir);

    Console.WriteLine();

    if (game is null || coop is null)
    {
        Error("Kurulum eksik. Bannerlord ve Bannerlord Coop modunun kurulu olduğundan emin olun.");
        return 1;
    }

    if (game.GameVersion is { } gv && coop.RequiredGameVersion is { } rv)
    {
        if (string.Equals(gv, rv, StringComparison.OrdinalIgnoreCase))
            Ok($"Sürümler uyumlu ({gv}).");
        else
            Warn($"Sürüm uyuşmazlığı: oyun {gv}, mod {rv} istiyor. Oyuncular bağlanamayabilir.");
    }

    var configPath = System.IO.Path.Combine(dataDir, "server-config.json");
    if (File.Exists(configPath))
    {
        var cfg = new ServerConfigStore(configPath).Read();
        Ok($"Yapılandırma bulundu: kampanya '{cfg.SaveName}', port {cfg.Port}.");

        if (!cfg.HasExplicitPort)
            Warn("server-config.json 'port' anahtarını içermiyor (eski sürüm). Varsayılan 4200 kullanılıyor.");
    }
    else
    {
        Warn("server-config.json henüz yok — server ilk çalıştığında oluşturulacak.");
    }

    return 0;
}

static int ListSaves(string dataDir)
{
    var repo = new SaveRepository(System.IO.Path.Combine(dataDir, "Game Saves"));
    var campaigns = repo.DiscoverCampaigns();

    Head("Kampanyalar");

    if (campaigns.Count == 0)
    {
        Console.WriteLine("  (hiç kampanya yok — 'coopkeep new <ad>' ile bir tane yaratabilirsiniz)");
        return 0;
    }

    string? active = null;
    var configPath = System.IO.Path.Combine(dataDir, "server-config.json");
    if (File.Exists(configPath))
        active = new ServerConfigStore(configPath).Read().SaveName;

    Console.WriteLine($"  {"",-2}{"Kampanya",-28}{"Oyuncu",-8}{"Boyut",-10}Son değişiklik");
    Console.WriteLine("  " + new string('-', 74));

    foreach (var s in campaigns)
    {
        var mark = string.Equals(s.Name, active, StringComparison.OrdinalIgnoreCase) ? "●" : " ";
        var size = $"{s.SizeBytes / 1024.0 / 1024.0:0.0} MB";
        var when = s.LastModifiedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        var incomplete = s.IsComplete ? "" : "  ⚠ eş JSON eksik";

        Console.WriteLine($"  {mark} {s.Name,-28}{s.PlayerCount,-8}{size,-10}{when}{incomplete}");

        foreach (var p in s.Players)
            Console.WriteLine($"      └─ {p.ControllerId}  ({p.HeroId})");
    }

    Console.WriteLine();
    Console.WriteLine("  ● = server-config.json'da aktif olan kampanya");
    return 0;
}

static int ShowConfig(string dataDir)
{
    var path = System.IO.Path.Combine(dataDir, "server-config.json");
    if (!File.Exists(path))
    {
        Error($"Yapılandırma bulunamadı: {path}");
        return 1;
    }

    var cfg = new ServerConfigStore(path).Read();

    Head("Server ayarları");
    Field("Kampanya", cfg.SaveName);
    Field("Port (oyuncular)", cfg.Port.ToString() + (cfg.HasExplicitPort ? "" : "  (varsayılan — dosyada tanımlı değil)"));
    Field("Şifre", cfg.HasPassword ? "var" : "yok (herkes girebilir)");
    Field("Otomatik kayıt", cfg.AutosaveEnabled ? $"{cfg.AutosaveMinutes} dakikada bir" : "kapalı");
    Field("Steam keşfi", cfg.Steam ? "açık" : "kapalı (yalnızca doğrudan bağlantı)");
    Field("Dosya", path);
    return 0;
}

static int UseSave(string dataDir, string[] rest)
{
    if (rest.Length == 0 || rest[0].StartsWith("--"))
    {
        Error("Kullanım: coopkeep use <kampanya-adı>");
        return 1;
    }

    var name = rest[0];
    var repo = new SaveRepository(System.IO.Path.Combine(dataDir, "Game Saves"));

    if (!repo.Exists(name))
    {
        Error($"'{name}' adlı kampanya bulunamadı. 'coopkeep saves' ile listeleyebilirsiniz.");
        return 1;
    }

    var configPath = System.IO.Path.Combine(dataDir, "server-config.json");
    if (!File.Exists(configPath))
    {
        Error($"Yapılandırma bulunamadı: {configPath}");
        return 1;
    }

    new ServerConfigStore(configPath).SetSaveName(name);
    Ok($"Aktif kampanya '{name}' olarak ayarlandı. Etkili olması için server'ı yeniden başlatın.");
    return 0;
}

/// <summary>
/// Yeni bir sandbox dünyası yaratır — oyuna hiç girmeden.
/// </summary>
/// <remarks>
/// Server, yapılandırılan save diskte yoksa <c>default_new_game.sav</c>'dan taze bir
/// dünya kuruyor. Biz de tam bunu tetikliyoruz: adı ayarla, başlat, <c>serving</c>
/// fazını bekle, <c>stop</c> ile kaydettir.
/// </remarks>
static async Task<int> NewWorldAsync(string dataDir, string[] rest)
{
    if (rest.Length == 0 || rest[0].StartsWith("--"))
    {
        Error("Kullanım: coopkeep new <kampanya-adı>");
        return 1;
    }

    var name = rest[0];
    if (!SaveSet.IsValidSaveName(name))
    {
        Error($"Geçersiz kampanya adı: '{name}'");
        return 1;
    }

    var repo = new SaveRepository(System.IO.Path.Combine(dataDir, "Game Saves"));
    if (repo.Exists(name))
    {
        Error($"'{name}' adlı bir kampanya zaten var. Farklı bir ad seçin.");
        return 1;
    }

    var coop = GameLocator.FindCoop();
    if (coop is null) { Error("Coop kurulumu bulunamadı."); return 1; }

    Directory.CreateDirectory(dataDir);

    // Yapılandırmayı server açılmadan ÖNCE yazıyoruz. Aksi hâlde server kendi
    // varsayılanını ('saveauto1') yazar ve istenen adla dünya yaratılmaz.
    var configPath = System.IO.Path.Combine(dataDir, "server-config.json");
    new ServerConfigStore(configPath).EnsureSaveName(name);

    Head($"Yeni dünya yaratılıyor: {name}");
    Console.WriteLine("  Kampanya kurulumu yaklaşık bir dakika sürüyor...");
    Console.WriteLine();

    await using var supervisor = new ServerSupervisor();
    var serving = new TaskCompletionSource();

    supervisor.PhaseChanged += phase =>
    {
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {DescribePhase(phase)}");
        if (phase == ServerPhase.Serving) serving.TrySetResult();
    };

    supervisor.Exited += info =>
    {
        if (!serving.Task.IsCompleted)
            serving.TrySetException(new InvalidOperationException(
                $"Server hazır olmadan kapandı (kod {info.ExitCode}). {ServerExit.Describe(info.Kind)}"));
    };

    supervisor.Start(new ServerLaunchOptions
    {
        ExecutablePath = coop.DedicatedServerExe,
        DataDirectory = dataDir,
    });

    var timeout = Task.Delay(TimeSpan.FromMinutes(5));
    if (await Task.WhenAny(serving.Task, timeout) == timeout)
    {
        Error("Server 5 dakika içinde hazır olmadı.");
        supervisor.Kill();
        return 1;
    }

    await serving.Task; // varsa hatayı yükselt

    Console.WriteLine();
    Ok("Dünya hazır. Kaydedilip kapatılıyor...");
    await supervisor.StopAsync();

    var created = repo.Find(name);
    if (created is null)
    {
        Warn("Dünya oluştu ama beklenen adla bulunamadı. 'coopkeep saves' ile kontrol edin.");
        return 1;
    }

    Ok($"'{name}' yaratıldı ({created.SizeBytes / 1024.0 / 1024.0:0.0} MB).");
    return 0;
}

/// <summary>Server'ı başlatır, çıktısını gösterir ve klavyeden komut kabul eder.</summary>
static async Task<int> RunAsync(string dataDir)
{
    var coop = GameLocator.FindCoop();
    if (coop is null) { Error("Coop kurulumu bulunamadı."); return 1; }

    await using var supervisor = new ServerSupervisor();

    supervisor.LineReceived += line =>
    {
        // Açılıştaki "Cannot load: X.dll" satırları zararsız gürültü: hemen ardından
        // coop assembly resolver devreye giriyor ve DLL'ler doğru dizinden yükleniyor.
        if (IsHarmlessStartupNoise(line)) return;

        if (line.Contains(DsEventParser.Marker, StringComparison.Ordinal)) return; // ayrı gösteriliyor
        Console.WriteLine(line);
    };

    supervisor.PhaseChanged += phase => Ok(DescribePhase(phase));

    supervisor.PlayersChanged += players =>
    {
        if (players.Count == 0) { Console.WriteLine("  (oyuncu yok)"); return; }

        Console.WriteLine($"  Oyuncular ({players.Count}):");
        foreach (var p in players)
            Console.WriteLine($"    [{p.Id}] {p.Name} — {p.State}");
    };

    supervisor.Exited += info =>
        Console.WriteLine($"\n  Server kapandı (kod {info.ExitCode}). {ServerExit.Describe(info.Kind)}");

    supervisor.Start(new ServerLaunchOptions
    {
        ExecutablePath = coop.DedicatedServerExe,
        DataDirectory = dataDir,
    });

    Head("Server çalışıyor");
    Console.WriteLine("  Komutlar: players · save · say <mesaj> · kick <id|isim> · status · stop");
    Console.WriteLine();

    while (supervisor.IsRunning)
    {
        var input = Console.ReadLine();
        if (input is null) break;

        input = input.Trim();
        if (input.Length == 0) continue;

        if (input is "stop" or "exit" or "quit")
        {
            Console.WriteLine("  Kapatılıyor (dünya kaydediliyor, ~30 sn sürebilir)...");
            await supervisor.StopAsync();
            break;
        }

        try
        {
            await supervisor.SendCommandAsync(input);
        }
        catch (ArgumentException)
        {
            // Oyun konsol komutları kasıtlı olarak engelli: 594 tanesi arasında
            // altın verme, kahraman öldürme gibi hile komutları var.
            Error($"İzin verilmeyen komut: '{input}'. İzinli olanlar: players, save, say, kick, status, stop.");
        }
    }

    return 0;
}

// ----------------------------------------------------------------------

static bool IsHarmlessStartupNoise(string line) =>
    line.Contains("Cannot load:", StringComparison.Ordinal) ||
    line.Contains("Could not load file or assembly", StringComparison.Ordinal) ||
    line.Contains("Could not find the event index for", StringComparison.Ordinal) ||
    line.Contains("Unable to find item to add dependency", StringComparison.Ordinal);

static string DescribePhase(ServerPhase phase) => phase switch
{
    ServerPhase.Boot => "Motor başlatılıyor...",
    ServerPhase.Loading => "Kampanya yükleniyor (~30-60 sn)...",
    ServerPhase.Serving => "HAZIR — server ayakta, oyuncular bağlanabilir.",
    ServerPhase.Stopping => "Kapatılıyor, dünya kaydediliyor...",
    ServerPhase.Shutdown => "Kapandı.",
    ServerPhase.Starting => "Başlatılıyor...",
    _ => "Durum bilinmiyor.",
};

static void Head(string text)
{
    Console.WriteLine();
    Console.WriteLine("  " + text);
    Console.WriteLine("  " + new string('=', text.Length));
    Console.WriteLine();
}

static void Field(string label, string value) => Console.WriteLine($"  {label,-28}{value}");

static void Ok(string text)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  ✓ " + text);
    Console.ForegroundColor = prev;
}

static void Warn(string text)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  ! " + text);
    Console.ForegroundColor = prev;
}

static void Error(string text)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("  ✗ " + text);
    Console.ForegroundColor = prev;
}
