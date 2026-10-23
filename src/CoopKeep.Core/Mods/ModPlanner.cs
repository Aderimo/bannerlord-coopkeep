namespace CoopKeep.Core.Mods;

/// <summary>
/// Dünyanın mod seçiminden sunucunun açılış planını çıkarır: bağımlılıkları ekler,
/// kurulum kiplerini seçer ve modül sırasını kurar.
/// </summary>
/// <remarks>
/// <para>
/// Coop'un doğrulayıcısı (<c>GameInterface.Services.Modules.Validators.ModuleValidator</c>,
/// v0.1.6) resmî olmayan her modülün sunucuda <b>ve</b> oyuncuda, <b>aynı sürümle</b>
/// bulunmasını şart koşuyor; sıraya bakmıyor. Sıra yalnızca motorun modları doğru
/// yüklemesi için önemli.
/// </para>
/// <para>
/// Özel sunucunun kendi listesi <c>Native, SandBoxCore, Sandbox, Coop, DedicatedServer.Windows</c>;
/// modlar varsayılan olarak Sandbox ile Coop arasına girer. Bir mod resmî modüllerden
/// önce yüklenmek istiyorsa (Harmony) onun isteği uygulanır.
/// </para>
/// </remarks>
public static class ModPlanner
{
    /// <summary>Özel sunucunun her zaman yüklediği resmî modüller, sırasıyla.</summary>
    public static readonly IReadOnlyList<string> BaseModules = ["Native", "SandBoxCore", "Sandbox"];

    /// <summary>Sunucuya özel modül; her zaman en sonda.</summary>
    public const string ServerModule = "DedicatedServer.Windows";

