using System.Diagnostics;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// Sunucu süreçlerine uygulanacak öncelik; <see langword="null"/> ise dokunulmaz.
    /// </summary>
    /// <remarks>
    /// Sunucu belleğini sınırlamanın ya da önceden "vermenin" bir yolu yok; Windows bellek
    /// ihtiyaç duyuldukça verir. Takılmayı azaltmak için elimizdeki gerçek araç işlemci
    /// önceliği. Asıl motor başlatıcının <b>çocuk süreci</b> olduğu ve öncelik çocuğa
    /// geçmediği için her ölçümde ağacın tamamına uygulanıyor.
    /// </remarks>
    public ProcessPriorityClass? DesiredPriority { get; set; }

    /// <summary>Son taramada sunucuya ait bulunan süreçler.</summary>
    private readonly HashSet<int> _serverPids = [];
    private DateTimeOffset _lastScanAt;

    /// <summary>
    /// Bütün süreçlerin yeniden taranma aralığı. Her ölçümde bilgisayardaki yüzlerce sürecin
    /// yolu okunuyordu; arada yalnızca bilinen sunucu süreçleri ölçülür.
    /// </summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

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

        var now = DateTimeOffset.UtcNow;
        var rescan = _serverPids.Count == 0 || now - _lastScanAt > ScanInterval;
        if (rescan) _lastScanAt = now;
        var found = new HashSet<int>();

        foreach (var process in rescan ? Process.GetProcesses() : KnownProcesses(rootProcessId))
        {
            try
            {
                if (!BelongsToServer(process, rootProcessId)) continue;
                found.Add(process.Id);

                if (DesiredPriority is { } priority && process.PriorityClass != priority)
                    process.PriorityClass = priority;

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

        // Süreç kümesi değiştiyse (çocuk süreç açıldı/kapandı) CPU farkı anlamsız olur.
        var sameSet = found.SetEquals(_serverPids);
        _serverPids.Clear();
        _serverPids.UnionWith(found);

        var cpuPercent = 0d;

        if (_hasPreviousSample && sameSet && count > 0)
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
        _serverPids.Clear();
    }

    private IEnumerable<Process> KnownProcesses(int? rootProcessId)
    {
        var ids = new HashSet<int>(_serverPids);
        if (rootProcessId is { } root) ids.Add(root);
        foreach (var id in ids)
        {
            Process? process = null;
            try { process = Process.GetProcessById(id); }
            catch (ArgumentException) { }
            if (process is not null) yield return process;
        }
    }

    private bool BelongsToServer(Process process, int? rootProcessId)
    {
        if (rootProcessId is { } root && process.Id == root) return true;
        if (_root.Length == 0) return false;

        var path = ImagePath(process.Id);
        return path is not null && path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sürecin dosya yolu. <c>Process.MainModule</c> sürecin bütün modüllerini dolaşıyor (o sırada
    /// oynanan oyun dahil); bu çağrı yalnızca yolu sorar ve sınırlı erişim yeter.
    /// </summary>
    private static string? ImagePath(int pid)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, [Out] char[] buffer, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static string NormalizeDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return string.Empty;

        var full = Path.GetFullPath(directory);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
