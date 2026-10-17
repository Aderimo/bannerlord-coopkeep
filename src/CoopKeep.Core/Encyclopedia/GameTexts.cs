using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CoopKeep.Core.Encyclopedia;

/// <summary>
/// Oyunun kendi çevirileri: <c>{=anahtar}İngilizce metin</c> → seçili dildeki metin.
/// </summary>
/// <remarks>
/// <para>
/// Oyun, veri dosyalarındaki her metni bir anahtarla yazar
/// (<c>name="{=CLqPbdiZ}Lucon"</c>). Çeviriler modüllerin <c>ModuleData/Languages/&lt;dil&gt;</c>
/// klasörlerindeki <c>std_*.xml</c> dosyalarında: <c>&lt;string id="CLqPbdiZ" text="…" /&gt;</c>.
/// Türkçe çeviri oyunun kendisiyle geliyor (<c>Languages/TR</c>, UTF-16).
/// </para>
/// <para>
/// Dosyalar kullanıcının kurulumundan okunur; CoopKeep oyun metni taşımaz.
/// </para>
/// </remarks>
public sealed partial class GameTexts
{
    [GeneratedRegex(@"^\{=(?<key>[^}]*)\}(?<text>.*)$", RegexOptions.Singleline)]
    private static partial Regex Keyed();

    [GeneratedRegex(@"\{[A-Z_.]+\}|\{\?[^}]*\}|\{\\\?\}")]
    private static partial Regex Variables();

    [GeneratedRegex(@"[ 	]{2,}")]
    private static partial Regex Spaces();

    private readonly Dictionary<string, string> _strings;

    private GameTexts(Dictionary<string, string> strings) => _strings = strings;

    /// <summary>Çevirisiz: metinler İngilizce kalır.</summary>
    public static GameTexts English { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Count => _strings.Count;

    /// <summary>
    /// Modüllerin çevirilerini yükler. <paramref name="language"/>: oyunun klasör adı (<c>TR</c>).
    /// </summary>
    public static GameTexts Load(string modulesDirectory, string language, IEnumerable<string>? modules = null)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in modules ?? ["Native", "SandBoxCore", "SandBox", "StoryMode"])
        {
            var dir = Path.Combine(modulesDirectory, module, "ModuleData", "Languages", language);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "std_*.xml"))
            {
                try
                {
                    // Dosyalar başlıkta "utf-16" yazıyor ama içerik UTF-8 (BOM'lu); XDocument.Load bu
                    // yüzden hata veriyor. Metin kodlaması BOM'dan algılanarak okunur.
                    foreach (var s in XDocument.Parse(File.ReadAllText(file)).Descendants("string"))
                    {
                        var id = (string?)s.Attribute("id");
                        var text = (string?)s.Attribute("text");
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(text)) strings[id] = text;
                    }
                }
                catch (Exception e) when (e is System.Xml.XmlException or IOException)
                {
                    // Bozuk bir çeviri dosyası bütün ansiklopediyi düşürmesin.
                }
            }
        }

        return new GameTexts(strings);
    }

    /// <summary>
    /// <c>{=anahtar}Metin</c> → çeviri; çeviri yoksa anahtarsız İngilizce metin.
    /// Oyunun metin değişkenleri (<c>{FIRSTNAME}</c>) temizlenir.
    /// </summary>
    public string Resolve(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var m = Keyed().Match(raw);
        var text = m.Success
            ? _strings.TryGetValue(m.Groups["key"].Value, out var tr) ? tr : m.Groups["text"].Value
            : raw;
        return Spaces().Replace(Variables().Replace(text, ""), " ").Trim();
    }
}
