using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;
using TaleWorlds.ObjectSystem;

namespace CoopKeep.ImageExporter;

/// <summary>
/// Bir tek oyunculu kayıt yüklendiğinde oyunun kendi çizdiği görselleri PNG olarak kaydeder.
/// </summary>
/// <remarks>
/// <para>
/// Oyun eşya, asker ve sancak görsellerini diske yazmıyor; envanterde anlık çiziyor.
/// İnternette de eşyaların çoğunun görseli yok. Bu modül oyunun aynı çizim yolunu
/// (<see cref="ThumbnailCacheManager"/>) kullanır ve sonucu kaydeder:
/// </para>
/// <code>
/// %LOCALAPPDATA%\CoopKeep\images\game\item\&lt;kimlik&gt;.png
/// %LOCALAPPDATA%\CoopKeep\images\troop\&lt;ad&gt;.png
/// %LOCALAPPDATA%\CoopKeep\images\lord\&lt;ad&gt;.png
/// %LOCALAPPDATA%\CoopKeep\images\clanbanner\&lt;klan adı&gt;.png
/// </code>
/// <para>
/// Dosya adları CoopKeep'in görsel servisiyle aynı kurala uyar (küçük harf, tire).
/// Bitince <c>export-done.txt</c> yazılır ve oyun kapanır. Bu dosya varken modül bir şey
/// yapmaz; CoopKeep yeniden çıkarmak isterse dosyayı siler.
/// </para>
/// </remarks>
public sealed class ImageExporterSubModule : MBSubModuleBase
{
    private const int MaxInFlight = 6;

    private readonly Queue<Job> _queue = new();
    private readonly List<Job> _inFlight = new();
    private readonly List<PendingFix> _pendingFix = new();
    private readonly List<DeferredSave> _deferredSave = new();

    private sealed class DeferredSave
    {
        public Job Job { get; set; } = null!;
        public Texture Texture { get; set; } = null!;
        public float Age { get; set; }
    }

    private sealed class PendingFix
    {
        public string Path { get; set; } = "";
        public string Info { get; set; } = "";
        public float Age { get; set; }
        public long LastLength { get; set; } = -1;
    }
    private string _root = "";
    private StreamWriter? _log;
    private bool _running;
    private int _total;
    private int _done;
    private int _failed;
    private float _idle;

    private sealed class Job
    {
        public string Path { get; set; } = "";
        public Func<Action<Texture>, Action, ThumbnailCreationData> Create { get; set; } = null!;
        public ThumbnailCreationData? Data { get; set; }
        public float Age { get; set; }
        public bool Finished { get; set; }
    }

