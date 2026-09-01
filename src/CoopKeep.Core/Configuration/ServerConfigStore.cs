using System.Text.Json;

namespace CoopKeep.Core.Configuration;

/// <summary>
/// <c>server-config.json</c> üzerinde tipli okuma ve cerrahi yazma.
/// </summary>
/// <remarks>
/// Anahtar kümesi kapalı kaynak launcher binary'sinden çıkarıldı ve canlı koşuyla
/// doğrulandı (2026-09-01). Tanımadığımız anahtarlar yazma sırasında korunur.
/// </remarks>
public sealed class ServerConfigStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public bool Exists => File.Exists(Path);

    public ServerConfig Read()
    {
        var editor = JsoncEditor.Load(Path);
        using var doc = editor.ToJsonDocument();
        var root = doc.RootElement;

        return new ServerConfig
        {
            SaveName = GetString(root, "saveName") ?? ServerConfig.DefaultSaveName,
            AutosaveMinutes = GetInt(root, "autosaveMinutes") ?? ServerConfig.DefaultAutosaveMinutes,
            Password = GetString(root, "password") ?? string.Empty,
            Port = GetInt(root, "port") ?? ServerConfig.DefaultPort,
            LogFile = GetBool(root, "logFile") ?? true,
            Steam = GetBool(root, "steam") ?? true,
            HasExplicitPort = root.TryGetProperty("port", out _),
        };
    }

    /// <summary>
    /// Yalnızca verilen alanları günceller; dosyanın yorumlarına, sırasına ve
    /// tanımadığımız anahtarlarına dokunmaz.
    /// </summary>
    public void Update(Action<ServerConfigWriter> mutate)
    {
        var editor = JsoncEditor.Load(Path);
        mutate(new ServerConfigWriter(editor));
        editor.Save(Path);
    }

    /// <summary>Aktif kampanyayı değiştirir. Etkili olması için server yeniden başlatılmalı.</summary>
    public void SetSaveName(string saveName) => Update(w => w.SaveName(saveName));

    /// <summary>
    /// Yapılandırma yoksa oluşturur; varsa yalnızca kampanya adını günceller.
    /// </summary>
    /// <remarks>
    /// İlk çalıştırmada server kendi varsayılan dosyasını yazar ve <c>saveName</c> olarak
    /// <c>saveauto1</c> kullanır. Kullanıcının istediği adla bir dünya yaratabilmek için
    /// dosyayı <b>server açılmadan önce</b> biz yazmalıyız.
    /// </remarks>
    public void EnsureSaveName(string saveName)
    {
        if (!Saves.SaveSet.IsValidSaveName(saveName))
            throw new ArgumentException($"Geçersiz save adı: '{saveName}'", nameof(saveName));

        if (Exists)
        {
            SetSaveName(saveName);
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(Path, BuildDefaultConfig(saveName));
    }

    /// <summary>
    /// Server'ın kendi şablonuyla aynı anahtar kümesini taşıyan yeni bir yapılandırma metni.
    /// </summary>
    private static string BuildDefaultConfig(string saveName) =>
        $$"""
        {
          // CoopKeep tarafından oluşturuldu. Yorumlar ve sondaki virgüller serbesttir.

          // Sunulacak kampanya (uzantısız). Diskte yoksa server default_new_game.sav'dan
          // sıfırdan yeni bir dünya kurar.
          "saveName": {{JsonSerializer.Serialize(saveName)}},

          // Otomatik kayıt aralığı (dakika). 0 = kapalı.
          "autosaveMinutes": {{ServerConfig.DefaultAutosaveMinutes}},

          // Bağlantı şifresi (en fazla 128 karakter). Boş = herkes girebilir.
          "password": "",

          // Oyuncuların bağlandığı UDP portu. Motorun kendi iç portuyla karıştırmayın.
          "port": {{ServerConfig.DefaultPort}},

          // Sunucu çıktısını logs\coop-server-*.log dosyalarına yaz.
          "logFile": true,

          // Steam üzerinden keşfedilebilirlik. false = yalnızca doğrudan bağlantı.
          "steam": true,

          // Tanılama. Yalnızca hata raporu için açın.
          "traceTick": false,
          "tracePublish": false,
          "traceBandits": false
        }
        """;

    private static string? GetString(JsonElement o, string n) =>
        o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement o, string n) =>
        o.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    private static bool? GetBool(JsonElement o, string n) =>
        o.TryGetProperty(n, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : null;
}

/// <summary>Yazma işlemlerini doğrulamayla birlikte sunan küçük yüz.</summary>
public sealed class ServerConfigWriter(JsoncEditor editor)
{
    public ServerConfigWriter SaveName(string value)
    {
        if (!Saves.SaveSet.IsValidSaveName(value))
            throw new ArgumentException($"Geçersiz save adı: '{value}'", nameof(value));

        editor.SetString("saveName", value);
        return this;
    }

    public ServerConfigWriter Password(string value)
    {
        if (value.Length > ServerConfig.MaxPasswordLength)
            throw new ArgumentException(
                $"Şifre en fazla {ServerConfig.MaxPasswordLength} karakter olabilir.", nameof(value));

        editor.SetString("password", value);
        return this;
    }

    public ServerConfigWriter Port(int value)
    {
        if (value is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(value), "Port 1-65535 aralığında olmalı.");

        editor.SetInt("port", value);
        return this;
    }

    public ServerConfigWriter AutosaveMinutes(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Otomatik kayıt aralığı negatif olamaz.");

        editor.SetInt("autosaveMinutes", value);
        return this;
    }

    public ServerConfigWriter Steam(bool value)
    {
        editor.SetBool("steam", value);
        return this;
    }
}

/// <summary>
/// <c>server-config.json</c>'ın bilinen alanları.
/// </summary>
public sealed record ServerConfig
{
    public const string DefaultSaveName = "saveauto1";
    public const int DefaultAutosaveMinutes = 5;

    /// <summary>Oyuncuların bağlandığı UDP portu.</summary>
    /// <remarks>
    /// Motorun <c>--port</c> argümanıyla (varsayılan 7210) <b>karıştırılmamalı</b>;
    /// o iç port ve oyuncular oraya bağlanmaz.
    /// </remarks>
    public const int DefaultPort = 4200;

    public const int MaxPasswordLength = 128;

    public required string SaveName { get; init; }
    public required int AutosaveMinutes { get; init; }
    public required string Password { get; init; }
    public required int Port { get; init; }
    public required bool LogFile { get; init; }

    /// <summary>Steam üzerinden keşfedilebilirlik. <c>false</c> = yalnızca doğrudan bağlantı.</summary>
    public required bool Steam { get; init; }

    /// <summary>
    /// Dosyada <c>port</c> anahtarı açıkça var mı?
    /// </summary>
    /// <remarks>
    /// Eski yapılandırmalarda bu anahtar yok ve server varsayılana düşüyor. Kullanıcının
    /// kendi makinesinde bulunan gerçek durum buydu — bunu tespit edip düzeltmeyi
    /// önerebilmek için tutuyoruz.
    /// </remarks>
    public bool HasExplicitPort { get; init; }

    public bool HasPassword => !string.IsNullOrEmpty(Password);

    public bool AutosaveEnabled => AutosaveMinutes > 0;
}
