using System.Text;
using System.Text.Json;

namespace CoopKeep.Core.Configuration;

/// <summary>
/// <c>server-config.json</c> ve <c>mod-config.json</c> üzerinde <b>cerrahi</b> düzenleme yapar:
/// yalnızca hedeflenen anahtarın değerini değiştirir, dosyanın geri kalanına dokunmaz.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden serializer kullanmıyoruz?</b> Bu dosyalar mod ekibine ait ve yoğun şekilde
/// yorumlanmış — her ayarın ne işe yaradığı satır satır anlatılmış. Bir POCO'ya deserialize
/// edip geri yazmak şunları yok eder: yorumlar, anahtar sırası, biçimlendirme ve
/// <b>bizim tanımadığımız anahtarlar</b>.
/// </para>
/// <para>
/// Son madde kritik: upstream günlük commit atıyor ve yeni ayarlar ekliyor. Tanımadığımız
/// bir anahtarı sessizce silmek, kullanıcının ayarını yok etmek demektir. Metin üzerinde
/// çalışarak bu riski tamamen ortadan kaldırıyoruz.
/// </para>
/// </remarks>
public sealed class JsoncEditor
{
    private string _text;

    private JsoncEditor(string text) => _text = text;

    /// <summary>Dosyanın güncel metni.</summary>
    public string Text => _text;

    public static JsoncEditor Parse(string text) => new(text);

    public static JsoncEditor Load(string path) => new(File.ReadAllText(path));

