namespace CoopKeep.Core.Configuration;

/// <summary>
/// Bir ayar listesi ve onun üstündeki ortak işlemler (varsayılanlar, zorluk ön ayarı,
/// doğrulama). Ayar formu hangi listeyle çalıştığını bilmez; liste dışarıdan verilir.
/// </summary>
/// <remarks>
/// Bugün tek liste var: Coop'un ayar dosyası (<see cref="ModSettingsCatalog"/>). Form listeyi
/// bilmez; ileride başka bir liste (ör. kendi modumuzun dünya ayarları) aynı formla gösterilir.
/// </remarks>
public sealed class SettingsCatalog(IReadOnlyList<ModSettingDefinition> all)
{
    public IReadOnlyList<ModSettingDefinition> All { get; } = all;

    public ModSettingDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public Dictionary<string, object> Defaults() => All.ToDictionary(d => d.Id, d => d.Default);

    /// <summary>Bütün seviye ayarlarını aynı seviyeye çeker (oyunun Kolay / Normal / Gerçekçi düğmeleri).</summary>
    public void ApplyPreset(IDictionary<string, object> values, string level)
    {
        if (!ModSettingsCatalog.Levels.Contains(level))
            throw new ArgumentException($"Bilinmeyen seviye: '{level}'", nameof(level));

        foreach (var d in All.Where(d => d.Kind == ModSettingKind.Level))
            values[d.Id] = level;
    }

    /// <summary>Bütün seviye ayarları aynıysa o seviye, değilse <see langword="null"/> (özel).</summary>
    public string? DetectPreset(IReadOnlyDictionary<string, object> values)
    {
        var levels = All.Where(d => d.Kind == ModSettingKind.Level)
            .Select(d => values.TryGetValue(d.Id, out var v) ? v as string : null)
            .Distinct()
            .ToList();

        return levels.Count == 1 ? levels[0] : null;
    }

    public object Normalize(ModSettingDefinition d, object? value) => ModSettingsCatalog.Normalize(d, value);
}
