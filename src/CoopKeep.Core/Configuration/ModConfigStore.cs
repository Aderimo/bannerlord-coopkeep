using System.Text.Json;

namespace CoopKeep.Core.Configuration;

/// <summary>
/// Coop'un oyun ayarları dosyası <c>CoopData\mod-config.json</c> üzerinde okuma ve cerrahi yazma.
/// </summary>
/// <remarks>
/// <para>
/// Sunucunun kendi yardım metni (v0.1.6): <i>"Gameplay settings live in mod-config.json
/// one level up, in CoopData\ itself - shared with player-hosted co-op."</i> Zorluk
/// değerleri <b>her açılışta</b> barındıran taraf tarafından uygulanıyor, dolayısıyla
/// dosyayı başlatmadan hemen önce yazmak yeterli.
/// </para>
/// <para>
/// Dosya oyunun "Host Co-op" düğmesiyle de paylaşıldığı için yalnızca tanıdığımız
/// anahtarlar değiştirilir; yorumlar ve geri kalan her şey korunur.
/// </para>
/// </remarks>
public sealed class ModConfigStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public bool Exists => File.Exists(Path);

    /// <summary>
    /// Dosya yoksa modun kendi şablonundan (<c>mod-config.default.json</c>) oluşturur.
    /// </summary>
    /// <remarks>Sunucu ilk açılışta aynı kopyalamayı kendisi yapıyor; biz önden davranıyoruz ki
    /// kullanıcının seçtiği ayarlar daha ilk açılışta geçerli olsun.</remarks>
    public void EnsureExists(string? templatePath)
    {
        if (Exists) return;

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (templatePath is not null && File.Exists(templatePath))
            File.Copy(templatePath, Path);
        else
            File.WriteAllText(Path, "{\n  \"difficulty\": {\n  },\n  \"modOptions\": {\n  }\n}\n");
    }

    /// <summary>Tüm bilinen ayarlar; dosyada olmayanlar modun varsayılanıyla.</summary>
    public Dictionary<string, object> Read()
    {
        var values = ModSettingsCatalog.Defaults();
        if (!Exists) return values;

        using var doc = JsoncEditor.Load(Path).ToJsonDocument();

        foreach (var d in ModSettingsCatalog.All)
        {
            if (doc.RootElement.TryGetProperty(d.Section, out var section) &&
                section.ValueKind == JsonValueKind.Object &&
                section.TryGetProperty(d.Key, out var value))
            {
                values[d.Id] = ModSettingsCatalog.FromJson(d, value);
            }
        }

        return values;
    }

    /// <summary>
    /// Verilen ayarları yazar. Yalnızca değeri gerçekten değişen anahtarlara dokunulur.
    /// </summary>
    /// <returns>Değiştirilen ayar sayısı.</returns>
    public int Write(IReadOnlyDictionary<string, object> values)
    {
        var editor = JsoncEditor.Load(Path);
        var current = Read();
        var changed = 0;

        foreach (var (id, value) in values)
        {
            if (ModSettingsCatalog.Find(id) is not { } d) continue;

            var normalized = ModSettingsCatalog.Normalize(d, value);
            if (current.TryGetValue(id, out var old) && Equals(old, normalized) &&
                editor.GetRawValue(d.Section, d.Key) is not null)
                continue;

            editor.SetRawValue(d.Section, d.Key, ModSettingsCatalog.ToJsonLiteral(d, normalized));
            changed++;
        }

        if (changed > 0) editor.Save(Path);
        return changed;
    }
}