    /// <summary>
    /// Yorumları ve trailing comma'ları atlayarak dosyayı okunabilir bir JSON belgesine çevirir.
    /// Yalnızca <i>okuma</i> için; yazma her zaman metin üzerinden yapılır.
    /// </summary>
    public JsonDocument ToJsonDocument() => JsonDocument.Parse(_text, new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    /// <summary>Anahtar dosyada tanımlı mı? (yorum satırındakiler sayılmaz)</summary>
    public bool ContainsKey(string key) => FindValueSpan(key, out _, out _);

    public void SetString(string key, string value) => SetRawValue(key, JsonSerializer.Serialize(value));

    public void SetInt(string key, int value) => SetRawValue(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void SetBool(string key, bool value) => SetRawValue(key, value ? "true" : "false");

    /// <summary>
    /// Anahtarın değerini önceden serileştirilmiş bir JSON literali ile değiştirir.
    /// Anahtar yoksa dosyanın sonuna, kapanış süslü parantezinden önce eklenir.
    /// </summary>
    public void SetRawValue(string key, string jsonLiteral)
    {
        if (FindValueSpan(key, out var start, out var length))
        {
            _text = _text[..start] + jsonLiteral + _text[(start + length)..];
            return;
        }

        AppendKey(key, jsonLiteral);
    }

    /// <summary>
    /// Dosyayı yazar. Üzerine yazmadan önce <c>.bak</c> kopyası alınır — config kaybı
    /// kullanıcının server'ını açılmaz hâle getirebilir.
    /// </summary>
    public void Save(string path)
    {
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        // Geçiciye yaz, sonra taşı: yazma sırasında kesinti olursa config yarım kalmasın.
        var temp = path + ".tmp";
        File.WriteAllText(temp, _text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    // ------------------------------------------------------------------
    // Metin tarama
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>"key"</c> : &lt;değer&gt; kalıbını bulup <i>değerin</i> metindeki konumunu döndürür.
    /// Yorum içindeki ve string literal içindeki eşleşmeler atlanır.
    /// </summary>
    private bool FindValueSpan(string key, out int valueStart, out int valueLength)
    {
        valueStart = 0;
        valueLength = 0;

        var needle = "\"" + key + "\"";
        var i = 0;

        while (i < _text.Length)
        {
            // Yorumları atla
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }

            if (_text[i] == '"')
            {
                var tokenStart = i;
                var tokenEnd = SkipString(i);

                if (tokenEnd - tokenStart == needle.Length &&
                    string.CompareOrdinal(_text, tokenStart, needle, 0, needle.Length) == 0)
                {
                    // Anahtar adayı: ardından ':' gelmeli (aradaki boşluk ve yorumlar atlanır)
                    var j = SkipTrivia(tokenEnd);
                    if (j < _text.Length && _text[j] == ':')
                    {
                        var vs = SkipTrivia(j + 1);
                        var ve = SkipValue(vs);
                        if (ve > vs)
                        {
                            valueStart = vs;
                            valueLength = ve - vs;
                            return true;
                        }
                    }
                }

                i = tokenEnd;
                continue;
            }

            i++;
        }

        return false;
    }

    private bool IsAt(int i, string s) =>
        i + s.Length <= _text.Length && string.CompareOrdinal(_text, i, s, 0, s.Length) == 0;

    private int SkipLineComment(int i)
    {
        while (i < _text.Length && _text[i] != '\n') i++;
        return i;
    }

    private int SkipBlockComment(int i)
    {
        i += 2;
        while (i + 1 < _text.Length && !(_text[i] == '*' && _text[i + 1] == '/')) i++;
        return Math.Min(i + 2, _text.Length);
    }

    /// <summary>Açılış tırnağından başlar, kapanış tırnağının BİR SONRASINI döndürür.</summary>
    private int SkipString(int i)
    {
        i++; // açılış tırnağı
        while (i < _text.Length)
        {
            if (_text[i] == '\\') { i += 2; continue; }
            if (_text[i] == '"') return i + 1;
            i++;
        }
        return i;
    }

    /// <summary>Boşluk ve yorumları atlar.</summary>
    private int SkipTrivia(int i)
    {
        while (i < _text.Length)
        {
            if (char.IsWhiteSpace(_text[i])) { i++; continue; }
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }
            break;
        }
        return i;
    }

    /// <summary>Tek bir JSON değerinin sonunu bulur (string, sayı, bool, null, nesne, dizi).</summary>
    private int SkipValue(int i)
    {
        if (i >= _text.Length) return i;

        return _text[i] switch
        {
            '"' => SkipString(i),
            '{' => SkipBalanced(i, '{', '}'),
            '[' => SkipBalanced(i, '[', ']'),
            _ => SkipScalar(i),
        };
    }

    private int SkipScalar(int i)
    {
        var start = i;
        while (i < _text.Length && _text[i] is not (',' or '}' or ']' or '\n' or '\r') && !IsAt(i, "//"))
            i++;

        // Sondaki boşlukları değerin dışında bırak
        while (i > start && char.IsWhiteSpace(_text[i - 1])) i--;
        return i;
    }

    private int SkipBalanced(int i, char open, char close)
    {
        var depth = 0;
        while (i < _text.Length)
        {
            var c = _text[i];
            if (c == '"') { i = SkipString(i); continue; }
            if (IsAt(i, "//")) { i = SkipLineComment(i); continue; }
            if (IsAt(i, "/*")) { i = SkipBlockComment(i); continue; }

            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            i++;
        }
        return i;
    }

    /// <summary>
    /// Eksik anahtarı, kök nesnenin kapanış parantezinden hemen önce ekler.
    /// </summary>
    /// <remarks>
    /// Kullanıcının makinesinde bulunan gerçek bir durum: 30.08 tarihli
    /// <c>server-config.json</c> güncel build'in eklediği <c>"port"</c> anahtarını
    /// içermiyordu. Böyle bir dosyaya anahtar eklemek zorundayız.
    /// </remarks>
    private void AppendKey(string key, string jsonLiteral)
    {
        var lastBrace = _text.LastIndexOf('}');
        if (lastBrace < 0)
            throw new InvalidOperationException("Geçerli bir JSON nesnesi bulunamadı; dosya bozuk olabilir.");

        // Kapanıştan önceki son anlamlı karakter virgül mü? Değilse virgül eklemeliyiz.
        var k = lastBrace - 1;
        while (k >= 0 && char.IsWhiteSpace(_text[k])) k--;

        var needsComma = k >= 0 && _text[k] != ',' && _text[k] != '{';
        var indent = DetectIndent();
        var newline = _text.Contains("\r\n") ? "\r\n" : "\n";

        var insertion = (needsComma ? "," : string.Empty)
                        + newline + indent + "\"" + key + "\": " + jsonLiteral + newline;

        _text = _text[..(k + 1)] + insertion + _text[lastBrace..];
    }

    /// <summary>Dosyadaki mevcut girinti stilini taklit eder.</summary>
    private string DetectIndent()
    {
        foreach (var line in _text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('"') && trimmed.Length < line.Length)
                return line[..(line.Length - trimmed.Length)].TrimEnd('\r');
        }
        return "  ";
    }
}
