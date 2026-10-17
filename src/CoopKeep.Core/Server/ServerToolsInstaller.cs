using System.Xml.Linq;

namespace CoopKeep.Core.Server;

/// <summary>
/// CoopKeep'in sunucu eklentisini (eşya ver/al komutları) Coop sunucusuna kurar ve kaldırır.
/// </summary>
/// <remarks>
/// <para>
/// Yayın sürümü sunucu, resmi olmayan <b>modülleri</b> reddediyor (çıkış kodu 4). Bu yüzden
/// eklenti ayrı bir modül değil, Coop modülünün <c>SubModule.xml</c>'ine eklenen bir alt
/// modül: modül listesi değişmez. DLL Coop'un <c>bin</c> klasörüne kopyalanır.
/// </para>
/// <para>
/// Coop'un lisansı, kişisel kullanım için yerel kopyada değişiklik yapmaya izin veriyor;
/// CoopKeep Coop dosyası dağıtmaz, yalnızca kullanıcının kendi kurulumuna kendi DLL'ini
/// ekler. Özgün <c>SubModule.xml</c> bir kez yedeklenir; kaldırınca geri yüklenir.
/// Steam Coop'u güncellerse dosyalar yenilenir ve eklenti kalkar — CoopKeep her
/// başlatmada kontrol edip yeniden kurar.
/// </para>
/// </remarks>
public static class ServerToolsInstaller
{
    public const string DllName = "CoopKeep.ServerTools.dll";
    public const string SubModuleName = "CoopKeep.ServerTools";
    public const string SubModuleClass = "CoopKeep.ServerTools.ServerToolsSubModule";
    public const string BackupSuffix = ".coopkeep-backup";

    /// <summary>Kurulu sunucunun Coop modülü klasörü.</summary>
    public static string CoopModuleDir(string dedicatedServerDir) =>
        Path.Combine(dedicatedServerDir, "engine", "Modules", "Coop");

    private static string SubModuleXml(string dedicatedServerDir) => Path.Combine(CoopModuleDir(dedicatedServerDir), "SubModule.xml");

    private static string TargetDll(string dedicatedServerDir) =>
        Path.Combine(CoopModuleDir(dedicatedServerDir), "bin", "Win64_Shipping_Server", DllName);

    /// <summary>Eklenti kurulu ve DLL güncel mi?</summary>
    public static bool IsInstalled(string dedicatedServerDir, string? pluginDll = null)
    {
        var xml = SubModuleXml(dedicatedServerDir);
        var dll = TargetDll(dedicatedServerDir);
        if (!File.Exists(xml) || !File.Exists(dll)) return false;
        if (!HasEntry(XDocument.Load(xml))) return false;

        return pluginDll is null || FilesEqual(pluginDll, dll);
    }

    /// <summary>Eklentiyi kurar (ya da günceller). Zaten kuruluysa dokunmaz.</summary>
    public static void Install(string dedicatedServerDir, string pluginDll)
    {
        var xmlPath = SubModuleXml(dedicatedServerDir);
        if (!File.Exists(xmlPath)) throw new FileNotFoundException("Coop SubModule.xml bulunamadı.", xmlPath);
        if (!File.Exists(pluginDll)) throw new FileNotFoundException("Eklenti DLL'i bulunamadı.", pluginDll);

        var dll = TargetDll(dedicatedServerDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        if (!File.Exists(dll) || !FilesEqual(pluginDll, dll)) File.Copy(pluginDll, dll, overwrite: true);

        var doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
        if (HasEntry(doc)) return;

        var backup = xmlPath + BackupSuffix;
        if (!File.Exists(backup)) File.Copy(xmlPath, backup);

        var subModules = doc.Root!.Element("SubModules");
        if (subModules is null)
        {
            subModules = new XElement("SubModules");
            doc.Root.Add(subModules);
        }

        subModules.Add(new XElement("SubModule",
            new XElement("Name", new XAttribute("value", SubModuleName)),
            new XElement("DLLName", new XAttribute("value", DllName)),
            new XElement("SubModuleClassType", new XAttribute("value", SubModuleClass)),
            new XElement("Tags")));

        var temp = xmlPath + ".tmp";
        doc.Save(temp);
        File.Move(temp, xmlPath, overwrite: true);
    }

    /// <summary>Eklentiyi kaldırır: özgün <c>SubModule.xml</c> geri gelir, DLL silinir.</summary>
    public static void Uninstall(string dedicatedServerDir)
    {
        var xmlPath = SubModuleXml(dedicatedServerDir);
        var backup = xmlPath + BackupSuffix;

        if (File.Exists(backup)) File.Move(backup, xmlPath, overwrite: true);
        else if (File.Exists(xmlPath))
        {
            var doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
            doc.Root?.Element("SubModules")?.Elements("SubModule")
                .Where(IsOurs).ToList().ForEach(e => e.Remove());
            doc.Save(xmlPath);
        }

        var dll = TargetDll(dedicatedServerDir);
        if (File.Exists(dll)) File.Delete(dll);
    }

    private static bool HasEntry(XDocument doc) =>
        doc.Root?.Element("SubModules")?.Elements("SubModule").Any(IsOurs) == true;

    private static bool IsOurs(XElement e) =>
        (string?)e.Element("DLLName")?.Attribute("value") == DllName;

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
