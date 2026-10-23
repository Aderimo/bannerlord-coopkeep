using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CoopKeep.Core.Protocol;

namespace CoopKeep.App.ViewModels;

/// <summary>Bu oturumda görülen tanıdık bir konsol satırı türü.</summary>
public sealed partial class SeenLogLine(KnownLogLine known, string title, string explanation) : ObservableObject
{
    public KnownLogLine Known { get; } = known;
    public string Title { get; } = title;
    public string Explanation { get; } = explanation;

    [ObservableProperty] private int _count = 1;

    public bool IsError => Known.Severity == LogSeverity.Error;
    public bool IsWarning => Known.Severity == LogSeverity.Warning;
    public bool IsHarmless => Known.Severity is LogSeverity.Harmless or LogSeverity.Info;
}

/// <summary>
/// Konsol: arama, "yalnızca sorunlar" süzgeci ve bilinen satırların açıklamaları.
/// </summary>
/// <remarks>
/// Konsol binlerce teknik satır basıyor. Kullanıcı neyin sorun olduğunu bilemediği için
/// tanıdık satırlar (bkz. <see cref="KnownLogLines"/>) üstte açıklamalarıyla toplanır.
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Süzgeçten geçen konsol satırları (görünüm bunu gösterir).</summary>
    public ObservableCollection<string> ConsoleView { get; } = [];

    public ObservableCollection<SeenLogLine> SeenLogLines { get; } = [];

    [ObservableProperty] private string _consoleSearch = "";
    [ObservableProperty] private bool _consoleProblemsOnly;

    public bool HasSeenLogLines => SeenLogLines.Count > 0;

    private bool PassesConsoleFilter(string line)
    {
        if (ConsoleProblemsOnly && !KnownLogLines.IsProblem(line)) return false;

        var q = ConsoleSearch.Trim();
        return q.Length == 0 || line.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Yeni satırı süzgece göre görünüme ekler ve tanıdıksa özetler.</summary>
    private void OnConsoleLineAdded(string line)
    {
        if (PassesConsoleFilter(line)) ConsoleView.Add(line);

        if (KnownLogLines.Match(line) is not { } known) return;

        var seen = SeenLogLines.FirstOrDefault(s => s.Known.Key == known.Key);
        if (seen is not null) { seen.Count++; return; }

        SeenLogLines.Insert(0, new SeenLogLine(known, L["log." + known.Key], L["log." + known.Key + ".desc"]));
        OnPropertyChanged(nameof(HasSeenLogLines));
    }

    private void RebuildConsoleView()
    {
        ConsoleView.Clear();
        foreach (var line in Console.Where(PassesConsoleFilter)) ConsoleView.Add(line);
    }

    private void ClearConsoleSummary()
    {
        SeenLogLines.Clear();
        ConsoleView.Clear();
        OnPropertyChanged(nameof(HasSeenLogLines));
    }

    // --- elle komut ----------------------------------------------------

    /// <summary>Konsol kutusuna yazılan komut.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendConsoleInputCommand))]
    private string _consoleInput = "";

    private readonly System.Collections.Generic.List<string> _consoleHistory = [];

    /// <summary>
    /// Yazılan satırı sunucuya gönderir. Oyuncular takılan oyunları kurtarmak için konsol
    /// istedi; sunucunun kendi komut listesi <c>help</c> ile görülür.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanSendConsoleInput))]
    private async System.Threading.Tasks.Task SendConsoleInputAsync()
    {
        var line = ConsoleInput.Trim();
        if (!Core.Server.ServerCommand.IsValidConsoleLine(line)) return;

        AppendConsole("> " + line);
        _consoleHistory.Remove(line);
        _consoleHistory.Add(line);
        ConsoleInput = "";

        try { await _supervisor.SendConsoleLineAsync(line); }
        catch (Exception ex) { AppendConsole("! " + ex.Message); }
    }

    private bool CanSendConsoleInput() => IsRunning && !string.IsNullOrWhiteSpace(ConsoleInput);

    /// <summary>Yukarı ok: önceki komutu geri getirir.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PreviousConsoleInput()
    {
        if (_consoleHistory.Count == 0) return;
        var i = _consoleHistory.IndexOf(ConsoleInput);
        ConsoleInput = _consoleHistory[i <= 0 ? _consoleHistory.Count - 1 : i - 1];
    }

    // --- toplu ekleme ----------------------------------------------------

    /// <summary>
    /// Sunucu açılışta saniyede yüzlerce satır basıyor. Her satır için ayrı UI işi
    /// arayüzü takıltıyordu; satırlar kuyruğa alınıp kısa aralıklarla topluca işlenir.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingLines = new();

    private Avalonia.Threading.DispatcherTimer? _consoleFlushTimer;

    private void StartConsoleFlush()
    {
        _consoleFlushTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _consoleFlushTimer.Tick += (_, _) => FlushPendingLines();
        _consoleFlushTimer.Start();
    }

    /// <summary>Kuyruktaki satırları sırayla işler. Olaylardan önce çağrılır ki sıra bozulmasın.</summary>
    private void FlushPendingLines()
    {
        while (_pendingLines.TryDequeue(out var line)) HandleServerLine(line);
        TrimConsole();
    }

    /// <summary>
    /// Sınırı biraz aşınca topluca kırpar: satır başına <c>RemoveAt(0)</c> her seferinde
    /// tüm listeyi kaydırıyordu.
    /// </summary>
    private void TrimConsole()
    {
        const int slack = 200;
        TrimFront(Console, MaxConsoleLines, slack);
        TrimFront(ConsoleView, MaxConsoleLines, slack);

        static void TrimFront(ObservableCollection<string> list, int max, int slack)
        {
            if (list.Count <= max + slack) return;
            var keep = list.Skip(list.Count - max).ToList();
            list.Clear();
            foreach (var l in keep) list.Add(l);
        }
    }

    partial void OnConsoleSearchChanged(string value) => RebuildConsoleView();
    partial void OnConsoleProblemsOnlyChanged(bool value) => RebuildConsoleView();
}