    /// <summary>
    /// <c>COOPKEEP_EXPORT_AUTOSTART=1</c> ise ana menüden kendiliğinden sade bir sandbox
    /// başlatılır; nesneler yüklenince (karakter oluşturma ekranında) çıkarma başlar. Böylece
    /// modlu bir kayda ya da tıklamaya gerek kalmaz.
    /// </summary>
    private bool _autoStart;
    private float _menuTimer = -1;
    private float _beginTimer = -1;

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        if (Environment.GetEnvironmentVariable("COOPKEEP_EXPORT_AUTOSTART") == "1" && !_autoStart)
        {
            _autoStart = true;
            _menuTimer = 4f;
        }
    }

    public override void OnGameLoaded(Game game, object initializerObject)
    {
        base.OnGameLoaded(game, initializerObject);
        if (game.GameType is Campaign) _beginTimer = 3f;
    }

    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);
        if (game.GameType is Campaign) _beginTimer = 5f;
    }

    private void Begin()
    {
        if (_running) return;

        _root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoopKeep", "images", "game");
        Directory.CreateDirectory(_root);
        if (File.Exists(System.IO.Path.Combine(_root, "export-done.txt"))) return;

        _log = new StreamWriter(System.IO.Path.Combine(_root, "export.log"), false, Encoding.UTF8) { AutoFlush = true };
        Log("CoopKeep görsel çıkarıcı başladı " + DateTime.Now.ToString("O"));

        try
        {
            foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
            {
                if (item is null || string.IsNullOrWhiteSpace(item.StringId) || item.IsBannerItem) continue;
                var target = Target("item", item.StringId);
                if (File.Exists(target)) continue;
                var captured = item;
                _queue.Enqueue(new Job { Path = target, Create = (set, cancel) => new ItemThumbnailCreationData(captured, "", set, cancel) });
            }

            foreach (var character in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
            {
                if (character is null || character.IsTemplate) continue;
                string kind;
                if (character.IsHero && character.HeroObject is { } hero && (hero.IsLord || hero.IsMinorFactionHero || hero.IsWanderer)) kind = "lord";
                else if (character.Occupation == Occupation.Soldier || character.Occupation == Occupation.Bandit || character.Occupation == Occupation.Mercenary) kind = "troop";
                else continue;

                var id = kind == "lord" && character.HeroObject is { } h ? h.StringId : character.StringId;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var target = Target(kind, id);
                if (File.Exists(target)) continue;

                var code = CharacterCode.CreateFrom(character);
                _queue.Enqueue(new Job { Path = target, Create = (set, cancel) => new CharacterThumbnailCreationData(code, set, cancel, true, 512, 240) });
            }

            // Sancaklar çıkarılamıyor: motor sancak dokularını sıkıştırılmış tutuyor, SaveToFile boş
            // dosya yazıyor ve GetPixelData oyunu çökertiyor (2026-10-16'da denendi). Haritada
            // kasabaların gerçek sancakları zaten oyunun harita görüntüsünde var.
        }
        catch (Exception e)
        {
            Log("! liste hazırlanamadı: " + e);
        }

        _total = _queue.Count;
        _running = _total > 0;
        Log($"{_total} görsel çıkarılacak");
        Message($"CoopKeep: {_total} görsel çıkarılıyor…");
        if (!_running) Finish();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);

        if (_menuTimer > 0 && (_menuTimer -= dt) <= 0)
        {
            try { MBGameManager.StartNewGame(new SandBox.SandBoxGameManager(() => new Campaign(CampaignGameMode.Campaign))); }
            catch (Exception e) { File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coopkeep-export-error.txt"), e + Environment.NewLine); }
        }

        if (_beginTimer > 0 && (_beginTimer -= dt) <= 0) Begin();

        if (!_running || ThumbnailCacheManager.Current is null) return;

        foreach (var job in _inFlight.ToList())
        {
            job.Age += dt;
            if (job.Finished) _inFlight.Remove(job);
            else if (job.Age > 20f)
            {
                Log("! zaman aşımı: " + job.Path);
                _failed++;
                job.Finished = true;
                _inFlight.Remove(job);
            }
        }

        while (_inFlight.Count < MaxInFlight && _queue.Count > 0)
        {
            var job = _queue.Dequeue();
            try
            {
                job.Data = job.Create(texture => OnTexture(job, texture), () => OnCancel(job));
                ThumbnailCacheManager.Current.CreateTexture(job.Data);
                _inFlight.Add(job);
            }
            catch (Exception e)
            {
                Log("! " + job.Path + ": " + e.Message);
                _failed++;
            }
        }

        foreach (var d in _deferredSave.ToList())
        {
            d.Age += dt;
            if (d.Age < 1.5f) continue;
            try
            {
                d.Texture.SaveToFile(d.Job.Path, false);
                _pendingFix.Add(new PendingFix { Path = d.Job.Path, Info = "banner" });
            }
            catch (Exception e) { Log("! sancak kaydedilemedi " + d.Job.Path + ": " + e.Message); }
            finally
            {
                try { if (d.Job.Data is not null) ThumbnailCacheManager.Current.DestroyTexture(d.Job.Data); } catch (Exception) { }
                _deferredSave.Remove(d);
            }
        }

        foreach (var fix in _pendingFix.ToList())
        {
            fix.Age += dt;
            var length = File.Exists(fix.Path) ? new FileInfo(fix.Path).Length : -1;
            if (length > 0 && length == fix.LastLength && fix.Age > 0.5f)
            {
                if (!PngFix.SwapRedBlue(fix.Path)) Log("~ düzeltilemedi (" + fix.Info + "): " + fix.Path);
                _pendingFix.Remove(fix);
            }
            else if (fix.Age > 30f)
            {
                Log("! diske yazılmadı: " + fix.Path);
                _pendingFix.Remove(fix);
            }
            else fix.LastLength = length;
        }

        if (_queue.Count == 0 && _inFlight.Count == 0 && _pendingFix.Count == 0 && _deferredSave.Count == 0)
        {
            _idle += dt;
            if (_idle > 1f) Finish();
        }
    }

    private void OnTexture(Job job, Texture texture)
    {
        if (job.Finished) return;
        var deferred = false;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(job.Path)!);
            // GetPixelData motoru çökertiyor (2026-10-16); yalnızca motorun kaydedicisi kullanılır.
            // Motor dosyayı GECİKMELİ yazar ve kırmızı/mavi ters olur: dosya diske düşünce
            // OnApplicationTick'te geri okunup düzeltilir.
            if (job.Path.Contains(System.IO.Path.DirectorySeparatorChar + "clanbanner" + System.IO.Path.DirectorySeparatorChar))
            {
                // Sancak dokusu bir render hedefi: geri çağrı geldiğinde henüz çizilmemiş olabiliyor.
                // Birkaç kare sonra kaydedilir (doku o zamana kadar serbest bırakılmaz).
                _deferredSave.Add(new DeferredSave { Job = job, Texture = texture });
                deferred = true;
                return;
            }

            texture.SaveToFile(job.Path, false);
            _pendingFix.Add(new PendingFix { Path = job.Path, Info = texture.Width + "x" + texture.Height + " " + texture.MemorySize });

            _done++;
        }
        catch (Exception e)
        {
            Log("! kaydedilemedi " + job.Path + ": " + e.Message);
            _failed++;
        }
        finally
        {
            job.Finished = true;
            if (!deferred)
                try { if (job.Data is not null) ThumbnailCacheManager.Current.DestroyTexture(job.Data); } catch (Exception) { }
        }

        if ((_done + _failed) % 100 == 0) Message($"CoopKeep: {_done + _failed}/{_total}");
    }

    private void OnCancel(Job job)
    {
        if (job.Finished) return;
        job.Finished = true;
        _failed++;
        Log("! iptal: " + job.Path);
    }

    private void Finish()
    {
        _running = false;
        Log($"bitti: {_done} kaydedildi, {_failed} başarısız");
        File.WriteAllText(System.IO.Path.Combine(_root, "export-done.txt"), $"{DateTime.Now:O}\n{_done} ok\n{_failed} failed\n");
        Message($"CoopKeep: {_done} görsel kaydedildi. Oyun kapanıyor.");
        _log?.Dispose();
        _log = null;
        if (Environment.GetEnvironmentVariable("COOPKEEP_EXPORT_STAY") is null) Utilities.QuitGame();
    }

    /// <summary>
    /// Dosya adı oyunun iç kimliği: oyunun dili ne olursa olsun CoopKeep aynı dosyayı bulur.
    /// </summary>
    private string Target(string kind, string id)
    {
        var safe = new string(id.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return System.IO.Path.Combine(_root, kind, safe + ".png");
    }

    /// <summary>DDS (BC1/BC3/BGRA) dosyasını PNG'ye çevirir; başarılıysa DDS silinir.</summary>
    private static bool DdsToPng(string dds, string png)
    {
        if (!File.Exists(dds)) return false;
        var b = File.ReadAllBytes(dds);
        if (b.Length < 128 || b[0] != 'D' || b[1] != 'D' || b[2] != 'S') return false;

        int h = BitConverter.ToInt32(b, 12), w = BitConverter.ToInt32(b, 16);
        var fourCc = System.Text.Encoding.ASCII.GetString(b, 84, 4);
        var data = new byte[b.Length - 128];
        Buffer.BlockCopy(b, 128, data, 0, data.Length);

        byte[] bgra;
        if (fourCc == "DXT1") bgra = Bc.DecodeBc1(data, w, h);
        else if (fourCc == "DXT5") bgra = Bc.DecodeBc3(data, w, h);
        else if (data.Length >= w * h * 4) { bgra = new byte[w * h * 4]; Buffer.BlockCopy(data, 0, bgra, 0, bgra.Length); }
        else return false;

        File.WriteAllBytes(png, Png.Encode(bgra, w, h, swapRedBlue: true));
        File.Delete(dds);
        return true;
    }

    /// <summary>CoopKeep.Core.Images.GameImageService.Slug ile aynı kural.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private void Log(string line) => _log?.WriteLine(line);

    private static void Message(string text)
    {
        try { InformationManager.DisplayMessage(new InformationMessage(text)); }
        catch (Exception) { }
    }
}

