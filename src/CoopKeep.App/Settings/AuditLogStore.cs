using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoopKeep.Core.Server;

namespace CoopKeep.App.Settings;

/// <summary>
/// Yönetici işlem kayıtlarını diskte saklar.
/// </summary>
/// <remarks>
/// Bu işlemler oyunun dengesini değiştiriyor (para, can). Uygulama kapanınca
/// silinen bir kaydın denetim değeri olmaz — birlikte oynayan insanlar arasında
/// "kim ne zaman ne yaptı" sorusunun cevabı kalıcı olmalı.
/// </remarks>
public static class AuditLogStore
{
    /// <summary>Saklanan en fazla kayıt sayısı.</summary>
    public const int MaxEntries = 200;

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep");

    private static string FilePath => Path.Combine(Directory, "admin-log.json");

    public static List<AdminAuditEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<AdminAuditEntry>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Bozuk kayıt dosyası uygulamayı açılmaz hâle getirmemeli.
        }

        return [];
    }

    public static void Save(IEnumerable<AdminAuditEntry> entries)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kaydedilemedi; işlevsel bir kayıp değil, kayıt bellekte duruyor.
        }
    }
}
