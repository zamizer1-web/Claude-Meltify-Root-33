using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Curator
{
    /// <summary>
    /// The legal-action key grammar the Curator's director reads and answers in (read.legal-actions promises only "stable
    /// action keys"; the plugin maps Root's real keys onto these): recruit (into the Burrow), build:&lt;clearing&gt;:&lt;type&gt;,
    /// sway:&lt;minister&gt;:&lt;vp&gt;, dig:&lt;clearing&gt;, move:&lt;from|burrow&gt;:&lt;to&gt;:&lt;count&gt;, battle:&lt;clearing&gt;:&lt;seat&gt;,
    /// home:&lt;clearing&gt;.
    /// </summary>
    public static class CuratorActionKey
    {
        public const string Recruit = "recruit", Build = "build", Sway = "sway", Dig = "dig", Move = "move", Battle = "battle",
            Home = "home", Burrow = "burrow";

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string DigKey(ClearingId c) => Dig + ":" + N(c.Value);
        public static string HomeKey(ClearingId c) => Home + ":" + N(c.Value);
        public static string BattleKey(ClearingId c, SeatId defender) => Battle + ":" + N(c.Value) + ":" + N(defender.Value);
        public static string BuildKey(ClearingId c, string type) => Build + ":" + N(c.Value) + ":" + type;
        public static string MoveKey(ClearingId? from, ClearingId to, int count) =>
            Move + ":" + (from.HasValue ? N(from.Value.Value) : Burrow) + ":" + N(to.Value) + ":" + N(count);

        private static string[] Parts(string key) => key == null ? new string[0] : key.Split(':');
        private static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);

        public static CuratorMove? ParseMove(string key)
        {
            var p = Parts(key);
            if (p.Length != 4 || p[0] != Move || !TryInt(p[2], out var to) || !TryInt(p[3], out var n)) return null;
            if (p[1] == Burrow) return new CuratorMove(null, new ClearingId(to), n);
            if (!TryInt(p[1], out var from)) return null;
            return new CuratorMove(new ClearingId(from), new ClearingId(to), n);
        }

        public static (ClearingId clearing, SeatId defender)? ParseBattle(string key)
        {
            var p = Parts(key);
            if (p.Length != 3 || p[0] != Battle || !TryInt(p[1], out var c) || !TryInt(p[2], out var s)) return null;
            return (new ClearingId(c), new SeatId(s));
        }

        public static (ClearingId clearing, string type)? ParseBuild(string key)
        {
            var p = Parts(key);
            if (p.Length != 3 || p[0] != Build || !TryInt(p[1], out var c)) return null;
            return (new ClearingId(c), p[2]);
        }

        public static (string minister, int vp)? ParseSway(string key)
        {
            var p = Parts(key);
            if (p.Length != 3 || p[0] != Sway || !TryInt(p[2], out var vp)) return null;
            return (p[1], vp);
        }
    }

    /// <summary>A move: from a clearing, or from the Burrow (From null), to a clearing.</summary>
    public readonly struct CuratorMove
    {
        public readonly ClearingId? From;
        public readonly ClearingId To;
        public readonly int Count;
        public CuratorMove(ClearingId? from, ClearingId to, int count) { From = from; To = to; Count = count; }
        public string Key => CuratorActionKey.MoveKey(From, To, Count);
    }

    /// <summary>Step 5's answer to the most urgent threat.</summary>
    public sealed class CuratorSpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        /// <summary>True when it takes the decision ahead of steps 1-4.</summary>
        public bool PreEmpts;
        public ClearingId Clearing;
        public string Key;
        public double Cost;
    }

    /// <summary>
    /// bot.curator: the Goal Director for the Curator's Duchy. One public [BotRule] method per bot_rules row, asked in order;
    /// the first that claims a decision takes it, and Root's own AI plays every decision none claims. Rule methods read only
    /// <see cref="BotView"/> and the legal action keys.
    /// </summary>
    public sealed class CuratorBot : IBotDirector
    {
        /// <summary>Step 1's "big fight": a battle against this many or more defending warriors (bot.curator.01-helper scoring;
        /// the sheet holds no tuning row for it).</summary>
        public const int BigFightDefenders = 2;

        /// <summary>Ministers that grant a Battle or a Move (Root's Underground Duchy: Captain, Marshal, Brigadier).</summary>
        public static readonly IReadOnlyList<string> BattleOrMoveMinisters = new[] { "captain", "marshal", "brigadier" };

        public string PlanId => Ids.BotPlans.Curator;

        private static ClearingId? MonolithOf(BotView v) => v.Role(Ids.MapRoles.Monolith);
        private static ClearingId? LumiereOf(BotView v) => v.Role(Ids.MapRoles.Lumiere);
        private static SeatId? PaintressOf(BotView v) => v.SeatOf(CharacterIds.Paintress);
        private static string Crack(BotView v) => v.State.CuratorCellCrack ?? CuratorTweaks.None;

        private static int EnemyWarriors(BotView v, ClearingId c) => v.Board.Seats().Where(s => s != v.Seat).Sum(s => v.Board.Warriors(s, c));

        private static IEnumerable<CuratorMove> MovesInto(IEnumerable<string> legal, ClearingId to) =>
            legal.Select(CuratorActionKey.ParseMove).Where(m => m.HasValue && m.Value.To == to).Select(m => m.Value);

        /// <summary>The smallest legal move into <paramref name="to"/> that brings his warriors there to <paramref name="need"/>.</summary>
        private static string MoveReaching(BotView v, IEnumerable<string> legal, ClearingId to, int need)
        {
            var have = v.Board.Warriors(v.Seat, to);
            return MovesInto(legal, to).Where(m => have + m.Count >= need)
                .OrderBy(m => m.Count).ThenBy(m => m.From.HasValue ? m.From.Value.Value : -1).Select(m => m.Key).FirstOrDefault();
        }

        /// <summary>goalModeTrigger (every difficulty): Act III once the Cell Crack has opened ('tunnel', or 'dig-owed').</summary>
        public bool InGoalMode(BotView view) =>
            view.Act >= 3 && (Crack(view) == CuratorTweaks.Tunnel || Crack(view) == CuratorTweaks.DigOwed);

        /// <summary>The plan keeps nothing in ModState: every step is worked out from the board each time.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        // ================================================================= 01 helper

        /// <summary>Step 1's limits on any battle he starts: in Acts I-II no battle against tune-free 2+ defending warriors,
        /// and never the Paintress unless she is one step from her win (or her banner is up), as the spoiler layer sees it.</summary>
        public bool CanBattle(BotView view, ClearingId c, SeatId defender)
        {
            if (view.Act >= 3) return true;
            if (view.Board.Warriors(defender, c) >= BigFightDefenders) return false;
            var p = PaintressOf(view);
            if (p.HasValue && defender == p.Value && !SpoilerLayer.Threatens(view, p.Value)) return false;
            return true;
        }

        /// <summary>Step 1 (Acts I-II): build a camp or stall in a ruled clearing with a free slot; sway the highest-VP minister
        /// he can afford; recruit into the Burrow while it holds fewer than tune.curator-burrow-stock.</summary>
        [BotRule("bot.curator.01-helper")]
        public string Helper(BotView view, IReadOnlyList<string> legal)
        {
            if (view.Act >= 3 || legal == null) return null;
            var b = view.Board;
            var me = view.Seat;
            var build = legal.Select(k => (k, p: CuratorActionKey.ParseBuild(k)))
                .Where(x => x.p.HasValue && b.Ruler(x.p.Value.clearing) == me && b.FreeSlots(x.p.Value.clearing) >= 1)
                .OrderBy(x => x.p.Value.clearing.Value).Select(x => x.k).FirstOrDefault();
            if (build != null) return build;
            var sway = BestSway(legal);
            if (sway != null) return sway;
            if (b.BurrowWarriors(me) < Tuning.CuratorBurrowStock && legal.Contains(CuratorActionKey.Recruit)) return CuratorActionKey.Recruit;
            return null;
        }

        /// <summary>The legal sway with the most VP (step 1).</summary>
        private static string BestSway(IEnumerable<string> legal) =>
            legal.Select(k => (k, p: CuratorActionKey.ParseSway(k))).Where(x => x.p.HasValue)
                .OrderByDescending(x => x.p.Value.vp).ThenBy(x => x.k, StringComparer.Ordinal).Select(x => x.k).FirstOrDefault();

        /// <summary>Whether a minister grants a Battle or a Move (<see cref="BattleOrMoveMinisters"/>).</summary>
        public static bool GrantsBattleOrMove(string minister) =>
            minister != null && BattleOrMoveMinisters.Contains(minister.ToLowerInvariant());

        /// <summary>Step 3's minister order: those that grant Battle or Move first, then by VP (ties: key order).</summary>
        public List<Ranked<string>> MinisterRanking(IEnumerable<string> legal) =>
            legal.Select(k => (k, p: CuratorActionKey.ParseSway(k))).Where(x => x.p.HasValue)
                .OrderByDescending(x => GrantsBattleOrMove(x.p.Value.minister)).ThenByDescending(x => x.p.Value.vp)
                .ThenBy(x => x.k, StringComparer.Ordinal)
                .Select(x => new Ranked<string>(x.k, (GrantsBattleOrMove(x.p.Value.minister) ? 100 : 0) + x.p.Value.vp)).ToList();

        // ================================================================= 02 reveal

        /// <summary>The Paintress's warriors in the Monolith + tune.curator-assault-margin.</summary>
        public int RevealWarriorsNeeded(BotView view)
        {
            var m = MonolithOf(view);
            var p = PaintressOf(view);
            var defenders = m.HasValue && p.HasValue ? view.Board.Warriors(p.Value, m.Value) : 0;
            return defenders + Tuning.CuratorAssaultMargin;
        }

        /// <summary>Step 2 (Act III, crack open): under 'dig-owed' the free Dig into the Monolith first; while her roost stands
        /// there, emerge from the Burrow with defenders + margin and battle it, recruiting into the Burrow until it holds that many.</summary>
        [BotRule("bot.curator.02-reveal")]
        public string Reveal(BotView view, IReadOnlyList<string> legal)
        {
            if (legal == null || !InGoalMode(view)) return null;
            var m = MonolithOf(view);
            if (!m.HasValue) return null;
            var b = view.Board;
            var me = view.Seat;
            if (Crack(view) == CuratorTweaks.DigOwed && b.TokensOfType(me, m.Value, PieceTypes.Tunnel) == 0)
                return legal.Contains(CuratorActionKey.DigKey(m.Value)) ? CuratorActionKey.DigKey(m.Value) : null;
            var p = PaintressOf(view);
            if (!p.HasValue || b.BuildingsOfType(p.Value, m.Value, PieceTypes.Roost) == 0) return null;
            var need = RevealWarriorsNeeded(view);
            if (b.Warriors(me, m.Value) >= need)
            {
                var battle = CuratorActionKey.BattleKey(m.Value, p.Value);
                return legal.Contains(battle) ? battle : null;
            }
            if (b.Warriors(me, m.Value) + b.BurrowWarriors(me) >= need)
            {
                var emerge = MoveReaching(view, legal.Where(k => CuratorActionKey.ParseMove(k)?.From == null), m.Value, need);
                if (emerge != null) return emerge;
                return null;
            }
            return legal.Contains(CuratorActionKey.Recruit) ? CuratorActionKey.Recruit : null;
        }

        // ================================================================= 03 lumiere

        private static int LumiereMargin(BotView v) =>
            v.Difficulty == Difficulty.Easy ? Tuning.CuratorEasyAssaultMargin : Tuning.CuratorAssaultMargin;

        /// <summary>Lumière itself when no keep stands there, otherwise the clearing next to it with the fewest enemy warriors
        /// (ties: lower clearing number).</summary>
        public ClearingId? DigTarget(BotView view)
        {
            var l = LumiereOf(view);
            if (!l.HasValue) return null;
            if (!CuratorParts.KeepIn(view.Board, l.Value)) return l;
            return Rank.Clearings(view.Board.PathNeighbours(l.Value), c => -EnemyWarriors(view, c))
                .Select(r => (ClearingId?)r.Item).FirstOrDefault();
        }

        /// <summary>The seat he battles in Lumière: its ruler, or with a tied rule the other seat with the most warriors plus
        /// buildings there (ties: lower seat); null when he rules it or no other seat has a piece there.</summary>
        public SeatId? LumiereDefender(BotView view)
        {
            var l = LumiereOf(view);
            if (!l.HasValue) return null;
            var b = view.Board;
            var ruler = b.Ruler(l.Value);
            if (ruler == view.Seat) return null;
            if (ruler.HasValue) return ruler;
            return b.Seats().Where(s => s != view.Seat && b.Warriors(s, l.Value) + b.Buildings(s, l.Value) > 0)
                .OrderByDescending(s => b.Warriors(s, l.Value) + b.Buildings(s, l.Value)).ThenBy(s => s.Value)
                .Select(s => (SeatId?)s).FirstOrDefault();
        }

        /// <summary>Lumière's ruler's warriors + the margin (Easy: tune.curator-easy-assault-margin).</summary>
        public int LumiereWarriorsNeeded(BotView view)
        {
            var l = LumiereOf(view);
            if (!l.HasValue) return 0;
            var d = LumiereDefender(view);
            var defenders = d.HasValue ? view.Board.Warriors(d.Value, l.Value) : 0;
            return defenders + LumiereMargin(view);
        }

        /// <summary>Step 3 (Act III): re-dig a lost Monolith tunnel (never on Easy), sway (Battle and Move ministers first), dig to
        /// <see cref="DigTarget"/>, march or battle into Lumière with <see cref="LumiereWarriorsNeeded"/>, and with no keep
        /// there build in it or hold tune.curator-lumiere-warriors warriors. Leaves the announce turn to step 4.</summary>
        [BotRule("bot.curator.03-lumiere")]
        public string Lumiere(BotView view, IReadOnlyList<string> legal)
        {
            if (legal == null || view.Act < 3 || IsAnnounceTurn(view)) return null;
            var l = LumiereOf(view);
            if (!l.HasValue) return null;
            var b = view.Board;
            var me = view.Seat;
            var m = MonolithOf(view);
            if (m.HasValue && Crack(view) == CuratorTweaks.Gone && b.TokensOfType(me, m.Value, PieceTypes.Tunnel) == 0
                && view.Difficulty != Difficulty.Easy && legal.Contains(CuratorActionKey.DigKey(m.Value)))
                return CuratorActionKey.DigKey(m.Value);
            var sway = MinisterRanking(legal).Select(r => r.Item).FirstOrDefault();
            if (sway != null) return sway;
            var target = DigTarget(view);
            if (target.HasValue && b.TokensOfType(me, target.Value, PieceTypes.Tunnel) == 0 && legal.Contains(CuratorActionKey.DigKey(target.Value)))
                return CuratorActionKey.DigKey(target.Value);
            var ruler = b.Ruler(l.Value);
            if (ruler != me)
            {
                var need = LumiereWarriorsNeeded(view);
                var defender = LumiereDefender(view);
                if (b.Warriors(me, l.Value) >= need && defender.HasValue)
                {
                    var battle = CuratorActionKey.BattleKey(l.Value, defender.Value);
                    return legal.Contains(battle) ? battle : null;
                }
                return MoveReaching(view, legal, l.Value, need);
            }
            if (!CuratorParts.KeepIn(b, l.Value) && b.Buildings(me, l.Value) == 0)
            {
                var build = legal.FirstOrDefault(k => CuratorActionKey.ParseBuild(k)?.clearing == l.Value);
                if (build != null) return build;
                if (b.Warriors(me, l.Value) < Tuning.CuratorLumiereWarriors)
                    return MoveReaching(view, legal, l.Value, Tuning.CuratorLumiereWarriors);
            }
            return null;
        }

        // ================================================================= 04 announce

        /// <summary>Warriors that can reach Lumière this turn: the Burrow's when one of his tunnels stands in or next to it,
        /// plus his warriors next to it.</summary>
        public int Reachable(BotView view, ClearingId lumiere)
        {
            var b = view.Board;
            var me = view.Seat;
            var near = b.PathNeighbours(lumiere);
            var tunnel = b.TokensOfType(me, lumiere, PieceTypes.Tunnel) > 0 || near.Any(c => b.TokensOfType(me, c, PieceTypes.Tunnel) > 0);
            return (tunnel ? b.BurrowWarriors(me) : 0) + near.Sum(c => b.Warriors(me, c));
        }

        /// <summary>Whether win.curator's announce test will pass at the end of this turn: its gates and Monolith parts pass now
        /// and the warriors that can reach Lumière make him its ruler with the garrison the test needs.</summary>
        public bool IsAnnounceTurn(BotView view)
        {
            var row = Data.WinConditions[Ids.WinConditions.Curator];
            var b = view.Board;
            var me = view.Seat;
            if (view.Act < row.ActMin || b.Vp(me) < row.VpMin || (row.OffWithDominance && b.ActiveDominance(me).HasValue)) return false;
            var m = MonolithOf(view);
            var l = LumiereOf(view);
            if (!m.HasValue || !l.HasValue) return false;
            if (!CuratorParts.RoostGone(b, PaintressOf(view), m.Value) || !CuratorParts.TunnelHeld(b, me, m.Value)) return false;
            var warriors = b.Warriors(me, l.Value) + Reachable(view, l.Value);
            var mine = warriors + b.Buildings(me, l.Value);
            var other = b.Seats().Where(s => s != me).Select(s => b.Warriors(s, l.Value) + b.Buildings(s, l.Value)).DefaultIfEmpty(0).Max();
            if (mine <= other) return false;
            return CuratorParts.KeepIn(b, l.Value) || b.Buildings(me, l.Value) >= 1 || warriors >= Tuning.CuratorLumiereWarriors;
        }

        /// <summary>Step 4: on the announce turn, recruit into the Burrow, then pour every warrior that can reach Lumière in
        /// (the largest legal move into it first; with none, out of the Burrow into his tunnel next to it).</summary>
        [BotRule("bot.curator.04-announce")]
        public string Announce(BotView view, IReadOnlyList<string> legal)
        {
            if (legal == null || !IsAnnounceTurn(view)) return null;
            if (legal.Contains(CuratorActionKey.Recruit)) return CuratorActionKey.Recruit;
            var l = LumiereOf(view).Value;
            var b = view.Board;
            var into = MovesInto(legal, l).OrderByDescending(m => m.Count).ThenBy(m => m.From.HasValue ? m.From.Value.Value : -1)
                .Select(m => m.Key).FirstOrDefault();
            if (into != null) return into;
            // By the tunnel next to it: out of the Burrow into a neighbour holding his tunnel, to march on in.
            var near = new HashSet<ClearingId>(b.PathNeighbours(l));
            return legal.Select(CuratorActionKey.ParseMove)
                .Where(m => m.HasValue && !m.Value.From.HasValue && near.Contains(m.Value.To) && b.TokensOfType(view.Seat, m.Value.To, PieceTypes.Tunnel) > 0)
                .Select(m => m.Value).OrderByDescending(m => m.Count).ThenBy(m => m.To.Value).Select(m => m.Key).FirstOrDefault();
        }

        // ================================================================= 05 spoiler

        private static readonly double[] LowDie = { 7 / 16.0, 5 / 16.0, 3 / 16.0, 1 / 16.0 };

        /// <summary>Expected hits he takes battling <paramref name="defender"/> (the defender rolls the lower die, capped by its warriors).</summary>
        public double ExpectedHitsTaken(BotView view, SeatId defender, ClearingId c)
        {
            var cap = view.IsVagabond(defender)
                ? view.Board.Items(defender).Count(i => i.Type == ItemType.Sword && !i.Damaged)
                : view.Board.Warriors(defender, c);
            var e = 0.0;
            for (var k = 0; k < LowDie.Length; k++) e += LowDie[k] * Math.Min(k, cap);
            return e;
        }

        /// <summary>Paths from <paramref name="c"/> to the nearest clearing holding his warriors, buildings or tokens (0 there;
        /// int.MaxValue with none on the map).</summary>
        public int DistanceToOwnPieces(BotView view, ClearingId c)
        {
            var b = view.Board;
            var me = view.Seat;
            bool Mine(ClearingId x) => b.Warriors(me, x) + b.Buildings(me, x) + b.Tokens(me, x) > 0;
            var seen = new HashSet<ClearingId> { c };
            var frontier = new List<ClearingId> { c };
            for (var d = 0; frontier.Count > 0; d++)
            {
                if (frontier.Any(Mine)) return d;
                frontier = frontier.SelectMany(x => b.PathNeighbours(x)).Where(seen.Add).ToList();
            }
            return int.MaxValue;
        }

        /// <summary>Step 5, the shared spoiler layer: for the most urgent threat (<see cref="SpoilerLayer.Threats"/>), the cheapest
        /// legal answer in its key clearings: a battle there (1 action + expected hits) or a move in, through the Burrow or by
        /// march, ahead of a battle (2 + hits); ties by key order (its win's met parts come first), then the answer nearest his
        /// pieces, then the lower clearing. Step 1's limits bind in Acts I-II.</summary>
        [BotRule("bot.curator.05-spoiler")]
        public CuratorSpoilerAnswer Spoiler(BotView view, IReadOnlyList<string> legal)
        {
            if (legal == null) return null;
            var b = view.Board;
            foreach (var threat in SpoilerLayer.Threats(view))
            {
                var keys = threat.KeyClearings.Count > 0 ? threat.KeyClearings.ToList()
                    : b.Clearings().Where(c => b.Warriors(threat.Seat, c) + b.Buildings(threat.Seat, c) + b.Tokens(threat.Seat, c) > 0
                        || b.PawnClearing(threat.Seat) == c).ToList();
                var answers = new List<(CuratorSpoilerAnswer a, int index)>();
                for (var i = 0; i < keys.Count; i++)
                {
                    var c = keys[i];
                    if (!CanBattle(view, c, threat.Seat)) continue;
                    var hits = ExpectedHitsTaken(view, threat.Seat, c);
                    var battle = CuratorActionKey.BattleKey(c, threat.Seat);
                    if (legal.Contains(battle))
                        answers.Add((new CuratorSpoilerAnswer { Threat = threat.Seat, Banner = threat.Banner, Clearing = c, Key = battle, Cost = 1 + hits }, i));
                    var move = MovesInto(legal, c).OrderByDescending(m => m.Count).Select(m => m.Key).FirstOrDefault();
                    if (move != null)
                        answers.Add((new CuratorSpoilerAnswer { Threat = threat.Seat, Banner = threat.Banner, Clearing = c, Key = move, Cost = 2 + hits }, i));
                }
                if (answers.Count == 0) continue;
                var best = answers.OrderBy(x => x.a.Cost).ThenBy(x => x.index).ThenBy(x => DistanceToOwnPieces(view, x.a.Clearing))
                    .ThenBy(x => x.a.Clearing.Value).First().a;
                best.PreEmpts = SpoilerLayer.PreEmpts(view, threat, IsAnnounceTurn(view));
                return best;
            }
            return null;
        }

        // ================================================================= decisions

        /// <summary>The legal actions step 1's limits leave him (<see cref="CanBattle"/> filters battles).</summary>
        public List<string> Allowed(BotView view, IReadOnlyList<string> legal)
        {
            var list = new List<string>();
            foreach (var k in legal)
            {
                var battle = CuratorActionKey.ParseBattle(k);
                if (battle == null || CanBattle(view, battle.Value.clearing, battle.Value.defender)) list.Add(k);
            }
            return list;
        }

        /// <summary>hook.bot-decision: the spoiler layer when it pre-empts, then steps 1-4 in order, then the spoiler layer with
        /// the actions they leave free; null lets Root's own AI decide (battles step 1 forbids are vetoed for his bot seat
        /// through hook.battle-legal, see <see cref="CuratorModule.CanStartBattle"/>).</summary>
        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var legal = Allowed(view, legalActions);
            if (legal.Count == 0) return null;
            var spoiler = Spoiler(view, legal);
            if (spoiler != null && spoiler.PreEmpts) return spoiler.Key;
            var key = Helper(view, legal) ?? Reveal(view, legal) ?? Lumiere(view, legal) ?? Announce(view, legal);
            if (key != null) return key;
            return spoiler?.Key;
        }
    }
}