/// <summary>Bağımlılıksız küçük PNG yazıcı (RGBA, 8 bit).</summary>
internal static class Png
{
    public static byte[] Encode(byte[] pixels, int width, int height, bool swapRedBlue)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            for (var x = 0; x < width; x++)
            {
                var s = y * stride + x * 4;
                var d = y * (stride + 1) + 1 + x * 4;
                raw[d] = swapRedBlue ? pixels[s + 2] : pixels[s];
                raw[d + 1] = pixels[s + 1];
                raw[d + 2] = swapRedBlue ? pixels[s] : pixels[s + 2];
                raw[d + 3] = pixels[s + 3];
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)width);
        BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; // 8 bit, RGBA
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", Zlib(raw));
        Chunk(ms, "IEND", new byte[0]);
        return ms.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            deflate.Write(data, 0, data.Length);

        uint a = 1, b = 0;
        foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
        var adler = new byte[4];
        BigEndian(adler, 0, (b << 16) | a);
        ms.Write(adler, 0, 4);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        var crc = new byte[4];
        BigEndian(crc, 0, Crc(t, data));
        s.Write(crc, 0, 4);
    }

    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in type) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in data) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BigEndian(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}

/// <summary>BC1/BC3 blok sıkıştırmasını çözer; çıktı BGRA.</summary>
internal static class Bc
{
    public static byte[] DecodeBc1(byte[] src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        var o = 0;
        for (var by = 0; by < h / 4; by++)
        for (var bx = 0; bx < w / 4; bx++, o += 8)
            ColorBlock(src, o, dst, w, bx, by, alphaFromColor: true);
        return dst;
    }

