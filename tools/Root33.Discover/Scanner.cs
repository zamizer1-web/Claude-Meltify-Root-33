using System.Reflection;
using System.Runtime.InteropServices;

namespace Root33.Discover;

/// <summary>
/// Reads every assembly of a folder as metadata (System.Reflection.MetadataLoadContext: nothing in them runs) and turns each
/// type into a <see cref="TypeEntry"/>. References are resolved from the folder first, then from the extra reference
/// folders (BepInEx's core folder next to an interop folder is added on its own), then from this runtime's own framework, so
/// a Mono build's mscorlib and Unity assemblies win over .NET's. A reference that cannot be found makes only the members
/// that use it read "&lt;unresolved Name&gt;".
/// </summary>
internal sealed class Scanner
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Assemblies that are not Root's own code: the report leaves them out unless asked (types.json keeps them).</summary>
    private static readonly string[] FrameworkPrefixes =
    {
        "System", "Microsoft", "mscorlib", "netstandard", "WindowsBase", "Mono.", "Il2CppMono", "Il2Cppmscorlib", "Il2CppSystem",
        "Il2CppInterop", "UnityEngine", "Unity.", "BepInEx", "0Harmony", "HarmonyX", "MonoMod", "Iced", "Newtonsoft",
        "Cpp2IL", "AsmResolver", "SemanticVersioning", "LibCpp2IL", "StableNameDotNet", "Disarm", "WasmDisassembler", "Gee.",
    };

    /// <summary>Generated plumbing in IL2CPP interop assemblies (Il2CppInterop): pointers to native fields and methods.</summary>
    private static readonly string[] InteropNoise = { "NativeFieldInfoPtr_", "NativeMethodInfoPtr_", "NativeClassPtr", "NativeMethodInfoPtr" };

    public static bool IsFramework(string assemblyName) =>
        FrameworkPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public List<AssemblyEntry> Assemblies { get; } = new();
    public List<TypeEntry> Types { get; } = new();
    /// <summary>Members or parts that read "&lt;unresolved ...&gt;", by missing assembly name.</summary>
    public SortedDictionary<string, int> Unresolved { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool PublicOnly { get; init; }

    public void Scan(string folder, IEnumerable<string> referenceFolders)
    {
        var targets = Directory.GetFiles(folder, "*.dll").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.dll"))
                paths.TryAdd(Path.GetFileNameWithoutExtension(f), f);
        }
        Add(folder);
        foreach (var r in referenceFolders) Add(r);
        Add(RuntimeEnvironment.GetRuntimeDirectory());

        var resolver = new PathAssemblyResolver(paths.Values);
        // A Mono build brings its own mscorlib (Unity's); anything else (IL2CPP interop assemblies, .NET builds) uses this
        // runtime's System.Private.CoreLib, which the framework facades (mscorlib, System.Runtime, netstandard) forward to.
        var core = File.Exists(Path.Combine(folder, "mscorlib.dll")) ? "mscorlib" : "System.Private.CoreLib";
        using var mlc = new MetadataLoadContext(resolver, core);
        foreach (var file in targets)
        {
            Assembly asm;
            try { asm = mlc.LoadFromAssemblyPath(file); }
            catch (Exception e)
            {
                Assemblies.Add(new AssemblyEntry(Path.GetFileNameWithoutExtension(file), "", Path.GetFileName(file), 0, false, e.GetType().Name + ": " + e.Message));
                continue;
            }
            var name = asm.GetName();
            var game = !IsFramework(name.Name ?? "");
            Type[] types;
            string? error = null;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; error = "some types could not be loaded"; }
            catch (Exception e) { types = Array.Empty<Type>(); error = e.GetType().Name + ": " + e.Message; }
            var count = 0;
            foreach (var t in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (Skip(t.Name) || (PublicOnly && !IsVisible(t))) continue;
                Types.Add(Read(t, name.Name ?? "", game));
                count++;
            }
            Assemblies.Add(new AssemblyEntry(name.Name ?? file, name.Version?.ToString() ?? "", Path.GetFileName(file), count, game, error));
        }
    }

    private static bool IsVisible(Type t) => t.IsPublic || t.IsNestedPublic;

    /// <summary>Compiler-generated names (lambdas, iterators, backing fields, &lt;Module&gt;).</summary>
    private static bool Skip(string name) => name.Contains('<');

    private string Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) when (e is FileNotFoundException or TypeLoadException or FileLoadException or BadImageFormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            var u = Names.Unresolved(e);
            Unresolved[u] = Unresolved.TryGetValue(u, out var n) ? n + 1 : 1;
            return u;
        }
    }

    private void Part(TypeEntry entry, string what, Action read)
    {
        try { read(); }
        catch (Exception e) { entry.Errors.Add($"{what}: {Names.Unresolved(e)} {e.Message}"); }
    }

    private TypeEntry Read(Type t, string assembly, bool game)
    {
        var e = new TypeEntry
        {
            Assembly = assembly, Game = game, Namespace = t.Namespace ?? "", Name = t.Name,
            FullName = Try(() => Names.Of(t)),
        };
        Part(e, "kind", () =>
        {
            e.Kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct"
                : t.BaseType?.FullName == "System.MulticastDelegate" ? "delegate" : "class";
            e.Visibility = t.IsPublic || t.IsNestedPublic ? "public" : t.IsNestedFamily ? "protected" : t.IsNestedPrivate ? "private" : "internal";
            e.Abstract = t.IsAbstract;
            e.Sealed = t.IsSealed;
        });
        if (t.IsNested) e.DeclaringType = Try(() => Names.Of(t.DeclaringType!));
        if (t.IsGenericTypeDefinition) Part(e, "generic parameters", () => e.GenericParameters.AddRange(t.GetGenericArguments().Select(a => a.Name)));
        Part(e, "base type", () => { if (t.BaseType != null) e.BaseType = Try(() => Names.Of(t.BaseType)); });
        Part(e, "interfaces", () => e.Interfaces.AddRange(t.GetInterfaces().Select(i => Try(() => Names.Of(i))).OrderBy(x => x, StringComparer.Ordinal)));

        Part(e, "fields", () =>
        {
            foreach (var f in t.GetFields(Declared))
            {
                if (Skip(f.Name) || InteropNoise.Any(n => f.Name.StartsWith(n, StringComparison.Ordinal))) continue;
                if (PublicOnly && !f.IsPublic && e.Kind != "enum") continue;
                if (e.Kind == "enum" && !f.IsStatic) continue;   // value__
                string? value = null;
                if (f.IsLiteral) value = Try(() => Literal(f.GetRawConstantValue()));
                e.Fields.Add(new FieldEntry(f.Name, Try(() => Names.Of(f.FieldType)), f.IsStatic, Visibility(f), value));
            }
        });
        Part(e, "properties", () =>
        {
            foreach (var p in t.GetProperties(Declared))
            {
                if (Skip(p.Name)) continue;
                var getter = p.GetGetMethod(true);
                var setter = p.GetSetMethod(true);
                var acc = getter ?? setter;
                if (PublicOnly && (acc == null || !acc.IsPublic)) continue;
                var index = new List<ParameterEntry>();
                Part(e, "indexer " + p.Name, () => index.AddRange(p.GetIndexParameters().Select(x => new ParameterEntry(x.Name ?? "", Try(() => Names.Of(x.ParameterType))))));
                e.Properties.Add(new PropertyEntry(p.Name, Try(() => Names.Of(p.PropertyType)), getter != null, setter != null,
                    acc?.IsStatic ?? false, acc == null ? "private" : Visibility(acc), index));
            }
        });
        Part(e, "methods", () =>
        {
            foreach (var m in t.GetMethods(Declared))
            {
                if (Skip(m.Name) || (m.IsSpecialName && Accessor(m.Name))) continue;
                if (PublicOnly && !m.IsPublic) continue;
                e.Methods.Add(Method(m, m.Name, Try(() => Names.Of(m.ReturnType))));
            }
        });
        Part(e, "constructors", () =>
        {
            foreach (var c in t.GetConstructors(Declared))
            {
                if (PublicOnly && !c.IsPublic) continue;
                e.Constructors.Add(Method(c, c.IsStatic ? ".cctor" : ".ctor", "void"));
            }
        });
        Part(e, "events", () =>
        {
            foreach (var ev in t.GetEvents(Declared))
            {
                if (Skip(ev.Name)) continue;
                var add = ev.GetAddMethod(true);
                if (PublicOnly && (add == null || !add.IsPublic)) continue;
                e.Events.Add(new EventEntry(ev.Name, Try(() => Names.Of(ev.EventHandlerType!)), add == null ? "private" : Visibility(add)));
            }
        });
        return e;
    }

    private MethodEntry Method(MethodBase m, string name, string returnType)
    {
        var ps = new List<ParameterEntry>();
        try { ps.AddRange(m.GetParameters().Select(p => new ParameterEntry(p.Name ?? "", Try(() => Names.Of(p.ParameterType))))); }
        catch (Exception e) { ps.Add(new ParameterEntry("?", Names.Unresolved(e))); }
        var generic = m.IsGenericMethodDefinition ? m.GetGenericArguments().Select(a => a.Name).ToList() : new List<string>();
        return new MethodEntry(name, returnType, ps, m.IsStatic, m.IsVirtual && !m.IsFinal, m.IsAbstract, Visibility(m), generic);
    }

    private static bool Accessor(string name) =>
        name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal) ||
        name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal);

    private static string Visibility(FieldInfo f) =>
        f.IsPublic ? "public" : f.IsFamilyOrAssembly ? "protected internal" : f.IsFamily ? "protected" : f.IsAssembly ? "internal" : "private";

    private static string Visibility(MethodBase m) =>
        m.IsPublic ? "public" : m.IsFamilyOrAssembly ? "protected internal" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";

    private static string Literal(object? v) => v switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        char c => "'" + c + "'",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
