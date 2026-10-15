using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Clea
{
    /// <summary>Step 8's answer to the most urgent threat.</summary>
    public sealed class CleaSpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        /// <summary>True when it takes the decision ahead of steps 1-7.</summary>
        public bool PreEmpts;
        public ClearingId Clearing;
        public string Key;
        public double Cost;
        public override string ToString() => Key + (PreEmpts ? " (pre-empts)" : " (free)");
    }

    /// <summary>
    /// bot.clea: Root's Lizard Cult actions (rituals, conspiracies) from read.legal-actions (<see cref="CleaAction"/>), steered by
    /// the plan's steps. One public [BotRule] method per bot_rules row (bot.clea.01..08); rule methods read only the
    /// <see cref="BotView"/> (public board, the guarded rocks, her own hand and Outcast) and return a ranking or a decision.
    /// Steps are asked in order and the first that claims the decision takes it; step 6 (goal-mode re-cover) and step 4's
    /// atelier rebuild pre-empt the steps above them while their condition holds, and the spoiler layer pre-empts as the
    /// shared <see cref="SpoilerLayer"/> says. Root's own AI plays every decision no step claims (null).
    /// </summary>
    public sealed class CleaBot : IBotDirector
    {
        private readonly Random _rng = new Random(33);

        public CleaBot()
        {
            EasyCoin = _ => _rng.Next(2) == 0;
        }

        public string PlanId => Ids.BotPlans.Clea;

        /// <summary>The bot's seeded random source for Easy's half-the-time choice: true = stack a covered clearing.</summary>
        public Func<BotView, bool> EasyCoin { get; set; }

        // ================================================================= shared reads

        private static ClearingId? MonolithOf(BotView v) => v.Role(Ids.MapRoles.Monolith);
        private static ClearingId? ManorOf(BotView v) => v.Role(Ids.MapRoles.FlyingManor);

        /// <summary>Her covered clearings (guards she hosts not counted, as in win.clea).</summary>
        public static HashSet<ClearingId> Covered(BotView v) => CleaParts.Covered(v.Board, v.State, v.Seat);

        private static int Own(BotView v, ClearingId c) => CleaParts.OwnWarriors(v.Board, v.State, v.Seat, c);

        /// <summary>A clearing where she hosts a guarded rock: she may not build, recruit or place pieces there.</summary>
        private static bool Blocked(BotView v, ClearingId c) => CleaParts.HostsGuardsIn(v.State, v.Seat, c);

        private static int EnemyWarriors(BotView v, ClearingId c) => v.Board.Seats().Where(s => s != v.Seat).Sum(s => v.Board.Warriors(s, c));

        private static int EnemyWarriorsInAndNext(BotView v, ClearingId c) =>
            EnemyWarriors(v, c) + v.Board.PathNeighbours(c).Sum(n => EnemyWarriors(v, n));

        private static bool HasSuit(BotView v, ClearingId c, Suit s) => v.Board.SuitsOf(c).Contains(s);

        private static int Birds(BotView v) => v.Board.HandSuits(v.Seat).Count(s => s == Suit.Bird);

        /// <summary>The other seat with the most VP (ties: lower seat), the 'VP leader' whose buildings she Sanctifies.</summary>
        public static SeatId? VpLeader(BotView v) =>
            v.Board.Seats().Where(s => s != v.Seat).OrderByDescending(s => v.Board.Vp(s)).ThenBy(s => s.Value).Select(s => (SeatId?)s).FirstOrDefault();

        /// <summary>goalModeTrigger (every difficulty): tune.clea-goal-coverage or more covered clearings.</summary>
        public bool InGoalMode(BotView view) => Covered(view).Count >= Tuning.CleaGoalCoverage;

        /// <summary>The plan keeps nothing in ModState: state.clea.covered (step 6's memory) is written by her module at
        /// hook.evening-end, for a human Clea too.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        /// <summary>Whether her own themed test (gates included) holds now, so this is her own announce turn.</summary>
        public static bool IsAnnounceTurn(BotView v)
        {
            var win = v.WinOf(v.Seat);
            if (win == null || v.Act < win.ActMin || v.Board.ActiveDominance(v.Seat).HasValue || v.Board.Vp(v.Seat) < win.VpMin) return false;
            return CleaParts.TestPasses(v.Board, v.State, v.Seat, MonolithOf(v), ManorOf(v));
        }

        // ================================================================= cards (steps 3 and 5)

        /// <summary>
        /// Step 5's order for the card a ritual reveals (lower first): a non-Outcast, non-bird card (0); a bird above the one step
        /// 3 keeps (1); an Outcast-suit card, kept for conspiracies (2); her last bird, kept for the Monolith (3).
        /// </summary>
        public int CardCost(BotView view, Suit card)
        {
            if (card == Suit.Bird) return Birds(view) >= 2 ? 1 : 3;
            return view.Board.OutcastSuit(view.Seat) == card ? 2 : 0;
        }

        /// <summary>Step 3: her last bird card is spent only to recruit into the Monolith (and on step 4's atelier rebuild,
        /// which pre-empts the steps above it).</summary>
        private bool CardAllowed(BotView v, CleaAction a)
        {
            if (a.CardSuit != Suit.Bird || Birds(v) >= 2) return true;
            if (a.Verb == CleaAction.Recruit && a.Clearing.HasValue && a.Clearing == MonolithOf(v)) return true;
            return a.Verb == CleaAction.Build && a.Clearing.HasValue && a.Clearing == ManorOf(v) && !CleaParts.AtelierHeld(v.Board, v.Seat, ManorOf(v));
        }

        /// <summary>The cheapest card among the ritual keys for one clearing (step 5's order), or null.</summary>
        private string Cheapest(BotView v, IEnumerable<CleaAction> rituals) =>
            rituals.Where(a => CardAllowed(v, a)).OrderBy(a => a.CardSuit.HasValue ? CardCost(v, a.CardSuit.Value) : 0)
                .Select(a => a.Key).FirstOrDefault();

        private static IEnumerable<CleaAction> Of(IEnumerable<CleaAction> acts, string verb, ClearingId c) =>
            acts.Where(a => a.Verb == verb && a.Clearing == c);

        // ================================================================= 01 recruit

        /// <summary>
        /// Step 1's ranking of the clearings a Recruit should fill: uncovered clearings (not her guarded-rock clearings, where she
        /// may not place), suits with the most uncovered clearings first; within a suit the fewest enemy warriors in and next to
        /// the clearing; ties: lower clearing number.
        /// </summary>
        [BotRule("bot.clea.01-recruit")]
        public List<Ranked<ClearingId>> Recruit(BotView view)
        {
            var b = view.Board;
            var covered = Covered(view);
            var uncovered = b.Clearings().Where(c => !covered.Contains(c) && !Blocked(view, c)).ToList();
            var bySuit = new Dictionary<Suit, int>();
            foreach (var c in uncovered)
                foreach (var s in b.SuitsOf(c).Distinct())
                    bySuit[s] = (bySuit.TryGetValue(s, out var n) ? n : 0) + 1;
            double SuitWeight(ClearingId c) => b.SuitsOf(c).Select(s => bySuit.TryGetValue(s, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return Rank.Clearings(uncovered, c => SuitWeight(c) * 10000 - EnemyWarriorsInAndNext(view, c));
        }

        /// <summary>Step 1's decision: the legal Recruit into the best-ranked uncovered clearing, with the cheapest card. Easy,
        /// half the time (<see cref="EasyCoin"/>), places where she already has warriors instead.</summary>
        public string RecruitChoice(BotView view, IReadOnlyList<string> legal)
        {
            var recruits = CleaAction.ParseAll(legal).Where(a => a.Verb == CleaAction.Recruit && a.Clearing.HasValue && !Blocked(view, a.Clearing.Value)).ToList();
            if (recruits.Count == 0) return null;
            if (view.Difficulty == Difficulty.Easy && EasyCoin(view))
            {
                var covered = Covered(view);
                foreach (var c in covered.OrderBy(c => c.Value))
                {
                    var k = Cheapest(view, Of(recruits, CleaAction.Recruit, c));
                    if (k != null) return k;
                }
            }
            foreach (var r in Recruit(view))
            {
                var k = Cheapest(view, Of(recruits, CleaAction.Recruit, r.Item));
                if (k != null) return k;
            }
            return null;
        }

        // ================================================================= 02 convert

        /// <summary>Step 2's Convert targets: Outcast-suit clearings holding an enemy warrior and none of hers (and not a
        /// guarded-rock clearing of hers); ties: lower clearing number.</summary>
        public List<Ranked<ClearingId>> ConvertTargets(BotView view)
        {
            var b = view.Board;
            var outcast = b.OutcastSuit(view.Seat);
            if (!outcast.HasValue) return new List<Ranked<ClearingId>>();
            var targets = b.Clearings().Where(c => HasSuit(view, c, outcast.Value) && EnemyWarriors(view, c) > 0
                && Own(view, c) == 0 && !Blocked(view, c));
            return Rank.Clearings(targets, _ => 0);
        }

        /// <summary>
        /// Step 2: Convert in the first target clearing (against the lowest enemy seat with a warrior there), then, with the
        /// acolytes left after Converts, Sanctify one of the VP leader's buildings (lowest clearing first). Easy never Sanctifies.
        /// </summary>
        [BotRule("bot.clea.02-convert")]
        public string Convert(BotView view, IReadOnlyList<string> legal)
        {
            var acts = CleaAction.ParseAll(legal);
            foreach (var r in ConvertTargets(view))
            {
                var k = Of(acts, CleaAction.Convert, r.Item).Where(a => a.Target.HasValue && a.Target != view.Seat)
                    .OrderBy(a => a.Target.Value.Value).Select(a => a.Key).FirstOrDefault();
                if (k != null) return k;
            }
            if (view.Difficulty == Difficulty.Easy) return null;
            var leader = VpLeader(view);
            if (!leader.HasValue) return null;
            return acts.Where(a => a.Verb == CleaAction.Sanctify && a.Target == leader && a.Clearing.HasValue && !Blocked(view, a.Clearing.Value))
                .OrderBy(a => a.Clearing.Value.Value).Select(a => a.Key).FirstOrDefault();
        }

        // ================================================================= 03 monolith

        /// <summary>Step 3's Crusade limit: Crusade (2 acolytes) only when the Monolith or a clearing next to it is the Outcast
        /// suit.</summary>
        public bool CrusadeAllowed(BotView view)
        {
            var outcast = view.Board.OutcastSuit(view.Seat);
            var m = MonolithOf(view);
            if (!outcast.HasValue || !m.HasValue) return false;
            return HasSuit(view, m.Value, outcast.Value) || view.Board.PathNeighbours(m.Value).Any(n => HasSuit(view, n, outcast.Value));
        }

        /// <summary>Step 3: while the Monolith is not covered, Recruit into it, with her kept bird card when no matching card is
        /// cheaper. Bird keeping and the Crusade limit bind every other step through <see cref="Allowed"/>.</summary>
        [BotRule("bot.clea.03-monolith")]
        public string Monolith(BotView view, IReadOnlyList<string> legal)
        {
            var m = MonolithOf(view);
            if (!m.HasValue || Own(view, m.Value) > 0 || Blocked(view, m.Value)) return null;
            return Cheapest(view, Of(CleaAction.ParseAll(legal), CleaAction.Recruit, m.Value));
        }

        // ================================================================= 04 home

        /// <summary>Whether her Flying Manor atelier is gone (step 4's rebuild then pre-empts steps 1-3).</summary>
        public bool AtelierMissing(BotView view) => ManorOf(view).HasValue && !CleaParts.AtelierHeld(view.Board, view.Seat, ManorOf(view));

        /// <summary>Step 4: rebuild the atelier (garden) in the Flying Manor when it is gone, then keep a warrior there.</summary>
        [BotRule("bot.clea.04-home")]
        public string Home(BotView view, IReadOnlyList<string> legal)
        {
            var fm = ManorOf(view);
            if (!fm.HasValue || Blocked(view, fm.Value)) return null;
            var acts = CleaAction.ParseAll(legal);
            if (AtelierMissing(view))
            {
                var build = Cheapest(view, Of(acts, CleaAction.Build, fm.Value));
                if (build != null) return build;
            }
            return Own(view, fm.Value) == 0 ? Cheapest(view, Of(acts, CleaAction.Recruit, fm.Value)) : null;
        }

        /// <summary>Step 4's pre-empting part: the atelier rebuild alone.</summary>
        public string Rebuild(BotView view, IReadOnlyList<string> legal)
        {
            var fm = ManorOf(view);
            if (!fm.HasValue || !AtelierMissing(view) || Blocked(view, fm.Value)) return null;
            return Cheapest(view, Of(CleaAction.ParseAll(legal), CleaAction.Build, fm.Value));
        }

        // ================================================================= 05 hand

        /// <summary>Step 5: Sacrifice a bird card for an acolyte only while she holds tune.clea-bot-sacrifice-birds or more birds
        /// (so the bird step 3 keeps is never sacrificed). Rituals spend cards in <see cref="CardCost"/> order.</summary>
        [BotRule("bot.clea.05-hand")]
        public string Hand(BotView view, IReadOnlyList<string> legal)
        {
            if (Birds(view) < Tuning.CleaBotSacrificeBirds) return null;
            return CleaAction.ParseAll(legal).Where(a => a.Verb == CleaAction.Sacrifice && a.CardSuit == Suit.Bird).Select(a => a.Key).FirstOrDefault();
        }

        // ================================================================= 06 goal mode

        /// <summary>
        /// Step 6's lost clearings: in goal mode, the clearings in state.clea.covered (covered at the end of her last turn) that
        /// she no longer covers; the Monolith first, then fewest enemy warriors; ties: lower clearing number. Empty outside goal
        /// mode.
        /// </summary>
        [BotRule("bot.clea.06-goal-mode")]
        public List<Ranked<ClearingId>> GoalMode(BotView view)
        {
            if (!InGoalMode(view) || view.State.CleaCovered == null) return new List<Ranked<ClearingId>>();
            var covered = Covered(view);
            var m = MonolithOf(view);
            var lost = view.State.CleaCovered.Where(c => !covered.Contains(c) && !Blocked(view, c)).ToList();
            return Rank.Clearings(lost, c => (c == m ? 10000 : 0) - EnemyWarriors(view, c));
        }

        /// <summary>Step 6's decision (pre-empts the steps above): Recruit, else Convert, into the first lost clearing.</summary>
        public string Recover(BotView view, IReadOnlyList<string> legal)
        {
            var acts = CleaAction.ParseAll(legal);
            foreach (var r in GoalMode(view))
            {
                var k = Cheapest(view, Of(acts, CleaAction.Recruit, r.Item))
                    ?? Of(acts, CleaAction.Convert, r.Item).Where(a => a.Target.HasValue).OrderBy(a => a.Target.Value.Value).Select(a => a.Key).FirstOrDefault();
                if (k != null) return k;
            }
            return null;
        }

        // ================================================================= 07 guards

        /// <summary>
        /// Step 7: whether an action leaves her guard stacks alone. Never a piece placed (Recruit, Build, Convert, Sanctify) in a
        /// clearing where she hosts a guarded rock, and never a Crusade move that takes her warriors there below the guards she
        /// hosts (the guard floor). Her coverage everywhere subtracts those guards (<see cref="CleaParts.Covered"/>).
        /// </summary>
        [BotRule("bot.clea.07-guards")]
        public bool Guards(BotView view, string actionKey)
        {
            var a = CleaAction.Parse(actionKey);
            if (a.Places && a.Clearing.HasValue && Blocked(view, a.Clearing.Value)) return false;
            if (a.Verb == CleaAction.Move && a.From.HasValue)
            {
                var guards = CleaParts.HostedGuards(view.State, view.Seat, a.From.Value);
                if (guards > 0 && view.Board.Warriors(view.Seat, a.From.Value) - a.Count < guards) return false;
            }
            if (a.Verb == CleaAction.Move && a.To.HasValue && Blocked(view, a.To.Value)) return false;
            return true;
        }

        // ================================================================= 08 spoiler

        /// <summary>Expected hits a Crusade battle costs her (Root's dice: two 0-3 dice, the defender deals the lower, capped by
        /// its warriors).</summary>
        public static double ExpectedHitsTaken(BotView v, SeatId defender, ClearingId c)
        {
            var w = v.Board.Warriors(defender, c);
            double e = 0;
            for (var k = 1; k <= Math.Min(3, w); k++) e += Math.Pow((4 - k) / 4.0, 2);
            return e;
        }

        private static int DistanceToOwnPieces(BotView v, ClearingId c)
        {
            var b = v.Board;
            bool Mine(ClearingId x) => b.Warriors(v.Seat, x) + b.Buildings(v.Seat, x) + b.Tokens(v.Seat, x) > 0;
            var seen = new HashSet<ClearingId> { c };
            var frontier = new List<ClearingId> { c };
            for (var d = 0; frontier.Count > 0; d++)
            {
                if (frontier.Any(Mine)) return d;
                frontier = frontier.SelectMany(x => b.PathNeighbours(x)).Where(seen.Add).ToList();
            }
            return int.MaxValue;
        }

        /// <summary>
        /// Step 8, the shared spoiler layer (<see cref="SpoilerLayer"/>): for the most urgent threat, the cheapest legal answer in
        /// its key clearings. Her answers: a Recruit placement there (1 action), a Convert or a Sanctify against that seat there
        /// (1 action; Outcast-suit clearings, as Root's legal keys allow), or a Crusade battle there within step 3's limit
        /// (1 action + expected hits taken). Ties: the key clearing's order (its win's met parts first), then the answer nearest
        /// her pieces, then the lower clearing. Only the actions <see cref="Allowed"/> leaves are considered (guard stacks never
        /// moved, the Flying Manor kept, Easy never Sanctifies).
        /// </summary>
        [BotRule("bot.clea.08-spoiler")]
        public CleaSpoilerAnswer Spoiler(BotView view, IReadOnlyList<string> legal)
        {
            if (legal == null) return null;
            var b = view.Board;
            var acts = CleaAction.ParseAll(Allowed(view, legal));
            foreach (var threat in SpoilerLayer.Threats(view))
            {
                var keys = threat.KeyClearings.Count > 0 ? threat.KeyClearings.ToList()
                    : b.Clearings().Where(c => b.Warriors(threat.Seat, c) + b.Buildings(threat.Seat, c) + b.Tokens(threat.Seat, c) > 0
                        || b.PawnClearing(threat.Seat) == c).ToList();
                var answers = new List<(CleaSpoilerAnswer a, int index)>();
                for (var i = 0; i < keys.Count; i++)
                {
                    var c = keys[i];
                    void Add(string key, double cost)
                    {
                        if (key != null) answers.Add((new CleaSpoilerAnswer { Threat = threat.Seat, Banner = threat.Banner, Clearing = c, Key = key, Cost = cost }, i));
                    }
                    Add(Cheapest(view, Of(acts, CleaAction.Recruit, c)), 1);
                    Add(Of(acts, CleaAction.Convert, c).Where(a => a.Target == threat.Seat).Select(a => a.Key).FirstOrDefault(), 1);
                    Add(Of(acts, CleaAction.Sanctify, c).Where(a => a.Target == threat.Seat).Select(a => a.Key).FirstOrDefault(), 1);
                    Add(Of(acts, CleaAction.Crusade, c).Where(a => a.Target == threat.Seat).Select(a => a.Key).FirstOrDefault(),
                        1 + ExpectedHitsTaken(view, threat.Seat, c));
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

        /// <summary>
        /// The legal actions the plan's limits leave her: step 7 (guard stacks), step 3 (Crusade only when the Monolith area is
        /// the Outcast suit; her last bird only for the Monolith or the atelier rebuild), step 4 (a Crusade move never takes her
        /// last warrior out of the Flying Manor), step 5 (Sacrifice only with tune.clea-bot-sacrifice-birds birds) and Easy's
        /// never-Sanctify.
        /// </summary>
        public List<string> Allowed(BotView view, IReadOnlyList<string> legal)
        {
            var list = new List<string>();
            if (legal == null) return list;
            var crusade = CrusadeAllowed(view);
            var fm = ManorOf(view);
            foreach (var k in legal)
            {
                var a = CleaAction.Parse(k);
                if (!Guards(view, k)) continue;
                if (a.IsCrusade && !crusade) continue;
                if (a.Verb == CleaAction.Sanctify && view.Difficulty == Difficulty.Easy) continue;
                if (a.Verb == CleaAction.Sacrifice && Birds(view) < Tuning.CleaBotSacrificeBirds) continue;
                if ((a.Verb == CleaAction.Recruit || a.Verb == CleaAction.Build) && !CardAllowed(view, a)) continue;
                if (a.Verb == CleaAction.Move && fm.HasValue && a.From == fm && Own(view, fm.Value) - a.Count < 1) continue;
                list.Add(k);
            }
            return list;
        }

        /// <summary>
        /// hook.bot-decision: the spoiler layer when it pre-empts; then step 6's re-cover and step 4's atelier rebuild (each
        /// pre-empts the steps above it); then steps 1-5 in order; then the spoiler layer with the actions they leave free.
        /// Null lets Root's own AI decide (her bot's battles outside step 3's limit are vetoed through hook.battle-legal).
        /// </summary>
        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var legal = Allowed(view, legalActions);
            if (legal.Count == 0) return null;
            var spoiler = Spoiler(view, legal);
            if (spoiler != null && spoiler.PreEmpts) return spoiler.Key;
            var key = Recover(view, legal) ?? Rebuild(view, legal)
                ?? RecruitChoice(view, legal) ?? Convert(view, legal) ?? Monolith(view, legal) ?? Home(view, legal) ?? Hand(view, legal);
            return key ?? spoiler?.Key;
        }
    }
}
