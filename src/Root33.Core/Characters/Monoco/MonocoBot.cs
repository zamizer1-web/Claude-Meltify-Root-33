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

        /// <summary>goalModeTrigger: feet-driven from his first Daylight on every difficulty (escorting starts at
        /// tune.monoco-bot-escort-after-feet feet, step 6).</summary>
        public bool InGoalMode(BotView view) => true;

        /// <summary>bot.monoco keeps no plan state beyond state.monoco.feet (no state.json row holds a planned battle), so the
        /// planned battle is recomputed from the board at each decision.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        /// <summary>How much a mood card adds to the planned battle's step 1 score (step 4). Root's mood effects are in no sheet
        /// (reskins.json keeps Root's own effects and pairs the cards on the creator's PC), so until the plugin supplies them
        /// step 4 claims nothing and Root's own AI picks the mood.</summary>
        public Func<BotView, MonocoBattle, string, double> MoodEffect { get; set; }

        /// <summary>The spoiler layer first when it pre-empts (a banner up, or on Hard a one-step threat off his announce turn);
        /// then steps 1-7 in order, the first that claims the decision takes it; then the spoiler layer with the actions the
        /// steps leave free; otherwise null (Root's own AI).</summary>
        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var acts = MonocoAction.ParseAll(legalActions);
            var spoiler = Spoiler(view);
            if (spoiler != null && spoiler.Preempts)
            {
                var k = SpoilerKey(view, spoiler, acts);
                if (k != null) return k;
            }
            return StepAction(view, acts) ?? (spoiler != null ? SpoilerKey(view, spoiler, acts) : null);
        }

        /// <summary>Steps 1-7 in priority order over the parsed legal actions.</summary>
        public string StepAction(BotView view, IReadOnlyList<MonocoAction> acts)
        {
            var b = view.Board;
            var w = b.PawnClearing(view.Seat);
            var announce = AnnounceTurn(view);
            var planned = announce ? null : Planned(view);

            // 1 + 2: fight the best legal Warlord battle (targets first, then step 1's ranking), else Advance toward the plan.
            if (!announce)
            {
                foreach (var bt in Candidates(view))
                {
                    var k = acts.FirstOrDefault(a => a.Verb == MonocoAction.Battle && a.Clearing == bt.Clearing
                        && (!a.Target.HasValue || a.Target == bt.Defender));
                    if (k != null) return k.Key;
                }
                if (planned != null && w.HasValue && planned.Clearing != w.Value)
                {
                    var k = Toward(view, acts.Where(a => a.IsAdvance), w.Value, planned.Clearing);
                    if (k != null) return k;
                }
            }

            // 3: Command-move rats into or next to the target, nearest first (Easy skips this step).
            if (planned != null)
            {
                var d = Distances(b, planned.Clearing);
                foreach (var src in BringRats(view, planned.Clearing))
                {
                    var k = acts.Where(a => a.IsCommandMove && a.From == src.Item && a.To.HasValue && d.ContainsKey(a.To.Value)
                            && d[a.To.Value] <= 1 && d[a.To.Value] < d[src.Item])
                        .OrderBy(a => d[a.To!.Value]).ThenBy(a => a.To!.Value.Value).FirstOrDefault();
                    if (k != null) return k.Key;
                }
            }

            // 4: the mood that boosts the planned battle (only when the mood effects are known).
            if (planned != null && MoodEffect != null)
            {
                var moods = acts.Where(a => a.Verb == MonocoAction.Mood && a.Arg != null).ToList();
                if (moods.Count > 0)
                {
                    var pick = Mood(view, moods.Select(a => a.Arg!).ToList(), m => MoodEffect(view, planned, m));
                    var k = moods.FirstOrDefault(a => a.Arg == pick);
                    if (k != null) return k.Key;
                }
            }

            // 5: Arenas along the route to the target.
            if (planned != null)
                foreach (var c in Arenas(view, planned.Clearing))
                {
                    var k = acts.FirstOrDefault(a => a.BuildsArena && a.Clearing == c.Item);
                    if (k != null) return k.Key;
                }

            // 6: escort the Warlord after tune.monoco-bot-escort-after-feet feet.
            if (w.HasValue && Escort(view) > 0)
            {
                var d = Distances(b, w.Value);
                var k = acts.Where(a => a.IsCommandMove && a.To == w.Value && a.From.HasValue && a.From != w.Value && d.ContainsKey(a.From.Value))
                    .OrderBy(a => d[a.From!.Value]).ThenBy(a => a.From!.Value.Value).FirstOrDefault();
                if (k != null) return k.Key;
            }

            // 7: on the announce turn, end in his strongest enemy-free clearing.
            if (announce && w.HasValue)
            {
                var top = Announce(view).Select(r => (ClearingId?)r.Item).FirstOrDefault();
                if (top.HasValue && top.Value != w.Value)
                {
                    var k = Toward(view, acts.Where(a => a.IsAdvance), w.Value, top.Value);
                    if (k != null) return k;
                }
            }
            return null;
        }

        /// <summary>The planned battle: step 2's top target, else step 1's top battle (Easy: only one where the Warlord's stack
        /// outnumbers the defenders by tune.monoco-easy-outnumber). Null when he can reach none.</summary>
        public MonocoBattle Planned(BotView view) => Candidates(view).FirstOrDefault();

        /// <summary>Battles in plan order: step 2's targets (distinct battles), then the rest of step 1's ranking; Easy keeps
        /// only battles where his stack outnumbers the defending warriors by tune.monoco-easy-outnumber.</summary>
        public List<MonocoBattle> Candidates(BotView view)
        {
            var list = new List<MonocoBattle>();
            void Add(MonocoBattle bt)
            {
                if (list.Any(x => x.Clearing == bt.Clearing && x.Defender == bt.Defender)) return;
                if (view.Difficulty == Difficulty.Easy && !Outnumbers(view, bt)) return;
                list.Add(bt);
            }
            foreach (var t in Targets(view)) Add(t.Item.Battle);
            foreach (var r in BattleScore(view)) Add(r.Item);
            return list;
        }

        /// <summary>Easy: his stack minus the defending warriors is at least tune.monoco-easy-outnumber.</summary>
        public static bool Outnumbers(BotView view, MonocoBattle bt) =>
            Stack(view, bt.Clearing) - view.Board.Warriors(bt.Defender, bt.Clearing) >= Tuning.MonocoEasyOutnumber;

        /// <summary>A legal move from <paramref name="from"/> one path step closer to <paramref name="target"/>.</summary>
        private static string Toward(BotView view, IEnumerable<MonocoAction> moves, ClearingId from, ClearingId target)
        {
            var d = Distances(view.Board, target);
            if (!d.TryGetValue(from, out var here)) return null;
            return moves.Where(a => a.To.HasValue && (!a.From.HasValue || a.From == from) && d.ContainsKey(a.To.Value) && d[a.To.Value] < here)
                .OrderBy(a => d[a.To!.Value]).ThenBy(a => a.To!.Value.Value).Select(a => a.Key).FirstOrDefault();
        }

        /// <summary>The legal key for a spoiler answer: the battle (or an Advance toward it), or the Command move that blocks.</summary>
        private static string SpoilerKey(BotView view, MonocoAnswer a, IReadOnlyList<MonocoAction> acts)
        {
            if (a.BreaksMetPart)
            {
                var battle = acts.FirstOrDefault(x => x.Verb == MonocoAction.Battle && x.Clearing == a.Clearing
                    && (!x.Target.HasValue || x.Target == a.Defender));
                if (battle != null) return battle.Key;
                var w = view.Board.PawnClearing(view.Seat);
                return w.HasValue ? Toward(view, acts.Where(x => x.IsAdvance), w.Value, a.Clearing) : null;
            }
            return acts.Where(x => x.IsCommandMove && x.To == a.Clearing && x.From.HasValue)
                .OrderBy(x => x.From == a.From ? 0 : 1).ThenBy(x => x.From!.Value.Value).Select(x => x.Key).FirstOrDefault();
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

        /// <summary>The most hits his stack can deal: the higher die (at most 3) capped by his stack, +1 against a defender
        /// with no warriors.</summary>
        public static int MaxHits(int stack, int defenders) => Math.Min(DieFaces - 1, stack) + (defenders <= 0 ? 1 : 0);

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
            if (!factions.Contains(bt.Defender) && factions.Count < MonocoWin.FactionsNeeded(view.Board, view.Seat)) score += Tuning.MonocoBotNewFaction;
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

        /// <summary>bot.monoco.02-targets: targets for the missing feet: Heavy / Caster an enemy building / token guarded by
        /// tune.monoco-bot-target-guards or fewer warriors; Agile a lone enemy warrior one Advance away; Balanced a fight where the
        /// defenders can hit back and he can still win; Almighty (from tune.monoco-almighty-act) a VP leader's building or a
        /// leader stack of tune.monoco-almighty-pieces+ pieces his hits can wipe out. Order: step 1's score (with its heavy
        /// penalty), then the fewest defending warriors (Balanced: the best win chance), then the lower clearing. Easy only
        /// where the Warlord outnumbers the defenders by tune.monoco-easy-outnumber.</summary>
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
                if (view.Difficulty == Difficulty.Easy && !Outnumbers(view, bt)) continue;
                foreach (var foot in FeetFilled(view, bt))
                {
                    var ok = foot switch
                    {
                        Foot.Heavy => guards <= Tuning.MonocoBotTargetGuards,
                        Foot.Caster => guards <= Tuning.MonocoBotTargetGuards,
                        Foot.Agile => guards == 1 && adj.Contains(c),
                        Foot.Balanced => guards >= 1 && WinChance(stack, guards) > 0,
                        Foot.Almighty => b.Buildings(d, c) > 0 || MaxHits(stack, guards) >= Pieces(b, d, c),
                        _ => false,
                    };
                    if (ok) list.Add(new MonocoTarget(foot, bt));
                }
            }
            return list.Select(t => new { t, risk = CouldWipeStack(view, t.Battle), s = Score(view, t.Battle),
                    g = b.Warriors(t.Battle.Defender, t.Battle.Clearing),
                    wc = WinChance(Stack(view, t.Battle.Clearing), b.Warriors(t.Battle.Defender, t.Battle.Clearing)) })
                .OrderBy(x => x.risk).ThenByDescending(x => x.s).ThenBy(x => x.t.Foot == Foot.Balanced ? -x.wc : x.g)
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
        /// cost (actions this turn + expected hits taken); then step 1's score; then nearest, then lower clearing. Null with no
        /// trigger. It obeys the steps' limits: on Easy the Warlord fights only where he outnumbers the defenders by
        /// tune.monoco-easy-outnumber. Whether it pre-empts the steps is <see cref="MonocoAnswer.Preempts"/>.</summary>
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
                    // LIMITS: Easy's Warlord fights only where he outnumbers the defenders (step 2's 'only').
                    if (view.Difficulty == Difficulty.Easy && !Outnumbers(view, bt)) continue;
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
