using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Root33.Core.Sheets;
using C = Root33.Core.StateCodec;

namespace Root33.Core
{
    /// <summary>
    /// Hand-written half of <see cref="ModState"/>: initial values (sheets/state.json 'initial') and the save format.
    /// Save() gives one entry per state row, keyed by the row id ("state.global.act"); Load() restores what it finds and
    /// leaves every missing or unreadable entry at its initial value. <see cref="ToText"/>/<see cref="FromText"/> flatten
    /// the map into the single string hook.save/hook.load carry.
    /// </summary>
    public sealed partial class ModState
    {
        /// <summary>state.esquie.rocks: entries in order Florrie, Dorrie, Soarrie, Urrie.</summary>
        public const int Florrie = 0, Dorrie = 1, Soarrie = 2, Urrie = 3;
        /// <summary>state.monoco.feet: entries in order Agile, Caster, Balanced, Heavy, Almighty.</summary>
        public const int Agile = 0, Caster = 1, Balanced = 2, Heavy = 3, Almighty = 4;
        /// <summary>state.lune.stains elements.</summary>
        public static readonly IReadOnlyList<string> StainElements = new[] { "fire", "ice", "lightning" };

        public const string TextHeader = "root33-state 1";

        public ModState() { Reset(); }

        /// <summary>Every field back to its sheet 'initial' value. With mapName "autumn" the roles are the fixed Autumn roles
        /// (state.global.roles) and Gustave's flags hold Lumière (state.gustave.flags); on other maps both start empty and
        /// are filled when the roles are assigned (<see cref="SetRoles"/>).</summary>
        public void Reset(string mapName = null)
        {
            GlobalAct = 1;
            GlobalRound = 1;
            GlobalCountdowns = new HashSet<SeatId>();
            GlobalSpoilerMode = SpoilerMode.Safe;
            GlobalDifficulty = Difficulty.Normal;
            GlobalSeatCharacters = new Dictionary<SeatId, string>();
            GlobalRoles = new Dictionary<RoleId, IReadOnlyList<ClearingId>>();
            GlobalE33Mode = false;
            GlobalCampaignGame = 0;
            GlobalCampaignJournal = false;
            GlobalPendingVp = new Dictionary<SeatId, int>();
            GlobalPendingDraws = new Dictionary<SeatId, int>();
            GustaveFlags = new HashSet<ClearingId>();
            GustaveArm = 0;
            GustaveOverchargeArmed = false;
            GustaveRoute = new ClearingId[0];
            EsquieRocks = Enumerable.Range(0, Tuning.EsquieRocks).Select(_ => new RockState()).ToArray();
            EsquieDecoy = false;
            EsquieTarget = null;
            PaintressStillLifeUsed = false;
            PaintressRepaintAvoid = new Dictionary<ClearingId, int>();
            ScielSun = false;
            ScielMoon = false;
            ScielTwilightArmed = false;
            VersoRank = 0;
            MaelleStance = "none";
            MaelleStanceTurn = new HashSet<string>();
            MaelleFamily = new HashSet<SeatId>();
            LuneTaggedRelic = null;
            LuneStains = StainElements.ToDictionary(e => e, e => 0);
            LuneStainArmed = null;
            MonocoFeet = Enumerable.Range(0, Tuning.MonocoFeet).Select(_ => new FootState()).ToArray();
            CuratorCellCrack = "none";
            CleaCovered = new HashSet<ClearingId>();
            WhiteHairedManBarrierDown = false;

            if (mapName == "autumn") SetRoles(Engine.AutumnMap.FixedRoles());
        }

