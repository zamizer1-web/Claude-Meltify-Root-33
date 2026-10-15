using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Gustave
{
    /// <summary>
    /// Assumed legal-action keys for Gustave's Marquise (until Root's own keys are known, like the Paintress's):
    /// build:&lt;clearing&gt;:&lt;type&gt;, recruit:&lt;clearing&gt;, move:&lt;from&gt;:&lt;to&gt;:&lt;count&gt;, battle:&lt;clearing&gt;:&lt;defender seat&gt;,
    /// overwork:&lt;clearing&gt;, field-hospital:&lt;clearing&gt;, extra-action:bird (spend a bird card for an extra action), pass.
    /// </summary>
    public static class GustaveActionKey
    {
        public const string Build = "build", Recruit = "recruit", Move = "move", Battle = "battle", Overwork = "overwork",
            FieldHospital = "field-hospital", BirdExtra = "extra-action:bird", Pass = "pass";

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string BuildKey(ClearingId c, string type) => Build + ":" + N(c.Value) + ":" + type;
        public static string RecruitKey(ClearingId c) => Recruit + ":" + N(c.Value);
        public static string MoveKey(ClearingId from, ClearingId to, int n) => Move + ":" + N(from.Value) + ":" + N(to.Value) + ":" + N(n);
        public static string BattleKey(ClearingId c, SeatId defender) => Battle + ":" + N(c.Value) + ":" + N(defender.Value);
        public static string FieldHospitalKey(ClearingId c) => FieldHospital + ":" + N(c.Value);
        public static string[] Parts(string key) => key == null ? new string[0] : key.Split(':');
        private static bool I(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        public static bool IsVerb(string key, string verb) => Parts(key).FirstOrDefault() == verb;

        public static GustaveBattle? ParseBattle(string key)
        {
            var p = Parts(key);
            if (p.Length != 3 || p[0] != Battle || !I(p[1], out var c) || !I(p[2], out var s)) return null;
            return new GustaveBattle(new ClearingId(c), new SeatId(s));
        }

        public static (ClearingId from, ClearingId to, int count)? ParseMove(string key)
        {
            var p = Parts(key);
            if (p.Length != 4 || p[0] != Move || !I(p[1], out var f) || !I(p[2], out var t) || !I(p[3], out var n)) return null;
            return (new ClearingId(f), new ClearingId(t), n);
        }

        /// <summary>build:&lt;clearing&gt;:&lt;type&gt; → the clearing, or null.</summary>
        public static ClearingId? BuildClearing(string key)
        {
            var p = Parts(key);
            return p.Length == 3 && p[0] == Build && I(p[1], out var c) ? new ClearingId(c) : (ClearingId?)null;
        }
    }

    public readonly struct GustaveBattle : IEquatable<GustaveBattle>
    {
        public readonly ClearingId Clearing;
        public readonly SeatId Defender;
        public GustaveBattle(ClearingId clearing, SeatId defender) { Clearing = clearing; Defender = defender; }
        public bool Equals(GustaveBattle o) => Clearing == o.Clearing && Defender == o.Defender;
        public override bool Equals(object obj) => obj is GustaveBattle o && Equals(o);
        public override int GetHashCode() => Clearing.Value * 31 + Defender.Value;
        public override string ToString() => "battle " + Clearing + " vs " + Defender;
    }

    /// <summary>bot.gustave.01-route: the planned Route and its cost.</summary>
    public sealed class RoutePlan
    {
        public IReadOnlyList<ClearingId> Path = new ClearingId[0];
        public int Cost;
    }

    /// <summary>bot.gustave.02-hold: one clearing to retake (or, on Hard, to top up).</summary>
    public sealed class HoldOrder
    {
        public ClearingId Clearing;
        public int WarriorsNeeded;
        public bool Recruit;
        public ClearingId? MarchFrom;
        public bool TopUp;
    }

    /// <summary>bot.gustave.03-build: where to build and what (null type: Root's Marquise AI picks).</summary>
    public sealed class BuildOrder
    {
        public ClearingId Clearing;
        public string Type;
        public bool Beachhead;
    }

    /// <summary>bot.gustave.08-spoiler: the chosen threat and where to answer it.</summary>
    public sealed class GustaveSpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        public bool PreEmpts;
        public IReadOnlyList<ClearingId> Clearings = new ClearingId[0];
    }

    /// <summary>
    /// bot.gustave: the Goal Director for Gustave's Marquise. One public [BotRule] method per bot_rules row, asked in order
    /// by <see cref="ChooseAction"/>; the spoiler layer pre-empts them for a banner that is up (and on Hard a one-step threat,
    /// except on his own announce turn). Rule methods read only <see cref="BotView"/>.
    /// </summary>
    public sealed class GustaveBot : IBotDirector
    {
        /// <summary>Root's Marquise building track: the cost of the next building by how many of that type are already built.</summary>
        private static readonly int[] MarquiseTrackCosts = { 0, 1, 2, 3, 3, 4 };
        private static readonly string[] MarquiseBuildings = { PieceTypes.Sawmill, PieceTypes.Workshop, PieceTypes.Recruiter };

        /// <summary>Hard: the cost each Route clearing had when the Route was planned (transient; after a load the Route is
        /// recomputed at his next Birdsong anyway).</summary>
        private readonly Dictionary<ClearingId, int> _plannedCosts = new Dictionary<ClearingId, int>();

        public string PlanId => Ids.BotPlans.Gustave;

        public bool InGoalMode(BotView view) => GoalMode(view);

        public void PlanTurn(BotView view, ModState state)
        {
            var plan = Route(view);
            state.GustaveRoute = plan?.Path.ToArray() ?? new ClearingId[0];
            _plannedCosts.Clear();
            foreach (var c in state.GustaveRoute) _plannedCosts[c] = RouteCost(view, c);
        }

        /// <summary>Hard: re-plan when an action raised the cost of a Route clearing. Returns true when it re-planned.</summary>
        public bool ReplanIfRaised(BotView view, ModState state)
        {
            if (view.Difficulty != Difficulty.Hard || state.GustaveRoute.Count == 0) return false;
            if (!state.GustaveRoute.Any(c => _plannedCosts.TryGetValue(c, out var old) && RouteCost(view, c) > old)) return false;
            PlanTurn(view, state);
            return true;
        }

        // ------------------------------------------------------------ helpers

        private static ClearingId? Lumiere(BotView v) => v.Role(Ids.MapRoles.Lumiere);
        private static ClearingId? Monolith(BotView v) => v.Role(Ids.MapRoles.Monolith);
        private static bool Rules(BotView v, ClearingId c) => v.Board.Ruler(c) == v.Seat;
        private static IReadOnlyList<ClearingId> Route(ModState s) => s.GustaveRoute ?? new ClearingId[0];

        private static int Strength(IBoardView b, SeatId s, ClearingId c) => b.Warriors(s, c) + b.Buildings(s, c);

        private static int EnemyWarriors(BotView v, ClearingId c) =>
            v.Board.Seats().Where(s => s != v.Seat).Sum(s => v.Board.Warriors(s, c));

        private static int Mask(IBoardView b, ClearingId c)
        {
            var m = 0;
            foreach (var s in b.SuitsOf(c))
                if (s == Suit.Fox) m |= 1; else if (s == Suit.Rabbit) m |= 2; else if (s == Suit.Mouse) m |= 4;
            return m;
        }

        /// <summary>The Route's Beachhead: its last clearing when it is next to the Monolith.</summary>
        public ClearingId? RouteBeachhead(BotView view)
        {
            var route = Route(view.State);
            if (route.Count == 0) return null;
            var last = route[route.Count - 1];
            return view.MonolithNeighbours().Contains(last) ? last : (ClearingId?)null;
        }

        /// <summary>E[max of two 0-3 dice capped at <paramref name="cap"/>] (the attacker's roll).</summary>
        public static double ExpectedHigh(int cap)
        {
            double e = 0;
            for (var a = 0; a < 4; a++) for (var b = 0; b < 4; b++) e += Math.Min(Math.Max(a, b), Math.Max(0, cap));
            return e / 16.0;
        }

        /// <summary>E[min of two 0-3 dice capped at <paramref name="cap"/>] (the defender's roll).</summary>
        public static double ExpectedLow(int cap)
        {
            double e = 0;
            for (var a = 0; a < 4; a++) for (var b = 0; b < 4; b++) e += Math.Min(Math.Min(a, b), Math.Max(0, cap));
            return e / 16.0;
        }

        /// <summary>His expected hits in c against a defender (+1 when the defender has no warriors).</summary>
        public static double ExpectedHits(IBoardView b, SeatId me, SeatId defender, ClearingId c) =>
            ExpectedHigh(b.Warriors(me, c)) + (b.Warriors(defender, c) == 0 ? 1 : 0);

        private static int DefenderPieces(IBoardView b, SeatId d, ClearingId c) => b.Warriors(d, c) + b.Buildings(d, c) + b.Tokens(d, c);

        /// <summary>Whether win.gustave's announce test will pass at the end of this turn if nothing changes (gates included;
        /// ruled clearings holding his building count as flagged).</summary>
        public bool AnnounceTurn(BotView view)
        {
            var win = view.WinOf(view.Seat);
            if (view.Act < (win?.ActMin ?? Data.WinConditions[Ids.WinConditions.Gustave].ActMin)) return false;
            if (view.Board.Vp(view.Seat) < Tuning.CountdownVp || view.Board.ActiveDominance(view.Seat).HasValue) return false;
            return GustaveChain.RoadTest(view.Board, view.State, view.Seat, Lumiere(view), Monolith(view), countPendingFlags: true);
        }

        // ------------------------------------------------------------ 01 Route

        /// <summary>cost(c) of bot.gustave.01-route.</summary>
        public int RouteCost(BotView view, ClearingId c)
        {
            var b = view.Board;
            int cost;
            if (Rules(view, c))
                cost = view.State.GustaveFlags.Contains(c) || GustaveChain.WouldFlag(b, view.Seat, c) ? 0 : Tuning.GustaveRouteCostRuled;
            else cost = Tuning.GustaveRouteCostUnruled + EnemyWarriors(view, c);
            if (b.FreeSlots(c) == 0 && b.Buildings(view.Seat, c) == 0) cost += Tuning.GustaveRouteCostFull;
            return cost;
        }

        private sealed class Label
        {
            public ClearingId C;
            public int Mask;
            public int Cost;
            public List<ClearingId> Path;
        }

        private static int Compare(Label a, Label b)
        {
            if (a.Cost != b.Cost) return a.Cost.CompareTo(b.Cost);
            if (a.Path.Count != b.Path.Count) return a.Path.Count.CompareTo(b.Path.Count);
            for (var i = 0; i < a.Path.Count; i++)
                if (a.Path[i] != b.Path[i]) return a.Path[i].Value.CompareTo(b.Path[i].Value);
            return 0;
        }

        /// <summary>bot.gustave.01-route: the cheapest path from Lumière to a Beachhead covering fox, rabbit and mouse, searched
        /// over (clearing, suits so far) along paths. Ties: fewer clearings, then lower numbers in path order. Null when none.</summary>
        [BotRule("bot.gustave.01-route")]
        public RoutePlan Route(BotView view)
        {
            var start = Lumiere(view);
            var beachheads = new HashSet<ClearingId>(view.MonolithNeighbours());
            if (!start.HasValue || beachheads.Count == 0) return null;
            var b = view.Board;
            var full = 7;
            var best = new Dictionary<(int, int), Label>();
            var settled = new HashSet<(int, int)>();
            var first = new Label { C = start.Value, Mask = Mask(b, start.Value), Cost = RouteCost(view, start.Value), Path = new List<ClearingId> { start.Value } };
            best[(first.C.Value, first.Mask)] = first;
            while (true)
            {
                Label cur = null;
                foreach (var kv in best)
                    if (!settled.Contains(kv.Key) && (cur == null || Compare(kv.Value, cur) < 0)) cur = kv.Value;
                if (cur == null) return null;
                settled.Add((cur.C.Value, cur.Mask));
                if (cur.Mask == full && beachheads.Contains(cur.C)) return new RoutePlan { Path = cur.Path, Cost = cur.Cost };
                foreach (var n in b.PathNeighbours(cur.C))
                {
                    if (cur.Path.Contains(n)) continue;
                    var next = new Label { C = n, Mask = cur.Mask | Mask(b, n), Cost = cur.Cost + RouteCost(view, n), Path = new List<ClearingId>(cur.Path) { n } };
                    var key = (n.Value, next.Mask);
                    if (settled.Contains(key)) continue;
                    if (best.TryGetValue(key, out var old) && Compare(old, next) <= 0) continue;
                    best[key] = next;
                }
            }
        }

        // ------------------------------------------------------------ 02 Hold first

        /// <summary>bot.gustave.02-hold: lost flagged Route clearings in Route order (recruit there with a recruiter, otherwise
        /// march from the ruled neighbour with the most spare warriors); Hard also tops every Chain clearing up to
        /// tune.gustave-hard-rule-margin. Easy skips this step.</summary>
        [BotRule("bot.gustave.02-hold")]
        public List<HoldOrder> Hold(BotView view)
        {
            var orders = new List<HoldOrder>();
            if (view.Difficulty == Difficulty.Easy) return orders;
            var b = view.Board;
            foreach (var c in Route(view.State))
            {
                if (!view.State.GustaveFlags.Contains(c) || Rules(view, c)) continue;
                var ruler = b.Ruler(c);
                var theirs = ruler.HasValue ? Strength(b, ruler.Value, c)
                    : b.Seats().Where(s => s != view.Seat).Select(s => Strength(b, s, c)).DefaultIfEmpty(0).Max();
                orders.Add(Order(view, c, theirs - Strength(b, view.Seat, c) + 1, false));
            }
            if (view.Difficulty == Difficulty.Hard)
            {
                var chain = GustaveChain.Chain(b, view.State, view.Seat, Lumiere(view));
                foreach (var c in chain.OrderBy(c => GustaveChain.Margin(b, view.Seat, c)).ThenBy(c => c.Value))
                {
                    var margin = GustaveChain.Margin(b, view.Seat, c);
                    if (margin < Tuning.GustaveHardRuleMargin) orders.Add(Order(view, c, Tuning.GustaveHardRuleMargin - margin, true));
                }
            }
            return orders;
        }

        private static HoldOrder Order(BotView view, ClearingId c, int needed, bool topUp)
        {
            var b = view.Board;
            var o = new HoldOrder { Clearing = c, WarriorsNeeded = Math.Max(1, needed), TopUp = topUp };
            if (b.BuildingsOfType(view.Seat, c, PieceTypes.Recruiter) > 0) { o.Recruit = true; return o; }
            o.MarchFrom = b.PathNeighbours(c).Where(n => Rules(view, n))
                .Select(n => (n, spare: Math.Min(b.Warriors(view.Seat, n), GustaveChain.Margin(b, view.Seat, n) - 1)))
                .Where(x => x.spare > 0).OrderByDescending(x => x.spare).ThenBy(x => x.n.Value)
                .Select(x => (ClearingId?)x.n).FirstOrDefault();
            return o;
        }

        // ------------------------------------------------------------ 03 Build on the Route

        /// <summary>bot.gustave.03-build: the Beachhead first whenever he rules it without a building there, then ruled, unflagged
        /// Route clearings with a free slot in Route order. A sawmill when no sawmill stands among the ruled clearings connected
        /// to it; otherwise the type with the lowest next track cost (ties: null, Root's Marquise AI picks).
        /// Easy: at most one Route build per turn (a ruled, unflagged Route clearing that already holds his building was built this turn).</summary>
        [BotRule("bot.gustave.03-build")]
        public List<BuildOrder> Build(BotView view)
        {
            var b = view.Board;
            var route = Route(view.State);
            var orders = new List<BuildOrder>();
            if (view.Difficulty == Difficulty.Easy
                && route.Any(c => Rules(view, c) && !view.State.GustaveFlags.Contains(c) && b.Buildings(view.Seat, c) > 0))
                return orders;
            var beach = RouteBeachhead(view);
            if (beach.HasValue && Rules(view, beach.Value) && b.Buildings(view.Seat, beach.Value) == 0 && b.FreeSlots(beach.Value) > 0)
                orders.Add(new BuildOrder { Clearing = beach.Value, Type = BuildingFor(view, beach.Value), Beachhead = true });
            foreach (var c in route)
            {
                if (orders.Any(o => o.Clearing == c)) continue;
                if (Rules(view, c) && !view.State.GustaveFlags.Contains(c) && b.FreeSlots(c) > 0)
                    orders.Add(new BuildOrder { Clearing = c, Type = BuildingFor(view, c) });
            }
            if (view.Difficulty == Difficulty.Easy && orders.Count > 1) orders.RemoveRange(1, orders.Count - 1);
            return orders;
        }

        private static string BuildingFor(BotView view, ClearingId c)
        {
            var b = view.Board;
            var connected = Graph.ConnectedWithin(c, x => b.PathNeighbours(x), x => x == c || Rules(view, x));
            if (!connected.Any(x => b.BuildingsOfType(view.Seat, x, PieceTypes.Sawmill) > 0)) return PieceTypes.Sawmill;
            var costs = MarquiseBuildings.Where(t => b.SupplyPieces(view.Seat, t) > 0)
                .Select(t => (t, cost: MarquiseTrackCosts[Math.Max(0, Math.Min(MarquiseTrackCosts.Length - 1, MarquiseTrackCosts.Length - b.SupplyPieces(view.Seat, t)))]))
                .OrderBy(x => x.cost).ToList();
            if (costs.Count == 0) return null;
            if (costs.Count > 1 && costs[1].cost == costs[0].cost) return null;
            return costs[0].t;
        }

        // ------------------------------------------------------------ 04 Charge the Arm

        /// <summary>bot.gustave.04-charge: while the Arm is under tune.gustave-arm-max, his legal winnable battles (more warriors
        /// than the defender there, expected hits of at least one): Route clearings first, then expected pieces removed minus
        /// expected warriors lost; ties nearer Lumière along the Route, then lower clearing number.</summary>
        [BotRule("bot.gustave.04-charge")]
        public List<Ranked<GustaveBattle>> Charge(BotView view)
        {
            if (view.State.GustaveArm >= Tuning.GustaveArmMax) return new List<Ranked<GustaveBattle>>();
            var b = view.Board;
            var route = Route(view.State).ToList();
            var battles = b.LegalActions(view.Seat).Select(GustaveActionKey.ParseBattle).Where(x => x.HasValue).Select(x => x.Value)
                .Where(t => t.Defender != view.Seat && b.Warriors(view.Seat, t.Clearing) > b.Warriors(t.Defender, t.Clearing)
                            && ExpectedHits(b, view.Seat, t.Defender, t.Clearing) >= 1)
                .ToList();
            double Score(GustaveBattle t)
            {
                var removed = Math.Min(ExpectedHits(b, view.Seat, t.Defender, t.Clearing), DefenderPieces(b, t.Defender, t.Clearing));
                var lost = ExpectedLow(b.Warriors(t.Defender, t.Clearing));
                return (route.Contains(t.Clearing) ? 1000 : 0) + removed - lost;
            }
            int Tie(GustaveBattle t) { var i = route.IndexOf(t.Clearing); return (i < 0 ? 100 : i) * 100 + t.Clearing.Value; }
            return Rank.Descending(battles, Score, Tie);
        }

        // ------------------------------------------------------------ 05 Overcharge

        /// <summary>bot.gustave.05-overcharge: arm only with a full Arm, for a battle in the Route's Beachhead or in a Lumière
        /// ruled by another seat; never on an expected announce turn unless that battle is required (he cannot win it without
        /// the extra hit and Lumière or the Beachhead is lost without it). Easy never Overcharges.</summary>
        [BotRule("bot.gustave.05-overcharge")]
        public bool Overcharge(BotView view, ClearingId c, SeatId defender)
        {
            if (view.Difficulty == Difficulty.Easy || view.State.GustaveArm < Tuning.GustaveArmMax) return false;
            var b = view.Board;
            var lumiere = Lumiere(view);
            var beach = RouteBeachhead(view);
            var key = (beach.HasValue && c == beach.Value) || (lumiere.HasValue && c == lumiere.Value && b.Ruler(c).HasValue && b.Ruler(c) != view.Seat);
            if (!key) return false;
            if (!AnnounceTurn(view)) return true;
            var need = b.Warriors(defender, c) + b.Buildings(defender, c);
            var hits = ExpectedHits(b, view.Seat, defender, c);
            return !Rules(view, c) && hits < need && hits + Tuning.GustaveOverchargeHits >= need;
        }

        // ------------------------------------------------------------ 06 Goal mode

        /// <summary>bot.gustave.06-goal-mode: Vp &gt;= tune.gustave-goal-vp or Route cost &lt;= tune.gustave-goal-route-cost
        /// (Easy: Vp &gt;= tune.gustave-easy-goal-vp only).</summary>
        [BotRule("bot.gustave.06-goal-mode")]
        public bool GoalMode(BotView view)
        {
            var vp = view.Board.Vp(view.Seat);
            if (view.Difficulty == Difficulty.Easy) return vp >= Tuning.GustaveEasyGoalVp;
            if (vp >= Tuning.GustaveGoalVp) return true;
            var plan = Route(view);
            return plan != null && plan.Cost <= Tuning.GustaveGoalRouteCost;
        }

        // ------------------------------------------------------------ 07 Announce turn

        /// <summary>bot.gustave.07-announce: on the announce turn, the clearings to thicken: Chain clearings in ascending margin
        /// (ties: lower number), then the Beachhead. Empty on any other turn.</summary>
        [BotRule("bot.gustave.07-announce")]
        public List<ClearingId> Announce(BotView view)
        {
            if (!AnnounceTurn(view)) return new List<ClearingId>();
            var b = view.Board;
            var chain = GustaveChain.ChainAfterFlags(b, view.State, view.Seat, Lumiere(view));
            var beach = GustaveChain.ChainBeachhead(b, view.Seat, chain, Monolith(view));
            var list = chain.Where(c => c != beach).OrderBy(c => GustaveChain.Margin(b, view.Seat, c)).ThenBy(c => c.Value).ToList();
            if (beach.HasValue) list.Add(beach.Value);
            return list;
        }

        // ------------------------------------------------------------ 08 Spoiler layer

        /// <summary>bot.gustave.08-spoiler: the most urgent threat (banners first, then the next Birdsong in turn order, then
        /// higher VP) and its key clearings; null with no trigger. Pre-empts the steps for a banner (Normal and Hard), and on
        /// Hard for a one-step threat unless this is his own announce turn. Easy answers banners only, with free actions.</summary>
        [BotRule("bot.gustave.08-spoiler")]
        public GustaveSpoilerAnswer Spoiler(BotView view)
        {
            var b = view.Board;
            var seats = b.Seats();
            var partner = b.CoalitionPartner(view.Seat);
            var threats = new List<(SeatId seat, bool banner)>();
            foreach (var s in seats)
            {
                if (s == view.Seat || partner == s || b.CoalitionPartner(s) == view.Seat) continue;
                var win = view.WinOf(s);
                if (win == null || view.Act < win.ActMin) continue;
                if (win.OffWithDominance && b.ActiveDominance(s).HasValue) continue;
                if (win.OffInCoalition && b.CoalitionPartner(s).HasValue) continue;
                var banner = view.Countdowns.Contains(s);
                if (!banner && (view.Difficulty == Difficulty.Easy || view.UnitsMissing(s) != 1)) continue;
                threats.Add((s, banner));
            }
            if (threats.Count == 0) return null;
            var order = seats.ToList();
            var cur = order.IndexOf(b.CurrentSeat());
            int Next(SeatId s) { var i = order.IndexOf(s); return cur < 0 ? i : (i - cur + order.Count) % order.Count; }
            var t = threats.OrderByDescending(x => x.banner).ThenBy(x => Next(x.seat)).ThenByDescending(x => b.Vp(x.seat)).ThenBy(x => x.seat.Value).First();
            var pre = view.Difficulty != Difficulty.Easy && (t.banner || (view.Difficulty == Difficulty.Hard && !AnnounceTurn(view)));
            return new GustaveSpoilerAnswer { Threat = t.seat, Banner = t.banner, PreEmpts = pre, Clearings = view.KeyClearings(t.seat) };
        }

        /// <summary>The cheapest legal answer to a spoiler threat: a battle against the threat in a key clearing (cost = one
        /// action + expected hits taken), else a march into a key clearing, else a recruit there. Never Overcharges.</summary>
        public string SpoilerKey(BotView view, GustaveSpoilerAnswer a, IReadOnlyList<string> legal)
        {
            if (a == null || a.Clearings.Count == 0) return null;
            var b = view.Board;
            var keys = new HashSet<ClearingId>(a.Clearings);
            var battle = legal.Select(GustaveActionKey.ParseBattle).Where(x => x.HasValue).Select(x => x.Value)
                .Where(x => x.Defender == a.Threat && keys.Contains(x.Clearing))
                .OrderBy(x => 1 + ExpectedLow(b.Warriors(x.Defender, x.Clearing))).ThenBy(x => x.Clearing.Value)
                .Select(x => (GustaveBattle?)x).FirstOrDefault();
            if (battle.HasValue) return GustaveActionKey.BattleKey(battle.Value.Clearing, battle.Value.Defender);
            var move = legal.Select(k => (k, m: GustaveActionKey.ParseMove(k))).Where(x => x.m.HasValue && keys.Contains(x.m.Value.to))
                .OrderByDescending(x => x.m.Value.count).ThenBy(x => x.m.Value.to.Value).Select(x => x.k).FirstOrDefault();
            if (move != null) return move;
            return a.Clearings.Select(GustaveActionKey.RecruitKey).FirstOrDefault(legal.Contains);
        }

        // ------------------------------------------------------------ ChooseAction

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            var legal = legalActions ?? new string[0];
            var spoiler = Spoiler(view);
            if (spoiler != null && spoiler.PreEmpts)
            {
                var k = SpoilerKey(view, spoiler, legal);
                if (k != null) return k;
            }
            var goal = GoalMode(view);
            foreach (var o in Hold(view))
            {
                if (o.Recruit && legal.Contains(GustaveActionKey.RecruitKey(o.Clearing))) return GustaveActionKey.RecruitKey(o.Clearing);
                var mv = MoveInto(legal, o.Clearing, o.MarchFrom);
                if (mv != null) return mv;
            }
            var builds = Build(view);
            foreach (var o in builds)
            {
                if (o.Type != null && legal.Contains(GustaveActionKey.BuildKey(o.Clearing, o.Type))) return GustaveActionKey.BuildKey(o.Clearing, o.Type);
                if (o.Type == null)
                {
                    var any = legal.FirstOrDefault(k => GustaveActionKey.BuildClearing(k) == o.Clearing);
                    if (any != null) return any;
                }
            }
            var charge = Charge(view);
            if (charge.Count > 0) return GustaveActionKey.BattleKey(charge[0].Item.Clearing, charge[0].Item.Defender);
            if (goal)
            {
                var routeWork = builds.Count > 0 || Hold(view).Count > 0;
                if (routeWork && legal.Contains(GustaveActionKey.BirdExtra) && view.Board.HandSuits(view.Seat).Contains(Suit.Bird))
                    return GustaveActionKey.BirdExtra;
            }
            var thicken = Announce(view);
            if (thicken.Count > 0)
            {
                foreach (var c in thicken)
                    if (legal.Contains(GustaveActionKey.FieldHospitalKey(c))) return GustaveActionKey.FieldHospitalKey(c);
                foreach (var c in thicken)
                {
                    if (legal.Contains(GustaveActionKey.RecruitKey(c))) return GustaveActionKey.RecruitKey(c);
                    var mv = MoveInto(legal, c, null);
                    if (mv != null) return mv;
                }
            }
            if (spoiler != null && !spoiler.PreEmpts && (spoiler.Banner || view.Difficulty != Difficulty.Easy))
            {
                var k = SpoilerKey(view, spoiler, legal);
                if (k != null) return k;
            }
            if (goal && legal.Any(k => GustaveActionKey.IsVerb(k, GustaveActionKey.Overwork)) && legal.Contains(GustaveActionKey.Pass))
                return GustaveActionKey.Pass;
            return null;
        }

        private static string MoveInto(IReadOnlyList<string> legal, ClearingId to, ClearingId? from) =>
            legal.Select(k => (k, m: GustaveActionKey.ParseMove(k)))
                .Where(x => x.m.HasValue && x.m.Value.to == to && (!from.HasValue || x.m.Value.from == from.Value))
                .OrderByDescending(x => x.m.Value.count).ThenBy(x => x.m.Value.from.Value).Select(x => x.k).FirstOrDefault();
    }
}
