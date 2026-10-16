using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoopKeep.App.Settings;

/// <summary>
/// Engellenen oyuncu adlarının kalıcı listesi.
/// </summary>
public static class BlockListStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "blocklist.json");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Bozuk dosya uygulamayı açılmaz hâle getirmemeli.
        }

        return [];
    }

    public static void Save(IEnumerable<string> names)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(names.ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kaydedilemedi; liste bu oturumda yine de çalışır.
        }
    }
}
