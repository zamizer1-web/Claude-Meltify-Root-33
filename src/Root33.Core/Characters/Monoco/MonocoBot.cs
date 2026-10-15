using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Monoco
{
    /// <summary>One Warlord battle: the clearing and the defending seat.</summary>
    public sealed class MonocoBattle
    {
        public MonocoBattle(ClearingId clearing, SeatId defender) { Clearing = clearing; Defender = defender; }
        public ClearingId Clearing { get; }
        public SeatId Defender { get; }
        public override string ToString() => "battle:" + Clearing.Value + ":" + Defender.Value;
    }

    /// <summary>A step-2 target: the foot it fills and the battle that fills it.</summary>
    public sealed class MonocoTarget
    {
        public MonocoTarget(int foot, MonocoBattle battle) { Foot = foot; Battle = battle; }
        public int Foot { get; }
        public MonocoBattle Battle { get; }
    }

    /// <summary>A spoiler answer: a Warlord battle (breaks a met part) or a Command move into a key clearing (blocks).</summary>
    public sealed class MonocoAnswer
    {
        public MonocoAnswer(bool breaksMetPart, ClearingId clearing, SeatId? defender, ClearingId? from, double cost, bool preempts)
        {
            BreaksMetPart = breaksMetPart; Clearing = clearing; Defender = defender; From = from; Cost = cost; Preempts = preempts;
        }
        public bool BreaksMetPart { get; }
        public ClearingId Clearing { get; }
        public SeatId? Defender { get; }
        public ClearingId? From { get; }
        public double Cost { get; }
        public bool Preempts { get; }
        public string ActionKey => BreaksMetPart && Defender.HasValue
            ? "battle:" + Clearing.Value + ":" + Defender.Value.Value
            : "command:" + (From.HasValue ? From.Value.Value : 0) + ":" + Clearing.Value;
    }

    /// <summary>
    /// bot.monoco. Steps in priority order (bot.monoco.01..08), one [BotRule] method each; the methods read only the
    /// <see cref="BotView"/>. Action keys follow the other bots' "verb:args" assumption (Root's key format is unverified):
    /// "battle:clearing:seat", "advance:from:to", "command:from:to", "mood:cardId", "build:clearing[:type]".
    /// </summary>
    public sealed class MonocoBot : IBotDirector
    {
        private const int DieFaces = 4;
        public string PlanId => Ids.BotPlans.Monoco;

        /// <summary>goalModeTrigger: feet-driven from his first Daylight on every difficulty.</summary>
        public bool InGoalMode(BotView view) => true;

        /// <summary>bot.monoco keeps no plan state beyond state.monoco.feet (no state.json row for a planned battle).</summary>
        public void PlanTurn(BotView view, ModState state) { }

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var spoiler = Spoiler(view);
            if (spoiler != null && spoiler.Preempts && legalActions.Contains(spoiler.ActionKey)) return spoiler.ActionKey;
            var best = BattleScore(view).Select(r => r.Item.ToString()).FirstOrDefault(legalActions.Contains);
            if (best != null) return best;
            if (spoiler != null && legalActions.Contains(spoiler.ActionKey)) return spoiler.ActionKey;
            return null;
        }

        // ================================================================= shared reads

        private static bool IsEnemy(BotView view, SeatId s) => s != view.Seat;

        /// <summary>Distances from <paramref name="from"/> along paths.</summary>
        public static Dictionary<ClearingId, int> Distances(IBoardView b, ClearingId from)
        {
            var d = new Dictionary<ClearingId, int> { [from] = 0 };
            var q = new Queue<ClearingId>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var n in b.PathNeighbours(c).OrderBy(x => x.Value))
                    if (!d.ContainsKey(n)) { d[n] = d[c] + 1; q.Enqueue(n); }
            }
            return d;
        }

        /// <summary>Clearings the Warlord can reach this Daylight: his own and those within the Prowess track (at least 1).</summary>
        public static List<ClearingId> Reach(BotView view)
        {
            var w = view.Board.PawnClearing(view.Seat);
            if (!w.HasValue) return new List<ClearingId>();
            var steps = Math.Max(1, view.Board.HoardTrack(view.Seat, "prowess"));
            return Distances(view.Board, w.Value).Where(kv => kv.Value <= steps).Select(kv => kv.Key).OrderBy(c => c.Value).ToList();
        }

        /// <summary>His stack in a battle: the Warlord, the warriors with him, and his warriors already there.</summary>
        public static int Stack(BotView view, ClearingId c)
        {
            var w = view.Board.PawnClearing(view.Seat);
            if (!w.HasValue) return 0;
            return 1 + view.Board.Warriors(view.Seat, w.Value) + (c == w.Value ? 0 : view.Board.Warriors(view.Seat, c));
        }

        /// <summary>The chance his hits remove every defending warrior (two 0-3 dice, attacker deals the higher capped by his
        /// stack, +1 hit against a defender with no warriors).</summary>
        public static double WinChance(int stack, int defenders)
        {
            if (defenders <= 0) return 1;
            var wins = 0;
            for (var a = 0; a < DieFaces; a++)
                for (var b = 0; b < DieFaces; b++)
                    if (Math.Min(Math.Max(a, b), stack) >= defenders) wins++;
            return wins / (double)(DieFaces * DieFaces);
        }

        /// <summary>Expected hits he takes (the defender deals the lower die capped by its warriors).</summary>
        public static double ExpectedHitsTaken(int defenders)
        {
            var sum = 0;
            for (var a = 0; a < DieFaces; a++)
                for (var b = 0; b < DieFaces; b++)
                    sum += Math.Min(Math.Min(a, b), defenders);
            return sum / (double)(DieFaces * DieFaces);
        }

        private static IEnumerable<SeatId> VpLeaders(BotView view)
        {
            var seats = view.Board.Seats();
            var top = seats.Max(s => view.Board.Vp(s));
            return seats.Where(s => s != view.Seat && view.Board.Vp(s) == top);
        }

        private static int Pieces(IBoardView b, SeatId s, ClearingId c) => b.Warriors(s, c) + b.Buildings(s, c) + b.Tokens(s, c);

        /// <summary>The missing feet a battle could fill.</summary>
        public static List<int> FeetFilled(BotView view, MonocoBattle bt)
        {
            var b = view.Board;
            var feet = view.State.MonocoFeet;
            var w = b.PawnClearing(view.Seat);
            var c = bt.Clearing;
            var d = bt.Defender;
            var res = new List<int>();
            if (!feet[Foot.Agile].Earned && b.Warriors(d, c) > 0 && w.HasValue && w.Value != c) res.Add(Foot.Agile);
            if (!feet[Foot.Caster].Earned && b.Tokens(d, c) > 0) res.Add(Foot.Caster);
            if (!feet[Foot.Balanced].Earned && b.Warriors(d, c) > 0) res.Add(Foot.Balanced);
            if (!feet[Foot.Heavy].Earned && b.Buildings(d, c) > 0) res.Add(Foot.Heavy);
            if (!feet[Foot.Almighty].Earned && view.Act >= Tuning.MonocoAlmightyAct && VpLeaders(view).Contains(d)
                && (b.Buildings(d, c) > 0 || Pieces(b, d, c) >= Tuning.MonocoAlmightyPieces)) res.Add(Foot.Almighty);
            return res;
        }

        /// <summary>Whether the defenders could deal hits equal to his stack (warriors capped at 3 &gt;= his stack).</summary>
        public static bool CouldWipeStack(BotView view, MonocoBattle bt) =>
            Math.Min(view.Board.Warriors(bt.Defender, bt.Clearing), DieFaces - 1) >= Stack(view, bt.Clearing);

        /// <summary>Step 1's score without the heavy-penalty ordering.</summary>
        public static double Score(BotView view, MonocoBattle bt)
        {
            var feet = view.State.MonocoFeet;
            var score = 0.0;
            if (FeetFilled(view, bt).Count > 0) score += Tuning.MonocoBotMissingFoot;
            var factions = feet.Where(f => f.Earned && f.Faction.HasValue).Select(f => f.Faction!.Value).Distinct().ToList();
            if (!factions.Contains(bt.Defender) && factions.Count < Tuning.MonocoFeetFactions) score += Tuning.MonocoBotNewFaction;
            score += WinChance(Stack(view, bt.Clearing), view.Board.Warriors(bt.Defender, bt.Clearing));
            return score;
        }

        /// <summary>Every Warlord battle he can reach (enemy pieces in a reachable clearing).</summary>
        public static List<MonocoBattle> Battles(BotView view)
        {
            var b = view.Board;
            return Reach(view).SelectMany(c => b.Seats().Where(s => IsEnemy(view, s) && Pieces(b, s, c) > 0)
                .Select(s => new MonocoBattle(c, s))).ToList();
        }

        // ================================================================= steps

        /// <summary>bot.monoco.01-battle-score: reachable battles by step 1's score; a battle that could wipe his stack ranks
        /// below every battle without that risk; ties toward the lower clearing.</summary>
        [BotRule("bot.monoco.01-battle-score")]
        public List<Ranked<MonocoBattle>> BattleScore(BotView view) =>
            Battles(view).Select(bt => new { bt, risk = CouldWipeStack(view, bt), s = Score(view, bt) })
                .OrderBy(x => x.risk).ThenByDescending(x => x.s).ThenBy(x => x.bt.Clearing.Value).ThenBy(x => x.bt.Defender.Value)
                .Select(x => new Ranked<MonocoBattle>(x.bt, x.s)).ToList();

        /// <summary>bot.monoco.02-targets: targets for the missing feet (step 1's score first, then the fewest defending
        /// warriors, then the lower clearing). Easy only where the Warlord outnumbers the defenders by tune.monoco-easy-outnumber.</summary>
        [BotRule("bot.monoco.02-targets")]
        public List<Ranked<MonocoTarget>> Targets(BotView view)
        {
            var b = view.Board;
            var w = b.PawnClearing(view.Seat);
            if (!w.HasValue) return new List<Ranked<MonocoTarget>>();
            var adj = b.PathNeighbours(w.Value);
            var list = new List<MonocoTarget>();
            foreach (var bt in Battles(view))
            {
                var c = bt.Clearing;
                var d = bt.Defender;
                var guards = b.Warriors(d, c);
                var stack = Stack(view, c);
                if (view.Difficulty == Difficulty.Easy && stack - guards < Tuning.MonocoEasyOutnumber) continue;
                foreach (var foot in FeetFilled(view, bt))
                {
                    var ok = foot switch
                    {
                        Foot.Heavy => guards <= Tuning.MonocoBotTargetGuards,
                        Foot.Caster => guards <= Tuning.MonocoBotTargetGuards,
                        Foot.Agile => guards == 1 && adj.Contains(c),
                        Foot.Balanced => guards >= 1 && WinChance(stack, guards) > 0,
                        Foot.Almighty => b.Buildings(d, c) > 0 || stack >= guards,
                        _ => false,
                    };
                    if (ok) list.Add(new MonocoTarget(foot, bt));
                }
            }
            return list.Select(t => new { t, s = Score(view, t.Battle), g = b.Warriors(t.Battle.Defender, t.Battle.Clearing),
                    wc = WinChance(Stack(view, t.Battle.Clearing), b.Warriors(t.Battle.Defender, t.Battle.Clearing)) })
                .OrderByDescending(x => x.s).ThenBy(x => x.t.Foot == Foot.Balanced ? -x.wc : x.g)
                .ThenBy(x => x.t.Battle.Clearing.Value).ThenBy(x => x.t.Foot)
                .Select(x => new Ranked<MonocoTarget>(x.t, x.s)).ToList();
        }

        /// <summary>bot.monoco.03-bring-rats: the turn before the planned battle, the clearings to Command-move rats from,
        /// nearest to the target first (fewest moves), ties toward the lower clearing. Easy skips this step.</summary>
        [BotRule("bot.monoco.03-bring-rats")]
        public List<Ranked<ClearingId>> BringRats(BotView view, ClearingId target)
        {
            if (view.Difficulty == Difficulty.Easy) return new List<Ranked<ClearingId>>();
            var d = Distances(view.Board, target);
            var w = view.Board.PawnClearing(view.Seat);
            var sources = d.Keys.Where(c => c != target && (!w.HasValue || c != w.Value) && view.Board.Warriors(view.Seat, c) > 0);
            return Rank.Clearings(sources, c => -d[c]);
        }

        /// <summary>bot.monoco.04-mood: the mood (card id from the legal mood actions) whose effect adds most to the planned
        /// battle's step 1 score; ties keep the current mood. Root's mood effects are not in the sheets, so the caller supplies
        /// how much each mood adds (<paramref name="addedScore"/>).</summary>
        [BotRule("bot.monoco.04-mood")]
        public string Mood(BotView view, IReadOnlyList<string> moods, Func<string, double> addedScore)
        {
            var current = view.Board.Mood(view.Seat);
            if (moods == null || moods.Count == 0) return current;
            var best = current;
            var bestScore = moods.Contains(current) ? addedScore(current) : double.NegativeInfinity;
            foreach (var m in moods)
            {
                var s = addedScore(m);
                if (s > bestScore) { best = m; bestScore = s; }
            }
            return best;
        }

        /// <summary>The shortest path from the Warlord to <paramref name="target"/> (ties toward the lower clearing).</summary>
        public static List<ClearingId> PathTo(IBoardView b, ClearingId from, ClearingId target)
        {
            var d = Distances(b, target);
            var path = new List<ClearingId>();
            if (!d.ContainsKey(from)) return path;
            var c = from;
            path.Add(c);
            while (c != target)
            {
                c = b.PathNeighbours(c).Where(n => d.ContainsKey(n) && d[n] == d[c] - 1).OrderBy(n => n.Value).First();
                path.Add(c);
            }
            return path;
        }

        /// <summary>bot.monoco.05-arenas: clearings on the Warlord's planned path that he rules with a free slot, nearest first.</summary>
        [BotRule("bot.monoco.05-arenas")]
        public List<Ranked<ClearingId>> Arenas(BotView view, ClearingId target)
        {
            var w = view.Board.PawnClearing(view.Seat);
            if (!w.HasValue) return new List<Ranked<ClearingId>>();
            var path = PathTo(view.Board, w.Value, target);
            var ok = path.Where(c => view.Board.Ruler(c) == view.Seat && view.Board.FreeSlots(c) > 0).ToList();
            return Rank.Clearings(ok, c => -path.IndexOf(c));
        }

        /// <summary>bot.monoco.06-escort: Gestrals still needed with the Warlord (0 before tune.monoco-bot-escort-after-feet feet).</summary>
        [BotRule("bot.monoco.06-escort")]
        public int Escort(BotView view)
        {
            var w = view.Board.PawnClearing(view.Seat);
            if (!w.HasValue || MonocoFeetRules.Earned(view.State.MonocoFeet) < Tuning.MonocoBotEscortAfterFeet) return 0;
            return Math.Max(0, Tuning.MonocoBotEscort - view.Board.Warriors(view.Seat, w.Value));
        }

        /// <summary>Whether win.monoco's announce (gates included) passes on the board now.</summary>
        public static bool AnnounceTurn(BotView view)
        {
            var win = view.WinOf(view.Seat);
            if (win == null || view.Act < win.ActMin || view.Board.Vp(view.Seat) < win.VpMin) return false;
            if (win.OffWithDominance && view.Board.ActiveDominance(view.Seat).HasValue) return false;
            var feet = view.State.MonocoFeet;
            return feet.All(f => f.Earned) && MonocoFeetRules.DistinctFactions(feet) >= MonocoWin.FactionsNeeded(view.Board, view.Seat);
        }

        /// <summary>bot.monoco.07-announce: on the announce turn, enemy-free clearings by his warriors + buildings (most first),
        /// ties fewest enemy warriors next to it, then lower clearing. Empty when it is not the announce turn.</summary>
        [BotRule("bot.monoco.07-announce")]
        public List<Ranked<ClearingId>> Announce(BotView view)
        {
            if (!AnnounceTurn(view)) return new List<Ranked<ClearingId>>();
            var b = view.Board;
            var enemies = b.Seats().Where(s => IsEnemy(view, s)).ToList();
            var free = b.Clearings().Where(c => enemies.All(s => Pieces(b, s, c) == 0 && b.PawnClearing(s) != c));
            return free.Select(c => new { c, s = b.Warriors(view.Seat, c) + b.Buildings(view.Seat, c),
                    n = b.PathNeighbours(c).Sum(x => enemies.Sum(s => b.Warriors(s, x))) })
                .OrderByDescending(x => x.s).ThenBy(x => x.n).ThenBy(x => x.c.Value)
                .Select(x => new Ranked<ClearingId>(x.c, x.s)).ToList();
        }

        /// <summary>bot.monoco.08-spoiler: the cheapest answer to the top threat (shared <see cref="SpoilerLayer"/>): Warlord
        /// battles that break a met part (the threat's pieces in a key clearing) before Command moves that block one; then
        /// cost (actions this turn + expected hits taken); then step 1's score; then nearest, then lower clearing. Null with no trigger.</summary>
        [BotRule("bot.monoco.08-spoiler")]
        public MonocoAnswer Spoiler(BotView view)
        {
            var threat = SpoilerLayer.Top(view);
            var w = view.Board.PawnClearing(view.Seat);
            if (threat == null || !w.HasValue) return null;
            var b = view.Board;
            var pre = SpoilerLayer.PreEmpts(view, threat, AnnounceTurn(view));
            var dist = Distances(b, w.Value);
            var reach = Reach(view);
            var answers = new List<(MonocoAnswer a, double score, int near)>();
            foreach (var k in threat.KeyClearings)
            {
                if (!dist.ContainsKey(k)) continue;
                if (Pieces(b, threat.Seat, k) > 0 || b.PawnClearing(threat.Seat) == k)
                {
                    if (!reach.Contains(k)) continue;
                    var bt = new MonocoBattle(k, threat.Seat);
                    var cost = Math.Max(1, dist[k]) + ExpectedHitsTaken(b.Warriors(threat.Seat, k));
                    answers.Add((new MonocoAnswer(true, k, threat.Seat, null, cost, pre), Score(view, bt), dist[k]));
                }
                else
                {
                    var from = b.Clearings().Where(c => b.Warriors(view.Seat, c) > 0 && c != k)
                        .Select(c => new { c, d = Distances(b, c).TryGetValue(k, out var x) ? x : int.MaxValue })
                        .Where(x => x.d != int.MaxValue).OrderBy(x => x.d).ThenBy(x => x.c.Value).FirstOrDefault();
                    if (from == null) continue;
                    answers.Add((new MonocoAnswer(false, k, null, from.c, from.d, pre), 0, dist[k]));
                }
            }
            return answers.OrderByDescending(x => x.a.BreaksMetPart).ThenBy(x => x.a.Cost).ThenByDescending(x => x.score)
                .ThenBy(x => x.near).ThenBy(x => x.a.Clearing.Value).Select(x => x.a).FirstOrDefault();
        }
    }
}
