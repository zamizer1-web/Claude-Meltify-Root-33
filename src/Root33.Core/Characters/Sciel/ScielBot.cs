using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>A move the bot wants (Evening Operations): her warriors from one clearing to the next.</summary>
    public sealed class ScielMove
    {
        public ClearingId From;
        public ClearingId To;
        public override string ToString() => From + "->" + To;
    }

    /// <summary>A seat the spoiler layer answers: its banner is up, or it is one step from its themed win.</summary>
    public sealed class SpoilerThreat
    {
        public SeatId Seat;
        public bool Banner;
        public IReadOnlyList<ClearingId> KeyClearings = new ClearingId[0];
        public override string ToString() => Seat + (Banner ? " banner" : " one-step");
    }

    /// <summary>The spoiler layer's pick: the action key, the threat it answers and whether it pre-empts the plan's steps.</summary>
    public sealed class SpoilerAnswer
    {
        public SpoilerThreat Threat;
        public string ActionKey;
        public bool Preempts;
        public double Cost;
        public override string ToString() => ActionKey + (Preempts ? " (pre-empts)" : " (free)");
    }

    /// <summary>
    /// bot.sciel: Root's Alliance actions (Revolt, Spread, Mobilize, Operations) from read.legal-actions, re-weighted by the
    /// plan's steps in priority order (bot.sciel.01..09). Every step is one [BotRule] method that reads only the
    /// <see cref="BotView"/> (public board, her own supporters) and returns a ranking or a decision. Root's own AI keeps hit
    /// allocation, Ambush and discards and plays any decision no step claims (<see cref="ChooseAction"/> returns null).
    /// </summary>
    public sealed class ScielBot : IBotDirector
    {
        /// <summary>Root's own Martial Law (base game Woodland Alliance): another faction with this many warriors in a clearing
        /// makes Spread there cost one more supporter. A Root rule, not a mod tuning, so no sheet holds it.</summary>
        public const int MartialLawWarriors = 3;

        private readonly Random _rng = new Random(33);

        public ScielBot()
        {
            EasyCoin = _ => _rng.Next(2) == 0;
        }

        public string PlanId => Ids.BotPlans.Sciel;

        /// <summary>The bot's seeded random source for Easy's careless Revolt: true = take the first legal Revolt.</summary>
        public Func<BotView, bool> EasyCoin { get; set; }

        /// <summary>Set by <see cref="ScielModule"/>: the public units a seat's themed test still misses (gates excluded), or
        /// null when its win does not report progress (<see cref="IGoalProgress"/>).</summary>
        public Func<SeatId, int?> UnitsMissingOf { get; set; }

        /// <summary>Set by <see cref="ScielModule"/>: a seat's key clearings from its win (<see cref="IGoalProgress"/>), or
        /// null to use the counterplay table in <see cref="FallbackKeyClearings"/>.</summary>
        public Func<SeatId, IReadOnlyList<ClearingId>> KeyClearingsOf { get; set; }

        /// <summary>goalModeTrigger: finish mode at tune.sciel-finish-sanctuaries Sanctuaries (the same on every difficulty).</summary>
        public bool InGoalMode(BotView view) => Finish(view);

        /// <summary>The plan keeps nothing in ModState (no bot state rows): every step is read from the board each decision.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        // ================================================================= decision

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var spoiler = Spoiler(view, legalActions);
            if (spoiler != null && spoiler.Preempts) return spoiler.ActionKey;
            return StepAction(view, legalActions) ?? spoiler?.ActionKey;
        }

        /// <summary>Steps 1-8 in order; the first that claims the decision takes it.</summary>
        public string StepAction(BotView view, IReadOnlyList<string> legal)
        {
            var b = view.Board;
            var me = view.Seat;
            var acts = ScielAction.ParseAll(legal);
            var finish = Finish(view);
            var announce = IsAnnounceTurn(view);
            var cluster = BiggestCluster(view);

            // 1 Spread (never on the announce turn: she keeps supporters; in finish mode only a Spread that makes a Sanctuary).
            if (!announce)
                foreach (var r in Spread(view, legal))
                {
                    if (finish && ScielBoard.EnemyPieces(b, me, r.Item) != 0) continue;
                    if (!SpreadKeepsReserve(view, r.Item)) continue;
                    return Key(acts, ScielAction.Spread, r.Item);
                }

            // 2 Revolt
            if (!announce)
            {
                var c = RevoltChoice(view, legal, finish);
                if (c.HasValue) return Key(acts, ScielAction.Revolt, c.Value);
            }

            // 3 Evict
            foreach (var r in Evict(view, legal))
            {
                var k = BattleKey(acts, r.Item, null);
                if (k != null) return k;
            }

            // 4 Organize
            foreach (var r in Organize(view, legal)) return Key(acts, ScielAction.Organize, r.Item);

            // 5 Recruit at threatened Aquafarms
            foreach (var r in DefendAquafarms(view, legal)) return Key(acts, ScielAction.Recruit, r.Item);

            // 6 Teacher-led group (7: in finish mode never out of the cluster)
            var m = TeacherGroup(view, legal);
            if (m != null && !(finish && PullsOut(cluster, m.From, m.To)))
            {
                var k = MoveKey(acts, m.From, m.To);
                if (k != null) return k;
            }

            // 8 Announce turn: garrison the most exposed Sanctuaries, one warrior each before a second anywhere.
            foreach (var t in Announce(view))
            {
                var move = acts.Where(a => a.Verb == ScielAction.Move && a.To == t.Item && a.Clearing.HasValue && a.Clearing != t.Item)
                    .Where(a => !(finish && PullsOut(cluster, a.Clearing.Value, t.Item)))
                    .Where(a => b.Warriors(me, a.Clearing.Value) > (ScielBoard.IsSanctuary(b, me, a.Clearing.Value) ? 1 : 0))
                    .OrderBy(a => ScielBoard.IsSanctuary(b, me, a.Clearing.Value) ? 1 : 0)
                    .ThenByDescending(a => b.Warriors(me, a.Clearing.Value)).ThenBy(a => a.Clearing.Value.Value)
                    .FirstOrDefault();
                if (move != null) return move.Key;
            }
            return null;
        }

        // ================================================================= step 1: Spread

        [BotRule("bot.sciel.01-spread")]
        public List<Ranked<ClearingId>> Spread(BotView view, IReadOnlyList<string> legal = null)
        {
            var b = view.Board;
            var me = view.Seat;
            var sanctuarySuits = ScielBoard.SuitsCovered(b, ScielBoard.Sanctuaries(b, me));
            var candidates = Legal(view, legal).Where(a => a.Verb == ScielAction.Spread && a.Clearing.HasValue)
                .Select(a => a.Clearing.Value).Distinct()
                .Where(c => ScielBoard.EnemyPieces(b, me, c) < Tuning.ScielSpreadMaxEnemy && !MartialLaw(b, me, c));
            return Rank.Clearings(candidates, c => SpreadScore(b, me, c, sanctuarySuits));
        }

        /// <summary>spread(c) = tune.sciel-spread-base - enemy pieces in c - enemy warriors next to c
        /// + tune.sciel-spread-new-suit if c's suit has no Sanctuary yet.</summary>
        public static int SpreadScore(IBoardView b, SeatId me, ClearingId c, HashSet<Suit> sanctuarySuits = null)
        {
            sanctuarySuits = sanctuarySuits ?? ScielBoard.SuitsCovered(b, ScielBoard.Sanctuaries(b, me));
            var newSuit = b.SuitsOf(c).Any(s => s != Suit.Bird && !sanctuarySuits.Contains(s));
            return Tuning.ScielSpreadBase - ScielBoard.EnemyPieces(b, me, c) - ScielBoard.EnemyWarriorsAdjacent(b, me, c)
                   + (newSuit ? Tuning.ScielSpreadNewSuit : 0);
        }

        /// <summary>Martial Law: one other faction has <see cref="MartialLawWarriors"/>+ warriors in the clearing.</summary>
        public static bool MartialLaw(IBoardView b, SeatId me, ClearingId c) =>
            ScielBoard.Others(b, me).Any(o => b.Warriors(o, c) >= MartialLawWarriors);

        /// <summary>Whether a Spread into <paramref name="c"/> leaves tune.sciel-revolt-supporters supporters matching the next
        /// Revolt target's suit (birds are wild; she pays with the clearing's own suit first). A Spread that does not touch
        /// that pool is always allowed.</summary>
        public bool SpreadKeepsReserve(BotView view, ClearingId c)
        {
            var reserve = RevoltReserve(view);
            if (!reserve.HasValue) return true;
            var r = reserve.Value;
            var sup = view.Board.Supporters(view.Seat);
            var t = view.Board.SuitsOf(c).FirstOrDefault(s => s != Suit.Bird);
            const int cost = 1; // Martial Law clearings are never spread into
            var birds = sup.Count(s => s == Suit.Bird);
            var before = sup.Count(s => s == r) + birds;
            int after;
            if (t == r) after = before - cost;
            else after = before - Math.Min(birds, Math.Max(0, cost - sup.Count(s => s == t)));
            return after == before || after >= Tuning.ScielRevoltSupporters;
        }

        // ================================================================= step 2: Revolt

        /// <summary>Legal Revolts in her Lantern clearings holding enemy buildings or tokens, in a suit with no Aquafarm:
        /// clearings that become a Sanctuary at once (no Vagabond pawn left) first, then by enemy buildings + tokens removed;
        /// ties: a suit with no Sanctuary yet, then the lower clearing.</summary>
        [BotRule("bot.sciel.02-revolt")]
        public List<Ranked<ClearingId>> Revolt(BotView view, IReadOnlyList<string> legal = null)
        {
            var legalRevolts = new HashSet<ClearingId>(Legal(view, legal).Where(a => a.Verb == ScielAction.Revolt && a.Clearing.HasValue)
                .Select(a => a.Clearing.Value));
            return RevoltTargets(view).Where(r => legalRevolts.Contains(r.Item)).ToList();
        }

        /// <summary>Every Revolt target on the board, legal now or not (the reserve looks at the next one).</summary>
        public List<Ranked<ClearingId>> RevoltTargets(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var sanctuarySuits = ScielBoard.SuitsCovered(b, ScielBoard.Sanctuaries(b, me));
            var targets = b.Clearings().Where(c => ScielBoard.HasLantern(b, me, c) && ScielBoard.EnemyBuildingsAndTokens(b, me, c) > 0
                                                   && b.SuitsOf(c).Any(s => s != Suit.Bird && !ScielBoard.HasAquafarmInSuit(b, me, s)));
            return Rank.Clearings(targets, c =>
                (ScielBoard.EnemyPawnIn(b, me, c) ? 0 : 1000)
                + 10 * ScielBoard.EnemyBuildingsAndTokens(b, me, c)
                + (b.SuitsOf(c).Any(s => s != Suit.Bird && !sanctuarySuits.Contains(s)) ? 1 : 0));
        }

        /// <summary>The suit whose tune.sciel-revolt-supporters supporters she keeps ready: the best Revolt target's suit.</summary>
        public Suit? RevoltReserve(BotView view)
        {
            var targets = RevoltTargets(view);
            if (targets.Count == 0) return null;
            return view.Board.SuitsOf(targets[0].Item).FirstOrDefault(s => s != Suit.Bird);
        }

        /// <summary>The Revolt she takes: the best-scored one, or on Easy, half the time (<see cref="EasyCoin"/>), the first
        /// legal Revolt. In finish mode only a Revolt that makes her next Sanctuary.</summary>
        public ClearingId? RevoltChoice(BotView view, IReadOnlyList<string> legal = null, bool finishMode = false)
        {
            var b = view.Board;
            var me = view.Seat;
            bool Allowed(ClearingId c) => !finishMode || (!ScielBoard.EnemyPawnIn(b, me, c) && !ScielBoard.IsSanctuary(b, me, c));
            if (view.Difficulty == Difficulty.Easy)
            {
                var first = Legal(view, legal).FirstOrDefault(a => a.Verb == ScielAction.Revolt && a.Clearing.HasValue);
                if (first != null && EasyCoin(view) && Allowed(first.Clearing.Value)) return first.Clearing;
            }
            foreach (var r in Revolt(view, legal))
                if (Allowed(r.Item)) return r.Item;
            return null;
        }

        // ================================================================= step 3: Evict

        /// <summary>Evening only: her Lantern clearings (with her warriors) holding exactly one enemy warrior, or enemy tokens
        /// and no enemy warriors, where a battle is legal. Clearings that become a Sanctuary after the battle first, then
        /// fewest enemy pieces. Easy needs tune.sciel-easy-evict-ratio of her warriors per enemy piece there. She prefers a
        /// Twilight Evening: while she holds both charges but Twilight is not armed this Evening (her next Evening will be),
        /// she waits.</summary>
        [BotRule("bot.sciel.03-evict")]
        public List<Ranked<ClearingId>> Evict(BotView view, IReadOnlyList<string> legal = null)
        {
            var b = view.Board;
            var me = view.Seat;
            if (b.Phase() != TurnPhase.Evening) return new List<Ranked<ClearingId>>();
            var st = view.State;
            if (!st.ScielTwilightArmed && st.ScielSun && st.ScielMoon) return new List<Ranked<ClearingId>>();
            var battles = new HashSet<ClearingId>(Legal(view, legal).Where(a => a.Verb == ScielAction.Battle && a.Clearing.HasValue)
                .Select(a => a.Clearing.Value));
            var targets = battles.Where(c => IsEvictTarget(b, me, c));
            if (view.Difficulty == Difficulty.Easy)
                targets = targets.Where(c => b.Warriors(me, c) >= Tuning.ScielEasyEvictRatio * ScielBoard.EnemyPieces(b, me, c));
            return Rank.Clearings(targets, c =>
            {
                var pieces = ScielBoard.EnemyPieces(b, me, c);
                var removable = ScielBoard.EnemyWarriors(b, me, c) == 1 ? 1 : ScielBoard.EnemyTokens(b, me, c);
                return (pieces == removable ? 1000 : 0) - pieces;
            });
        }

        public static bool IsEvictTarget(IBoardView b, SeatId me, ClearingId c)
        {
            if (!ScielBoard.HasLantern(b, me, c) || b.Warriors(me, c) < 1) return false;
            var w = ScielBoard.EnemyWarriors(b, me, c);
            return w == 1 || (w == 0 && ScielBoard.EnemyTokens(b, me, c) > 0);
        }

        // ================================================================= step 4: Organize

        /// <summary>While the Sun gauge (Sanctuaries) is short of tune.sciel-sanctuaries and a Lantern is in supply: legal
        /// Organizes in clearings without her Lantern that hold an idle warrior (not in an eviction target, not her
        /// Teacher-led group), ordered by step 1's spread score.</summary>
        [BotRule("bot.sciel.04-organize")]
        public List<Ranked<ClearingId>> Organize(BotView view, IReadOnlyList<string> legal = null)
        {
            var b = view.Board;
            var me = view.Seat;
            if (ScielBoard.Sanctuaries(b, me).Count >= Tuning.ScielSanctuaries || b.SupplyPieces(me, PieceTypes.Sympathy) <= 0)
                return new List<Ranked<ClearingId>>();
            var busy = new HashSet<ClearingId>(Evict(view, legal).Select(r => r.Item));
            var group = b.Officers(me) > 0 ? GroupClearing(view) : null;
            if (group.HasValue) busy.Add(group.Value);
            var candidates = Legal(view, legal).Where(a => a.Verb == ScielAction.Organize && a.Clearing.HasValue).Select(a => a.Clearing.Value)
                .Distinct().Where(c => !ScielBoard.HasLantern(b, me, c) && b.Warriors(me, c) >= 1 && !busy.Contains(c));
            return Rank.Clearings(candidates, c => SpreadScore(b, me, c));
        }

        // ================================================================= step 5: Recruit at threatened Aquafarms

        /// <summary>Legal Recruits at her Aquafarms with enemy warriors in or next to their clearing, most threatened first.</summary>
        [BotRule("bot.sciel.05-defend-aquafarms")]
        public List<Ranked<ClearingId>> DefendAquafarms(BotView view, IReadOnlyList<string> legal = null)
        {
            var b = view.Board;
            var me = view.Seat;
            int Threat(ClearingId c) => ScielBoard.EnemyWarriors(b, me, c) + ScielBoard.EnemyWarriorsAdjacent(b, me, c);
            var candidates = Legal(view, legal).Where(a => a.Verb == ScielAction.Recruit && a.Clearing.HasValue).Select(a => a.Clearing.Value)
                .Distinct().Where(c => b.BuildingsOfType(me, c, PieceTypes.Base) > 0 && Threat(c) > 0);
            return Rank.Clearings(candidates, c => Threat(c));
        }

        // ================================================================= step 6: Teacher-led group

        /// <summary>Her biggest warrior stack (moved by her officers' Operations) stays in or beside her biggest Lantern
        /// cluster; when it is elsewhere, the next step along the shortest path toward it. Null when no move is needed.</summary>
        [BotRule("bot.sciel.06-teacher-group")]
        public ScielMove TeacherGroup(BotView view, IReadOnlyList<string> legal = null)
        {
            var b = view.Board;
            if (b.Officers(view.Seat) <= 0) return null;
            var cluster = BiggestCluster(view);
            var group = GroupClearing(view);
            if (cluster.Count == 0 || !group.HasValue) return null;
            bool Beside(ClearingId c) => cluster.Contains(c) || b.PathNeighbours(c).Any(cluster.Contains);
            if (Beside(group.Value)) return null;
            var path = Graph.CheapestPath(group.Value, Beside, b.PathNeighbours, _ => 1, out _);
            return path == null || path.Count < 2 ? null : new ScielMove { From = group.Value, To = path[1] };
        }

        /// <summary>Her biggest warrior stack (ties: lower clearing), or null with no warriors on the map.</summary>
        public static ClearingId? GroupClearing(BotView view)
        {
            var b = view.Board;
            var best = b.Clearings().Where(c => b.Warriors(view.Seat, c) > 0)
                .OrderByDescending(c => b.Warriors(view.Seat, c)).ThenBy(c => c.Value).ToList();
            return best.Count > 0 ? best[0] : (ClearingId?)null;
        }

        /// <summary>Her Lantern clearings joined by paths, biggest first (most clearings, then most Sanctuaries, then lowest clearing).</summary>
        public static List<HashSet<ClearingId>> LanternClusters(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var seen = new HashSet<ClearingId>();
            var clusters = new List<HashSet<ClearingId>>();
            foreach (var c in ScielBoard.Lanterns(b, me))
            {
                if (seen.Contains(c)) continue;
                var set = Graph.ConnectedWithin(c, b.PathNeighbours, x => ScielBoard.HasLantern(b, me, x));
                seen.UnionWith(set);
                clusters.Add(set);
            }
            return clusters.OrderByDescending(s => s.Count).ThenByDescending(s => s.Count(c => ScielBoard.IsSanctuary(b, me, c)))
                .ThenBy(s => s.Min(c => c.Value)).ToList();
        }

        public static HashSet<ClearingId> BiggestCluster(BotView view) =>
            LanternClusters(view).FirstOrDefault() ?? new HashSet<ClearingId>();

        private static bool PullsOut(HashSet<ClearingId> cluster, ClearingId from, ClearingId to) => cluster.Contains(from) && !cluster.Contains(to);

        // ================================================================= step 7: Finish mode

        /// <summary>Finish mode: tune.sciel-finish-sanctuaries Sanctuaries or more. In it she spends supporters only on the
        /// Spread or Revolt that makes her next Sanctuary and never moves warriors out of her biggest Lantern cluster (applied
        /// by <see cref="StepAction"/> and <see cref="Spoiler"/>).</summary>
        [BotRule("bot.sciel.07-finish")]
        public bool Finish(BotView view) => ScielBoard.Sanctuaries(view.Board, view.Seat).Count >= Tuning.ScielFinishSanctuaries;

        // ================================================================= step 8: Announce turn

        /// <summary>win.sciel's announce test (gates included) passes on the board now, so it will at the end of this turn.</summary>
        public static bool IsAnnounceTurn(BotView view)
        {
            var row = Data.WinConditions[Ids.WinConditions.Sciel];
            var b = view.Board;
            if (view.Act < row.ActMin) return false;
            if (row.OffWithDominance && b.ActiveDominance(view.Seat).HasValue) return false;
            if (row.OffInCoalition && b.CoalitionPartner(view.Seat).HasValue) return false;
            if (row.VpMin > 0 && b.Vp(view.Seat) < row.VpMin) return false;
            return ScielBoard.UnitsMissing(b, view.Seat) == 0;
        }

        /// <summary>On the announce turn: her Sanctuaries to garrison, one warrior each before a second anywhere, then by
        /// exposure (enemy warriors next to it), ties to the lower clearing. Empty on any other turn. She also keeps her
        /// supporters (no Spread or Revolt) for Outrage income.</summary>
        [BotRule("bot.sciel.08-announce")]
        public List<Ranked<ClearingId>> Announce(BotView view)
        {
            if (!IsAnnounceTurn(view)) return new List<Ranked<ClearingId>>();
            var b = view.Board;
            var me = view.Seat;
            return Rank.Clearings(ScielBoard.Sanctuaries(b, me),
                c => -1000.0 * b.Warriors(me, c) + ScielBoard.EnemyWarriorsAdjacent(b, me, c));
        }

        // ================================================================= step 9: Spoiler layer

        /// <summary>
        /// The shared spoiler layer. Threats: other seats (not her coalition partner) whose themed win is unlocked and not
        /// switched off, with a banner up or one step away (one public unit missing; the VP gate counts as one); Easy looks at
        /// banners only. Order: banners first, then the seat whose next Birdsong comes first, then the higher VP. Her answers
        /// are a Revolt in a key clearing holding her Lantern, or Evening Operations battles and moves into a key clearing;
        /// the cheapest legal one (actions + expected hits taken), ties nearest her pieces then the lower clearing. It
        /// pre-empts the steps for a banner (not on Easy) and, on Hard, for a one-step threat unless this is her own announce
        /// turn; otherwise it only takes a decision the steps leave free. In finish mode it never pulls warriors out of the
        /// cluster and revolts only where that makes a Sanctuary.
        /// </summary>
        [BotRule("bot.sciel.09-spoiler")]
        public SpoilerAnswer Spoiler(BotView view, IReadOnlyList<string> legal = null)
        {
            var acts = Legal(view, legal);
            if (acts.Count == 0) return null;
            var finish = Finish(view);
            var cluster = BiggestCluster(view);
            foreach (var threat in Threats(view))
            {
                var answer = Answer(view, threat, acts, finish, cluster);
                if (answer == null) continue;
                answer.Preempts = threat.Banner
                    ? view.Difficulty != Difficulty.Easy
                    : view.Difficulty == Difficulty.Hard && !IsAnnounceTurn(view);
                return answer;
            }
            return null;
        }

        /// <summary>The seats the spoiler layer answers, in the row's order.</summary>
        public List<SpoilerThreat> Threats(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var seats = b.Seats();
            var list = new List<(SpoilerThreat t, int dist, int vp)>();
            for (var i = 0; i < seats.Count; i++)
            {
                var o = seats[i];
                if (o == me || b.CoalitionPartner(o) == me || b.CoalitionPartner(me) == o) continue;
                var ch = view.CharacterOf(o);
                if (ch == null || !Data.Characters.TryGetValue(ch, out var chRow)) continue;
                if (!Data.WinConditions.TryGetValue(chRow.WinCondition ?? "", out var row)) continue;
                if (view.Act < row.ActMin) continue;
                var dominance = b.ActiveDominance(o).HasValue;
                if (row.OffWithDominance && dominance) continue;
                if (row.OffInCoalition && b.CoalitionPartner(o).HasValue) continue;
                var banner = view.Countdowns.Contains(o);
                if (!banner)
                {
                    if (view.Difficulty == Difficulty.Easy) continue;
                    var units = UnitsMissingOf?.Invoke(o);
                    if (!units.HasValue) continue;
                    var total = units.Value + (row.VpMin > 0 && !dominance && b.Vp(o) < row.VpMin ? 1 : 0);
                    if (total != 1) continue;
                }
                var keys = KeyClearingsOf?.Invoke(o) ?? FallbackKeyClearings(view, o, ch);
                var cur = IndexOf(seats, b.CurrentSeat());
                var dist = cur < 0 ? i : (i - cur + seats.Count) % seats.Count;
                if (dist == 0) dist = seats.Count;
                list.Add((new SpoilerThreat { Seat = o, Banner = banner, KeyClearings = keys }, dist, dominance ? 0 : b.Vp(o)));
            }
            return list.OrderBy(x => x.t.Banner ? 0 : 1).ThenBy(x => x.dist).ThenByDescending(x => x.vp).Select(x => x.t).ToList();
        }

        private static int IndexOf(IReadOnlyList<SeatId> seats, SeatId s)
        {
            for (var i = 0; i < seats.Count; i++) if (seats[i] == s) return i;
            return -1;
        }

        /// <summary>Key clearings from each win's counterplay, read from public board and Goal Tracker state, for wins that do
        /// not report their own (<see cref="IGoalProgress"/>).</summary>
        public static IReadOnlyList<ClearingId> FallbackKeyClearings(BotView view, SeatId o, string character)
        {
            var b = view.Board;
            var keys = new List<ClearingId>();
            void Add(ClearingId? c) { if (c.HasValue && !keys.Contains(c.Value)) keys.Add(c.Value); }
            var monolith = view.Role(Ids.MapRoles.Monolith);
            var lumiere = view.Role(Ids.MapRoles.Lumiere);
            switch (character)
            {
                case CharacterIds.Gustave:
                    foreach (var c in view.State.GustaveFlags.OrderBy(c => c.Value)) Add(c);
                    break;
                case CharacterIds.Esquie:
                case CharacterIds.Monoco:
                    Add(b.PawnClearing(o));
                    break;
                case CharacterIds.Paintress:
                    foreach (var c in b.Clearings().Where(c => b.BuildingsOfType(o, c, PieceTypes.Roost) > 0
                                                                && b.Warriors(o, c) >= Tuning.PaintressPortraitWarriors)) Add(c);
                    Add(monolith);
                    break;
                case CharacterIds.Verso:
                    Add(monolith);
                    break;
                case CharacterIds.Maelle:
                    Add(lumiere);
                    break;
                case CharacterIds.Curator:
                    Add(lumiere);
                    Add(monolith);
                    break;
                case CharacterIds.Lune:
                    if (view.State.LuneTaggedRelic.HasValue) Add(b.RelicClearing(view.State.LuneTaggedRelic.Value));
                    break;
                case CharacterIds.Clea:
                    foreach (var c in view.State.CleaCovered.OrderBy(c => b.Warriors(o, c)).ThenBy(c => c.Value)) Add(c);
                    Add(monolith);
                    Add(view.Role(Ids.MapRoles.FlyingManor));
                    break;
                case CharacterIds.WhiteHairedMan:
                    foreach (var c in view.MonolithNeighbours()) Add(c);
                    Add(monolith);
                    break;
            }
            return keys;
        }

        private static SpoilerAnswer Answer(BotView view, SpoilerThreat threat, List<ScielAction> acts, bool finish, HashSet<ClearingId> cluster)
        {
            var b = view.Board;
            var me = view.Seat;
            var keys = new HashSet<ClearingId>(threat.KeyClearings);
            var options = new List<(ScielAction a, double cost, int near, int c)>();
            foreach (var a in acts)
            {
                if (!a.Clearing.HasValue) continue;
                var c = a.Clearing.Value;
                if (a.Verb == ScielAction.Revolt && keys.Contains(c) && ScielBoard.HasLantern(b, me, c))
                {
                    if (finish && (ScielBoard.EnemyPawnIn(b, me, c) || ScielBoard.IsSanctuary(b, me, c))) continue;
                    options.Add((a, 1, 0, c.Value));
                }
                else if (a.Verb == ScielAction.Battle && keys.Contains(c) && (!a.Defender.HasValue || a.Defender == threat.Seat))
                    options.Add((a, 1 + ExpectedHitsTaken(b.Warriors(threat.Seat, c)), 0, c.Value));
                else if (a.Verb == ScielAction.Move && a.To.HasValue && keys.Contains(a.To.Value))
                {
                    if (finish && PullsOut(cluster, c, a.To.Value)) continue;
                    options.Add((a, 2 + ExpectedHitsTaken(b.Warriors(threat.Seat, a.To.Value)), 1, a.To.Value.Value));
                }
            }
            if (options.Count == 0) return null;
            var best = options.OrderBy(o => o.cost).ThenBy(o => o.near).ThenBy(o => o.c).First();
            return new SpoilerAnswer { Threat = threat, ActionKey = best.a.Key, Cost = best.cost };
        }

        /// <summary>Expected hits the attacker takes from Root's dice: the defender deals the lower of two 0-3 dice, capped by
        /// its warriors (P(lower &gt;= k) = ((4-k)/4)^2).</summary>
        public static double ExpectedHitsTaken(int defenderWarriors)
        {
            var e = 0.0;
            for (var k = 1; k <= Math.Min(defenderWarriors, 3); k++) e += Math.Pow((4.0 - k) / 4.0, 2);
            return e;
        }

        // ================================================================= helpers

        private static List<ScielAction> Legal(BotView view, IReadOnlyList<string> legal) =>
            ScielAction.ParseAll(legal ?? view.Board.LegalActions(view.Seat));

        private static string Key(List<ScielAction> acts, string verb, ClearingId c) => acts.FirstOrDefault(a => a.Is(verb, c))?.Key;

        private static string BattleKey(List<ScielAction> acts, ClearingId c, SeatId? defender) =>
            acts.FirstOrDefault(a => a.Is(ScielAction.Battle, c) && (!defender.HasValue || !a.Defender.HasValue || a.Defender == defender))?.Key;

        private static string MoveKey(List<ScielAction> acts, ClearingId from, ClearingId to) =>
            acts.FirstOrDefault(a => a.Verb == ScielAction.Move && a.Clearing == from && a.To == to)?.Key;
    }
}
