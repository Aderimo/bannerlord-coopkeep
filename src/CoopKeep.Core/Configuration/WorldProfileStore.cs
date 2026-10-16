using System.Text.Json;

namespace CoopKeep.Core.Configuration;

/// <summary>
/// Her sunucu dünyasının kendi oyun ayarları.
/// </summary>
/// <remarks>
/// <para>
/// <c>mod-config.json</c> tek ve ortak bir dosya; oysa kullanıcı "kolay" bir dünya ile
/// "gerçekçi" bir dünyayı yan yana tutmak istiyor. Bu yüzden her dünyanın ayarlarını
/// kendi dosyamızda saklıyoruz ve sunucu başlamadan hemen önce o dünyanınkini
/// <c>mod-config.json</c>'a yazıyoruz.
/// </para>
/// <para>
/// Profiller save klasörüne <b>değil</b> CoopKeep'in kendi klasörüne yazılır: save
/// klasörü sunucuya ait ve oradaki <c>.sav</c> + <c>.json</c> çiftine üçüncü bir dosya
/// eklemek modun kendi araçlarını şaşırtabilirdi.
/// </para>
/// </remarks>
public sealed class WorldProfileStore(string directory)
{
    public string Directory { get; } = System.IO.Path.GetFullPath(directory);

    public static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoopKeep", "worlds");

    private string PathFor(string saveName)
    {
        if (!Saves.SaveSet.IsValidSaveName(saveName))
            throw new ArgumentException($"Geçersiz save adı: '{saveName}'", nameof(saveName));

        return System.IO.Path.Combine(Directory, saveName + ".settings.json");
    }

    public bool Exists(string saveName) => File.Exists(PathFor(saveName));

    /// <summary>Dünyanın kayıtlı ayarları; profil yoksa <see langword="null"/>.</summary>
    public Dictionary<string, object>? Load(string saveName)
    {
        var path = PathFor(saveName);
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var values = ModSettingsCatalog.Defaults();

            foreach (var d in ModSettingsCatalog.All)
            {
                if (doc.RootElement.TryGetProperty(d.Id, out var e))
                    values[d.Id] = ModSettingsCatalog.FromJson(d, e);
            }

            return values;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Bozuk profil: yok say; kullanıcı ayarları yeniden seçebilir.
            return null;
        }
    }

    public void Save(string saveName, IReadOnlyDictionary<string, object> values)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var normalized = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var d in ModSettingsCatalog.All)
        {
            values.TryGetValue(d.Id, out var v);
            normalized[d.Id] = ModSettingsCatalog.Normalize(d, v);
        }

        var path = PathFor(saveName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    public void Rename(string from, string to)
    {
        var source = PathFor(from);
        if (File.Exists(source)) File.Move(source, PathFor(to), overwrite: true);
    }

    public void Duplicate(string from, string to)
    {
        var source = PathFor(from);
        if (File.Exists(source)) File.Copy(source, PathFor(to), overwrite: true);
    }

    public void Delete(string saveName)
    {
        var path = PathFor(saveName);
        if (File.Exists(path)) File.Delete(path);
    }
}
