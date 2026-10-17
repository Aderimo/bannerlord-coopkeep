using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoopKeep.App.ViewModels;

/// <summary>Bir oyuncunun bu sunucudaki geçmişi.</summary>
public sealed class PlayerStat
{
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public double TotalSeconds { get; set; }
}

/// <summary>
/// Oyuncuların son görülme zamanı ve toplam oynama süresi.
/// </summary>
/// <remarks>
/// Yalnızca bu bilgisayarda, CoopKeep açıkken görülen oturumlar sayılır
/// (<c>%APPDATA%\CoopKeep\player-stats.json</c>). Ad bazlı tutulur; SteamID yazılmaz.
/// </remarks>
public partial class MainViewModel
{
    private readonly Dictionary<string, PlayerStat> _playerStats = LoadPlayerStats();
    private readonly Dictionary<string, DateTimeOffset> _sessionStarts = new(StringComparer.OrdinalIgnoreCase);

    private static string PlayerStatsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "player-stats.json");

    private void PlayerSeen(string name)
    {
        var now = DateTimeOffset.Now;
        _sessionStarts[name] = now;
        if (!_playerStats.TryGetValue(name, out var stat))
            _playerStats[name] = stat = new PlayerStat { FirstSeen = now };
        stat.LastSeen = now;
        SavePlayerStats();
        UpdatePlayerStatTexts();
    }

    private void PlayerLeft(string name)
    {
        if (!_sessionStarts.Remove(name, out var start)) return;
        if (!_playerStats.TryGetValue(name, out var stat)) _playerStats[name] = stat = new PlayerStat { FirstSeen = start };

        stat.LastSeen = DateTimeOffset.Now;
        stat.TotalSeconds += (stat.LastSeen - start).TotalSeconds;
        SavePlayerStats();
        UpdatePlayerStatTexts();
    }

    /// <summary>"Şu an oyunda · toplam 3 sa 20 dk" / "2 gün önce · toplam 45 dk".</summary>
    public string DescribePlayerStat(string name)
    {
        var online = _sessionStarts.TryGetValue(name, out var start);
        _playerStats.TryGetValue(name, out var stat);
        if (stat is null && !online) return L["players.neverSeen"];

        var total = TimeSpan.FromSeconds((stat?.TotalSeconds ?? 0) + (online ? (DateTimeOffset.Now - start).TotalSeconds : 0));
        var totalText = total.TotalHours >= 1
            ? string.Format(L["time.hm"], (int)total.TotalHours, total.Minutes)
            : string.Format(L["time.m"], Math.Max(1, (int)total.TotalMinutes));

        var seen = online ? L["players.onlineNow"] : Ago(stat!.LastSeen);
        return string.Format(L["players.statLine"], seen, totalText);
    }

    private string Ago(DateTimeOffset at)
    {
        var d = DateTimeOffset.Now - at;
        if (d.TotalMinutes < 2) return L["time.justNow"];
        if (d.TotalHours < 1) return string.Format(L["time.minAgo"], (int)d.TotalMinutes);
        if (d.TotalDays < 1) return string.Format(L["time.hourAgo"], (int)d.TotalHours);
        return string.Format(L["time.dayAgo"], (int)d.TotalDays);
    }

    private void UpdatePlayerStatTexts()
    {
        foreach (var h in Heroes) h.StatText = DescribePlayerStat(h.Name);
    }

    private static Dictionary<string, PlayerStat> LoadPlayerStats()
    {
        try
        {
            if (File.Exists(PlayerStatsPath))
                return new Dictionary<string, PlayerStat>(
                    JsonSerializer.Deserialize<Dictionary<string, PlayerStat>>(File.ReadAllText(PlayerStatsPath)) ?? [],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        return new Dictionary<string, PlayerStat>(StringComparer.OrdinalIgnoreCase);
    }

    private void SavePlayerStats()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerStatsPath)!);
            File.WriteAllText(PlayerStatsPath, JsonSerializer.Serialize(_playerStats.ToDictionary(k => k.Key, k => k.Value)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
