using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Root33.Core;
using Root33.Plugin;
using UnityEngine;
using RFactions = tuber_canis.data.Factions;

namespace Root33.DevMatch
{
    /// <summary>
    /// Dev copy only: confirms the Root 33 plugin against a real game without anyone at the keyboard.
    /// Mode = list: writes BepInEx\runs\templates.txt (Root's ready-made offline matches) once the title screen is up.
    /// Mode = play: clones one of them, makes every seat Root's own AI on the Autumn map, starts it offline, logs every
    /// Root 33 hook, writes a board snapshot (every mapped read) and a probe file at key moments, and quits Root after
    /// Turns turns. Nothing here ships with the mod.
    /// </summary>
    [BepInPlugin("gg.root33.devmatch", "Root 33 dev match (dev only)", "0.0.1")]
    [BepInDependency("gg.root33.mod")]
    public sealed class DevMatchPlugin : BasePlugin
    {
        internal static ManualLogSource L = null!;
        internal static ConfigEntry<string> Mode = null!, Template = null!, Factions = null!;
        internal static ConfigEntry<int> Turns = null!, AiLevel = null!;
        internal static string Runs = "";

        public override void Load()
        {
            L = Log;
            Mode = Config.Bind("DevMatch", "Mode", "off", new ConfigDescription("off, list or play", new AcceptableValueList<string>("off", "list", "play")));
            Template = Config.Bind("DevMatch", "Template", "0", "Index (#n in templates.txt) of Root's ready-made offline match to clone");
            Factions = Config.Bind("DevMatch", "Factions", "MarquiseDeCat,EyrieDynasties,WoodlandAlliance,Vagabond", "Root's factions, one seat each, in seat order");
            Turns = Config.Bind("DevMatch", "Turns", 8, "Quit Root after this many turns have ended");
            AiLevel = Config.Bind("DevMatch", "AiLevel", 1, "Root's AI level for every seat (0 easy, 1 medium, 2 hard)");
            if (Mode.Value == "off") { Log.LogInfo("Root 33 dev match: off"); return; }
            Runs = Path.Combine(BepInEx.Paths.BepInExRootPath, "runs");
            Directory.CreateDirectory(Runs);
            new Harmony("gg.root33.devmatch").PatchAll(typeof(DevMatchPlugin).Assembly);
            ClassInjector.RegisterTypeInIl2Cpp<DevMatchDriver>();
            AddComponent<DevMatchDriver>();
            Log.LogInfo($"Root 33 dev match: mode {Mode.Value}, writing to {Runs}");
        }
    }

    /// <summary>Waits for the title screen, then lists the templates or starts the match (main thread).</summary>
    public sealed class DevMatchDriver : MonoBehaviour
    {
        public DevMatchDriver(IntPtr ptr) : base(ptr) { }

        internal static float TitleShownAt = -1f;
        internal static volatile bool QuitRequested;
        private bool _done;