        /// <summary>Sets the map roles and keeps the state.gustave.flags invariant (Lumière always included).</summary>
        public void SetRoles(IReadOnlyDictionary<RoleId, IReadOnlyList<ClearingId>> roles)
        {
            GlobalRoles = roles.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ClearingId>)kv.Value.ToArray());
            EnsureLumiereFlag();
        }

        /// <summary>Adds Lumière to state.gustave.flags when the role is known.</summary>
        public void EnsureLumiereFlag()
        {
            if (GlobalRoles.TryGetValue(new RoleId(Ids.MapRoles.Lumiere), out var l) && l.Count > 0) GustaveFlags.Add(l[0]);
        }

        // ---------------------------------------------------------------- save format

        private sealed class Field
        {
            public string Key;
            public Func<ModState, string> Save;
            public Action<ModState, string> Load;
        }

        private static Field F(string key, Func<ModState, string> save, Action<ModState, string> load) =>
            new Field { Key = key, Save = save, Load = load };

        private static string Rock(RockState r) =>
            string.Join("|", C.Esc(r.Status), C.NClearing(r.Clearing), C.NSeat(r.HostSeat), C.Int(r.GuardsLeft));

        private static RockState ParseRock(string s)
        {
            var f = C.Fields(s, 4);
            return new RockState { Status = C.Unesc(f[0]), Clearing = C.ParseNClearing(f[1]), HostSeat = C.ParseNSeat(f[2]), GuardsLeft = C.ParseInt(f[3]) };
        }

        private static string Foot(FootState x) => string.Join("|", C.Bool(x.Earned), C.NSeat(x.Faction), C.Int(x.Order));

        private static FootState ParseFoot(string s)
        {
            var f = C.Fields(s, 3);
            return new FootState { Earned = C.ParseBool(f[0]), Faction = C.ParseNSeat(f[1]), Order = C.ParseInt(f[2]) };
        }

        private static T[] FixedArray<T>(string s, int count, Func<string, T> one)
        {
            var parts = s.Length == 0 ? new string[0] : s.Split(';');
            if (parts.Length != count) throw new FormatException("expected " + count + " entries");
            return parts.Select(one).ToArray();
        }

        private static readonly Field[] Fields =
        {
            F(Ids.State.GlobalAct, m => C.Int(m.GlobalAct), (m, s) => m.GlobalAct = C.ParseInt(s)),
            F(Ids.State.GlobalRound, m => C.Int(m.GlobalRound), (m, s) => m.GlobalRound = C.ParseInt(s)),
            F(Ids.State.GlobalCountdowns, m => C.List(m.GlobalCountdowns.OrderBy(x => x.Value), C.Seat),
                (m, s) => m.GlobalCountdowns = new HashSet<SeatId>(C.ParseList(s, C.ParseSeat))),
            F(Ids.State.GlobalSpoilerMode, m => C.Enum(m.GlobalSpoilerMode), (m, s) => m.GlobalSpoilerMode = C.ParseEnum<SpoilerMode>(s)),
            F(Ids.State.GlobalDifficulty, m => C.Enum(m.GlobalDifficulty), (m, s) => m.GlobalDifficulty = C.ParseEnum<Difficulty>(s)),
            F(Ids.State.GlobalSeatCharacters, m => C.Map(m.GlobalSeatCharacters, C.Seat, C.Esc),
                (m, s) => m.GlobalSeatCharacters = C.ParseMap(s, C.ParseSeat, C.Unesc)),
            F(Ids.State.GlobalRoles, m => C.Map(m.GlobalRoles, r => C.Esc(r.Value), l => C.List(l, C.Clearing)),
                (m, s) => m.GlobalRoles = C.ParseMap(s, k => new RoleId(C.Unesc(k)), v => (IReadOnlyList<ClearingId>)C.ParseList(v, C.ParseClearing).ToArray())),
            F(Ids.State.GlobalE33Mode, m => C.Bool(m.GlobalE33Mode), (m, s) => m.GlobalE33Mode = C.ParseBool(s)),
            F(Ids.State.GlobalCampaignGame, m => C.Int(m.GlobalCampaignGame), (m, s) => m.GlobalCampaignGame = C.ParseInt(s)),
            F(Ids.State.GlobalCampaignJournal, m => C.Bool(m.GlobalCampaignJournal), (m, s) => m.GlobalCampaignJournal = C.ParseBool(s)),
            F(Ids.State.GlobalPendingVp, m => C.Map(m.GlobalPendingVp, C.Seat, C.Int), (m, s) => m.GlobalPendingVp = C.ParseMap(s, C.ParseSeat, C.ParseInt)),
            F(Ids.State.GlobalPendingDraws, m => C.Map(m.GlobalPendingDraws, C.Seat, C.Int), (m, s) => m.GlobalPendingDraws = C.ParseMap(s, C.ParseSeat, C.ParseInt)),
            F(Ids.State.GustaveFlags, m => C.List(m.GustaveFlags.OrderBy(x => x.Value), C.Clearing),
                (m, s) => m.GustaveFlags = new HashSet<ClearingId>(C.ParseList(s, C.ParseClearing))),
            F(Ids.State.GustaveArm, m => C.Int(m.GustaveArm), (m, s) => m.GustaveArm = C.ParseInt(s)),
            F(Ids.State.GustaveOverchargeArmed, m => C.Bool(m.GustaveOverchargeArmed), (m, s) => m.GustaveOverchargeArmed = C.ParseBool(s)),
            F(Ids.State.GustaveRoute, m => C.List(m.GustaveRoute, C.Clearing), (m, s) => m.GustaveRoute = C.ParseList(s, C.ParseClearing).ToArray()),
            F(Ids.State.EsquieRocks, m => string.Join(";", m.EsquieRocks.Select(Rock)),
                (m, s) => m.EsquieRocks = FixedArray(s, Tuning.EsquieRocks, ParseRock)),
            F(Ids.State.EsquieDecoy, m => C.Bool(m.EsquieDecoy), (m, s) => m.EsquieDecoy = C.ParseBool(s)),
            F(Ids.State.EsquieTarget, m => C.NInt(m.EsquieTarget), (m, s) => m.EsquieTarget = C.ParseNInt(s)),
            F(Ids.State.PaintressStillLifeUsed, m => C.Bool(m.PaintressStillLifeUsed), (m, s) => m.PaintressStillLifeUsed = C.ParseBool(s)),
            F(Ids.State.PaintressRepaintAvoid, m => C.Map(m.PaintressRepaintAvoid, C.Clearing, C.Int),
                (m, s) => m.PaintressRepaintAvoid = C.ParseMap(s, C.ParseClearing, C.ParseInt)),
            F(Ids.State.ScielSun, m => C.Bool(m.ScielSun), (m, s) => m.ScielSun = C.ParseBool(s)),
            F(Ids.State.ScielMoon, m => C.Bool(m.ScielMoon), (m, s) => m.ScielMoon = C.ParseBool(s)),
            F(Ids.State.ScielTwilightArmed, m => C.Bool(m.ScielTwilightArmed), (m, s) => m.ScielTwilightArmed = C.ParseBool(s)),
            F(Ids.State.VersoRank, m => C.Int(m.VersoRank), (m, s) => m.VersoRank = C.ParseInt(s)),
            F(Ids.State.MaelleStance, m => C.Esc(m.MaelleStance), (m, s) => m.MaelleStance = C.Unesc(s)),
            F(Ids.State.MaelleStanceTurn, m => C.List(m.MaelleStanceTurn.OrderBy(x => x, StringComparer.Ordinal), C.Esc),
                (m, s) => m.MaelleStanceTurn = new HashSet<string>(C.ParseList(s, C.Unesc))),
            F(Ids.State.MaelleFamily, m => C.List(m.MaelleFamily.OrderBy(x => x.Value), C.Seat),
                (m, s) => m.MaelleFamily = new HashSet<SeatId>(C.ParseList(s, C.ParseSeat))),
            F(Ids.State.LuneTaggedRelic, m => C.NInt(m.LuneTaggedRelic), (m, s) => m.LuneTaggedRelic = C.ParseNInt(s)),
            F(Ids.State.LuneStains, m => C.Map(m.LuneStains, C.Esc, C.Int), (m, s) => m.LuneStains = C.ParseMap(s, C.Unesc, C.ParseInt)),
            F(Ids.State.LuneStainArmed, m => C.Esc(m.LuneStainArmed), (m, s) => m.LuneStainArmed = C.Unesc(s)),
            F(Ids.State.MonocoFeet, m => string.Join(";", m.MonocoFeet.Select(Foot)),
                (m, s) => m.MonocoFeet = FixedArray(s, Tuning.MonocoFeet, ParseFoot)),
            F(Ids.State.CuratorCellCrack, m => C.Esc(m.CuratorCellCrack), (m, s) => m.CuratorCellCrack = C.Unesc(s)),
            F(Ids.State.CleaCovered, m => C.List(m.CleaCovered.OrderBy(x => x.Value), C.Clearing),
                (m, s) => m.CleaCovered = new HashSet<ClearingId>(C.ParseList(s, C.ParseClearing))),
            F(Ids.State.WhiteHairedManBarrierDown, m => C.Bool(m.WhiteHairedManBarrierDown), (m, s) => m.WhiteHairedManBarrierDown = C.ParseBool(s)),
        };

        /// <summary>The state row ids Save() writes (one per state.json row).</summary>
        public static IReadOnlyList<string> SavedKeys => Fields.Select(f => f.Key).ToArray();

        /// <summary>Every field as invariant text, keyed by its state row id.</summary>
        public Dictionary<string, string> Save()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Fields) d[f.Key] = f.Save(this);
            return d;
        }

        /// <summary>Resets, then restores every entry it can read. Returns the keys that were present and valid; a missing
        /// or malformed entry keeps its initial value, so the caller can apply the row's 'if the save has none' rule.</summary>
        public IReadOnlyCollection<string> Load(IReadOnlyDictionary<string, string> saved)
        {
            Reset();
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            if (saved == null) return loaded;
            foreach (var f in Fields)
            {
                if (!saved.TryGetValue(f.Key, out var text) || text == null) continue;
                var probe = new ModState();
                try { f.Load(probe, text); }
                catch (FormatException) { continue; }
                catch (OverflowException) { continue; }
                f.Load(this, text);
                loaded.Add(f.Key);
            }
            return loaded;
        }

        /// <summary>The whole state as one string (hook.save): a header line, then one 'key=value' line per entry.</summary>
        public string ToText() => ToText(Save());

        public static string ToText(IReadOnlyDictionary<string, string> map)
        {
            var sb = new StringBuilder(TextHeader).Append('\n');
            foreach (var kv in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(EscLine(kv.Value)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Parses <see cref="ToText"/> output. Empty, null or foreign text gives an empty map (a save with no mod state).</summary>
        public static Dictionary<string, string> FromText(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return d;
            var lines = text.Replace("\r", "").Split('\n');
            if (lines[0] != TextHeader) return d;
            for (var i = 1; i < lines.Length; i++)
            {
                var eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                d[lines[i].Substring(0, eq)] = UnescLine(lines[i].Substring(eq + 1));
            }
            return d;
        }

        // Values are already %-escaped inside; only raw newlines (none are produced) and '%' itself need care at line level.
        private static string EscLine(string v) => v.Replace("%", "%%").Replace("\n", "%n");

        private static string UnescLine(string v)
        {
            var sb = new StringBuilder(v.Length);
            for (var i = 0; i < v.Length; i++)
            {
                if (v[i] == '%' && i + 1 < v.Length)
                {
                    sb.Append(v[i + 1] == 'n' ? '\n' : v[i + 1]);
                    i++;
                }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }
    }
}
