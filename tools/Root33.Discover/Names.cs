using System.Text;
using System.Text.RegularExpressions;

namespace Root33.Discover;

/// <summary>Type names as C# reads them, identifiers split into words, and the Root concepts the report groups by.</summary>
internal static class Names
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["System.Void"] = "void", ["System.Boolean"] = "bool", ["System.Byte"] = "byte", ["System.SByte"] = "sbyte",
        ["System.Int16"] = "short", ["System.UInt16"] = "ushort", ["System.Int32"] = "int", ["System.UInt32"] = "uint",
        ["System.Int64"] = "long", ["System.UInt64"] = "ulong", ["System.Single"] = "float", ["System.Double"] = "double",
        ["System.Decimal"] = "decimal", ["System.Char"] = "char", ["System.String"] = "string", ["System.Object"] = "object",
    };

    /// <summary>A type as C# writes it, namespace-qualified: "System.Collections.Generic.List&lt;Root.Clearing&gt;", "int[]",
    /// "ref Root.Player", "Outer.Inner". Generic parameters by name ("T").</summary>
    public static string Of(Type t)
    {
        if (t.IsByRef) return "ref " + Of(t.GetElementType()!);
        if (t.IsPointer) return Of(t.GetElementType()!) + "*";
        if (t.IsArray) return Of(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        if (t.IsGenericParameter) return t.Name;
        var name = t.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);
        string prefix;
        if (t.IsNested && t.DeclaringType != null) prefix = Of(t.DeclaringType) + ".";
        else prefix = string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".";
        var full = prefix + name;
        if (Aliases.TryGetValue(full, out var alias)) return alias;
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            // Nested types repeat their outer type's arguments; show only the ones this type adds.
            var args = t.GetGenericArguments();
            var own = t.IsNested && t.DeclaringType != null ? args.Skip(t.DeclaringType.GetGenericArguments().Length).ToArray() : args;
            if (own.Length > 0) full += "<" + string.Join(", ", own.Select(Of)) + ">";
        }
        else if (t.IsGenericTypeDefinition)
        {
            var args = t.GetGenericArguments();
            var own = t.IsNested && t.DeclaringType != null ? args.Skip(t.DeclaringType.GetGenericArguments().Length).ToArray() : args;
            if (own.Length > 0) full += "<" + string.Join(", ", own.Select(a => a.Name)) + ">";
        }
        return full;
    }

    /// <summary>The same with every namespace left out ("List&lt;Clearing&gt;").</summary>
    public static string Short(string typeName) =>
        typeName.StartsWith("<unresolved", StringComparison.Ordinal) ? typeName : QualifiedPart.Replace(typeName, "");

    private static readonly Regex QualifiedPart = new(@"\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+(?=[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>What a reference to a missing assembly reads as: "&lt;unresolved Il2CppInterop.Runtime&gt;".</summary>
    public static string Unresolved(Exception e)
    {
        var what = e is FileNotFoundException f && !string.IsNullOrEmpty(f.FileName) ? f.FileName : e.GetType().Name;
        var comma = what.IndexOf(',');
        if (comma > 0) what = what.Substring(0, comma);
        return "<unresolved " + what + ">";
    }

    /// <summary>
    /// An identifier split into lower-case words: "BirdsongPhase" gives birdsong, phase; "AIPlayer" gives ai, player;
    /// "VPTrack" gives vp, track; "m_handSize" gives m, hand, size; "Clearing2" gives clearing, 2.
    /// </summary>
    public static List<string> Words(string identifier)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        void Flush() { if (sb.Length > 0) { words.Add(sb.ToString().ToLowerInvariant()); sb.Clear(); } }
        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (!char.IsLetterOrDigit(c)) { Flush(); continue; }
            if (sb.Length > 0)
            {
                var prev = identifier[i - 1];
                var next = i + 1 < identifier.Length ? identifier[i + 1] : '\0';
                var boundary =
                    (char.IsUpper(c) && char.IsLower(prev)) ||                                // camelCase
                    (char.IsUpper(c) && char.IsUpper(prev) && char.IsLower(next)) ||           // AIPlayer -> AI | Player
                    (char.IsDigit(c) != char.IsDigit(prev));                                   // Clearing2
                if (boundary) Flush();
            }
            sb.Append(c);
        }
        Flush();
        return words;
    }
}

/// <summary>
/// One Root concept the report groups by: a heading and the word stems that match it. A stem of 5+ letters matches any word
/// it begins ("craft": crafting, crafted); a shorter stem matches the word itself and its plain endings ("hand": hands, but
/// not handler). <see cref="Except"/> lists words a stem must not match ("quest" is not "question").
/// </summary>
internal sealed record Concept(string Heading, string[] Stems, string[] Except)
{
    private static readonly string[] ShortEndings = { "", "s", "es", "ed", "er", "ers", "ing", "d", "r", "rs" };

    public bool MatchesWord(string word)
    {
        foreach (var x in Except) if (word.StartsWith(x, StringComparison.Ordinal)) return false;
        foreach (var stem in Stems)
        {
            if (stem.Length >= 5) { if (word.StartsWith(stem, StringComparison.Ordinal)) return true; }
            else foreach (var end in ShortEndings) if (word == stem + end) return true;
        }
        return false;
    }

    public bool Matches(string identifier) => Names.Words(identifier).Any(MatchesWord);

    private static Concept C(string heading, params string[] stems) => new(heading, stems.Length == 0 ? new[] { heading } : stems, Array.Empty<string>());

    /// <summary>The concepts of Root's board, rules, factions and menus that Root 33 needs to find in Root's code.</summary>
    public static readonly Concept[] All =
    {
        C("clearing"), C("board"), C("map"), C("path"), C("forest"), C("river"), C("ruin"),
        C("faction"), C("player"), C("seat"), C("warrior"), C("building"), C("token"), C("pawn"), C("piece"),
        C("card"), C("deck"), C("hand"), C("discard"), C("suit"), C("craft"), C("item"), C("satchel"),
        C("relationship"), new("quest", new[] { "quest" }, new[] { "questio" }), C("battle"), C("attack"),
        C("defend", "defend", "defens", "defenc"), C("hit"), C("dice"), C("roll"), C("ambush"),
        C("turn"), C("phase"), C("birdsong"), C("daylight"), C("evening"),
        C("score"), C("vp"), C("victory"), C("dominance"), C("coalition"),
        C("decree"), C("roost"), C("sympathy"), C("base"), C("officer"), C("sawmill"), C("workshop"), C("recruiter", "recruit"), C("keep"),
        C("vagabond"), C("marquise"), C("eyrie"), C("alliance"),
        C("ai"), C("bot"), C("localization", "localiz", "localis"), C("title"), C("menu"), C("logo"),
    };
}
