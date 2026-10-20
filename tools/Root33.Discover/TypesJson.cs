using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Root33.Discover;

/// <summary>
/// types.json: { "tool", "source", "assemblies": [...], "unresolved": {...}, "types": [ one type per line ] }. One type per
/// line keeps the file greppable ("grep '\"Name\":\"Clearing\"' types.json") while it stays valid JSON.
/// </summary>
internal static class TypesJson
{
    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Write(string path, string source, Scanner scan)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var text = new StreamWriter(file, new UTF8Encoding(false));
        text.Write("{\n\"tool\": \"Root33.Discover\",\n\"source\": ");
        text.Write(JsonSerializer.Serialize(source));
        text.Write(",\n\"assemblies\": [\n");
        for (var i = 0; i < scan.Assemblies.Count; i++)
        {
            var a = scan.Assemblies[i];
            text.Write(Line(w =>
            {
                w.WriteStartObject();
                w.WriteString("name", a.Name);
                w.WriteString("version", a.Version);
                w.WriteString("file", a.File);
                w.WriteNumber("types", a.Types);
                w.WriteBoolean("game", a.Game);
                if (a.Error != null) w.WriteString("error", a.Error);
                w.WriteEndObject();
            }));
            text.Write(i + 1 < scan.Assemblies.Count ? ",\n" : "\n");
        }
        text.Write("],\n\"unresolved\": ");
        text.Write(Line(w =>
        {
            w.WriteStartObject();
            foreach (var kv in scan.Unresolved) w.WriteNumber(kv.Key, kv.Value);
            w.WriteEndObject();
        }));
        text.Write(",\n\"types\": [\n");
        for (var i = 0; i < scan.Types.Count; i++)
        {
            text.Write(Line(w => Type(w, scan.Types[i])));
            text.Write(i + 1 < scan.Types.Count ? ",\n" : "\n");
        }
        text.Write("]\n}\n");
    }

    private static string Line(Action<Utf8JsonWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Compact)) write(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Type(Utf8JsonWriter w, TypeEntry t)
    {
        w.WriteStartObject();
        w.WriteString("assembly", t.Assembly);
        w.WriteString("namespace", t.Namespace);
        w.WriteString("name", t.Name);
        w.WriteString("fullName", t.FullName);
        w.WriteString("kind", t.Kind);
        w.WriteString("visibility", t.Visibility);
        if (t.Abstract && !t.Sealed) w.WriteBoolean("abstract", true);
        if (t.Sealed && !t.Abstract) w.WriteBoolean("sealed", true);
        if (t.Static) w.WriteBoolean("static", true);
        if (t.DeclaringType != null) w.WriteString("declaringType", t.DeclaringType);
        if (t.GenericParameters.Count > 0) Strings(w, "genericParameters", t.GenericParameters);
        if (t.BaseType != null) w.WriteString("baseType", t.BaseType);
        if (t.Interfaces.Count > 0) Strings(w, "interfaces", t.Interfaces);

        if (t.Fields.Count > 0)
        {
            w.WriteStartArray(t.Kind == "enum" ? "values" : "fields");
            foreach (var f in t.Fields)
            {
                w.WriteStartObject();
                w.WriteString("name", f.Name);
                if (t.Kind != "enum")
                {
                    w.WriteString("type", f.Type);
                    w.WriteString("visibility", f.Visibility);
                    if (f.Static) w.WriteBoolean("static", true);
                }
                if (f.Value != null) w.WriteString("value", f.Value);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (t.Properties.Count > 0)
        {
            w.WriteStartArray("properties");
            foreach (var p in t.Properties)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("type", p.Type);
                w.WriteString("visibility", p.Visibility);
                w.WriteBoolean("get", p.CanRead);
                w.WriteBoolean("set", p.CanWrite);
                if (p.Static) w.WriteBoolean("static", true);
                if (p.Index.Count > 0) Parameters(w, "index", p.Index);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (t.Constructors.Count > 0) Methods(w, "constructors", t.Constructors);
        if (t.Methods.Count > 0) Methods(w, "methods", t.Methods);
        if (t.Events.Count > 0)
        {
            w.WriteStartArray("events");
            foreach (var e in t.Events)
            {
                w.WriteStartObject();
                w.WriteString("name", e.Name);
                w.WriteString("type", e.Type);
                w.WriteString("visibility", e.Visibility);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (t.Errors.Count > 0) Strings(w, "errors", t.Errors);
        w.WriteEndObject();
    }

    private static void Methods(Utf8JsonWriter w, string name, IEnumerable<MethodEntry> methods)
    {
        w.WriteStartArray(name);
        foreach (var m in methods)
        {
            w.WriteStartObject();
            w.WriteString("name", m.Name);
            w.WriteString("signature", m.Signature);
            w.WriteString("returnType", m.ReturnType);
            Parameters(w, "parameters", m.Parameters);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void Parameters(Utf8JsonWriter w, string name, IEnumerable<ParameterEntry> ps)
    {
        w.WriteStartArray(name);
        foreach (var p in ps)
        {
            w.WriteStartObject();
            w.WriteString("name", p.Name);
            w.WriteString("type", p.Type);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void Strings(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WriteStartArray(name);
        foreach (var v in values) w.WriteStringValue(v);
        w.WriteEndArray();
    }
}
