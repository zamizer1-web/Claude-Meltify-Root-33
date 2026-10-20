using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Root33.Plugin.Diagnostics
{
    /// <summary>One GameObject in the probe file: its name, whether it is active, its components' type names and its children.</summary>
    public sealed class ProbeNode
    {
        public ProbeNode(string name, bool active, int childCount)
        {
            Name = name;
            Active = active;
            ChildCount = childCount;
        }

        public string Name { get; }
        public bool Active { get; }
        /// <summary>All children the object has (the file may list fewer: see <see cref="ChildrenCut"/>).</summary>
        public int ChildCount { get; }
        public List<string> Components { get; } = new List<string>();
        public List<ProbeNode> Children { get; } = new List<ProbeNode>();
        /// <summary>Some children were left out (depth, width or size limit).</summary>
        public bool ChildrenCut { get; set; }
    }

    /// <summary>One scene (or the DontDestroyOnLoad objects) and its root objects.</summary>
    public sealed class ProbeScene
    {
        public ProbeScene(string name, string path, bool loaded, int rootCount)
        {
            Name = name;
            Path = path;
            Loaded = loaded;
            RootCount = rootCount;
        }

        public string Name { get; }
        public string Path { get; }
        public bool Loaded { get; }
        public int RootCount { get; }
        public List<ProbeNode> Roots { get; } = new List<ProbeNode>();
    }

    /// <summary>
    /// What the F9 probe writes to BepInEx\root33-probe.json: every loaded scene's root GameObjects and their children (depth-,
    /// width- and size-limited), each with its components' type names, plus how often each component type occurs, so the PC
    /// session can see which of Root's objects hold the board, the pieces and the turn state at runtime. Plain C#: the Unity
    /// part (Unity/Probe.cs) only walks the scene and fills it.
    /// </summary>
    public sealed class ProbeSnapshot
    {
        public ProbeSnapshot(DateTime taken, string activeScene, int maxDepth = 8, int maxChildren = 80, int maxNodes = 20000)
        {
            Taken = taken;
            ActiveScene = activeScene;
            MaxDepth = maxDepth;
            MaxChildren = maxChildren;
            MaxNodes = maxNodes;
        }

        public DateTime Taken { get; }
        public string ActiveScene { get; }
        public int MaxDepth { get; }
        public int MaxChildren { get; }
        public int MaxNodes { get; }
        public int NodeCount { get; private set; }
        public List<ProbeScene> Scenes { get; } = new List<ProbeScene>();
        public List<string> Notes { get; } = new List<string>();
        private readonly Dictionary<string, int> _componentCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Whether one more object fits (counts it when it does).</summary>
        public bool TakeNode()
        {
            if (NodeCount >= MaxNodes) return false;
            NodeCount++;
            return true;
        }

        /// <summary>Records a component on a node and in the type counts.</summary>
        public void AddComponent(ProbeNode node, string typeName)
        {
            node.Components.Add(typeName);
            _componentCounts[typeName] = _componentCounts.TryGetValue(typeName, out var n) ? n + 1 : 1;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            Prop(sb, 1, "tool", Quote("Root 33 probe")).Append(",\n");
            Prop(sb, 1, "taken", Quote(Taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
            Prop(sb, 1, "activeScene", Quote(ActiveScene)).Append(",\n");
            Prop(sb, 1, "limits", $"{{\"depth\": {MaxDepth}, \"children\": {MaxChildren}, \"objects\": {MaxNodes}}}").Append(",\n");
            Prop(sb, 1, "objects", NodeCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            Prop(sb, 1, "notes", "[" + string.Join(", ", Notes.Select(Quote)) + "]").Append(",\n");
            Indent(sb, 1).Append("\"componentCounts\": {");
            var counts = _componentCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < counts.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n");
                Indent(sb, 2).Append(Quote(counts[i].Key)).Append(": ").Append(counts[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(counts.Count > 0 ? "\n" : "");
            if (counts.Count > 0) Indent(sb, 1);
            sb.Append("},\n");
            Indent(sb, 1).Append("\"scenes\": [");
            for (var i = 0; i < Scenes.Count; i++)
            {
                var s = Scenes[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                Indent(sb, 2).Append("{\"name\": ").Append(Quote(s.Name)).Append(", \"path\": ").Append(Quote(s.Path))
                    .Append(", \"loaded\": ").Append(s.Loaded ? "true" : "false")
                    .Append(", \"rootCount\": ").Append(s.RootCount.ToString(CultureInfo.InvariantCulture)).Append(", \"roots\": [");
                Nodes(sb, s.Roots, 3);
                sb.Append("]}");
            }
            sb.Append(Scenes.Count > 0 ? "\n" : "");
            if (Scenes.Count > 0) Indent(sb, 1);
            sb.Append("]\n}\n");
            return sb.ToString();
        }

        private static void Nodes(StringBuilder sb, List<ProbeNode> nodes, int depth)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                Indent(sb, depth).Append("{\"name\": ").Append(Quote(n.Name))
                    .Append(", \"active\": ").Append(n.Active ? "true" : "false")
                    .Append(", \"components\": [").Append(string.Join(", ", n.Components.Select(Quote))).Append(']')
                    .Append(", \"childCount\": ").Append(n.ChildCount.ToString(CultureInfo.InvariantCulture));
                if (n.ChildrenCut) sb.Append(", \"childrenCut\": true");
                if (n.Children.Count > 0)
                {
                    sb.Append(", \"children\": [");
                    Nodes(sb, n.Children, depth + 1);
                    sb.Append('\n');
                    Indent(sb, depth).Append(']');
                }
                sb.Append('}');
            }
            if (nodes.Count > 0) sb.Append('\n').Append(' ', (depth - 1) * 2);
        }

        private static StringBuilder Prop(StringBuilder sb, int depth, string name, string json) =>
            Indent(sb, depth).Append(Quote(name)).Append(": ").Append(json);

        private static StringBuilder Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        /// <summary>A JSON string literal.</summary>
        public static string Quote(string? s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
