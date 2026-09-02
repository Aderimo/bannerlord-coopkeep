using System.Diagnostics;

namespace CoopKeep.Core.Server;

/// <summary>Sunucunun o anki kaynak kullanımı.</summary>
/// <param name="MemoryBytes">Süreç ağacının toplam çalışma kümesi.</param>
/// <param name="CpuPercent">Son örnekten bu yana ortalama CPU kullanımı (tüm çekirdekler üzerinden).</param>
/// <param name="ProcessCount">Ölçüme dahil edilen süreç sayısı.</param>
public readonly record struct ServerResources(long MemoryBytes, double CpuPercent, int ProcessCount)
{
    public double MemoryGigabytes => MemoryBytes / 1024d / 1024d / 1024d;

    public static ServerResources Empty => new(0, 0, 0);
}

/// <summary>
/// Sunucu süreç ağacının bellek ve CPU kullanımını ölçer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden süreç ağacı?</b> <c>BannerlordCoopServer.exe</c> yalnızca bir başlatıcı;
/// asıl oyun motorunu ayrı bir çocuk süreç olarak açıyor ve belleğin neredeyse tamamı
/// orada. Sadece başlatıcıyı ölçmek birkaç megabayt gösterirdi.
/// </para>
/// <para>
/// Süreçler <b>yol eşleşmesiyle</b> bulunuyor: yürütülebilir dosyası sunucunun kendi
/// klasörünün altında olan süreçler. Ada göre eşleştirmek tehlikeli olurdu — kullanıcının
/// aynı anda çalışan oyun istemcisi de benzer adlar taşıyor ve yanlışlıkla sayılırdı.
/// </para>
/// <para>
/// <b>Not:</b> Bannerlord dedicated server'ının bellek sınırı ayarı yoktur; bu sınıf
/// yalnızca <i>ölçer</i>, sınırlamaz.
/// </para>
/// </remarks>
public sealed class ServerResourceMonitor(string serverRootDirectory)
{
    private readonly string _root = NormalizeDirectory(serverRootDirectory);

    private TimeSpan _previousCpuTime;
    private DateTimeOffset _previousSampleAt;
    private bool _hasPreviousSample;

    /// <summary>Anlık ölçüm alır.</summary>
    /// <param name="rootProcessId">Başlatıcı sürecin kimliği; ağacın kökü.</param>
    public ServerResources Sample(int? rootProcessId)
    {
        long memory = 0;
        var cpuTime = TimeSpan.Zero;
        var count = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!BelongsToServer(process, rootProcessId)) continue;

                memory += process.WorkingSet64;
                cpuTime += process.TotalProcessorTime;
                count++;
            }
            catch (Exception e) when (e is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
            {
                // Süreç bu arada kapandı ya da erişim reddedildi; atla.
            }
            finally
            {
                process.Dispose();
            }
        }

        var now = DateTimeOffset.UtcNow;
        var cpuPercent = 0d;

        if (_hasPreviousSample && count > 0)
        {
            var wall = (now - _previousSampleAt).TotalMilliseconds;
            var used = (cpuTime - _previousCpuTime).TotalMilliseconds;

            if (wall > 0)
                cpuPercent = Math.Clamp(used / (wall * Environment.ProcessorCount) * 100d, 0, 100);
        }

        _previousCpuTime = cpuTime;
        _previousSampleAt = now;
        _hasPreviousSample = count > 0;

        return count == 0 ? ServerResources.Empty : new ServerResources(memory, cpuPercent, count);
    }

    /// <summary>Sunucu durduğunda çağrılır; sonraki ölçümün yanlış CPU vermesini önler.</summary>
    public void Reset()
    {
        _hasPreviousSample = false;
        _previousCpuTime = TimeSpan.Zero;
    }

    private bool BelongsToServer(Process process, int? rootProcessId)
    {
        if (rootProcessId is { } root && process.Id == root) return true;
        if (_root.Length == 0) return false;

        var path = process.MainModule?.FileName;
        return path is not null && path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return string.Empty;

        var full = Path.GetFullPath(directory);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
