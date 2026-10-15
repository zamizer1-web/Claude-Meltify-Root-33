using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>Step 1's reading of one Ring clearing.</summary>
    public sealed class RingSpot
    {
        public ClearingId Clearing;
        /// <summary>His warriors (minus hosted guards) minus the rule count of the strongest faction there that is not the Paintress.</summary>
        public int Margin;
        /// <summary>His face-up Snare is there: it holds the clearing whatever the margin.</summary>
        public bool Snare;
        /// <summary>The clearing passes the win's Ring test right now.</summary>
        public bool Held;
        public override string ToString() => Clearing + " margin " + Margin + (Snare ? " (snare)" : "");
    }

    /// <summary>Step 3's plan for plots this turn.</summary>
    public sealed class PlotPlan
    {
        /// <summary>Where a new Snare goes: the lowest-margin Ring clearing without a plot of his, or null.</summary>
        public ClearingId? Snare;
        /// <summary>His face-down Snares in the Ring to flip face up (goal mode only), lowest margin first.</summary>
        public List<ClearingId> Flip = new List<ClearingId>();
        /// <summary>Bomb sites in or next to the Ring with at least tune.white-haired-man-bomb-warriors enemy warriors, most first.</summary>
        public List<Ranked<ClearingId>> Bomb = new List<Ranked<ClearingId>>();
        /// <summary>A Raid as a refill (a Ring clearing with no plot), only when no Snare or Bomb is due.</summary>
        public ClearingId? Raid;
        /// <summary>Easy only: the Ring clearing he leaves to a single face-down plot (never flipped, never reinforced).</summary>
        public ClearingId? EasyLeft;
    }

    /// <summary>Step 6 in force: what goal mode forbids and what it orders.</summary>
    public sealed class GoalModeOrders
    {
        public bool Active;
        /// <summary>The Ring clearings and the Monolith: in goal mode no warrior of his leaves them for a clearing outside them.</summary>
        public HashSet<ClearingId> Hold = new HashSet<ClearingId>();
        /// <summary>Vagabonds whose pawn stands in the Monolith while he has warriors there: he battles them.</summary>
        public List<SeatId> MonolithVagabonds = new List<SeatId>();
    }

    public enum WhmAnswerKind { Bomb, Battle, MoveThenBattle, Recruit }

    /// <summary>Step 7's answer to the most urgent threat.</summary>
    public sealed class WhmSpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        /// <summary>True when it takes the decision ahead of steps 1-6; false when it may only use the actions they leave free.</summary>
        public bool PreEmpts;
        public WhmAnswerKind Kind;
        public ClearingId Clearing;
        /// <summary>For a move-then-battle answer: where the warriors come from.</summary>
        public ClearingId? MoveFrom;
        /// <summary>For a Recruit answer: the card suit spent.</summary>
        public Suit? RecruitSuit;
        /// <summary>Actions needed this turn + expected hits taken.</summary>
        public double Cost;
        /// <summary>True when it breaks a met part (removes the threat's pieces); false for a block (a Recruit placement).</summary>
        public bool Breaks;
        public override string ToString() => Kind + " " + Clearing + " vs " + Threat + " (" + Cost + ")";
    }

    /// <summary>
    /// bot.white-haired-man: the Goal Director for the White-Haired Man's Corvids. One public [BotRule] method per bot_rules
    /// row, asked in order; the first step that claims a decision takes it, and Root's own AI plays every decision none claims
    /// (hit allocation, Ambush, discards). Rule methods read only <see cref="BotView"/>: the public board, his own hand and his
    /// own hidden plots, and the public mod state. Action keys: <see cref="WhiteHairedManAction"/>.
    /// </summary>
    public sealed class WhiteHairedManBot : IBotDirector
    {
        /// <summary>bot.white-haired-man.04-move-battle: a Ring clearing is fixed at margin 1 (he rules it) on Easy and Normal.</summary>
        public const int NormalFixedMargin = 1;

        public string PlanId => Ids.BotPlans.WhiteHairedMan;

        private static WinConditionDef WinRow => Data.WinConditions[Ids.WinConditions.WhiteHairedMan];

        private static ClearingId? Monolith(BotView v) => v.Role(Ids.MapRoles.Monolith);
        private static SeatId? Paintress(BotView v) => v.SeatOf(CharacterIds.Paintress);
        private static IReadOnlyList<ClearingId> RingOf(BotView v) => WhiteHairedManRing.Ring(v.Board, Monolith(v));
        private static int Own(BotView v, ClearingId c) => WhiteHairedManRing.OwnWarriors(v.Board, v.State, v.Seat, c);
        private static int PlotsAt(BotView v, ClearingId c) => WhiteHairedManRing.Plots(v.Board, v.Seat, c);

        private static int EnemyWarriors(BotView v, ClearingId c) =>
            v.Board.Seats().Where(s => s != v.Seat).Sum(s => v.Board.Warriors(s, c));

        private static int Presence(BotView v, SeatId s, ClearingId c) =>
            v.Board.Warriors(s, c) + v.Board.Buildings(s, c) + v.Board.Tokens(s, c) + (v.Board.PawnClearing(s) == c ? 1 : 0);

        private static int SuitOrder(Suit s) => s == Suit.Fox ? 0 : s == Suit.Rabbit ? 1 : s == Suit.Mouse ? 2 : 3;

        /// <summary>goalModeTrigger, the same at every difficulty: Vp(seat) &gt;= tune.white-haired-man-goal-vp.</summary>
        public bool InGoalMode(BotView view) => view.Board.Vp(view.Seat) >= Tuning.WhiteHairedManGoalVp;

        /// <summary>The plan keeps nothing in ModState: every step is worked out from the board each time.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        /// <summary>The margin a Ring clearing needs to count as fixed: 1, Hard tune.white-haired-man-hard-margin.</summary>
        public int FixedMargin(BotView view) => view.Difficulty == Difficulty.Hard ? Tuning.WhiteHairedManHardMargin : NormalFixedMargin;

        /// <summary>The goal move he names at Birdsong (trigger.goal-move): the Ring clearing step 4 fixes first, else the
        /// clearing step 3 Snares; null when the Ring needs nothing.</summary>
        public ClearingId? GoalMoveTarget(BotView view)
        {
            var fix = MoveBattle(view);
            if (fix.Count > 0) return fix[0].Item;
            return Plots(view).Snare;
        }

        // ================================================================= 01 ring status

        /// <summary>Step 1: his margin in each Ring clearing (the Monolith's path neighbours), by clearing number.
        /// margin(r) = his warriors in r (minus GuardsLeft of rocks he hosts there) - the rule count (warriors + buildings) of
        /// the strongest faction in r that is not the Paintress; a face-up Snare holds r whatever the margin.</summary>
        [BotRule("bot.white-haired-man.01-ring-status")]
        public List<RingSpot> RingStatus(BotView view)
        {
            var b = view.Board;
            var p = Paintress(view);
            return RingOf(view).OrderBy(r => r.Value).Select(r => new RingSpot
            {
                Clearing = r,
                Margin = WhiteHairedManRing.Margin(b, view.State, view.Seat, p, r),
                Snare = WhiteHairedManRing.FaceUpSnare(b, view.Seat, r),
                Held = WhiteHairedManRing.RingClearingHeld(b, view.State, view.Seat, p, r),
            }).ToList();
        }

        public int Margin(BotView view, ClearingId r) => WhiteHairedManRing.Margin(view.Board, view.State, view.Seat, Paintress(view), r);

        // ================================================================= 02 recruit

        /// <summary>Step 2: the card suits he recruits with, best first: only suits of his hand that some Ring clearing has
        /// (Corvid Recruit places a warrior in every clearing of that suit), and only with warriors in supply. The suit whose
        /// Ring clearings have the lowest total margin first; ties: more Ring clearings of that suit, then fox, rabbit, mouse.
        /// Score = minus the total margin.</summary>
        [BotRule("bot.white-haired-man.02-recruit")]
        public List<Ranked<Suit>> Recruit(BotView view)
        {
            var b = view.Board;
            if (b.SupplyWarriors(view.Seat) <= 0) return new List<Ranked<Suit>>();
            var spots = RingStatus(view);
            var rows = b.HandSuits(view.Seat).Where(s => s != Suit.Bird).Distinct()
                .Select(s => (suit: s, ring: spots.Where(sp => b.SuitsOf(sp.Clearing).Contains(s)).ToList()))
                .Where(x => x.ring.Count > 0)
                .Select(x => (x.suit, total: x.ring.Sum(sp => sp.Margin), count: x.ring.Count));
            return rows.OrderBy(x => x.total).ThenByDescending(x => x.count).ThenBy(x => SuitOrder(x.suit))
                .Select(x => new Ranked<Suit>(x.suit, -x.total)).ToList();
        }

        // ================================================================= 03 plots

        /// <summary>Easy only: the Ring clearing he leaves to a single face-down plot: one where a lone face-down plot of his
        /// already stands (no warriors of his), else the lowest-margin Ring clearing without a face-up Snare. Null on Normal and
        /// Hard.</summary>
        public ClearingId? EasyLeft(BotView view)
        {
            if (view.Difficulty != Difficulty.Easy) return null;
            var b = view.Board;
            var spots = RingStatus(view);
            var lone = spots.Where(sp => PlotsAt(view, sp.Clearing) >= 1 && b.FaceUpPlot(view.Seat, sp.Clearing) == null && Own(view, sp.Clearing) == 0)
                .Select(sp => (ClearingId?)sp.Clearing).FirstOrDefault();
            if (lone.HasValue) return lone;
            return spots.Where(sp => !sp.Snare).OrderBy(sp => sp.Margin).ThenBy(sp => sp.Clearing.Value)
                .Select(sp => (ClearingId?)sp.Clearing).FirstOrDefault();
        }

        /// <summary>Step 3: a Snare in the Ring clearing hardest to out-stack (the lowest margin, step 1) among those without a
        /// plot of his, flipped face up from goal mode (step 6); a Bomb in or next to the Ring where at least
        /// tune.white-haired-man-bomb-warriors enemy warriors gather (most first, never where the Paintress has pieces unless
        /// step 5 lets him attack her); a Raid in a Ring clearing with no plot as a refill when no Snare or Bomb is due. Easy
        /// leaves one Ring clearing to a single face-down plot: it is never flipped. Ties: lower clearing number.
        /// <paramref name="legal"/> (the legal action keys, optional) limits each pick to what he can place now.</summary>
        [BotRule("bot.white-haired-man.03-plots")]
        public PlotPlan Plots(BotView view, IReadOnlyCollection<string> legal = null)
        {
            bool Can(string key) => legal == null || legal.Contains(key);
            var b = view.Board;
            var me = view.Seat;
            var spots = RingStatus(view);
            var plan = new PlotPlan { EasyLeft = EasyLeft(view) };

            plan.Snare = spots.Where(sp => PlotsAt(view, sp.Clearing) == 0 && Can(WhiteHairedManAction.PlotKey(sp.Clearing, CorvidPlots.Snare)))
                .OrderBy(sp => sp.Margin).ThenBy(sp => sp.Clearing.Value).Select(sp => (ClearingId?)sp.Clearing).FirstOrDefault();

            if (InGoalMode(view))
                plan.Flip = spots.Where(sp => sp.Clearing != plan.EasyLeft && b.HiddenPlotType(me, sp.Clearing) == CorvidPlots.Snare
                                              && Can(WhiteHairedManAction.FlipKey(sp.Clearing)))
                    .OrderBy(sp => sp.Margin).ThenBy(sp => sp.Clearing.Value).Select(sp => sp.Clearing).ToList();

            var area = new HashSet<ClearingId>(spots.Select(sp => sp.Clearing));
            foreach (var r in spots) foreach (var n in b.PathNeighbours(r.Clearing)) area.Add(n);
            var bombs = area.Where(c => PlotsAt(view, c) == 0 && EnemyWarriors(view, c) >= Tuning.WhiteHairedManBombWarriors
                                        && MayBomb(view, c) && Can(WhiteHairedManAction.PlotKey(c, CorvidPlots.Bomb)));
            plan.Bomb = Rank.Clearings(bombs, c => EnemyWarriors(view, c));

            if (!plan.Snare.HasValue && plan.Bomb.Count == 0)
                plan.Raid = spots.Where(sp => PlotsAt(view, sp.Clearing) == 0 && Can(WhiteHairedManAction.PlotKey(sp.Clearing, CorvidPlots.Raid)))
                    .Select(sp => (ClearingId?)sp.Clearing).FirstOrDefault();
            return plan;
        }

        // ================================================================= 04 move and battle

        /// <summary>Step 4: the Ring clearings to fix, lowest margin first (ties: lower clearing). A clearing is fixed when its
        /// margin is <see cref="NormalFixedMargin"/> or more (Hard: tune.white-haired-man-hard-margin) or it holds his face-up
        /// Snare; Easy never reinforces the clearing it leaves to a face-down plot. Score = minus the margin.</summary>
        [BotRule("bot.white-haired-man.04-move-battle")]
        public List<Ranked<ClearingId>> MoveBattle(BotView view)
        {
            var need = FixedMargin(view);
            var left = EasyLeft(view);
            var spots = RingStatus(view).Where(sp => !sp.Snare && sp.Margin < need && sp.Clearing != left).ToList();
            return Rank.Clearings(spots.Select(sp => sp.Clearing), c => -spots.First(sp => sp.Clearing == c).Margin);
        }

        /// <summary>Warriors he can move out of a clearing without unfixing it: in a Ring clearing, those above the fixed margin
        /// (all of them under a face-up Snare); in the Monolith, those above what keeps another faction from ruling it (one stays
        /// while a Vagabond pawn is there); elsewhere every warrior that is not a hosted guard.</summary>
        public int Spare(BotView view, ClearingId c)
        {
            var b = view.Board;
            var own = Own(view, c);
            var m = Monolith(view);
            if (RingOf(view).Contains(c))
                return WhiteHairedManRing.FaceUpSnare(b, view.Seat, c) ? own : Math.Max(0, Math.Min(own, Margin(view, c) - FixedMargin(view)));
            if (m.HasValue && c == m.Value)
            {
                var other = WhiteHairedManRing.StrongestOther(b, view.Seat, Paintress(view), c);
                var keep = other == 0 ? 0 : other + 1;
                if (WhiteHairedManRing.VagabondIn(b, c)) keep = Math.Max(keep, 1);
                return Math.Max(0, own - keep);
            }
            return own;
        }

        // ================================================================= 05 spare the Paintress

        /// <summary>Step 5: true while he must spare the Paintress: she is seated and not one step from her own win as the
        /// spoiler layer defines it (banner up, or the Goal Tracker one unit short). Then no battle against her seat and no Bomb
        /// where she has pieces.</summary>
        [BotRule("bot.white-haired-man.05-spare-paintress")]
        public bool SparePaintress(BotView view)
        {
            var p = Paintress(view);
            return p.HasValue && p.Value != view.Seat && !SpoilerLayer.Threatens(view, p.Value);
        }

        public bool MayBattle(BotView view, SeatId defender) =>
            defender != view.Seat && !(Paintress(view) == defender && SparePaintress(view));

        public bool MayBomb(BotView view, ClearingId c)
        {
            var p = Paintress(view);
            return !p.HasValue || Presence(view, p.Value, c) == 0 || !SparePaintress(view);
        }

        // ================================================================= 06 goal mode

        /// <summary>Step 6: in goal mode (Vp &gt;= tune.white-haired-man-goal-vp) no warrior of his in a Ring clearing or the
        /// Monolith moves to a clearing outside them, and he battles any Vagabond pawn in the Monolith when he has warriors there.</summary>
        [BotRule("bot.white-haired-man.06-goal-mode")]
        public GoalModeOrders GoalMode(BotView view)
        {
            var o = new GoalModeOrders { Active = InGoalMode(view) };
            var m = Monolith(view);
            foreach (var r in RingOf(view)) o.Hold.Add(r);
            if (m.HasValue) o.Hold.Add(m.Value);
            if (o.Active && m.HasValue && view.Board.Warriors(view.Seat, m.Value) >= 1)
                o.MonolithVagabonds = view.Board.Seats().Where(s => s != view.Seat && view.IsVagabond(s) && view.Board.PawnClearing(s) == m.Value)
                    .OrderBy(s => s.Value).ToList();
            return o;
        }

        /// <summary>Whether step 6 lets warriors move from one clearing to another.</summary>
        public bool MayMove(BotView view, ClearingId from, ClearingId to)
        {
            if (!InGoalMode(view)) return true;
            var hold = GoalMode(view).Hold;
            return !hold.Contains(from) || hold.Contains(to);
        }

        // ================================================================= 07 spoiler layer

        /// <summary>The probability of each value of the lower of two 0-3 dice (Root's battle roll): 7/16, 5/16, 3/16, 1/16.</summary>
        private static readonly double[] LowDie = { 7 / 16.0, 5 / 16.0, 3 / 16.0, 1 / 16.0 };

        /// <summary>Expected hits he takes attacking <paramref name="defender"/> in a clearing: the defender deals the lower die,
        /// capped by its warriors (a Vagabond: its undamaged swords).</summary>
        public double ExpectedHitsTaken(BotView view, SeatId defender, ClearingId c)
        {
            var cap = view.IsVagabond(defender)
                ? view.Board.Items(defender).Count(i => i.Type == ItemType.Sword && !i.Damaged)
                : view.Board.Warriors(defender, c);
            var e = 0.0;
            for (var k = 0; k < LowDie.Length; k++) e += LowDie[k] * Math.Min(k, cap);
            return e;
        }

        /// <summary>Whether this is his own announce turn: his win's gates and test pass on the board now.</summary>
        public bool OwnAnnounceTurn(BotView view)
        {
            var row = WinRow;
            if (view.Act < row.ActMin || view.Board.Vp(view.Seat) < row.VpMin) return false;
            if (row.OffWithDominance && view.Board.ActiveDominance(view.Seat).HasValue) return false;
            return WhiteHairedManRing.NobodyReachesHer(view.Board, view.State, view.Seat, Paintress(view), Monolith(view));
        }

        /// <summary>Step 7, the shared spoiler layer: for the most urgent threat (<see cref="SpoilerLayer.Threats"/>) the
        /// cheapest answer in that win's key clearings; null with no trigger. See <see cref="SpoilerAnswers"/>.</summary>
        [BotRule("bot.white-haired-man.07-spoiler")]
        public WhmSpoilerAnswer Spoiler(BotView view) => SpoilerAnswers(view).FirstOrDefault();

        /// <summary>Every answer to the most urgent threat that has one, best first. Answers in a key clearing where the threat
        /// has pieces break a met part: a battle where he has warriors (1 action + expected hits), a move in from a neighbour then
        /// a battle (2 actions + expected hits), or a Bomb where the threat has warriors (1 action). In a key clearing where it
        /// has none he blocks with a Recruit placement (a card of that clearing's suit, 1 action). Breaking comes before
        /// blocking; then the cost; then the key order; then the answer nearest his pieces; then the lower clearing. Step 5 and
        /// step 6 bind: no battle against a spared Paintress or Bomb on her pieces, and in goal mode no move out of the Ring and
        /// the Monolith. Pre-emption: <see cref="SpoilerLayer.PreEmpts"/> (banners, and on Hard one-step threats unless this is
        /// his own announce turn).</summary>
        public List<WhmSpoilerAnswer> SpoilerAnswers(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            foreach (var threat in SpoilerLayer.Threats(view))
            {
                var t = threat.Seat;
                var keys = threat.KeyClearings.ToList();
                if (keys.Count == 0) keys = b.Clearings().Where(c => Presence(view, t, c) > 0).ToList();
                var found = new List<(WhmSpoilerAnswer a, int key, int distance)>();
                for (var i = 0; i < keys.Count; i++)
                {
                    var c = keys[i];
                    if (Presence(view, t, c) > 0)
                    {
                        var hits = ExpectedHitsTaken(view, t, c);
                        if (MayBattle(view, t))
                        {
                            if (b.Warriors(me, c) >= 1)
                                found.Add((new WhmSpoilerAnswer { Kind = WhmAnswerKind.Battle, Clearing = c, Cost = 1 + hits, Breaks = true }, i, 0));
                            else
                            {
                                var from = b.PathNeighbours(c).Where(n => b.Warriors(me, n) >= 1 && MayMove(view, n, c))
                                    .OrderByDescending(n => b.Warriors(me, n)).ThenBy(n => n.Value).Select(n => (ClearingId?)n).FirstOrDefault();
                                if (from.HasValue)
                                    found.Add((new WhmSpoilerAnswer { Kind = WhmAnswerKind.MoveThenBattle, Clearing = c, MoveFrom = from, Cost = 2 + hits, Breaks = true }, i, 1));
                            }
                        }
                        if (b.Warriors(me, c) >= 1 && PlotsAt(view, c) == 0 && b.Warriors(t, c) >= 1 && MayBomb(view, c))
                            found.Add((new WhmSpoilerAnswer { Kind = WhmAnswerKind.Bomb, Clearing = c, Cost = 1, Breaks = true }, i, 0));
                    }
                    else if (b.SupplyWarriors(me) >= 1)
                    {
                        var hand = b.HandSuits(me);
                        var suit = b.SuitsOf(c).Where(s => s != Suit.Bird && hand.Contains(s)).OrderBy(SuitOrder).Select(s => (Suit?)s).FirstOrDefault();
                        if (suit.HasValue)
                            found.Add((new WhmSpoilerAnswer { Kind = WhmAnswerKind.Recruit, Clearing = c, RecruitSuit = suit, Cost = 1, Breaks = false }, i, 0));
                    }
                }
                if (found.Count == 0) continue;
                var preEmpts = SpoilerLayer.PreEmpts(view, threat, OwnAnnounceTurn(view));
                return found.OrderByDescending(x => x.a.Breaks).ThenBy(x => x.a.Cost).ThenBy(x => x.key).ThenBy(x => x.distance)
                    .ThenBy(x => x.a.Clearing.Value).ThenBy(x => (int)x.a.Kind)
                    .Select(x =>
                    {
                        x.a.Threat = t;
                        x.a.Banner = threat.Banner;
                        x.a.PreEmpts = preEmpts;
                        return x.a;
                    }).ToList();
            }
            return new List<WhmSpoilerAnswer>();
        }

        // ================================================================= decisions

        /// <summary>Whether a legal key obeys steps 5 and 6 (every step, the spoiler layer included, obeys them).</summary>
        public bool Allowed(BotView view, WhiteHairedManAction a)
        {
            if (a.IsMove) return MayMove(view, a.From.Value, a.To.Value);
            if (a.IsBattle) return MayBattle(view, a.Target.Value);
            if (a.IsPlot && a.PlotType == CorvidPlots.Bomb) return MayBomb(view, a.Clearing.Value);
            return true;
        }

        /// <summary>hook.bot-decision: the spoiler layer when it pre-empts, then steps 2, 3, 4 and 6 in order (steps 1 and 5 are
        /// read by the others), then the spoiler layer with the actions they leave free; null lets Root's own AI decide.</summary>
        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            var parsed = WhiteHairedManAction.ParseAll(legalActions).Where(a => Allowed(view, a)).ToList();
            if (parsed.Count == 0) return null;
            var legal = new HashSet<string>(parsed.Select(a => a.Key), StringComparer.Ordinal);

            var answers = SpoilerAnswers(view);
            if (answers.Count > 0 && answers[0].PreEmpts)
            {
                var k = answers.Select(a => SpoilerKey(a, parsed)).FirstOrDefault(x => x != null);
                if (k != null) return k;
            }

            // 02: recruit with Ring-suit cards.
            foreach (var r in Recruit(view))
            {
                var key = WhiteHairedManAction.RecruitKey(r.Item);
                if (legal.Contains(key)) return key;
            }

            // 03: flip Snares (goal mode), a new Snare, a Bomb, a Raid.
            var plan = Plots(view, legal);
            if (plan.Flip.Count > 0) return WhiteHairedManAction.FlipKey(plan.Flip[0]);
            if (plan.Snare.HasValue) return WhiteHairedManAction.PlotKey(plan.Snare.Value, CorvidPlots.Snare);
            if (plan.Bomb.Count > 0) return WhiteHairedManAction.PlotKey(plan.Bomb[0].Item, CorvidPlots.Bomb);
            if (plan.Raid.HasValue) return WhiteHairedManAction.PlotKey(plan.Raid.Value, CorvidPlots.Raid);

            // 04: fix the lowest-margin Ring clearing: move in, then battle intruders there.
            foreach (var r in MoveBattle(view))
            {
                var move = BestMoveInto(view, parsed, r.Item);
                if (move != null) return move;
                var battle = BestBattleIn(view, parsed, r.Item);
                if (battle != null) return battle;
            }

            // 06: battle any Vagabond pawn in the Monolith.
            var m = Monolith(view);
            if (m.HasValue)
                foreach (var v in GoalMode(view).MonolithVagabonds)
                {
                    var key = WhiteHairedManAction.BattleKey(m.Value, v);
                    if (legal.Contains(key)) return key;
                }

            // 07: the spoiler layer with the actions the steps leave free.
            return answers.Select(a => SpoilerKey(a, parsed)).FirstOrDefault(x => x != null);
        }

        /// <summary>The legal move into a Ring clearing that brings the most of the warriors it lacks without unfixing its
        /// source; ties: fewer warriors moved, then the lower source clearing.</summary>
        private string BestMoveInto(BotView view, List<WhiteHairedManAction> parsed, ClearingId r)
        {
            var need = Math.Max(1, FixedMargin(view) - Margin(view, r));
            return parsed.Where(a => a.IsMove && a.To.Value == r && a.From.Value != r && a.Count >= 1 && a.Count <= Spare(view, a.From.Value))
                .OrderByDescending(a => Math.Min(a.Count, need)).ThenBy(a => a.Count).ThenBy(a => a.From.Value)
                .Select(a => a.Key).FirstOrDefault();
        }

        /// <summary>The legal battle against the strongest intruder (not the Paintress) in a Ring clearing; ties: lower seat.</summary>
        private string BestBattleIn(BotView view, List<WhiteHairedManAction> parsed, ClearingId r)
        {
            var p = Paintress(view);
            return parsed.Where(a => a.IsBattle && a.Clearing.Value == r && a.Target.Value != view.Seat && a.Target != p)
                .OrderByDescending(a => view.Board.Warriors(a.Target.Value, r) + view.Board.Buildings(a.Target.Value, r))
                .ThenBy(a => a.Target.Value).Select(a => a.Key).FirstOrDefault();
        }

        private static string SpoilerKey(WhmSpoilerAnswer a, List<WhiteHairedManAction> parsed)
        {
            string Find(Func<WhiteHairedManAction, bool> f) => parsed.Where(f).Select(x => x.Key).FirstOrDefault();
            switch (a.Kind)
            {
                case WhmAnswerKind.Battle:
                    return Find(x => x.IsBattle && x.Clearing.Value == a.Clearing && x.Target.Value == a.Threat);
                case WhmAnswerKind.Bomb:
                    return Find(x => x.IsPlot && x.Clearing.Value == a.Clearing && x.PlotType == CorvidPlots.Bomb);
                case WhmAnswerKind.Recruit:
                    return Find(x => x.IsRecruit && x.CardSuit == a.RecruitSuit);
                case WhmAnswerKind.MoveThenBattle:
                    return parsed.Where(x => x.IsMove && x.From.Value == a.MoveFrom && x.To.Value == a.Clearing)
                        .OrderByDescending(x => x.Count).Select(x => x.Key).FirstOrDefault();
            }
            return null;
        }
    }
}
