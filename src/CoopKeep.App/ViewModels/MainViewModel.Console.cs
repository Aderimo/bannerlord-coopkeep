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
        if (PassesConsoleFilter(line))
        {
            ConsoleView.Add(line);
            while (ConsoleView.Count > MaxConsoleLines) ConsoleView.RemoveAt(0);
        }

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

    partial void OnConsoleSearchChanged(string value) => RebuildConsoleView();
    partial void OnConsoleProblemsOnlyChanged(bool value) => RebuildConsoleView();
}