        public void Update()
        {
            if (QuitRequested)
            {
                QuitRequested = false;
                DevMatchPlugin.L.LogInfo("Root 33 dev match: done, quitting Root");
                Application.Quit();
            }
            if (_done || TitleShownAt < 0 || Time.unscaledTime < TitleShownAt + 5f) return;
            _done = true;
            try
            {
                if (DevMatchPlugin.Mode.Value == "list") Templates.Write();
                else Starter.Start();
            }
            catch (Exception e) { DevMatchPlugin.L.LogError("Root 33 dev match failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(tuber.client.menus.prompts.LandingPromptBehaviour), nameof(tuber.client.menus.prompts.LandingPromptBehaviour.initialize))]
    internal static class TitlePatch
    {
        private static void Postfix()
        {
            if (DevMatchDriver.TitleShownAt < 0) DevMatchDriver.TitleShownAt = Time.unscaledTime;
        }
    }

    internal static class Templates
    {
        /// <summary>Root's ready-made offline matches, in the combined collection's order (its lookup's own enumerator).</summary>
        public static List<tuber.canis.TuberMatchInitData> All()
        {
            Step("AllTuberOfflineMatches.Get()");
            var set = lib.src.menus.AllTuberOfflineMatches.Get();
            Step("set " + (set == null ? "null" : "found") + "; matches");
            var lookup = set?.matches;
            Step("lookup " + (lookup == null ? "null" : "found") + "; enumerating");
            var list = new List<tuber.canis.TuberMatchInitData>();
            if (lookup == null) return list;
            var e = lookup.GetEnumerator();
            var step = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (step.MoveNext()) list.Add(e.Current);
            Step($"{list.Count} matches");
            return list;
        }

        internal static void Step(string what) => DevMatchPlugin.L.LogInfo("Root 33 dev match: step: " + what);

        public static void Write()
        {
            var sb = new StringBuilder();
            var all = All();
            for (var i = 0; i < all.Count; i++)
            {
                var key = "#" + i;
                try
                {
                    var m = all[i];
                    if (m == null) { sb.AppendLine($"{key}: null"); continue; }
                    var players = Il2.Each(m.TuberPlayers).Select(p =>
                        $"{p.Faction}{(p.isHuman ? " human" : " ai" + p.aiLevel)}{(p.StartingCharacter != tuber_canis.data.VagabondCharacters.Unknown ? " " + p.StartingCharacter : "")}");
                    var options = m.options == null ? "" : string.Join(",", Il2.Each(m.options.Keys.Cast<Il2CppSystem.Collections.Generic.IEnumerable<string>>()));
                    sb.AppendLine($"{key}: map {m.ChosenMap}; players [{string.Join("; ", players)}]; options [{options}]");
                }
                catch (Exception e) { sb.AppendLine($"{key}: error {e.Message}"); }
            }
            var path = Path.Combine(DevMatchPlugin.Runs, "templates.txt");
            File.WriteAllText(path, sb.ToString());
            DevMatchPlugin.L.LogInfo($"Root 33 dev match: wrote {path}");
        }
    }

    internal static class Starter
    {
        public static void Start()
        {
            var all = Templates.All();
            var key = DevMatchPlugin.Template.Value.TrimStart('#');
            var index = int.TryParse(key, out var n) ? n : 0;
            if (index < 0 || index >= all.Count) throw new InvalidOperationException($"no offline match #{index} (there are {all.Count})");
            Templates.Step($"cloning #{index}");
            var data = all[index].TuberClone();
            var factions = DevMatchPlugin.Factions.Value.Split(',').Select(f => (RFactions)Enum.Parse(typeof(RFactions), f.Trim())).ToList();
            var players = data.TuberPlayers;
            while (players.Count < factions.Count)
            {
                var copy = players[0].Clone().Cast<tuber.canis.data.matchinitdata.TuberPlayerMatchInitData>();
                copy.accountID = Canis.utils.ids.AccountID.CreateID(Guid.NewGuid().ToString()).Cast<Canis.utils.ids.AccountID>();
                data.AddTuberPlayer(copy);
                players = data.TuberPlayers;
            }
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                p.isHuman = false;
                p.aiLevel = DevMatchPlugin.AiLevel.Value;
                p.name = "Dev AI " + (i + 1);
                if (i < factions.Count) p.Faction = factions[i];
                if (p.Faction == RFactions.Vagabond) p.StartingCharacter = tuber_canis.data.VagabondCharacters.Tinker;
            }
            data.ChosenMap = tuber_canis.data.MapLayout.Fall;
            DevMatchPlugin.L.LogInfo($"Root 33 dev match: starting '{key}' with {players.Count} AI seats ({string.Join(", ", Il2.Each(players).Select(p => p.Faction))}) on Fall; base players list {data.players?.Count}");
            var model = MatchModel.Find() ?? throw new InvalidOperationException("MatchModel.Find() returned null");
            var command = model.StartOfflineMatch(data) ?? throw new InvalidOperationException("StartOfflineMatch returned null");
            var queue = UnityEngine.Object.FindObjectOfType<dwd.core.commands.CommandQueue>();
            if (queue != null)
            {
                queue.Enqueue(command);
                DevMatchPlugin.L.LogInfo("Root 33 dev match: start command queued on " + queue.GetIl2CppType().FullName);
            }
            else throw new InvalidOperationException("no CommandQueue in the scene to run the start command");
        }
    }

    /// <summary>Every Root 33 hook, logged; board snapshots and probe files at the moments the plan asks for.</summary>
    [HarmonyPatch(typeof(Root33Runtime), nameof(Root33Runtime.Hook))]
    internal static class HookWatch
    {
        private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        private static readonly HashSet<string> FirstOnly = new HashSet<string>
        {
            "hook.setup-complete", "hook.battle-start", "hook.vp-changed", "hook.piece-moved", "hook.piece-placed",
            "hook.explore-resolved", "hook.aid-resolved", "hook.relationship-changed", "hook.turmoil", "hook.game-over",
        };
        private static int _turnEnds, _snap;

        private static void Prefix(string hookId)
        {
            int n;
            lock (Seen) Seen[hookId] = n = (Seen.TryGetValue(hookId, out var c) ? c : 0) + 1;
            if (n <= 3 || hookId == "hook.round-start") DevMatchPlugin.L.LogInfo($"Root 33 dev match: {hookId} #{n} (thread {Environment.CurrentManagedThreadId})");
            var snap = (FirstOnly.Contains(hookId) && n == 1) || (hookId == "hook.birdsong-start" && n <= 2) || (hookId == "hook.evening-start" && n == 1);
            if (snap) Snapshot(hookId.Substring(5) + "-" + n);
        }

        /// <summary>hook.turn-end goes through Root33Runtime.TurnEnd, not Hook.</summary>
        [HarmonyPatch(typeof(Root33Runtime), nameof(Root33Runtime.TurnEnd))]
        internal static class TurnEndWatch
        {
            private static void Prefix(SeatId seat)
            {
                var n = ++_turnEnds;
                DevMatchPlugin.L.LogInfo($"Root 33 dev match: hook.turn-end #{n} {seat} (thread {Environment.CurrentManagedThreadId})");
                Snapshot("turn-end-" + n);
                if (n >= DevMatchPlugin.Turns.Value)
                {
                    lock (Seen) DevMatchPlugin.L.LogInfo("Root 33 dev match: hook counts " + string.Join(", ", Seen.OrderBy(k => k.Key).Select(k => k.Key.Substring(5) + "=" + k.Value)));
                    DevMatchDriver.QuitRequested = true;
                }
            }
        }

        public static void Snapshot(string moment)
        {
            var name = $"{++_snap:00}-{moment}";
            try
            {
                File.WriteAllText(Path.Combine(DevMatchPlugin.Runs, name + ".txt"), BoardDump.Text());
                Root33.Plugin.Unity.Probe.Request(name);
            }
            catch (Exception e) { DevMatchPlugin.L.LogWarning($"Root 33 dev match: snapshot {name} failed: {e.Message}"); }
        }
    }

    /// <summary>Every mapped board read, each in its own try so one failure shows without hiding the rest.</summary>
    internal static class BoardDump
    {
        public static string Text()
        {
            var b = Root33Runtime.Current?.Board ?? throw new InvalidOperationException("Root 33 runtime not started");
            var sb = new StringBuilder();
            void R(string what, Func<object?> read)
            {
                string v;
                try { v = Show(read()); }
                catch (Exception e) { v = "ERR " + e.GetType().Name + ": " + e.Message; }
                sb.AppendLine(what + " = " + v);
            }
            R("map", () => b.MapName());
            R("current", () => b.CurrentSeat());
            R("phase", () => b.Phase());
            R("round", () => b.Round());
            R("clearings", () => b.Clearings());
            R("corners", () => b.Corners());
            IReadOnlyList<SeatId> seats = Array.Empty<SeatId>();
            try { seats = b.Seats(); } catch (Exception e) { sb.AppendLine("seats = ERR " + e.Message); }
            foreach (var s in seats)
            {
                R($"{s}.engine", () => b.EngineOf(s));
                R($"{s}.bot", () => b.IsBot(s));
                R($"{s}.vp", () => b.Vp(s));
                R($"{s}.dominance", () => b.ActiveDominance(s));
                R($"{s}.coalition", () => b.CoalitionPartner(s));
                R($"{s}.hand", () => b.HandSize(s) + " " + Show(b.HandSuits(s)));
                R($"{s}.pawn", () => b.PawnClearing(s) + " forest " + b.PawnForest(s));
                R($"{s}.items", () => b.Items(s).Select(i => $"{i.Type}{(i.Damaged ? "/dmg" : "")}{(i.Exhausted ? "/exh" : "")}"));
                R($"{s}.crafted", () => b.CraftedItems(s));
                foreach (var t in new[] { "sawmill", "workshop", "recruiter", "roost", "base", "wood", "keep", "sympathy" })
                    R($"{s}.supply.{t}", () => b.SupplyPieces(s, t));
                foreach (var f in seats.Where(f => f != s)) R($"{s}.relationship.{f}", () => b.RelationshipOf(s, f));
                R($"{s}.decree", () => string.Join(" | ", new[] { "recruit", "move", "battle", "build" }.Select(c => c + ":" + Show(b.DecreeColumn(s, c)))));
                R($"{s}.supporters", () => b.Supporters(s) + " officers " + b.Officers(s));
            }
            List<ClearingId> clearings;
            try { clearings = b.Clearings().ToList(); } catch { clearings = new List<ClearingId>(); }
            foreach (var c in clearings)
            {
                R($"{c}", () => $"suits {Show(b.SuitsOf(c))} slots {b.TotalSlots(c)} free {b.FreeSlots(c)} ruin {b.HasRuin(c)} river {b.OnRiver(c)} ruler {Show(b.Ruler(c))}");
                R($"{c}.paths", () => b.PathNeighbours(c));
                R($"{c}.rivers", () => b.RiverNeighbours(c));
                R($"{c}.forests", () => b.ForestsAround(c));
                R($"{c}.opposite", () => b.Corners().Contains(c) ? b.OppositeCorner(c) : null);
                foreach (var s in seats)
                    R($"{c}.{s}", () => $"warriors {b.Warriors(s, c)} buildings {b.Buildings(s, c)} tokens {b.Tokens(s, c)}" +
                                        $" (sawmill {b.BuildingsOfType(s, c, "sawmill")} roost {b.BuildingsOfType(s, c, "roost")} base {b.BuildingsOfType(s, c, "base")}" +
                                        $" wood {b.TokensOfType(s, c, "wood")} keep {b.TokensOfType(s, c, "keep")} sympathy {b.TokensOfType(s, c, "sympathy")})");
            }
            return sb.ToString();
        }

        private static string Show(object? v) => v switch
        {
            null => "null",
            string s => s,
            System.Collections.IEnumerable e => "[" + string.Join(",", e.Cast<object>().Select(Show)) + "]",
            _ => v.ToString() ?? "",
        };
    }

    internal static class Il2
    {
        public static List<T> Each<T>(Il2CppSystem.Collections.Generic.List<T>? source)
        {
            var list = new List<T>();
            if (source != null) for (var i = 0; i < source.Count; i++) list.Add(source[i]);
            return list;
        }

        public static List<T> Each<T>(Il2CppSystem.Collections.Generic.IEnumerable<T>? source)
        {
            var list = new List<T>();
            if (source == null) return list;
            var e = source.GetEnumerator();
            var step = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (step.MoveNext()) list.Add(e.Current);
            return list;
        }
    }
}