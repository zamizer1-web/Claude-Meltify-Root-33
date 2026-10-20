using System.Text;

namespace Root33.Discover;

/// <summary>
/// report.md: where Root keeps what Root 33 needs. Types and members whose names hold a Root concept (clearing, faction,
/// battle, birdsong, dominance, ...), grouped by concept, and the likely game-flow types (singletons, managers,
/// controllers, state machines), all from the game's own assemblies unless <c>--all</c> asks for every assembly.
/// </summary>
internal sealed class Report
{
    /// <summary>Per concept: types listed, then members listed (the rest is counted and left to types.json).</summary>
    public int TypesPerConcept { get; init; } = 60;
    public int MembersPerConcept { get; init; } = 120;
    public bool AllAssemblies { get; init; }

    /// <summary>Type names that suggest the game's flow: who runs turns, phases, the board, the AI and the menus.</summary>
    private static readonly string[] FlowSuffixes =
    {
        "Manager", "Controller", "Ctrl", "StateMachine", "Machine", "Fsm", "FSM", "State", "States", "Director", "Coordinator",
        "Sequencer", "Flow", "Loop", "System", "Service", "Handler", "Engine", "Game", "Session", "Match", "Simulation", "Runner",
        "Driver", "Brain", "Planner", "Executor", "Resolver", "Processor", "Tracker", "Model", "Rules",
    };