    public static byte[] DecodeBc3(byte[] src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        var o = 0;
        for (var by = 0; by < h / 4; by++)
        for (var bx = 0; bx < w / 4; bx++, o += 16)
        {
            ColorBlock(src, o + 8, dst, w, bx, by, alphaFromColor: false);
            AlphaBlock(src, o, dst, w, bx, by);
        }

        return dst;
    }

    private static void ColorBlock(byte[] s, int o, byte[] d, int w, int bx, int by, bool alphaFromColor)
    {
        int c0 = s[o] | (s[o + 1] << 8), c1 = s[o + 2] | (s[o + 3] << 8);
        var pal = new int[4, 4];
        Rgb(c0, pal, 0); Rgb(c1, pal, 1);
        var opaque = c0 > c1 || !alphaFromColor;
        for (var k = 0; k < 3; k++)
        {
            pal[2, k] = opaque ? (2 * pal[0, k] + pal[1, k]) / 3 : (pal[0, k] + pal[1, k]) / 2;
            pal[3, k] = opaque ? (pal[0, k] + 2 * pal[1, k]) / 3 : 0;
        }

        pal[0, 3] = pal[1, 3] = pal[2, 3] = 255;
        pal[3, 3] = opaque ? 255 : 0;

        var bits = (uint)(s[o + 4] | (s[o + 5] << 8) | (s[o + 6] << 16) | (s[o + 7] << 24));
        for (var i = 0; i < 16; i++)
        {
            var idx = (int)((bits >> (2 * i)) & 3);
            var p = ((by * 4 + i / 4) * w + bx * 4 + i % 4) * 4;
            d[p] = (byte)pal[idx, 2]; d[p + 1] = (byte)pal[idx, 1]; d[p + 2] = (byte)pal[idx, 0]; d[p + 3] = (byte)pal[idx, 3];
        }
    }

