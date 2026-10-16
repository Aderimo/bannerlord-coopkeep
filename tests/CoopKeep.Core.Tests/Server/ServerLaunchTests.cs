using CoopKeep.Core.Installation;
using CoopKeep.Core.Server;

namespace CoopKeep.Core.Tests.Server;

/// <summary>
/// Sunucu lisans sözleşmesi (EULA) ile ilgili davranışlar.
/// </summary>
/// <remarks>
/// Coop v0.1.6 ile sunucu, onay olmadan <c>exit 6</c> ile kapanıyor (2026-10-16'da
/// temiz bir veri klasöründe doğrulandı). v0.1.0 bu kodu tanımıyordu ve yeni
/// kullanıcılar sunucuyu hiç açamıyordu.
/// </remarks>
public sealed class ServerLaunchTests : IDisposable
{
    private readonly string _dizin = Path.Combine(Path.GetTempPath(), "coopkeep-eula-" + Guid.NewGuid().ToString("N"));

    public ServerLaunchTests() => Directory.CreateDirectory(_dizin);

    public void Dispose() => Directory.Delete(_dizin, recursive: true);

    [Fact]
    public void cikis_kodu_6_eula_gerekli_ve_yeniden_baslatilmaz()
    {
        Assert.Equal(ServerExitKind.EulaRequired, ServerExit.Classify(6));
        Assert.False(ServerExit.IsAutoRestartable(ServerExitKind.EulaRequired));
    }

    [Fact]
    public void eula_bayragi_yalnizca_onay_varsa_eklenir()
    {
        var onaysiz = new ServerLaunchOptions { ExecutablePath = "x.exe" }.BuildArguments();
        var onayli = new ServerLaunchOptions { ExecutablePath = "x.exe", AcceptEula = true }.BuildArguments();

        Assert.DoesNotContain("--accept-eula", onaysiz);
        Assert.Contains("--accept-eula", onayli);
    }

    [Fact]
    public void sunucunun_kaydettigi_onay_okunur()
    {
        Assert.False(CoopInstallation.HasAcceptedEula(_dizin));

        // Sunucunun yazdığı dosyanın biçimi (v0.1.6).
        File.WriteAllLines(Path.Combine(_dizin, "eula.txt"),
        [
            "# Read EULA.md and LICENSE.txt packaged with the server before accepting.",
            "eula-sha256=a3d1998b757feded9739a5c6127be51204c0942a92b601703da8081ccc42213d",
            "eula=true",
        ]);

        Assert.True(CoopInstallation.HasAcceptedEula(_dizin));
    }
}
