namespace Root33.Discover;

/// <summary>
/// Root33.Discover: reads a folder of .NET assemblies as metadata (nothing runs) and writes types.json (every type with its
/// namespace, base type, interfaces, fields, properties, methods and signatures) and report.md (types and members named
/// after Root concepts, grouped by concept, and the likely game-flow types).
///
///   dotnet run --project tools/Root33.Discover -- &lt;assemblyFolder&gt; &lt;outFolder&gt; [--ref &lt;folder&gt;]... [--all] [--public-only]
///
/// For Root under BepInEx 6 IL2CPP the folder is BepInEx\interop (BepInEx\core next to it is added as a reference folder by
/// itself); for a Mono build it is Root_Data\Managed.
/// </summary>
public static class Program
{
    public const string Usage =
        "usage: Root33.Discover <assemblyFolder> <outFolder> [--ref <folder>]... [--all] [--public-only]\n" +
        "  <assemblyFolder>  BepInEx\\interop (IL2CPP) or Root_Data\\Managed (Mono): every *.dll in it is read as metadata\n" +
        "  <outFolder>       where types.json and report.md go (created if missing)\n" +
        "  --ref <folder>    another folder to resolve references from (BepInEx\\core next to an interop folder is added by itself)\n" +
        "  --all             report on every assembly, not only the game's own (types.json always has every assembly)\n" +
        "  --public-only     leave out non-public types and members";

    public static int Main(string[] args)
    {
        try { return Run(args, Console.Out); }
        catch (Exception e)
        {
            Console.Error.WriteLine("Root33.Discover failed: " + e.Message);
            return 2;
        }
    }

    public static int Run(string[] args, TextWriter output)
    {
        var positional = new List<string>();
        var refs = new List<string>();
        bool all = false, publicOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ref" when i + 1 < args.Length: refs.Add(args[++i]); break;
                case "--all": all = true; break;
                case "--public-only": publicOnly = true; break;
                case "-h": case "--help": output.WriteLine(Usage); return 0;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal)) { output.WriteLine("unknown option " + args[i]); output.WriteLine(Usage); return 1; }
                    positional.Add(args[i]);
                    break;
            }
        }
        if (positional.Count != 2) { output.WriteLine(Usage); return 1; }
        var folder = Path.GetFullPath(positional[0]);
        var outFolder = Path.GetFullPath(positional[1]);
        if (!Directory.Exists(folder)) { output.WriteLine($"no such folder: {positional[0]}"); return 1; }
        if (Directory.GetFiles(folder, "*.dll").Length == 0) { output.WriteLine($"no .dll files in {positional[0]}"); return 1; }

        // BepInEx 6 layout: interop assemblies reference Il2CppInterop.Runtime and friends, which live in BepInEx\core.
        var sibling = Path.Combine(Path.GetDirectoryName(folder) ?? folder, "core");
        if (Directory.Exists(sibling) && !refs.Contains(sibling)) refs.Add(sibling);

        var result = Discover(folder, outFolder, refs, all, publicOnly);
        output.WriteLine($"Read {result.Assemblies} assemblies, {result.Types} types ({result.GameTypes} in the game's own assemblies).");
        output.WriteLine($"Wrote {Path.Combine(outFolder, "types.json")}");
        output.WriteLine($"Wrote {Path.Combine(outFolder, "report.md")}");
        if (result.Unresolved > 0)
            output.WriteLine($"{result.Unresolved} references could not be resolved (listed at the top of report.md; add their folder with --ref).");
        return 0;
    }

    public sealed record Result(int Assemblies, int Types, int GameTypes, int Unresolved);

    /// <summary>Scans <paramref name="folder"/> and writes types.json and report.md into <paramref name="outFolder"/>.</summary>
    public static Result Discover(string folder, string outFolder, IEnumerable<string> referenceFolders, bool allAssemblies = false, bool publicOnly = false)
    {
        var scan = new Scanner { PublicOnly = publicOnly };
        scan.Scan(folder, referenceFolders);
        Directory.CreateDirectory(outFolder);
        // Only the folder's own name goes into the files: they may be shared, and a full path can hold private names.
        var source = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        TypesJson.Write(Path.Combine(outFolder, "types.json"), source, scan);
        File.WriteAllText(Path.Combine(outFolder, "report.md"), new Report { AllAssemblies = allAssemblies }.Build(source, scan));
        return new Result(scan.Assemblies.Count, scan.Types.Count, scan.Types.Count(t => t.Game), scan.Unresolved.Values.Sum());
    }
}