    /// <summary>
    /// Yalnızca istemcide anlamı olan, sunucuda kodu çalışamayan bilinen kütüphaneler.
    /// Otomatik kipte "yalnızca kimlik" kurulurlar.
    /// </summary>
    public static readonly IReadOnlySet<string> ClientOnlyLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
        "Bannerlord.BLSE", "Bannerlord.ModuleLoader",
    };

    /// <summary>Sunucuda olmayan ama oyuncunun açık tutabileceği resmî modüller; bağımlılık olarak sayılmaz.</summary>
    private static readonly IReadOnlySet<string> OfficialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "Multiplayer", "BirthAndDeath", "NavalDLC",
        "FastMode", ServerModule,
    };

    /// <summary>Otomatik kipte modun nasıl kurulacağı.</summary>
    /// <param name="clientOnlyHint">Topluluk listesi bu modu "yalnız istemci" olarak işaretlemiş mi?</param>
    public static ModInstallMode ResolveMode(InstalledModule module, ModInstallMode requested, bool clientOnlyHint = false)
    {
        if (requested == ModInstallMode.Full && !module.HasServerBinaries && module.HasCode)
            requested = ModInstallMode.Auto; // sunucu kodu yok: "tam" olamaz

        if (requested == ModInstallMode.RunCode && !module.HasCode)
            requested = ModInstallMode.Full; // çalıştırılacak kod yok: saf veri modu

        if (requested != ModInstallMode.Auto) return requested;

        if (!module.HasCode) return ModInstallMode.Full;            // saf veri modu: olduğu gibi
        if (module.HasServerBinaries) return ModInstallMode.Full;   // sunucu için derlenmiş
        if (clientOnlyHint || ClientOnlyLibraries.Contains(module.Id)) return ModInstallMode.IdOnly;
        return ModInstallMode.DataOnly;
    }

    /// <param name="selection">Kullanıcının seçtikleri, seçim sırasıyla.</param>
    /// <param name="installed">Bilgisayardaki modlar (<see cref="ModuleScanner.Scan"/>).</param>
    /// <param name="coopModuleId">Sunucudaki Coop modülünün kimliği (<c>Coop</c> ya da <c>CoopNightly</c>).</param>
    /// <param name="clientOnly">Topluluk listesinde "yalnız istemci" işaretli kimlikler.</param>
    public static ModPlan Plan(
        IReadOnlyList<WorldMod> selection,
        IReadOnlyList<InstalledModule> installed,
        string coopModuleId = "Coop",
        IReadOnlySet<string>? clientOnly = null)
    {
        var byId = installed.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var chosen = new List<PlannedMod>();
        var chosenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var missingDeps = new List<(string, string)>();

        void Add(InstalledModule module, ModInstallMode requested, bool asDependency)
        {
            if (!chosenIds.Add(module.Id)) return;
            var mode = ResolveMode(module, requested, clientOnly?.Contains(module.Id) == true);
            chosen.Add(new PlannedMod(module, mode, asDependency));

            foreach (var dep in module.Dependencies)
            {
                if (OfficialIds.Contains(dep.Id) || dep.Id.Equals(coopModuleId, StringComparison.OrdinalIgnoreCase) ||
                    dep.Id.Equals("Coop", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (byId.TryGetValue(dep.Id, out var d))
                {
                    // Bağımlılıklar da otomatik kipte eklenir; kullanıcı ayrıca seçtiyse onun kipi geçerli.
                    var explicitMode = selection.FirstOrDefault(s => s.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase))?.Mode
                                       ?? ModInstallMode.Auto;
                    Add(d, explicitMode, asDependency: selection.All(s => !s.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase)));
                }
                else if (!dep.Optional)
                {
                    missingDeps.Add((module.Id, dep.Id));
                }
            }
        }

        foreach (var s in selection)
        {
            if (byId.TryGetValue(s.Id, out var m) && m.IsSelectable) Add(m, s.Mode, asDependency: false);
            else if (!byId.ContainsKey(s.Id)) missing.Add(s.Id);
        }

        var (ordered, conflict) = Order(chosen, coopModuleId);

        return new ModPlan
        {
            Mods = ordered,
            ModuleOrder = BuildModuleOrder(ordered, coopModuleId, out var orderConflict),
            MissingMods = missing,
            MissingDependencies = missingDeps.Distinct().ToList(),
            OrderConflict = conflict || orderConflict,
        };
    }

    /// <summary>
    /// Bir modun sunucuya eklenince gerektirdiği bütün modlar (kendisi dahil, bağımlılık önce).
    /// Coop'ta oyuncunun da bunların hepsine sahip olması gerekir.
    /// </summary>
    public static ModPlan ForMod(string modId, IReadOnlyList<InstalledModule> installed, string coopModuleId = "Coop") =>
        Plan([new WorldMod(modId)], installed, coopModuleId);

    /// <summary>
    /// <paramref name="worldModIds"/> içinden <paramref name="modId"/>'ye ihtiyaç duyanlar
    /// (doğrudan ya da dolaylı). Mod dünyadan çıkarılırsa bunlar çalışmaz.
    /// </summary>
    public static IReadOnlyList<string> NeededBy(
        string modId, IEnumerable<string> worldModIds, IReadOnlyList<InstalledModule> installed, string coopModuleId = "Coop") =>
        worldModIds
            .Where(id => !id.Equals(modId, StringComparison.OrdinalIgnoreCase))
            .Where(id => ForMod(id, installed, coopModuleId).Mods.Any(m => m.Module.Id.Equals(modId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Modları kendi aralarında sıralar (bağımlılık önce).</summary>
    private static (IReadOnlyList<PlannedMod>, bool conflict) Order(List<PlannedMod> mods, string coopId)
    {
        var ids = mods.Select(m => m.Module.Id).ToList();
        var edges = BuildEdges(mods.Select(m => m.Module), ids);
        var sorted = TopologicalSort(ids, edges, out var conflict);
        var byId = mods.ToDictionary(m => m.Module.Id, StringComparer.OrdinalIgnoreCase);
        return (sorted.Select(id => byId[id]).ToList(), conflict);
    }

    /// <summary>Tam liste: resmî modüller + modlar + Coop + sunucu modülü.</summary>
    private static IReadOnlyList<string> BuildModuleOrder(IReadOnlyList<PlannedMod> mods, string coopId, out bool conflict)
    {
        var nodes = new List<string>(BaseModules);
        nodes.AddRange(mods.Select(m => m.Module.Id));
        nodes.Add(coopId);
        nodes.Add(ServerModule);

        var edges = BuildEdges(mods.Select(m => m.Module), nodes);

        // Resmî sıra ve Coop sabit.
        for (var i = 0; i + 1 < BaseModules.Count; i++) edges.Add((BaseModules[i], BaseModules[i + 1]));
        edges.Add((BaseModules[^1], coopId));
        edges.Add((coopId, ServerModule));

        foreach (var m in mods)
        {
            // Varsayılan: Sandbox'tan sonra, Coop'tan önce — mod resmî bir modülden önce
            // yüklenmek istemedikçe (Harmony: ModulesToLoadAfterThis = Native…).
            var wantsBeforeBase = m.Module.LoadBefore.Any(b => BaseModules.Contains(b, StringComparer.OrdinalIgnoreCase));
            if (!wantsBeforeBase) edges.Add((BaseModules[^1], m.Module.Id));
            edges.Add((m.Module.Id, coopId));
        }

        return TopologicalSort(nodes, edges, out conflict);
    }

    /// <summary>"a, b'den önce yüklenmeli" kenarları; yalnızca listedeki düğümler arasında.</summary>
    private static HashSet<(string Before, string After)> BuildEdges(IEnumerable<InstalledModule> mods, IReadOnlyCollection<string> nodes)
    {
        var set = new HashSet<string>(nodes, StringComparer.OrdinalIgnoreCase);
        var edges = new HashSet<(string, string)>(new PairComparer());

        foreach (var m in mods)
        {
            // Bağımlılık her zaman önce yüklenir (tarayıcı bunu LoadAfter'a da yazıyor; elle
            // kurulmuş modüller için ayrıca).
            foreach (var before in m.LoadAfter.Concat(m.Dependencies.Select(d => d.Id)))
                if (set.Contains(before) && !before.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                    edges.Add((Canonical(nodes, before), m.Id));

            foreach (var after in m.LoadBefore)
                if (set.Contains(after) && !after.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                    edges.Add((m.Id, Canonical(nodes, after)));
        }

        return edges;
    }

    private static string Canonical(IReadOnlyCollection<string> nodes, string id) =>
        nodes.First(n => n.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kahn sıralaması; eşitlikte özgün sıra korunur. Döngü varsa kalanlar özgün sırayla
    /// sona eklenir ve <paramref name="conflict"/> işaretlenir.
    /// </summary>
    private static List<string> TopologicalSort(IReadOnlyList<string> nodes, HashSet<(string Before, string After)> edges, out bool conflict)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
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

    private sealed class PairComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);

        public int GetHashCode((string, string) obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
