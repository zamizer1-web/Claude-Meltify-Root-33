using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Paintress
{
    /// <summary>
    /// The legal-action key grammar the Paintress's director reads and answers in: "verb:arg:arg". read.legal-actions only
    /// promises "stable action keys"; until Root's real keys are known (creator's PC) the plugin maps them onto these:
    /// decree:&lt;column&gt;:&lt;suit&gt; (column recruit|move|battle|build, suit fox|rabbit|mouse|bird), build:&lt;clearing&gt;,
    /// recruit:&lt;clearing&gt;, move:&lt;from&gt;:&lt;to&gt;:&lt;count&gt;, battle:&lt;clearing&gt;:&lt;defender seat&gt;, home:&lt;clearing&gt;.
    /// </summary>
    public static class PaintressActionKey
    {
        public const string Decree = "decree", Build = "build", Recruit = "recruit", Move = "move", Battle = "battle";

        public static string Format(string verb, params string[] args) => args.Length == 0 ? verb : verb + ":" + string.Join(":", args);

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string SuitName(Suit s) => s.ToString().ToLowerInvariant();

        public static string DecreeKey(DecreeCard card) => Format(Decree, card.Column, SuitName(card.Suit));
        public static string BuildKey(ClearingId c) => Format(Build, N(c.Value));
        public static string RecruitKey(ClearingId c) => Format(Recruit, N(c.Value));
        public static string BattleKey(ClearingId c, SeatId defender) => Format(Battle, N(c.Value), N(defender.Value));

        public static string[] Parts(string key) => key == null ? new string[0] : key.Split(':');

        public static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);

        /// <summary>battle:&lt;clearing&gt;:&lt;seat&gt; → the target, or null.</summary>
        public static BattleTarget? ParseBattle(string key)
        {
            var p = Parts(key);
            if (p.Length != 3 || p[0] != Battle || !TryInt(p[1], out var c) || !TryInt(p[2], out var s)) return null;
            return new BattleTarget(new ClearingId(c), new SeatId(s));
        }

        /// <summary>move:&lt;from&gt;:&lt;to&gt;:&lt;count&gt; → (from, to, count), or null.</summary>
        public static (ClearingId from, ClearingId to, int count)? ParseMove(string key)
        {
            var p = Parts(key);
            if (p.Length != 4 || p[0] != Move || !TryInt(p[1], out var f) || !TryInt(p[2], out var t) || !TryInt(p[3], out var n)) return null;
            return (new ClearingId(f), new ClearingId(t), n);
        }
    }

    /// <summary>One card to add to a Decree column ('recruit', 'move', 'battle', 'build'); birds are Suit.Bird.</summary>
    public readonly struct DecreeCard : IEquatable<DecreeCard>
    {
        public const string Recruit = "recruit", Move = "move", Battle = "battle", Build = "build";
        public static readonly IReadOnlyList<string> Columns = new[] { Recruit, Move, Battle, Build };

        public readonly string Column;
        public readonly Suit Suit;
        public DecreeCard(string column, Suit suit) { Column = column; Suit = suit; }
        public bool Equals(DecreeCard o) => Column == o.Column && Suit == o.Suit;
        public override bool Equals(object obj) => obj is DecreeCard o && Equals(o);
        public override int GetHashCode() => (Column?.GetHashCode() ?? 0) * 7 + (int)Suit;
        public override string ToString() => Column + ":" + PaintressActionKey.SuitName(Suit);
    }

    /// <summary>A battle she could start: the clearing and the defending seat.</summary>
    public readonly struct BattleTarget : IEquatable<BattleTarget>
    {
        public readonly ClearingId Clearing;
        public readonly SeatId Defender;
        public BattleTarget(ClearingId clearing, SeatId defender) { Clearing = clearing; Defender = defender; }
        public bool Equals(BattleTarget o) => Clearing == o.Clearing && Defender == o.Defender;
        public override bool Equals(object obj) => obj is BattleTarget o && Equals(o);
        public override int GetHashCode() => Clearing.Value * 31 + Defender.Value;
        public override string ToString() => "battle " + Clearing + " vs " + Defender;
    }

    /// <summary>Step 4's plan: the exact cards for the third Portrait, and the only clearings she may battle in.</summary>
    public sealed class FinishPlan
    {
        public Suit? MissingSuit;
        public ClearingId? Target;
        public List<DecreeCard> Cards = new List<DecreeCard>();
        public List<ClearingId> BattleClearings = new List<ClearingId>();
    }

    /// <summary>Step 7's answer to the most urgent threat: battle in (or move toward) one key clearing.</summary>
    public sealed class SpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        /// <summary>True when it takes the decision ahead of steps 1-6; false when it may only use actions they leave free.</summary>
        public bool PreEmpts;
        public ClearingId Clearing;
        /// <summary>For an answer that first moves in: the clearing the warrior comes from.</summary>
        public ClearingId? MoveFrom;
        public double Cost;
        /// <summary>A Decree battle card for the block, only when the lookahead says it cannot cause Grief.</summary>
        public DecreeCard? Card;
    }

    /// <summary>
    /// bot.paintress: the Goal Director for the Paintress's Eyrie. One public [BotRule] method per bot_rules row, asked in
    /// order; the first step that claims a decision takes it, and Root's own AI plays every decision none claims. Rule
    /// methods read only <see cref="BotView"/> (public board, her own hand and the public mod state).
    /// </summary>
    public sealed class PaintressBot : IBotDirector
    {
        public string PlanId => Ids.BotPlans.Paintress;

        private static WinConditionDef WinRow => Data.WinConditions[Ids.WinConditions.Paintress];

        private static ClearingId? Monolith(BotView v) => v.Role(Ids.MapRoles.Monolith);

        private static List<ClearingId> Portraits(BotView v) =>
            PaintressPortraits.LivingPortraits(v.Board, v.State, v.Seat, Monolith(v));

        private static List<Suit> Missing(BotView v) => PaintressPortraits.MissingSuits(v.Board, v.State, v.Seat, Monolith(v));

        private static int EnemyWarriors(BotView v, ClearingId c) =>
            v.Board.Seats().Where(s => s != v.Seat).Sum(s => v.Board.Warriors(s, c));

        private static bool Matches(BotView v, ClearingId c, Suit cardSuit) => cardSuit == Suit.Bird || v.Board.SuitsOf(c).Contains(cardSuit);

        /// <summary>goalModeTrigger (the same at every difficulty): finish mode at Vp &gt;= tune.paintress-goal-vp with
        /// tune.paintress-finish-portraits Living Portraits.</summary>
        public bool InGoalMode(BotView view) =>
            view.Board.Vp(view.Seat) >= Tuning.PaintressGoalVp && Portraits(view).Count >= Tuning.PaintressFinishPortraits;

        /// <summary>The plan keeps nothing in ModState at Birdsong: state.paintress.repaint-avoid is kept by the module's
        /// hook.piece-removed and hook.turn-end handlers, and every step is worked out from the board each time.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        // ================================================================= 01 site score

        /// <summary>site(c) = +3 next to a clearing she rules, +2 within 2 path steps of the Monolith, -1 per enemy warrior,
        /// -3 for a guarded rock (tune.paintress-site-*).</summary>
        public int SiteScore(BotView view, ClearingId c)
        {
            var b = view.Board;
            var score = 0;
            if (b.PathNeighbours(c).Any(n => b.Ruler(n) == view.Seat)) score += Tuning.PaintressSiteAdjacent;
            var m = Monolith(view);
            if (m.HasValue && Graph.Distances(m.Value, b.PathNeighbours).TryGetValue(c, out var d) && d <= Tuning.PaintressSiteNearMonolithSteps)
                score += Tuning.PaintressSiteNearMonolith;
            score += Tuning.PaintressSiteEnemyWarrior * EnemyWarriors(view, c);
            if (PaintressPortraits.HasGuardedRock(view.State, c)) score += Tuning.PaintressSiteGuardedRock;
            return score;
        }

        /// <summary>Step 1: candidate sites for her next easel, best first: clearings of a suit still lacking a Living
        /// Portrait, outside the Monolith, with a free slot, not in state.paintress.repaint-avoid. Ties: lower clearing.</summary>
        [BotRule("bot.paintress.01-site")]
        public List<Ranked<ClearingId>> Site(BotView view)
        {
            var b = view.Board;
            var m = Monolith(view);
            var missing = Missing(view);
            var avoid = view.State.PaintressRepaintAvoid;
            var candidates = b.Clearings().Where(c =>
                (!m.HasValue || c != m.Value) && b.FreeSlots(c) >= 1
                && b.SuitsOf(c).Any(missing.Contains)
                && !(avoid.TryGetValue(c, out var left) && left > 0));
            return Rank.Clearings(candidates, c => SiteScore(view, c));
        }

        /// <summary>The best missing suit: the missing suit of the best site (tracker order inside a Lost City), or null.</summary>
        public Suit? BestMissingSuit(BotView view)
        {
            var site = Site(view);
            if (site.Count == 0) return null;
            var missing = Missing(view);
            var suits = view.Board.SuitsOf(site[0].Item);
            return missing.Where(suits.Contains).Select(s => (Suit?)s).FirstOrDefault();
        }

        /// <summary>The goal-move bark for step 1: the site she names this turn, or null. Normal and Hard name it on the turn
        /// a Build card can raise it (the Decree's build column holds its suit or a bird); Easy names it a turn early, as soon
        /// as her hand holds such a card.</summary>
        public ClearingId? NextPortraitBark(BotView view)
        {
            var site = Site(view);
            if (site.Count == 0) return null;
            var c = site[0].Item;
            if (view.Board.DecreeColumn(view.Seat, DecreeCard.Build).Any(s => Matches(view, c, s))) return c;
            if (view.Difficulty == Difficulty.Easy && view.Board.HandSuits(view.Seat).Any(s => Matches(view, c, s))) return c;
            return null;
        }

        // ================================================================= 02 decree

        /// <summary>Step 2: the cards to add to the Decree, best first, from her own hand. Build of the best missing suit,
        /// then Recruit (bird first, then the suit of the thinnest Portrait under tune.paintress-portrait-warriors); once two
        /// suits have Living Portraits, a bird Build before a suited one. A card the lookahead says may fail is dropped.</summary>
        [BotRule("bot.paintress.02-decree")]
        public List<DecreeCard> Decree(BotView view)
        {
            var candidates = new List<DecreeCard>();
            var covered = PaintressPortraits.CoveredSuits(view.Board, Portraits(view)).Count;
            var best = BestMissingSuit(view);
            if (covered >= 2) candidates.Add(new DecreeCard(DecreeCard.Build, Suit.Bird));
            if (best.HasValue) candidates.Add(new DecreeCard(DecreeCard.Build, best.Value));
            candidates.Add(new DecreeCard(DecreeCard.Recruit, Suit.Bird));
            var thin = ThinnestEasel(view);
            if (thin.HasValue)
            {
                var suit = view.Board.SuitsOf(thin.Value).Where(s => s != Suit.Bird).Select(s => (Suit?)s).FirstOrDefault();
                if (suit.HasValue) candidates.Add(new DecreeCard(DecreeCard.Recruit, suit.Value));
            }
            var hand = view.Board.HandSuits(view.Seat).ToList();
            var result = new List<DecreeCard>();
            foreach (var card in candidates.Distinct())
            {
                if (!hand.Remove(card.Suit)) continue;
                if (!Lookahead(view, result.Concat(new[] { card }))) { hand.Add(card.Suit); continue; }
                result.Add(card);
            }
            return result;
        }

        /// <summary>Her easel (roost outside the Monolith) with the fewest counted warriors below the Portrait threshold.</summary>
        private ClearingId? ThinnestEasel(BotView view)
        {
            var m = Monolith(view);
            return view.Board.Clearings()
                .Where(c => PaintressPortraits.IsEasel(view.Board, view.Seat, c, m))
                .Select(c => (c, w: PaintressPortraits.PortraitWarriors(view.Board, view.State, view.Seat, c)))
                .Where(x => x.w < Tuning.PaintressPortraitWarriors)
                .OrderBy(x => x.w).ThenBy(x => x.c.Value)
                .Select(x => (ClearingId?)x.c).FirstOrDefault();
        }

        /// <summary>The one-turn lookahead: whether every Decree column, with <paramref name="added"/> on top, can be resolved
        /// on the current board (recruit: a matching roost and warriors in supply; move: a matching clearing with her
        /// warriors; battle: a matching clearing with her warriors and an enemy piece; build: a distinct matching clearing she
        /// rules with a free slot and no roost of hers, and roosts in supply). False means the Decree may fail (Grief).</summary>
        public bool Lookahead(BotView view, IEnumerable<DecreeCard> added)
        {
            var b = view.Board;
            var me = view.Seat;
            var columns = DecreeCard.Columns.ToDictionary(col => col, col => b.DecreeColumn(me, col).ToList());
            foreach (var card in added) columns[card.Column].Add(card.Suit);
            var clearings = b.Clearings();

            var recruit = columns[DecreeCard.Recruit];
            var perRecruit = b.EyrieLeader(me) == "charismatic" ? 2 : 1;
            foreach (var s in recruit)
                if (!clearings.Any(c => Matches(view, c, s) && b.BuildingsOfType(me, c, PieceTypes.Roost) > 0)) return false;
            if (recruit.Count * perRecruit > b.SupplyWarriors(me)) return false;

            foreach (var s in columns[DecreeCard.Move])
                if (!clearings.Any(c => Matches(view, c, s) && b.Warriors(me, c) > 0)) return false;

            foreach (var s in columns[DecreeCard.Battle])
                if (!clearings.Any(c => Matches(view, c, s) && b.Warriors(me, c) > 0 && HasEnemyPiece(view, c))) return false;

            var build = columns[DecreeCard.Build];
            if (build.Count > b.SupplyPieces(me, PieceTypes.Roost)) return false;
            var sites = clearings.Where(c => b.Ruler(c) == me && b.FreeSlots(c) >= 1 && b.BuildingsOfType(me, c, PieceTypes.Roost) == 0).ToList();
            var used = new HashSet<ClearingId>();
            foreach (var s in build.OrderBy(s => s == Suit.Bird ? 1 : 0))
            {
                var site = sites.Where(c => !used.Contains(c) && Matches(view, c, s)).Select(c => (ClearingId?)c).FirstOrDefault();
                if (!site.HasValue) return false;
                used.Add(site.Value);
            }
            return true;
        }

        private static bool HasEnemyPiece(BotView v, ClearingId c) =>
            v.Board.Seats().Any(s => s != v.Seat && (v.Board.Warriors(s, c) + v.Board.Buildings(s, c) + v.Board.Tokens(s, c) > 0 || v.Board.PawnClearing(s) == c));

        // ================================================================= 03 garrison

        /// <summary>Warriors each Portrait is garrisoned to: tune.paintress-portrait-warriors, Hard tune.paintress-hard-portrait-warriors.</summary>
        public int PortraitGarrison(BotView view) =>
            view.Difficulty == Difficulty.Hard ? Tuning.PaintressHardPortraitWarriors : Tuning.PaintressPortraitWarriors;

        /// <summary>Enemy warriors stand in a clearing next to the Monolith: she recruits there first.</summary>
        public bool MonolithThreatened(BotView view)
        {
            var m = Monolith(view);
            return m.HasValue && view.Board.PathNeighbours(m.Value).Any(n => EnemyWarriors(view, n) > 0);
        }

        /// <summary>Step 3: where spare recruits and moves go, first first. The Monolith whenever it holds fewer than
        /// tune.paintress-monolith-garrison of her warriors or enemy warriors are next to it; then her easels under the
        /// Portrait garrison, ascending counted warriors (hosted guards not counted), then by enemy warriors next to them
        /// (more first); ties: lower clearing.</summary>
        [BotRule("bot.paintress.03-garrison")]
        public List<Ranked<ClearingId>> Garrison(BotView view)
        {
            var b = view.Board;
            var m = Monolith(view);
            var order = new List<ClearingId>();
            if (m.HasValue && (b.Warriors(view.Seat, m.Value) < Tuning.PaintressMonolithGarrison || MonolithThreatened(view))) order.Add(m.Value);
            var target = PortraitGarrison(view);
            order.AddRange(b.Clearings()
                .Where(c => PaintressPortraits.IsEasel(b, view.Seat, c, m))
                .Select(c => (c, w: PaintressPortraits.PortraitWarriors(b, view.State, view.Seat, c), threat: b.PathNeighbours(c).Sum(n => EnemyWarriors(view, n))))
                .Where(x => x.w < target)
                .OrderBy(x => x.w).ThenByDescending(x => x.threat).ThenBy(x => x.c.Value)
                .Select(x => x.c));
            return order.Select((c, i) => new Ranked<ClearingId>(c, order.Count - i)).ToList();
        }

        // ================================================================= 04 finish mode

        /// <summary>Step 4: in finish mode, exactly the cards that build and stock the third Portrait (one Build of the
        /// missing suit, or a bird, then Recruit or Move cards for the warriors it lacks), each checked by the lookahead; Easy
        /// adds one more card from her hand to the build column without the check. Battles only to retake rule of the
        /// Monolith or of an easel. Null outside finish mode.</summary>
        [BotRule("bot.paintress.04-finish")]
        public FinishPlan Finish(BotView view)
        {
            if (!InGoalMode(view)) return null;
            var b = view.Board;
            var me = view.Seat;
            var m = Monolith(view);
            var plan = new FinishPlan();
            if (m.HasValue && b.Ruler(m.Value) != me) plan.BattleClearings.Add(m.Value);
            plan.BattleClearings.AddRange(b.Clearings().Where(c => PaintressPortraits.IsEasel(b, me, c, m) && b.Ruler(c) != me));

            var missing = Missing(view);
            if (missing.Count == 0) return plan;
            var hand = b.HandSuits(me).ToList();
            var garrison = Tuning.PaintressPortraitWarriors;

            // The third Portrait: a thin easel of a missing suit if she has one, else the best site.
            var easel = b.Clearings().Where(c => PaintressPortraits.IsEasel(b, me, c, m) && b.SuitsOf(c).Any(missing.Contains))
                .OrderByDescending(c => PaintressPortraits.PortraitWarriors(b, view.State, me, c)).ThenBy(c => c.Value)
                .Select(c => (ClearingId?)c).FirstOrDefault();
            var target = easel ?? Site(view).Select(r => (ClearingId?)r.Item).FirstOrDefault();
            if (!target.HasValue) return plan;
            plan.Target = target;
            plan.MissingSuit = missing.Where(b.SuitsOf(target.Value).Contains).Select(s => (Suit?)s).FirstOrDefault();
            var suit = plan.MissingSuit ?? missing[0];

            bool TryAdd(DecreeCard card)
            {
                if (!hand.Contains(card.Suit)) return false;
                if (!Lookahead(view, plan.Cards.Concat(new[] { card }))) return false;
                hand.Remove(card.Suit);
                plan.Cards.Add(card);
                return true;
            }

            if (!easel.HasValue && !TryAdd(new DecreeCard(DecreeCard.Build, suit))) TryAdd(new DecreeCard(DecreeCard.Build, Suit.Bird));

            var need = Math.Max(0, garrison - PaintressPortraits.PortraitWarriors(b, view.State, me, target.Value));
            var perRecruit = b.EyrieLeader(me) == "charismatic" ? 2 : 1;
            while (need > 0 && (TryAdd(new DecreeCard(DecreeCard.Recruit, suit)) || TryAdd(new DecreeCard(DecreeCard.Recruit, Suit.Bird))))
                need -= perRecruit;
            foreach (var n in b.PathNeighbours(target.Value).OrderByDescending(n => b.Warriors(me, n)).ThenBy(n => n.Value))
            {
                if (need <= 0) break;
                if (b.Warriors(me, n) == 0) continue;
                foreach (var s in b.SuitsOf(n).Concat(new[] { Suit.Bird }))
                    if (TryAdd(new DecreeCard(DecreeCard.Move, s))) { need -= Math.Min(need, b.Warriors(me, n)); break; }
            }

            if (view.Difficulty == Difficulty.Easy && hand.Count > 0)
            {
                var extra = hand.OrderBy(s => s == Suit.Bird ? 1 : 0).ThenBy(s => (int)s).First();
                plan.Cards.Add(new DecreeCard(DecreeCard.Build, extra));
            }
            return plan;
        }

        // ================================================================= 05 repaint

        /// <summary>Step 5: where a destroyed Portrait is rebuilt: step 1's site score without the clearings in
        /// state.paintress.repaint-avoid (kept there for tune.paintress-repaint-avoid-turns of her turns).</summary>
        [BotRule("bot.paintress.05-repaint")]
        public List<Ranked<ClearingId>> Repaint(BotView view)
        {
            var avoid = view.State.PaintressRepaintAvoid;
            return Site(view).Where(r => !(avoid.TryGetValue(r.Item, out var left) && left > 0)).ToList();
        }

        // ================================================================= 06 the White-Haired Man last

        /// <summary>Step 6: the battles the steps above would take, in their order, except that every battle against the
        /// White-Haired Man's seat moves to the end (stable).</summary>
        [BotRule("bot.paintress.06-spare-white-haired-man")]
        public List<BattleTarget> SpareWhiteHairedMan(BotView view, IReadOnlyList<BattleTarget> battles)
        {
            var whm = view.SeatOf(Ids.Characters.WhiteHairedMan);
            if (!whm.HasValue) return battles.ToList();
            return battles.Where(t => t.Defender != whm.Value).Concat(battles.Where(t => t.Defender == whm.Value)).ToList();
        }

        // ================================================================= 07 spoiler layer

        /// <summary>The probability of each value of the lower of two 0-3 dice (Root's battle roll): 7/16, 5/16, 3/16, 1/16.</summary>
        private static readonly double[] LowDie = { 7 / 16.0, 5 / 16.0, 3 / 16.0, 1 / 16.0 };

        /// <summary>Expected hits she takes attacking <paramref name="defender"/> in a clearing: the defender deals the lower
        /// die, capped by its warriors (a Vagabond: its undamaged swords).</summary>
        public double ExpectedHitsTaken(BotView view, SeatId defender, ClearingId c)
        {
            var cap = view.IsVagabond(defender)
                ? view.Board.Items(defender).Count(i => i.Type == ItemType.Sword && !i.Damaged)
                : view.Board.Warriors(defender, c);
            var e = 0.0;
            for (var k = 0; k < LowDie.Length; k++) e += LowDie[k] * Math.Min(k, cap);
            return e;
        }

        /// <summary>Whether this is her own announce turn: her own win's gates and test pass on the board now, so her
        /// confirmation would land before the threat's.</summary>
        public bool OwnAnnounceTurn(BotView view)
        {
            var row = WinRow;
            if (view.Act < row.ActMin || view.Board.Vp(view.Seat) < row.VpMin) return false;
            if (row.OffWithDominance && view.Board.ActiveDominance(view.Seat).HasValue) return false;
            return PaintressPortraits.FamilyPortrait(view.Board, view.State, view.Seat, Monolith(view));
        }

        /// <summary>Spoiler threats, most urgent first: banners before one-step threats, then the seat whose next Birdsong
        /// comes first after the current seat, then higher VP. Easy sees banners only.</summary>
        public List<(SeatId seat, bool banner)> Threats(BotView view)
        {
            var b = view.Board;
            var seats = b.Seats();
            var current = seats.ToList().IndexOf(b.CurrentSeat());
            var list = new List<(SeatId seat, bool banner)>();
            foreach (var s in seats)
            {
                if (s == view.Seat || b.CoalitionPartner(s) == view.Seat || b.CoalitionPartner(view.Seat) == s) continue;
                var win = view.WinOf(s);
                if (win == null || view.Act < win.ActMin) continue;
                if (win.OffWithDominance && b.ActiveDominance(s).HasValue) continue;
                if (win.OffInCoalition && b.CoalitionPartner(s).HasValue) continue;
                var banner = view.Countdowns.Contains(s);
                if (!banner && (view.Difficulty == Difficulty.Easy || view.UnitsMissing(s) != 1)) continue;
                list.Add((s, banner));
            }
            int Next(SeatId s)
            {
                var i = seats.ToList().IndexOf(s);
                var d = (i - current + seats.Count) % Math.Max(1, seats.Count);
                return d == 0 ? seats.Count : d;
            }
            return list.OrderBy(t => t.banner ? 0 : 1).ThenBy(t => Next(t.seat)).ThenByDescending(t => b.Vp(t.seat)).ToList();
        }

        /// <summary>Step 7, the shared spoiler layer: for the most urgent threat, the cheapest answer in one of that win's key
        /// clearings (cost = actions this turn + expected hits taken), breaking a met part before blocking a missing unit
        /// (the win's key order), then the answer nearest her pieces, then the lower clearing. She attacks the White-Haired Man
        /// like anyone else here. Pre-empts steps 1-6 for a banner (not on Easy) and, on Hard only, for a one-step threat unless
        /// this is her own announce turn. A Decree battle card is offered only when the lookahead passes. Null with no trigger.</summary>
        [BotRule("bot.paintress.07-spoiler")]
        public SpoilerAnswer Spoiler(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            foreach (var (threat, banner) in Threats(view))
            {
                var keys = view.KeyClearings(threat).ToList();
                if (keys.Count == 0)
                    keys = b.Clearings().Where(c => b.Warriors(threat, c) + b.Buildings(threat, c) + b.Tokens(threat, c) > 0 || b.PawnClearing(threat) == c).ToList();
                var answers = new List<(SpoilerAnswer a, int keyIndex, int distance)>();
                for (var i = 0; i < keys.Count; i++)
                {
                    var c = keys[i];
                    var present = b.Warriors(threat, c) + b.Buildings(threat, c) + b.Tokens(threat, c) > 0 || b.PawnClearing(threat) == c;
                    if (!present) continue;
                    var hits = ExpectedHitsTaken(view, threat, c);
                    if (b.Warriors(me, c) > 0)
                    {
                        answers.Add((new SpoilerAnswer { Threat = threat, Banner = banner, Clearing = c, Cost = 1 + hits }, i, 0));
                        continue;
                    }
                    var from = b.PathNeighbours(c).Where(n => b.Warriors(me, n) > 0)
                        .OrderByDescending(n => b.Warriors(me, n)).ThenBy(n => n.Value).Select(n => (ClearingId?)n).FirstOrDefault();
                    if (from.HasValue)
                        answers.Add((new SpoilerAnswer { Threat = threat, Banner = banner, Clearing = c, MoveFrom = from, Cost = 2 + hits }, i, 1));
                }
                if (answers.Count == 0) continue;
                var best = answers.OrderBy(x => x.a.Cost).ThenBy(x => x.keyIndex).ThenBy(x => x.distance).ThenBy(x => x.a.Clearing.Value).First().a;
                best.PreEmpts = banner
                    ? view.Difficulty != Difficulty.Easy
                    : view.Difficulty == Difficulty.Hard && !OwnAnnounceTurn(view);
                foreach (var s in b.SuitsOf(best.Clearing).Concat(new[] { Suit.Bird }))
                {
                    var card = new DecreeCard(DecreeCard.Battle, s);
                    if (b.HandSuits(me).Contains(s) && Lookahead(view, new[] { card })) { best.Card = card; break; }
                }
                return best;
            }
            return null;
        }

        // ================================================================= decisions

        /// <summary>hook.bot-decision: the spoiler layer when it pre-empts, then steps 1-6 in order, then the spoiler layer
        /// with the actions they leave free; null lets Root's own AI decide.</summary>
        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            var legal = new HashSet<string>(legalActions ?? new string[0], StringComparer.Ordinal);
            if (legal.Count == 0) return null;
            var spoiler = Spoiler(view);
            if (spoiler != null && spoiler.PreEmpts)
            {
                var k = SpoilerKey(spoiler, legal);
                if (k != null) return k;
            }
            var goal = GoalKey(view, legalActions, legal);
            if (goal != null) return goal;
            return spoiler != null ? SpoilerKey(spoiler, legal) : null;
        }

        private static string SpoilerKey(SpoilerAnswer a, HashSet<string> legal)
        {
            if (a.Card.HasValue && legal.Contains(PaintressActionKey.DecreeKey(a.Card.Value))) return PaintressActionKey.DecreeKey(a.Card.Value);
            if (!a.MoveFrom.HasValue)
            {
                var key = PaintressActionKey.BattleKey(a.Clearing, a.Threat);
                return legal.Contains(key) ? key : null;
            }
            return legal.Select(k => (k, m: PaintressActionKey.ParseMove(k)))
                .Where(x => x.m.HasValue && x.m.Value.from == a.MoveFrom.Value && x.m.Value.to == a.Clearing)
                .Select(x => x.k).FirstOrDefault();
        }

        /// <summary>Steps 1-6 for one decision.</summary>
        private string GoalKey(BotView view, IReadOnlyList<string> ordered, HashSet<string> legal)
        {
            var b = view.Board;
            var me = view.Seat;
            var m = Monolith(view);
            var finish = Finish(view);

            // 02 / 04: cards for the Decree.
            foreach (var card in finish != null ? finish.Cards : Decree(view))
            {
                var key = PaintressActionKey.DecreeKey(card);
                if (legal.Contains(key)) return key;
            }

            // 01 / 05: build the next easel at the best site (avoided clearings skipped).
            if (finish?.Target is ClearingId target && legal.Contains(PaintressActionKey.BuildKey(target))) return PaintressActionKey.BuildKey(target);
            foreach (var r in Repaint(view))
                if (legal.Contains(PaintressActionKey.BuildKey(r.Item))) return PaintressActionKey.BuildKey(r.Item);

            // 03: recruit and move to garrison the Monolith and the Portraits.
            var garrison = Garrison(view).Select(r => r.Item).ToList();
            if (m.HasValue && MonolithThreatened(view) && legal.Contains(PaintressActionKey.RecruitKey(m.Value))) return PaintressActionKey.RecruitKey(m.Value);
            foreach (var c in garrison)
                if (legal.Contains(PaintressActionKey.RecruitKey(c))) return PaintressActionKey.RecruitKey(c);
            foreach (var c in garrison)
            {
                var move = ordered.Select(k => (k, mv: PaintressActionKey.ParseMove(k)))
                    .Where(x => x.mv.HasValue && x.mv.Value.to == c && Spare(view, x.mv.Value.from) >= x.mv.Value.count)
                    .OrderBy(x => x.mv.Value.from.Value).Select(x => x.k).FirstOrDefault();
                if (move != null) return move;
            }

            // 04 / 06: battles.
            var battles = ordered.Select(PaintressActionKey.ParseBattle).Where(t => t.HasValue).Select(t => t.Value).ToList();
            if (battles.Count == 0) return null;
            List<BattleTarget> wanted;
            if (finish != null) wanted = battles.Where(t => finish.BattleClearings.Contains(t.Clearing)).ToList();
            else
            {
                var retake = new List<ClearingId>();
                if (m.HasValue && b.Ruler(m.Value) != me) retake.Add(m.Value);
                retake.AddRange(b.Clearings().Where(c => PaintressPortraits.IsEasel(b, me, c, m) && b.Ruler(c) != me));
                retake.AddRange(Site(view).Select(r => r.Item).Where(c => !retake.Contains(c)));
                wanted = battles.Where(t => retake.Contains(t.Clearing)).OrderBy(t => retake.IndexOf(t.Clearing)).ToList();
                // A battle card in the Decree must be resolved: when only battles are legal, every one of them is a step choice.
                if (battles.Count == legal.Count) wanted.AddRange(battles.Where(t => !wanted.Contains(t)));
            }
            var pick = SpareWhiteHairedMan(view, wanted).Select(t => (BattleTarget?)t).FirstOrDefault();
            return pick.HasValue ? PaintressActionKey.BattleKey(pick.Value.Clearing, pick.Value.Defender) : null;
        }

        /// <summary>Warriors she can move out of a clearing without leaving a Portrait or the Monolith under its garrison.</summary>
        private int Spare(BotView view, ClearingId c)
        {
            var b = view.Board;
            var m = Monolith(view);
            var w = PaintressPortraits.PortraitWarriors(b, view.State, view.Seat, c);
            if (m.HasValue && c == m.Value) return Math.Max(0, w - Tuning.PaintressMonolithGarrison);
            if (PaintressPortraits.IsEasel(b, view.Seat, c, m)) return Math.Max(0, w - PortraitGarrison(view));
            return w;
        }
    }
}
