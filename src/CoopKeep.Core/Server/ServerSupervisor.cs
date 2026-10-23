using System.Diagnostics;
using CoopKeep.Core.Protocol;

namespace CoopKeep.Core.Server;

/// <summary>
/// <c>BannerlordCoopServer.exe</c> process'ini dışarıdan yönetir: başlatır, çıktısını
/// yorumlar, komut gönderir, güvenle durdurur.
/// </summary>
/// <remarks>
/// <para>
/// Coop modülünün DLL'leri SHA-256 ile pinlendiği için mod'a kod ekleyemiyoruz
/// (değiştirilmiş modül → server exit code 4 ile açılmayı reddediyor). Bu sınıf,
/// elimizdeki üç kanalı kullanan tamamen out-of-process yaklaşımın merkezi:
/// process yaşam döngüsü, stdin komutları ve stdout'taki <c>@DS@</c> olay akışı.
/// </para>
/// <para>
/// <b>Neden stdout, log dosyası değil?</b> Genel Bannerlord dedicated server'ları kendi
/// konsol buffer'ına yazar ve redirect edilemez; bu server farklı — launcher çocuk
/// motorun çıktısını kendi stdout'una aktarıyor ve <c>--no-tui</c> stdio redirect
/// edilince kendiliğinden devreye giriyor. 2026-09-01'de ölçüldü: 3811 satır eksiksiz
/// yakalandı.
/// </para>
/// </remarks>
public sealed class ServerSupervisor : IAsyncDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    // Exited olayı thread havuzundan gelir; okuma ve yazma farklı thread'lerde.
    private volatile bool _stopRequested;

    /// <summary>
    /// Modlu (doğrudan motor) açılışta çıktının yazıldığı günlük. Normalde bunu sarmalayıcı
    /// yazıyor (<c>logs\coop-server-*.log</c>); onun yerine biz yazarız ki hata tespiti ve
    /// hata raporu aynı dosyayı bulsun.
    /// </summary>
    private StreamWriter? _directLog;
    private readonly object _logGate = new();

    /// <summary>Son açılış modlu muydu (motor doğrudan mı başlatıldı)?</summary>
    public bool LastStartWasDirect { get; private set; }

    /// <summary>Ham çıktı satırı (hem <c>@DS@</c> hem normal loglar). Konsol görünümü bunu tüketir.</summary>
    public event Action<string>? LineReceived;

    /// <summary>Ayrıştırılmış protokol olayı.</summary>
    public event Action<DsEvent>? EventReceived;

    public event Action<ServerPhase>? PhaseChanged;

    public event Action<IReadOnlyList<ConnectedPlayer>>? PlayersChanged;

    /// <summary>Process sonlandı. <c>Expected</c>, <c>stop</c> gönderdiğimiz için beklenen çıkışsa true.</summary>
    public event Action<ServerExitInfo>? Exited;

    public ServerPhase Phase { get; private set; } = ServerPhase.Unknown;

    public IReadOnlyList<ConnectedPlayer> Players { get; private set; } = Array.Empty<ConnectedPlayer>();

    public string? ActiveSaveName { get; private set; }

    public bool PasswordRequired { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public int? ProcessId => _process?.Id;

    public bool IsRunning
    {
        get
        {
            var p = _process;
            return p is { HasExited: false };
        }
    }

    /// <summary>Server ayakta ve oyuncu kabul ediyor mu?</summary>
    public bool IsServing => IsRunning && Phase == ServerPhase.Serving;

    /// <summary>
    /// Server'ı başlatır.
    /// </summary>
    public void Start(ServerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        lock (_gate)
        {
            if (IsRunning)
                throw new InvalidOperationException("Server zaten çalışıyor.");

            _stopRequested = false;
            Phase = ServerPhase.Unknown;
            Players = Array.Empty<ConnectedPlayer>();
            ActiveSaveName = null;

            if (options.IsDirect) options.PrepareDirectDataDirectory();
            LastStartWasDirect = options.IsDirect;

            var psi = new ProcessStartInfo
            {
                FileName = options.FileName,
                WorkingDirectory = options.ProcessWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var arg in options.BuildArguments())
                psi.ArgumentList.Add(arg);
            foreach (var (key, value) in options.BuildEnvironment())
                psi.Environment[key] = value;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;

            if (!process.Start())
                throw new InvalidOperationException("Server process'i başlatılamadı.");

            // Önceki (kapanmış) process'in tutamaçları serbest kalsın.
            _process?.Dispose();
            _process = process;
            StartedAt = DateTimeOffset.UtcNow;
            OpenDirectLog(options);

            // Akışları ayrı görevlerde okuyoruz. stderr'i de mutlaka boşaltmak gerekiyor:
            // okunmayan bir pipe dolduğunda yazan taraf bloklanır ve server donar.
            _stdoutPump = Task.Run(() => PumpAsync(process.StandardOutput));
            _stderrPump = Task.Run(() => PumpAsync(process.StandardError));
        }
    }

    /// <summary>
    /// Allow-list'ten geçmiş bir komutu stdin'e yazar.
    /// </summary>
    /// <exception cref="ArgumentException">Komut allow-list'te değilse veya satır sonu içeriyorsa.</exception>
    public async Task SendCommandAsync(string commandLine, CancellationToken cancellationToken = default)
    {
        // İki allow-list: yerleşik sunucu komutları ve açıkça izin verilen
        // yönetici komutları. Geri kalan 590+ oyun konsol komutu erişilemez.
        if (!ServerCommand.IsAllowed(commandLine) && !AdminCommand.IsAllowed(commandLine))
            throw new ArgumentException($"İzin verilmeyen komut: '{commandLine}'", nameof(commandLine));

        await WriteLineAsync(commandLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sunucu sahibinin konsol kutusuna kendi yazdığı satırı gönderir.
    /// </summary>
    /// <remarks>
    /// Allow-list'e takılmaz: sunucu sahibi zaten kendi sunucusunun konsolunu kullanıyor
    /// (oyuncular hata çözmek için konsol istedi). Yine de tek satır olmalı; gömülü satır
    /// sonu bir komutu ikiye bölerdi. Arayüzün kendi düğmeleri bunu kullanmaz.
    /// </remarks>
    /// <exception cref="ArgumentException">Satır boşsa, çok uzunsa veya satır sonu içeriyorsa.</exception>
    public Task SendConsoleLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (!ServerCommand.IsValidConsoleLine(line))
            throw new ArgumentException("Geçersiz konsol satırı.", nameof(line));

        return WriteLineAsync(line.Trim(), cancellationToken);
    }

    private async Task WriteLineAsync(string commandLine, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
            throw new InvalidOperationException("Server çalışmıyor.");

        await process.StandardInput.WriteLineAsync(commandLine.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Komutu gönderir ve sunucunun yanıt satırlarını toplar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Konsol yanıtları ayrı bir kanal değil, normal log akışının içinde geliyor. Sunucu
    /// önce komutu <c>[DedicatedServer] &gt; komut</c> diye yankılıyor; toplama o satırdan
    /// sonra başlar. Yanıt, <paramref name="isTerminal"/> eşleşince ya da satırlar bir
    /// süre kesilince biter.
    /// </para>
    /// <para>
    /// Arada başka log satırları da gelebilir; çağıran yalnızca tanıdığı satırları
    /// yorumlamalı (bkz. <see cref="AdminResponseParser"/>).
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> SendAndCollectAsync(
        string commandLine,
        Func<string, bool>? isTerminal = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        var gate = new object();
        var echoSeen = false;
        var lastLineAt = DateTimeOffset.UtcNow;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var echo = "> " + commandLine;

        void OnLine(string line)
        {
            lock (gate)
            {
                if (!echoSeen)
                {
                    if (line.EndsWith(echo, StringComparison.Ordinal)) echoSeen = true;
                    return;
                }

                // Sonraki komutun yankısı: bu komutun yanıtı bitti.
                if (line.StartsWith("[DedicatedServer] > ", StringComparison.Ordinal))
                {
                    done.TrySetResult();
                    return;
                }

                lines.Add(line);
                lastLineAt = DateTimeOffset.UtcNow;
                if (isTerminal?.Invoke(line) == true) done.TrySetResult();
            }
        }

        LineReceived += OnLine;
        try
        {
            await SendCommandAsync(commandLine, cancellationToken).ConfigureAwait(false);

            var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(4));
            while (!done.Task.IsCompleted && DateTimeOffset.UtcNow < deadline)
            {
                await Task.WhenAny(done.Task, Task.Delay(100, cancellationToken)).ConfigureAwait(false);

                lock (gate)
                {
                    // Yanıt geldiyse ve 500 ms yeni satır yoksa bitmiş say.
                    if (lines.Count > 0 && DateTimeOffset.UtcNow - lastLineAt > TimeSpan.FromMilliseconds(500))
                        break;
                }
            }
        }
        finally
        {
            LineReceived -= OnLine;
        }

        lock (gate) return lines.ToList();
    }

    /// <summary>
    /// Server'ı güvenle durdurur: <c>stop</c> komutu gönderir, dünyanın kaydedilmesini bekler,
    /// süre aşılırsa process'i sonlandırır.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Faz 0'da ölçülen tipik süre 2 saniye, ama server'ın kendi uyarısı kapanış save'inin
    /// ~30 saniyeye kadar sürebileceğini söylüyor; varsayılan süre bunu fazlasıyla kapsıyor.
    /// </para>
    /// <para>
    /// Force-kill son çaredir: SIGKILL ile kapatmak save bozulmasına yol açabiliyor
    /// (upstream #2593) ve Steam lobisi geride hayalet olarak kalıyor.
    /// </para>
    /// </remarks>
    public async Task<bool> StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || process.HasExited) return true;

        _stopRequested = true;

        try
        {
            await SendCommandAsync(ServerCommand.Stop(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // stdin kapanmışsa doğrudan sonlandırmaya geç.
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            Kill();
            return false;
        }
    }

    /// <summary>
    /// Process'i zorla sonlandırır — yalnızca graceful durdurma başarısız olduğunda.
    /// </summary>
    /// <remarks>
    /// <c>entireProcessTree: true</c> önemli: Faz 0'da ölçüldü ki launcher'ı tek başına
    /// öldürmek geride yetim bir <c>Watchdog.exe</c> bırakıyor ve sonraki başlatma
    /// portu bağlayamıyor.
    /// </remarks>
    public void Kill()
    {
        var process = _process;
        if (process is null || process.HasExited) return;

        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* zaten çıkmış */ }
    }

    // ------------------------------------------------------------------

    private async Task PumpAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                HandleLine(line);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Process kapandı; akışın sonu.
        }
    }

    private void HandleLine(string line)
    {
        WriteDirectLog(line);
        LineReceived?.Invoke(line);

        if (!DsEventParser.TryParse(line, out var evt) || evt is null)
            return;

        EventReceived?.Invoke(evt);

        switch (evt)
        {
            case DsEvent.State state:
                ActiveSaveName = state.SaveName ?? ActiveSaveName;
                PasswordRequired = state.PasswordRequired;

                if (state.Phase != Phase)
                {
                    Phase = state.Phase;
                    PhaseChanged?.Invoke(state.Phase);
                }
                break;

            case DsEvent.Players players:
                Players = players.List;
                PlayersChanged?.Invoke(players.List);
                break;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var process = (Process)sender!;
        var code = process.ExitCode;

        // Exited, akışlar boşalmadan tetiklenebiliyor; çöküşü anlatan son satırlar
        // konsola düşmeden "kapandı" denmesin diye pompaları kısa süre bekle.
        WaitForPumps(TimeSpan.FromSeconds(2));

        WriteDirectLog($"[CoopKeep] engine exited with code {code}");
        CloseDirectLog();

        Exited?.Invoke(new ServerExitInfo(
            ExitCode: code,
            Kind: ServerExit.Classify(code),
            WasExpected: _stopRequested));
    }

    private void WaitForPumps(TimeSpan timeout)
    {
        var pumps = new[] { _stdoutPump, _stderrPump }.OfType<Task>().ToArray();
        if (pumps.Length == 0) return;
        try { Task.WaitAll(pumps, timeout); }
        catch (AggregateException) { /* pompalar kendi hatalarını yutuyor */ }
    }

    // ------------------------------------------------------------------
    // Modlu açılışın günlük dosyası
    // ------------------------------------------------------------------

    /// <summary>Sarmalayıcı gibi en yeni 10 günlüğü tutar.</summary>
    private const int KeepLogs = 10;

    private void OpenDirectLog(ServerLaunchOptions options)
    {
        CloseDirectLog();
        if (!options.IsDirect) return;

        try
        {
            var dir = Path.Combine(options.EffectiveDataDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"coop-server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            lock (_logGate) _directLog = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true };

            WriteDirectLog("[CoopKeep] modded launch: " + ServerLaunchOptions.ModuleArgument(options.EngineModules!));

            foreach (var old in new DirectoryInfo(dir).GetFiles("coop-server-*.log")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepLogs))
            {
                try { old.Delete(); } catch (IOException) { /* açık olabilir; sonra silinir */ }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Günlük yazılamıyorsa sunucu yine çalışır; yalnızca dosya olmaz.
        }
    }

    private void WriteDirectLog(string line)
    {
        lock (_logGate)
        {
            try { _directLog?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {line}"); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { _directLog = null; }
        }
    }

    private void CloseDirectLog()
    {
        lock (_logGate)
        {
            try { _directLog?.Dispose(); } catch (IOException) { }
            _directLog = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Büyük dünyalarda kapanıştaki kayıt uzun sürebiliyor; erken zorla kapatmak kaydı bozar.
            if (IsRunning)
                await StopAsync(TimeSpan.FromSeconds(120)).ConfigureAwait(false);
        }
        catch
        {
            // Dispose sırasında yutulur; aşağıda zorla temizleniyor.
        }

        Kill();

        if (_stdoutPump is not null) await SafeAwait(_stdoutPump).ConfigureAwait(false);
        if (_stderrPump is not null) await SafeAwait(_stderrPump).ConfigureAwait(false);

        _process?.Dispose();
        _process = null;

        static async Task SafeAwait(Task t)
        {
            try { await t.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { /* yok say */ }
        }
    }
}

/// <param name="WasExpected">
/// <c>stop</c> komutunu biz gönderdiysek true. Crash tespitinin yanlış alarm vermemesi
/// için gerekli — beklenen bir kapanış crash sayılmamalı.
/// </param>
public readonly record struct ServerExitInfo(int ExitCode, ServerExitKind Kind, bool WasExpected)
{
    /// <summary>Bu çıkış bir çökme mi?</summary>
    public bool IsCrash => !WasExpected && Kind != ServerExitKind.Clean;
}
