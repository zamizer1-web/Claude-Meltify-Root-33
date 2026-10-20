namespace Root33.Discover;

/// <summary>One assembly of the scanned folder.</summary>
internal sealed record AssemblyEntry(string Name, string Version, string File, int Types, bool Game, string? Error);

/// <summary>A field (or enum value): its declared type, and its value when it is a constant.</summary>
internal sealed record FieldEntry(string Name, string Type, bool Static, string Visibility, string? Value);

internal sealed record PropertyEntry(string Name, string Type, bool CanRead, bool CanWrite, bool Static, string Visibility, IReadOnlyList<ParameterEntry> Index);

internal sealed record ParameterEntry(string Name, string Type);

/// <summary>A method or constructor. <see cref="Signature"/> is how it reads in C# ("public static int Score(Player p)").</summary>
internal sealed record MethodEntry(string Name, string ReturnType, IReadOnlyList<ParameterEntry> Parameters, bool Static, bool Virtual,
    bool Abstract, string Visibility, IReadOnlyList<string> GenericArguments)
{
    public string Signature =>
        $"{Visibility}{(Static ? " static" : Abstract ? " abstract" : Virtual ? " virtual" : "")} {ReturnType} {Name}" +
        (GenericArguments.Count > 0 ? "<" + string.Join(", ", GenericArguments) + ">" : "") +
        "(" + string.Join(", ", Parameters.Select(p => p.Type + " " + p.Name)) + ")";

    /// <summary>The signature with namespaces left out (for the report).</summary>
    public string ShortSignature =>
        $"{Names.Short(ReturnType)} {Name}(" + string.Join(", ", Parameters.Select(p => Names.Short(p.Type) + " " + p.Name)) + ")";
}

internal sealed record EventEntry(string Name, string Type, string Visibility);

/// <summary>One type with every member the scanner could read. A member whose types live in an assembly the folder does not
/// have reads "&lt;unresolved Name&gt;"; a part that could not be read at all is noted in <see cref="Errors"/>.</summary>
internal sealed class TypeEntry
{
    public string Assembly { get; init; } = "";
    public bool Game { get; init; }
    public string Namespace { get; init; } = "";
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Kind { get; set; } = "class";
    public string Visibility { get; set; } = "public";
    public string? DeclaringType { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; } = new();
    public bool Abstract { get; set; }
    public bool Sealed { get; set; }
    public List<string> GenericParameters { get; } = new();
    public List<FieldEntry> Fields { get; } = new();
    public List<PropertyEntry> Properties { get; } = new();
    public List<MethodEntry> Methods { get; } = new();
    public List<MethodEntry> Constructors { get; } = new();
    public List<EventEntry> Events { get; } = new();
    public List<string> Errors { get; } = new();

    public bool Static => Abstract && Sealed && Kind == "class";

    /// <summary>Every member name, for concept matching: fields (enum values included), properties, methods, events.</summary>
    public IEnumerable<(string Kind, string Name, string Text)> Members()
    {
        foreach (var f in Fields)
            yield return (Kind == "enum" ? "value" : "field", f.Name,
                Kind == "enum" ? $"{f.Name} = {f.Value}" : $"{Names.Short(f.Type)} {f.Name}{(f.Value != null ? " = " + f.Value : "")}");
        foreach (var p in Properties) yield return ("property", p.Name, $"{Names.Short(p.Type)} {p.Name} {{{(p.CanRead ? " get;" : "")}{(p.CanWrite ? " set;" : "")} }}");
        foreach (var m in Methods) yield return ("method", m.Name, m.ShortSignature);
        foreach (var e in Events) yield return ("event", e.Name, $"event {Names.Short(e.Type)} {e.Name}");
    }
}
