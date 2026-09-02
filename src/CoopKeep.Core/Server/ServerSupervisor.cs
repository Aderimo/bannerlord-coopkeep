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
    private bool _stopRequested;

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

            var psi = new ProcessStartInfo
            {
                FileName = options.ExecutablePath,
                WorkingDirectory = options.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var arg in options.BuildArguments())
                psi.ArgumentList.Add(arg);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;

            if (!process.Start())
                throw new InvalidOperationException("Server process'i başlatılamadı.");

            _process = process;
            StartedAt = DateTimeOffset.UtcNow;

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

        var process = _process;
        if (process is null || process.HasExited)
            throw new InvalidOperationException("Server çalışmıyor.");

        await process.StandardInput.WriteLineAsync(commandLine.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
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

        Exited?.Invoke(new ServerExitInfo(
            ExitCode: code,
            Kind: ServerExit.Classify(code),
            WasExpected: _stopRequested));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsRunning)
                await StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
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
