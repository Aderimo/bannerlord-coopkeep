using System.Xml.Linq;

namespace CoopKeep.Core.Mods;

/// <summary>Oyun başlatıcısındaki tek oyunculu mod listesinde bir satır.</summary>
public sealed record LauncherMod(string Id, bool IsSelected);

/// <summary>
/// Oyun başlatıcısının mod listesi (<c>Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml</c>):
/// hangi modların açık olduğu ve yüklenme sırası.
/// </summary>
/// <remarks>
/// <para>
/// Oyun açıkken dosya değiştirilmemeli: başlatıcı kapanırken kendi listesini üstüne yazar.
/// Yalnızca <c>SingleplayerData</c> bölümüne dokunulur; çok oyunculu liste aynı kalır.
/// </para>
/// <para>
/// Her yazımdan önce bir önceki hâl <c>LauncherData.xml.coopkeep-last</c> olarak saklanır.
/// </para>
/// </remarks>
public static class LauncherProfile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mount and Blade II Bannerlord", "Configs", "LauncherData.xml");

    public static string LastBackupPath(string path) => path + ".coopkeep-last";

    /// <summary>Listeyi dosyadaki sırayla okur; dosya yoksa boş.</summary>
    public static IReadOnlyList<LauncherMod> Read(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return [];

        try
        {
            var doc = XDocument.Load(path);
            return Entries(doc)
                .Select(e => new LauncherMod(e.Element("Id")?.Value?.Trim() ?? "",
                    string.Equals(e.Element("IsSelected")?.Value, "true", StringComparison.OrdinalIgnoreCase)))
                .Where(m => m.Id.Length > 0)
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>
    /// Listeyi verilen sırayla ve açık/kapalı durumlarıyla yazar.
    /// </summary>
    /// <remarks>
    /// Listede olmayan ama dosyada bulunan modlar sona, kapalı olarak taşınır (silinmez).
    /// Dosyada olmayan modlar için yeni satır eklenir. Dosya hiç yoksa oluşturulur.
    /// </remarks>
    public static void Write(IReadOnlyList<LauncherMod> mods, string? path = null)
    {
        path ??= DefaultPath;
        XDocument doc;
        if (File.Exists(path))
        {
            doc = XDocument.Load(path);
            File.Copy(path, LastBackupPath(path), overwrite: true);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            doc = new XDocument(new XElement("UserData",
                new XElement("GameType", "Singleplayer"),
                new XElement("SingleplayerData", new XElement("ModDatas")),
                new XElement("MultiplayerData", new XElement("ModDatas"))));
        }

        var container = ModDatas(doc);
        var existing = container.Elements("UserModData")
            .GroupBy(e => e.Element("Id")?.Value?.Trim() ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<XElement>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (!written.Add(mod.Id)) continue;
            var element = existing.TryGetValue(mod.Id, out var e)
                ? new XElement(e)
                : new XElement("UserModData", new XElement("Id", mod.Id), new XElement("LastKnownVersion", ""), new XElement("IsSelected", "false"));

            var selected = element.Element("IsSelected");
            if (selected is null) element.Add(selected = new XElement("IsSelected"));
            selected.Value = mod.IsSelected ? "true" : "false";
            ordered.Add(element);
        }

        foreach (var (id, element) in existing)
        {
            if (written.Contains(id)) continue;
            var copy = new XElement(element);
            copy.Element("IsSelected")?.SetValue("false");
            ordered.Add(copy);
        }

        container.RemoveNodes();
        container.Add(ordered);

        var temp = path + ".tmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }

    private static IEnumerable<XElement> Entries(XDocument doc) =>
        doc.Root?.Element("SingleplayerData") is { } single
            ? single.Descendants("UserModData")
            : doc.Descendants("UserModData");

    private static XElement ModDatas(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("LauncherData.xml has no root.");
        var single = root.Element("SingleplayerData");
        if (single is null) root.Add(single = new XElement("SingleplayerData"));
        var datas = single.Element("ModDatas");
        if (datas is null) single.Add(datas = new XElement("ModDatas"));
        return datas;
    }
}

/// <summary>Başlatıcı için yükleme sırası: resmî modüller önce, sonra bağımlılık sırasıyla modlar.</summary>
public static class LoadOrder
{
    /// <summary>Oyunun resmî modülleri, oyunun kendi sırasıyla.</summary>
    public static readonly IReadOnlyList<string> Official =
        ["Native", "SandBoxCore", "BirthAndDeath", "CustomBattle", "Sandbox", "StoryMode", "FastMode", "NavalDLC", "Multiplayer"];

    public const string CoopKeepId = "CoopKeep";

    /// <summary>En sona yüklenenler: co-op modları (diğer modları senkron edebilmek için).</summary>
    public static readonly IReadOnlyList<string> LoadLast = [CoopKeepId, "Coop", "CoopNightly"];

    /// <summary>
    /// Kimlikleri yükleme sırasına dizer: resmî modüller kendi aralarındaki sırayla, diğerleri
    /// <c>DependedModules</c> / <c>ModulesToLoadAfterThis</c>'e göre, co-op modları en sona.
    /// Eşitlikte verilen sıra korunur.
    /// </summary>
    /// <remarks>
    /// Harmony, ButterLib, UIExtenderEx, MCM gibi kütüphaneler resmî modüllerden <b>önce</b>
    /// yüklenmek ister (<c>Native</c> vb. için "LoadAfterThis"). Eskiden resmî modüller her zaman
    /// başa konuyordu; ButterLib oyun açılırken "Native'den önce yüklenmeli, oyunu kapat"
    /// uyarısı veriyordu (kullanıcı bildirdi, 2026-10-23). Artık böyle modlar ve onların
    /// gerektirdikleri resmî modüllerin önüne alınır.
    /// </remarks>
    /// <param name="ids">Sıralanacak modlar (mevcut sıra).</param>
    /// <param name="installed">Bilgisayardaki modlar (bağımlılık bilgisi için).</param>
    /// <param name="conflict">Döngü varsa (iki mod birbirinden önce olmak istiyor) <see langword="true"/>.</param>
    public static IReadOnlyList<string> Sort(IReadOnlyList<string> ids, IReadOnlyList<InstalledModule> installed, out bool conflict)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var byId = installed.GroupBy(m => m.Id, comparer).ToDictionary(g => g.Key, g => g.First(), comparer);

        var official = Official.Where(o => ids.Contains(o, comparer)).ToList();
        var last = LoadLast.Where(id => ids.Contains(id, comparer)).ToList();
        var mods = ids.Where(i => !Official.Contains(i, comparer) && !LoadLast.Contains(i, comparer))
            .Distinct(comparer).ToList();

        var nodes = official.Concat(mods).Concat(last).ToList();
        var set = new HashSet<string>(nodes, comparer);
        string Node(string id) => nodes.First(x => comparer.Equals(x, id));

        IEnumerable<string> Before(InstalledModule m) =>
            m.LoadAfter.Concat(m.Dependencies.Where(d => !d.Optional).Select(d => d.Id));

        // Resmî modüllerden önce yüklenmek isteyenler ve (zinciri bozmasınlar diye) gerektirdikleri.
        var early = new HashSet<string>(comparer);
        var queue = new Queue<string>(mods.Where(id => byId.TryGetValue(id, out var m) && m.LoadBefore.Any(b => Official.Contains(b, comparer))));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!early.Add(id) || !byId.TryGetValue(id, out var m)) continue;
            foreach (var dep in Before(m))
                if (mods.Contains(dep, comparer) && !early.Contains(dep)) queue.Enqueue(Node(dep));
        }

        var edges = new HashSet<(string Before, string After)>();
        void Edge(string before, string after)
        {
            if (!comparer.Equals(before, after)) edges.Add((Node(before), Node(after)));
        }

        // Resmî modüller kendi sıralarında; co-op modları her şeyden sonra.
        for (var i = 0; i + 1 < official.Count; i++) Edge(official[i], official[i + 1]);
        foreach (var l in last)
            foreach (var n in official.Concat(mods)) Edge(n, l);

        foreach (var id in mods)
        {
            if (early.Contains(id))
            {
                if (official.Count > 0) Edge(id, official[0]);
            }
            else if (official.Count > 0)
            {
                Edge(official[^1], id);
            }

            if (!byId.TryGetValue(id, out var m)) continue;
            foreach (var before in Before(m))
                if (set.Contains(before)) Edge(before, id);
            // Resmî modülden önce olmak isteyen zaten "early"de; diğer istekler mod-mod arası.
            foreach (var after in m.LoadBefore)
                if (set.Contains(after)) Edge(id, after);
        }

        var indegree = nodes.ToDictionary(n => n, _ => 0, comparer);
        foreach (var (_, after) in edges) indegree[after]++;

        var result = new List<string>();
        var remaining = new List<string>(nodes);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(n => indegree[n] == 0);
            if (next is null) break;
            remaining.Remove(next);
            result.Add(next);
            foreach (var (before, after) in edges)
                if (comparer.Equals(before, next)) indegree[after]--;
        }

        conflict = remaining.Count > 0;
        result.AddRange(remaining);
        return result;
    }
}
