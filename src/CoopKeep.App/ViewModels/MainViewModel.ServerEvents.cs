using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoopKeep.App.Localization;
using CoopKeep.Core.Configuration;
using CoopKeep.Core.Mods;
using CoopKeep.Core.Installation;
using CoopKeep.Core.Protocol;
using CoopKeep.Core.Saves;
using CoopKeep.Core.Server;

namespace CoopKeep.App.ViewModels;

/// <summary>Sunucu sürecinden gelen olaylar: durum, konsol satırları, çökme.</summary>
public partial class MainViewModel
{
    // --- iç işleyiş ------------------------------------------------------

    private void WireSupervisor()
    {
        // Satırlar kuyruğa girer, StartConsoleFlush'taki zamanlayıcı topluca işler.
        _supervisor.LineReceived += _pendingLines.Enqueue;

        _supervisor.PhaseChanged += phase => Dispatcher.UIThread.Post(() =>
        {
            FlushPendingLines();
            Phase = phase;
            if (phase == ServerPhase.Serving)
            {
                BusyMessage = "";
                if (!IsCreatingWorld) ShowToast(L["msg.online"]);

                // "Başlat ve oyna": oyun açıldıysa adres panoya; açılamadıysa şimdi açılır.
                if (_joinWhenServing && !IsCreatingWorld)
                {
                    _joinWhenServing = false;
                    _ = JoinOwnServerAsync();
                }
                // Yayına geçer geçmez sunucuya iş yığılmasın: kahramanlar diskteki save'den,
                // sunucu sorguları sayfa açılınca. Eklenti yalnızca yoklanır.
                ReloadHeroes();
                if (CurrentPage is AppPage.Players or AppPage.Cheats) _ = RefreshHeroesAsync();
                _ = DetectServerToolsAsync();
            }

            RaiseRunStateChanged();
        });

        _supervisor.PlayersChanged += players => Dispatcher.UIThread.Post(() =>
        {
            var joined = players.Where(p => _allPlayers.All(o => o.Id != p.Id)).ToList();

            var left = _allPlayers.Where(o => players.All(p => p.Id != o.Id)).ToList();

            _allPlayers.Clear();
            _allPlayers.AddRange(players);
            ApplyPlayerFilter();
            UpdateHeroFlags();
            RaiseHomeChanged();

            foreach (var p in left) PlayerLeft(p.Name);

            foreach (var p in joined) OnPlayerJoined(p);
        });

        _supervisor.Exited += info => Dispatcher.UIThread.Post(async () =>
        {
            FlushPendingLines();
            AppendConsole("— " + DescribeExit(info.Kind));

            Phase = ServerPhase.Unknown;
            BusyMessage = "";
            SteamHostName = "";

            foreach (var p in _allPlayers) PlayerLeft(p.Name);
            _allPlayers.Clear();
            ApplyPlayerFilter();
            UpdateHeroFlags();

            RaiseRunStateChanged();
            ReloadCampaigns();
            ReloadBackups();

            // Sözleşme değişmiş ya da onay silinmiş: yeniden sor.
            if (info.Kind == ServerExitKind.EulaRequired)
            {
                _ui.AcceptedEulaFingerprint = null;
                _ui.Save();
                await RequireEulaAsync(StartAsync);
                return;
            }

            // Geri sayımlı yeniden başlatma çöküşle bittiyse bayrak takılı kalmasın.
            var restartRequested = _restartAfterExit;
            _restartAfterExit = false;

            if (restartRequested && !info.IsCrash)
            {
                await StartAsync();
                return;
            }

            // Coop'un çıkış kodları: ne olduğunu ve ne yapılacağını söyle.
            ExplainExit(info);

            await HandleCrashAsync(info);
        });
    }

    private bool IsCreatingWorld => WizardCreating;

    /// <summary>
    /// Açılışta basılan "Cannot load: X.dll" satırları zararsızdır: hemen ardından
    /// coop assembly resolver devreye giriyor ve DLL'ler doğru dizinden yükleniyor.
    /// Kullanıcıya hata gibi göstermek her açılışta boş yere panik yaratırdı.
    /// </summary>
    private static bool IsHarmlessStartupNoise(string line) =>
        line.Contains("Cannot load:", StringComparison.Ordinal) ||
        line.Contains("Could not load file or assembly", StringComparison.Ordinal) ||
        line.Contains("Could not find the event index for", StringComparison.Ordinal) ||
        line.Contains("Unable to find item to add dependency", StringComparison.Ordinal);

    /// <summary>Sunucudan gelen bir satırı işler (UI thread'inde, sırayla).</summary>
    private void HandleServerLine(string line)
    {
        TryCaptureSteamHostName(line);
        TryAutoBackup(line);

        if (IsHarmlessStartupNoise(line)) return;
        if (line.Contains(DsEventParser.Marker, StringComparison.Ordinal)) return;
        if (IsToolsDataLine(line)) return;
        AppendConsole(line);
    }

    private void AppendConsole(string line)
    {
        Console.Add(line);
        OnConsoleLineAdded(line);
    }

    /// <summary>Çöküş sonrası politikayı uygular: gerekiyorsa bekleyip yeniden başlatır.</summary>
    private async Task HandleCrashAsync(ServerExitInfo info)
    {
        _crashPolicy.Enabled = AutoRestart;

        var decision = _crashPolicy.Evaluate(info, DateTimeOffset.UtcNow);

        switch (decision.Reason)
        {
            case CrashDecisionReason.NotACrash:
                return;

            case CrashDecisionReason.NotRecoverable:
                AlertText = L["msg.noAutoRestart"] + " " + DescribeExit(info.Kind);
                return;

            case CrashDecisionReason.CrashLoop:
                AlertText = L["msg.crashLoop"];
                return;

            case CrashDecisionReason.Disabled:
                AlertText = L["msg.crashed"];
                return;
        }

        if (!decision.ShouldRestart || _coop is null) return;

        AlertText = L["msg.crashed"] + " " + L["msg.restartingIn"];
        await Task.Delay(decision.Delay);

        // Kullanıcı bu arada elle başlattıysa araya girme.
        if (IsRunning) return;

        // Çöken dünya yeniden açılır; kullanıcı bu arada listede başka dünya seçmiş olabilir.
        var saveName = _lastLaunchedSave ?? SelectedCampaign?.Name;
        if (saveName is null) return;

        try
        {
            PrepareAndLaunch(saveName);
            BusyMessage = L["msg.readyIn"];
        }
        catch (Exception ex)
        {
            AppendConsole("! " + ex.Message);
            BusyMessage = "";
        }
    }

    /// <summary>
    /// Sunucunun Steam listesinde göründüğü adı log satırından yakalar.
    /// </summary>
    /// <remarks>
    /// Kaynak satır:
    /// <c>[DedicatedServer] Steam user session initialized (appid 261550, persona 'ad')</c>
    /// </remarks>
    private void TryCaptureSteamHostName(string line)
    {
        const string marker = "persona '";

        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return;

        var start = at + marker.Length;
        var end = line.IndexOf('\'', start);
        if (end > start) SteamHostName = line[start..end];
    }
}