    private static readonly HashSet<string> SingletonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Instance", "_instance", "s_Instance", "s_instance", "m_Instance", "instance", "Singleton", "Current", "Main",
    };

    public string Build(string source, Scanner scan)
    {
        var types = scan.Types.Where(t => AllAssemblies || t.Game).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Root 33 discovery report");
        sb.AppendLine();
        sb.AppendLine($"Read from `{source}`: {scan.Assemblies.Count} assemblies, {scan.Types.Count} types " +
                      $"({types.Count} in the {(AllAssemblies ? "report (every assembly)" : "game's own assemblies")}). " +
                      "Metadata only: nothing in these assemblies ran.");
        sb.AppendLine();
        sb.AppendLine("How to use it: find each Root concept below, open the types in `types.json` (one type per line; " +
                      "`grep '\"name\":\"<Type>\"' types.json`), confirm them in the game (UnityExplorer, or the F9 probe file " +
                      "`BepInEx/root33-probe.json`), then write the confirmed `engineSymbol` / `engineClearingId` / `patchKind` " +
                      "into the sheets and clear their `_unverified` entries (`python3 -I tools/preflight.py --target plugin`).");
        sb.AppendLine();

        Assemblies(sb, scan);
        Summary(sb, types);
        Flow(sb, types);
        Enums(sb, types);
        Concepts(sb, types);
        return sb.ToString();
    }

    private void Assemblies(StringBuilder sb, Scanner scan)
    {
        sb.AppendLine("## Assemblies");
        sb.AppendLine();
        sb.AppendLine("| assembly | version | types | in report | note |");
        sb.AppendLine("|---|---|---:|---|---|");
        foreach (var a in scan.Assemblies.OrderByDescending(a => a.Game).ThenByDescending(a => a.Types))
            sb.AppendLine($"| {Md(a.Name)} | {a.Version} | {a.Types} | {(a.Game || AllAssemblies ? "yes" : "no (framework)")} | {Md(a.Error ?? "")} |");
        sb.AppendLine();
        if (scan.Unresolved.Count > 0)
        {
            sb.AppendLine("References the scan could not resolve (members using them read `<unresolved ...>`; pass the folder that " +
                          "holds them with `--ref <folder>`):");
            sb.AppendLine();
            foreach (var kv in scan.Unresolved.OrderByDescending(kv => kv.Value)) sb.AppendLine($"- `{kv.Key}`: {kv.Value}");
            sb.AppendLine();
        }
    }

    private static void Summary(StringBuilder sb, List<TypeEntry> types)
    {
        sb.AppendLine("## Concepts at a glance");
        sb.AppendLine();
        sb.AppendLine("| concept | types | members |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var c in Concept.All)
        {
            var nt = types.Count(t => c.Matches(t.Name));
            var nm = types.Sum(t => t.Members().Count(m => c.Matches(m.Name)));
            sb.AppendLine($"| [{c.Heading}](#concept-{Anchor(c.Heading)}) | {nt} | {nm} |");
        }
        sb.AppendLine();
    }

    private void Flow(StringBuilder sb, List<TypeEntry> types)
    {
        sb.AppendLine("## Likely game flow");
        sb.AppendLine();
        var singletons = types.Where(IsSingleton).ToList();
        sb.AppendLine($"### Singletons ({singletons.Count})");
        sb.AppendLine();
        sb.AppendLine("A static field or property of the type's own type (or a `*Singleton*` base): one live instance the plugin can read.");
        sb.AppendLine();
        foreach (var t in singletons.Take(150)) sb.AppendLine("- " + TypeLine(t));
        More(sb, singletons.Count, 150);
        sb.AppendLine();

        var flow = types.Where(t => t.Kind is "class" or "struct" && FlowSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
            .OrderByDescending(t => Concept.All.Count(c => c.Matches(t.Name)) + (IsSingleton(t) ? 2 : 0))
            .ThenBy(t => t.FullName, StringComparer.Ordinal).ToList();
        sb.AppendLine($"### Managers, controllers, state machines ({flow.Count})");
        sb.AppendLine();
        sb.AppendLine($"Names ending in {string.Join(", ", FlowSuffixes.Take(12))}, ...; the ones naming a Root concept first.");
        sb.AppendLine();
        foreach (var t in flow.Take(200)) sb.AppendLine("- " + TypeLine(t) + Counts(t));
        More(sb, flow.Count, 200);
        sb.AppendLine();

        var bases = types.Where(t => t.BaseType != null && t.BaseType != "object" && !t.BaseType.StartsWith("<unresolved", StringComparison.Ordinal))
            .GroupBy(t => t.BaseType!).Where(g => g.Count() >= 3).OrderByDescending(g => g.Count()).Take(40).ToList();
        sb.AppendLine("### Base types most derived from");
        sb.AppendLine();
        sb.AppendLine("A game framework shows here (actions, commands, states, views): the base type and how many types derive from it.");
        sb.AppendLine();
        foreach (var g in bases)
            sb.AppendLine($"- `{g.Key}` ({g.Count()}): {string.Join(", ", g.Take(8).Select(t => "`" + t.Name + "`"))}{(g.Count() > 8 ? ", ..." : "")}");
        sb.AppendLine();
    }

    private static bool IsSingleton(TypeEntry t) =>
        (t.BaseType?.Contains("Singleton", StringComparison.OrdinalIgnoreCase) ?? false) ||
        t.Fields.Any(f => f.Static && (f.Type == t.FullName || (SingletonNames.Contains(f.Name) && f.Type.EndsWith(t.Name, StringComparison.Ordinal)))) ||
        t.Properties.Any(p => p.Static && (p.Type == t.FullName || (SingletonNames.Contains(p.Name) && p.Type.EndsWith(t.Name, StringComparison.Ordinal))));

    private static void Enums(StringBuilder sb, List<TypeEntry> types)
    {
        var enums = types.Where(t => t.Kind == "enum" && (Concept.All.Any(c => c.Matches(t.Name)) || t.Fields.Any(f => Concept.All.Any(c => c.Matches(f.Name)))))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        sb.AppendLine($"## Enums naming a Root concept ({enums.Count})");
        sb.AppendLine();
        sb.AppendLine("Faction ids, phases, suits, piece types and AI levels usually live in enums; every value is listed.");
        sb.AppendLine();
        foreach (var t in enums.Take(300))
        {
            var values = string.Join(", ", t.Fields.Select(f => $"{f.Name}={f.Value}"));
            sb.AppendLine($"- `{t.FullName}` ({t.Assembly}): {Md(Trim(values, 600))}");
        }
        More(sb, enums.Count, 300);
        sb.AppendLine();
    }

    private void Concepts(StringBuilder sb, List<TypeEntry> types)
    {
        sb.AppendLine("## By concept");
        sb.AppendLine();
        foreach (var c in Concept.All)
        {
            sb.AppendLine($"<a id=\"concept-{Anchor(c.Heading)}\"></a>");
            sb.AppendLine($"### {c.Heading}");
            sb.AppendLine();
            var hitTypes = types.Where(t => c.Matches(t.Name))
                .OrderBy(t => t.Name.Length).ThenBy(t => t.FullName, StringComparer.Ordinal).ToList();
            sb.AppendLine($"Types ({hitTypes.Count}):");
            sb.AppendLine();
            foreach (var t in hitTypes.Take(TypesPerConcept)) sb.AppendLine("- " + TypeLine(t) + Counts(t));
            More(sb, hitTypes.Count, TypesPerConcept);
            sb.AppendLine();
            var hitMembers = types.SelectMany(t => t.Members().Where(m => c.Matches(m.Name)).Select(m => (t, m)))
                .OrderBy(x => x.m.Kind == "method" ? 1 : 0).ThenBy(x => x.t.FullName, StringComparer.Ordinal).ThenBy(x => x.m.Name, StringComparer.Ordinal)
                .ToList();
            sb.AppendLine($"Members ({hitMembers.Count}):");
            sb.AppendLine();
            foreach (var (t, m) in hitMembers.Take(MembersPerConcept))
                sb.AppendLine($"- `{t.FullName}.{m.Name}` {m.Kind}: `{Trim(m.Text, 200)}`");
            More(sb, hitMembers.Count, MembersPerConcept);
            sb.AppendLine();
        }
    }

    private static string TypeLine(TypeEntry t) =>
        $"`{t.FullName}` {t.Kind}{(t.BaseType != null && t.BaseType != "object" && t.Kind == "class" ? " : `" + t.BaseType + "`" : "")} ({t.Assembly})";

    private static string Counts(TypeEntry t) =>
        $" · {t.Fields.Count} fields, {t.Properties.Count} properties, {t.Methods.Count} methods";

    private static void More(StringBuilder sb, int total, int shown)
    {
        if (total > shown) sb.AppendLine($"- ... and {total - shown} more (see types.json)");
    }

    private static string Anchor(string heading) => heading.ToLowerInvariant().Replace(' ', '-');
    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + " ...";
    private static string Md(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
