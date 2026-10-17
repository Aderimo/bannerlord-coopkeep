using System.Buffers.Binary;

namespace CoopKeep.Core.Map;

/// <summary>Kampanya haritasının navmesh'indeki bir yüz: çokgen ve arazi tipi.</summary>
public sealed record NavFace(IReadOnlyList<(float X, float Y)> Points, int Terrain);

/// <summary>
/// Oyunun kampanya haritası navmesh'ini okur
/// (<c>Modules\SandBox\SceneObj\Main_map\navmesh.bin</c>).
/// </summary>
/// <remarks>
/// <para>
/// Biçim belgelenmemiş; 2026-10-16'da Bannerlord v1.4.8 dosyasından çözüldü:
/// </para>
/// <list type="bullet">
/// <item>0x30'dan itibaren tek bir LZ4 bloğu.</item>
/// <item>Açılmış veri: <c>"NMG9"</c>, köşe sayısı, köşeler (3×float: x, y, z).</item>
/// <item>Kenar sayısı, kenarlar (6×int32).</item>
/// <item>
/// Yüz sayısı, yüzler: <c>n</c>, <c>n</c> köşe indeksi, <c>n</c> kenar indeksi, ardından
/// 21 baytlık bir kuyruk; kuyruğun üçüncü int32'si arazi tipidir.
/// </item>
/// </list>
/// <para>
/// Koordinatlar <c>settlements.xml</c> ve sunucunun parti konumlarıyla aynı sistemde.
/// Yalnızca kara (ve nehir/göl) yüzleri var; deniz boşluk olarak kalır. Dosya her
/// kullanıcının kendi oyun kurulumundan okunur, CoopKeep içinde taşınmaz.
/// </para>
/// </remarks>
public static class NavMeshReader
{
    private const int CompressedStart = 0x30;
    private const int FaceTailBytes = 21;

    /// <summary>Oyun kurulumundaki navmesh dosyası.</summary>
    public static string PathIn(string modulesDirectory) =>
        System.IO.Path.Combine(modulesDirectory, "SandBox", "SceneObj", "Main_map", "navmesh.bin");

    /// <summary>Navmesh'i okur; biçim beklenenden farklıysa <see langword="null"/>.</summary>
    public static IReadOnlyList<NavFace>? Read(string path)
    {
        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<NavFace>? Parse(byte[] file)
    {
        if (file.Length < CompressedStart + 16 || file[0] != 'R' || file[1] != 'N' || file[2] != 'M') return null;

        var expected = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x10));
        if (expected <= 0 || expected > 64 * 1024 * 1024) return null;

        byte[] data;
        try
        {
            data = Lz4Block(file.AsSpan(CompressedStart), expected);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
        {
            return null;
        }

        var s = data.AsSpan();
        if (s.Length < 8 || s[0] != 'N' || s[1] != 'M' || s[2] != 'G') return null;

        var p = 4;
        var vertexCount = Int(s, ref p);
        if (vertexCount <= 0 || p + vertexCount * 12 > s.Length) return null;

        var vx = new float[vertexCount];
        var vy = new float[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            vx[i] = BinaryPrimitives.ReadSingleLittleEndian(s[p..]);
            vy[i] = BinaryPrimitives.ReadSingleLittleEndian(s[(p + 4)..]);
            p += 12;
        }

        var edgeCount = Int(s, ref p);
        if (edgeCount < 0) return null;
        p += edgeCount * 24;

        var faceCount = Int(s, ref p);
        if (faceCount <= 0) return null;

        var faces = new List<NavFace>(faceCount);
        for (var f = 0; f < faceCount; f++)
        {
            if (p + 4 > s.Length) return null;
            var n = Int(s, ref p);
            if (n is < 3 or > 16 || p + n * 8 + FaceTailBytes > s.Length) return null;

            var points = new (float, float)[n];
            for (var k = 0; k < n; k++)
            {
                var v = BinaryPrimitives.ReadInt32LittleEndian(s[(p + k * 4)..]);
                if ((uint)v >= (uint)vertexCount) return null;
                points[k] = (vx[v], vy[v]);
            }

            p += n * 8;
            var terrain = BinaryPrimitives.ReadInt32LittleEndian(s[(p + 8)..]);
            p += FaceTailBytes;

            faces.Add(new NavFace(points, terrain));
        }

        return faces;
    }

    private static int Int(ReadOnlySpan<byte> s, ref int p)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(s[p..]);
        p += 4;
        return v;
    }

    /// <summary>Tek bir LZ4 bloğunu (çerçevesiz) açar.</summary>
    private static byte[] Lz4Block(ReadOnlySpan<byte> src, int outputLength)
    {
        var dst = new byte[outputLength];
        int i = 0, o = 0;

        while (i < src.Length && o < outputLength)
        {
            int token = src[i++];

            var literal = token >> 4;
            if (literal == 15)
            {
                byte b;
                do { b = src[i++]; literal += b; } while (b == 255);
            }

            src.Slice(i, literal).CopyTo(dst.AsSpan(o));
            i += literal;
            o += literal;
            if (o >= outputLength || i >= src.Length) break;

            var offset = src[i] | (src[i + 1] << 8);
            i += 2;
            if (offset == 0 || offset > o) throw new InvalidDataException("LZ4: geçersiz geri başvuru.");

            var match = (token & 15) + 4;
            if ((token & 15) == 15)
            {
                byte b;
                do { b = src[i++]; match += b; } while (b == 255);
            }

            for (var k = 0; k < match; k++, o++) dst[o] = dst[o - offset];
        }

        if (o != outputLength) throw new InvalidDataException("LZ4: beklenen uzunluk tutmadı.");
        return dst;
    }
}
