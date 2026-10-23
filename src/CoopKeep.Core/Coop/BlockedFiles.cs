namespace CoopKeep.Core.Coop;

/// <summary>
/// Windows'un "internetten indirildi" işareti (<c>Zone.Identifier</c>) taşıyan DLL ve EXE'ler.
/// </summary>
/// <remarks>
/// Zip'ten elle kurulan modların ve sunucu dosyalarının DLL'leri bu işaretle engellenince oyun ya
/// da sunucu onları yükleyemiyor ve hata mesajı sebebi söylemiyor. İşaret, dosyanın yanındaki
/// gizli bir akışta (alternate data stream) duruyor; silinince dosya açılır ("Engellemeyi kaldır").
/// </remarks>
public static class BlockedFiles
{
    private const string ZoneStream = ":Zone.Identifier";
    private static readonly string[] Patterns = ["*.dll", "*.exe"];

    /// <summary>Klasörlerdeki engellenmiş DLL/EXE'ler.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<string> folders)
    {
        var found = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pattern in Patterns)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

                foreach (var file in files)
                    if (IsBlocked(file)) found.Add(file);
            }
        }

        return found;
    }

    public static bool IsBlocked(string file)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return File.Exists(file + ZoneStream); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>İşareti kaldırır; kaldırılabilenlerin sayısını döner.</summary>
    public static int Unblock(IEnumerable<string> files)
    {
        var count = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file + ZoneStream);
                count++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Dosya kullanımda ya da salt okunur: diğerleriyle devam.
            }
        }

        return count;
    }
}