    private static void Rgb(int c, int[,] pal, int i)
    {
        pal[i, 0] = ((c >> 11) & 31) * 255 / 31;
        pal[i, 1] = ((c >> 5) & 63) * 255 / 63;
        pal[i, 2] = (c & 31) * 255 / 31;
    }

    private static void AlphaBlock(byte[] s, int o, byte[] d, int w, int bx, int by)
    {
        int a0 = s[o], a1 = s[o + 1];
        var a = new int[8];
        a[0] = a0; a[1] = a1;
        if (a0 > a1) for (var i = 1; i < 7; i++) a[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else { for (var i = 1; i < 5; i++) a[i + 1] = ((5 - i) * a0 + i * a1) / 5; a[6] = 0; a[7] = 255; }

        ulong bits = 0;
        for (var i = 0; i < 6; i++) bits |= (ulong)s[o + 2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
        {
            var p = ((by * 4 + i / 4) * w + bx * 4 + i % 4) * 4;
            d[p + 3] = (byte)a[(int)((bits >> (3 * i)) & 7)];
        }
    }
}

/// <summary>Motorun yazdığı 8 bit RGBA PNG'yi okuyup kırmızı/mavi kanalları yer değiştirir.</summary>
internal static class PngFix
{
    public static bool SwapRedBlue(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 33 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;

        int w = 0, h = 0;
        var idat = new MemoryStream();
        var i = 8;
        while (i + 8 <= b.Length)
        {
            var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
            var type = Encoding.ASCII.GetString(b, i + 4, 4);
            if (type == "IHDR")
            {
                w = (b[i + 8] << 24) | (b[i + 9] << 16) | (b[i + 10] << 8) | b[i + 11];
                h = (b[i + 12] << 24) | (b[i + 13] << 16) | (b[i + 14] << 8) | b[i + 15];
                if (b[i + 16] != 8 || b[i + 17] != 6) return false; // yalnızca 8 bit RGBA
            }
            else if (type == "IDAT") idat.Write(b, i + 8, len);
            i += 12 + len;
        }

        if (w <= 0 || h <= 0) return false;

        // zlib başlığını atla, inflate et.
        var z = idat.ToArray();
        byte[] raw;
        using (var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(z, 2, z.Length - 2), System.IO.Compression.CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            inflate.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        var stride = w * 4;
        if (raw.Length < (stride + 1) * h) return false;

        var pixels = new byte[stride * h];
        var prev = new byte[stride];
        for (var y = 0; y < h; y++)
        {
            var filter = raw[y * (stride + 1)];
            var row = new byte[stride];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= 4 ? row[x - 4] : 0, up = prev[x], c = x >= 4 ? prev[x - 4] : 0;
                row[x] = filter switch
                {
                    1 => (byte)(row[x] + a),
                    2 => (byte)(row[x] + up),
                    3 => (byte)(row[x] + (a + up) / 2),
                    4 => (byte)(row[x] + Paeth(a, up, c)),
                    _ => row[x],
                };
            }

            Buffer.BlockCopy(row, 0, pixels, y * stride, stride);
            prev = row;
        }

        File.WriteAllBytes(path, Png.Encode(pixels, w, h, swapRedBlue: true));
        return true;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
