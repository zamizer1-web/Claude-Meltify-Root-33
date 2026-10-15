using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Maelle
{
    /// <summary>A place to Aid a family faction (bot.maelle.02-aid).</summary>
    public sealed class AidTarget
    {
        public SeatId Faction;
        public ClearingId Clearing;
        public int Moves;
        public override string ToString() => Faction + "@" + Clearing + " (" + Moves + " moves)";
    }

    /// <summary>The goal-mode step this turn (bot.maelle.04-goal-mode): stay in Lumière, rest in a forest, or move.</summary>
    public sealed class GoalStep
    {
        public bool Stay;
        public bool Rest;
        public ClearingId? ToClearing;
        public int? ToForest;
        public override string ToString() => Stay ? "stay" : Rest ? "rest" : ToForest.HasValue ? "f" + ToForest : ToClearing?.ToString() ?? "-";
    }

    public sealed class MaelleThreat
    {
        public SeatId Seat;
        public bool Banner;
        public IReadOnlyList<ClearingId> KeyClearings = new ClearingId[0];
        public override string ToString() => Seat + (Banner ? " banner" : " one-step");
    }

    public sealed class MaelleAnswer
    {
        public MaelleThreat Threat;
        public string ActionKey;
        public bool Preempts;
        public double Cost;
        public override string ToString() => ActionKey + (Preempts ? " (pre-empts)" : " (free)");
    }

    /// <summary>
    /// bot.maelle: layer 2 of the Maelle bot. Root's own AI keeps hit allocation, Ambush and discards and plays every
    /// decision no step claims. Steps in priority order (bot.maelle.01..06), each one [BotRule] method that reads only the
    /// <see cref="BotView"/> (public board, her own hand) and returns a ranking or a decision; only
    /// <see cref="PlanTurn"/> writes state (state.maelle.family).
    /// </summary>
    public sealed class MaelleBot : IBotDirector
    {

        public string PlanId => Ids.BotPlans.Maelle;

        /// <summary>goalModeTrigger (all difficulties): the relationship parts of win.maelle hold.</summary>
        public bool InGoalMode(BotView view) => MaelleRules.RelationshipsHold(view.Board, view.Seat);

        /// <summary>Her Birdsong: keep or re-pick the family (state.maelle.family).</summary>
        public void PlanTurn(BotView view, ModState state)
        {
            var pick = FamilyPick(view);
            state.MaelleFamily = new HashSet<SeatId>(pick);
        }

        // ================================================================= decision

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var acts = MaelleAction.ParseAll(legalActions).Where(a => Allowed(view, a)).ToList();
            if (acts.Count == 0) return null;
            var spoiler = Spoiler(view, legalActions);
            if (spoiler != null && spoiler.Preempts) return spoiler.ActionKey;
            return StepAction(view, acts) ?? spoiler?.ActionKey;
        }

        /// <summary>Never a battle step 3 forbids; never a move out of Lumière while goal mode holds there.</summary>
        private bool Allowed(BotView view, MaelleAction a)
        {
            if (a.Verb == MaelleAction.Battle && a.Target.HasValue && !MayBattle(view, a.Target.Value)) return false;
            if (a.IsMove && GoalModeActive(view) && MaelleRules.InLumiere(view.Board, view.Seat, view.Role(Ids.MapRoles.Lumiere))) return false;
            return true;
        }

        /// <summary>Steps 2-5 in order; the first that claims the decision takes it (step 1 is planned at Birdsong).</summary>
        public string StepAction(BotView view, List<MaelleAction> acts)
        {
            var b = view.Board;
            var me = view.Seat;

            // 2 Aid: crafted items offered (swords, boots, others), Aid in place, or move toward the best Aid clearing.
            var take = acts.Where(a => a.Verb == MaelleAction.Take && a.Item.HasValue)
                .OrderByDescending(a => ItemScore(a.Item.Value)).ThenBy(a => (int)a.Item.Value).FirstOrDefault();
            if (take != null) return take.Key;
            foreach (var t in Aid(view))
            {
                if (t.Item.Moves == 0)
                {
                    var aid = acts.Where(a => a.Verb == MaelleAction.Aid && a.Clearing == t.Item.Clearing && (!a.Target.HasValue || a.Target == t.Item.Faction))
                        .OrderByDescending(a => a.Item.HasValue ? ItemScore(a.Item.Value) : -1).FirstOrDefault();
                    if (aid != null) return aid.Key;
                    continue;
                }
                var step = MoveToward(view, acts, t.Item.Clearing);
                if (step != null) return step;
            }

            // 3 No fights: battle only a faction near Root's win; Disarm the VP leader; ruins and quests for VP.
            var near = acts.Where(a => a.Verb == MaelleAction.Battle && a.Target.HasValue && NearRootWin(b, a.Target.Value))
                .OrderByDescending(a => b.Vp(a.Target.Value)).FirstOrDefault();
            if (near != null) return near.Key;
            foreach (var r in NoFights(view))
            {
                var steal = acts.FirstOrDefault(a => a.Verb == MaelleAction.Steal && a.Target == r.Item && a.Clearing == b.PawnClearing(me));
                if (steal != null) return steal.Key;
                var there = b.Clearings().Where(c => HasPieces(b, r.Item, c)).OrderBy(c => c.Value);
                foreach (var c in there)
                {
                    var mv = acts.FirstOrDefault(a => a.IsMove && a.To == c);
                    if (mv != null) return mv.Key;
                }
                break;
            }
            var vpAct = acts.FirstOrDefault(a => a.Verb == MaelleAction.Quest) ?? acts.FirstOrDefault(a => a.Verb == MaelleAction.Explore);
            if (vpAct != null) return vpAct.Key;

            // 4 Goal mode (5: on a one-faction table only once she has tune.maelle-2p-vp).
            if (GoalModeActive(view))
            {
                var g = GoalMode(view);
                if (g != null)
                {
                    if (g.Stay || g.Rest)
                    {
                        var stay = acts.FirstOrDefault(a => g.Rest && a.Verb == MaelleAction.Rest) ?? acts.FirstOrDefault(a => a.IsStay);
                        if (stay != null) return stay.Key;
                    }
                    else if (g.ToForest.HasValue)
                    {
                        var mv = acts.FirstOrDefault(a => a.IsMove && a.ToForest == g.ToForest);
                        if (mv != null) return mv.Key;
                    }
                    else if (g.ToClearing.HasValue)
                    {
                        var mv = acts.FirstOrDefault(a => a.IsMove && a.To == g.ToClearing);
                        if (mv != null) return mv.Key;
                    }
                }
            }
            return null;
        }

        // ================================================================= step 1: Family

        /// <summary>
        /// Family candidates: non-Hostile, non-Vagabond factions, closest to Allied first (Allied 3, Friendly 2, Amiable 1,
        /// Indifferent 0); ties: lower VP; the VP leader goes last whenever another candidate exists.
        /// </summary>
        [BotRule("bot.maelle.01-family")]
        public List<Ranked<SeatId>> Family(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var cands = MaelleRules.Factions(b, me).Where(o => b.RelationshipOf(me, o) != Relationship.Hostile).ToList();
            var others = b.Seats().Where(o => o != me).ToList();
            var top = others.Count == 0 ? 0 : others.Max(o => b.Vp(o));
            return Rank.Descending(cands,
                o => (b.Vp(o) == top ? -1000000 : 0) + Steps(b.RelationshipOf(me, o)) * 1000 - b.Vp(o),
                o => o.Value);
        }

        /// <summary>The family to keep: the saved one, unless a member passed tune.maelle-family-repick-vp VP (full
        /// re-pick) or a member is gone or Hostile (filled from the ranking). Size: tune.maelle-allied, or 1 on a
        /// one-faction table (step 5).</summary>
        public List<SeatId> FamilyPick(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var ranking = Family(view).Select(r => r.Item).ToList();
            var size = Math.Min(TwoPlayer(view) ? 1 : Tuning.MaelleAllied, ranking.Count);
            var current = view.State.MaelleFamily.Where(ranking.Contains).OrderBy(s => s.Value).ToList();
            if (current.Any(s => b.Vp(s) > Tuning.MaelleFamilyRepickVp)) return ranking.Take(size).ToList();
            if (current.Count > size) current = current.Take(size).ToList();
            foreach (var s in ranking)
            {
                if (current.Count >= size) break;
                if (!current.Contains(s)) current.Add(s);
            }
            return current;
        }

        public static int Steps(Relationship r) =>
            r == Relationship.Allied ? 3 : r == Relationship.Friendly ? 2 : r == Relationship.Amiable ? 1 : 0;

        // ================================================================= step 2: Aid

        /// <summary>
        /// Where to Aid: clearings where a family faction has pieces and whose suit matches a card in her hand (birds are
        /// wild); the family member with fewer relationship steps first, then fewest moves. Easy Aids once per turn. While
        /// goal mode holds she only Aids where she stands.
        /// </summary>
        [BotRule("bot.maelle.02-aid")]
        public List<Ranked<AidTarget>> Aid(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var list = new List<AidTarget>();
            if (view.Difficulty == Difficulty.Easy && view.State.MaelleStanceTurn.Contains(MaelleTweaks.Aided)) return new List<Ranked<AidTarget>>();
            var hand = b.HandSuits(me);
            var moves = MovesFrom(view);
            var goal = GoalModeActive(view);
            foreach (var f in view.State.MaelleFamily.OrderBy(s => s.Value))
            {
                if (b.RelationshipOf(me, f) == Relationship.Hostile) continue;
                foreach (var c in b.Clearings())
                {
                    if (!HasPieces(b, f, c) || !moves.TryGetValue(c, out var m)) continue;
                    if (goal && m > 0) continue;
                    if (!hand.Contains(Suit.Bird) && !b.SuitsOf(c).Any(hand.Contains)) continue;
                    list.Add(new AidTarget { Faction = f, Clearing = c, Moves = m });
                }
            }
            return list.Select(t => new Ranked<AidTarget>(t, (3 - Steps(b.RelationshipOf(me, t.Faction))) * 1000 - t.Moves))
                .OrderByDescending(r => r.Score).ThenBy(r => r.Item.Clearing.Value).ThenBy(r => r.Item.Faction.Value).ToList();
        }

        /// <summary>Crafted items taken when offered: swords, then boots, then any other.</summary>
        public static List<Ranked<ItemType>> ItemPreference(IEnumerable<ItemType> offered) =>
            Rank.Descending(offered.Distinct(), ItemScore, i => (int)i);

        private static double ItemScore(ItemType i) => i == ItemType.Sword ? 2 : i == ItemType.Boots ? 1 : 0;

        // ================================================================= step 3: No fights

        /// <summary>
        /// Disarm (Steal) targets: seats with pieces in her clearing or one move away and cards in hand; the VP leader
        /// first, ties: more cards in hand.
        /// </summary>
        [BotRule("bot.maelle.03-no-fights")]
        public List<Ranked<SeatId>> NoFights(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var moves = MovesFrom(view);
            var near = moves.Where(kv => kv.Value <= 1).Select(kv => kv.Key).ToList();
            var cands = b.Seats().Where(o => o != me && b.HandSize(o) > 0 && near.Any(c => HasPieces(b, o, c))).ToList();
            return Rank.Descending(cands, o => b.Vp(o) * 1000 + b.HandSize(o), o => o.Value);
        }

        /// <summary>She battles a faction only if it is Hostile or within tune.maelle-threat-vp VP of Root's 30 VP win.</summary>
        public bool MayBattle(BotView view, SeatId defender) =>
            view.Board.RelationshipOf(view.Seat, defender) == Relationship.Hostile || NearRootWin(view.Board, defender);

        public static bool NearRootWin(IBoardView b, SeatId s) => b.Vp(s) >= Tuning.RootWinVp - Tuning.MaelleThreatVp;

        // ================================================================= step 4: Goal mode

        /// <summary>
        /// Shortest path to Lumière and stay there. With a damaged item (not on Easy) the turn before the last move ends in
        /// a forest beside the path, where she rests (repairs) before arriving.
        /// </summary>
        [BotRule("bot.maelle.04-goal-mode")]
        public GoalStep GoalMode(BotView view)
        {
            if (!InGoalMode(view)) return null;
            var b = view.Board;
            var me = view.Seat;
            var lumiere = view.Role(Ids.MapRoles.Lumiere);
            if (!lumiere.HasValue) return null;
            var repair = view.Difficulty != Difficulty.Easy && b.Items(me).Any(i => i.Damaged);
            var pawn = b.PawnClearing(me);
            if (pawn == lumiere.Value) return new GoalStep { Stay = true };
            var dist = Graph.Distances(lumiere.Value, b.PathNeighbours);
            var forest = b.PawnForest(me);
            if (forest.HasValue)
            {
                if (repair) return new GoalStep { Rest = true };
                var exit = b.ForestClearings(forest.Value).Where(dist.ContainsKey).OrderBy(c => dist[c]).ThenBy(c => c.Value).ToList();
                return exit.Count == 0 ? null : new GoalStep { ToClearing = exit[0] };
            }
            if (!pawn.HasValue || !dist.TryGetValue(pawn.Value, out var d)) return null;
            if (d == 1 && repair)
            {
                var around = b.ForestsAround(pawn.Value);
                var f = around.Where(x => b.ForestsAround(lumiere.Value).Contains(x)).OrderBy(x => x).Select(x => (int?)x).FirstOrDefault()
                        ?? around.OrderBy(x => x).Select(x => (int?)x).FirstOrDefault();
                if (f.HasValue) return new GoalStep { ToForest = f };
            }
            var next = b.PathNeighbours(pawn.Value).Where(n => dist.TryGetValue(n, out var dn) && dn == d - 1).OrderBy(n => n.Value).ToList();
            return next.Count == 0 ? null : new GoalStep { ToClearing = next[0] };
        }

        /// <summary>Goal mode drives her moves: the relationships hold and (step 5) a one-faction table has its VP.</summary>
        public bool GoalModeActive(BotView view) =>
            InGoalMode(view) && !(TwoPlayer(view) && view.Board.Vp(view.Seat) < Tuning.Maelle2pVp);

        // ================================================================= step 5: One-faction table

        /// <summary>Only one non-Vagabond faction seated: one family faction, then push to tune.maelle-2p-vp VP
        /// (<see cref="FamilyPick"/>, <see cref="GoalModeActive"/>).</summary>
        [BotRule("bot.maelle.05-two-player")]
        public bool TwoPlayer(BotView view) => MaelleRules.Factions(view.Board, view.Seat).Count == 1;

        // ================================================================= step 6: Spoiler layer

        /// <summary>
        /// The shared spoiler layer, bound by step 3: she battles a non-Hostile threat only near Root's win. Answers: battle
        /// the threat in a key clearing (breaks a met part), walk her pawn in (the Monolith against the White-Haired Man or
        /// before Verso's banner; a key clearing before Sciel's banner), or Disarm the threat's seat; then the cheapest,
        /// ties nearest then the lower clearing. Pre-empts the steps for a banner (not on Easy) and, on Hard, for a one-step
        /// threat unless this is her own announce turn.
        /// </summary>
        [BotRule("bot.maelle.06-spoiler")]
        public MaelleAnswer Spoiler(BotView view, IReadOnlyList<string> legal = null)
        {
            var acts = MaelleAction.ParseAll(legal ?? view.Board.LegalActions(view.Seat));
            if (acts.Count == 0) return null;
            foreach (var threat in Threats(view))
            {
                var answer = Answer(view, threat, acts);
                if (answer == null) continue;
                answer.Preempts = threat.Banner
                    ? view.Difficulty != Difficulty.Easy
                    : view.Difficulty == Difficulty.Hard && !IsAnnounceTurn(view);
                return answer;
            }
            return null;
        }

        /// <summary>Other seats (not her coalition partner) whose win is unlocked and not switched off, with a banner up or
        /// one step away (Easy: banners only); banners first, then the next Birdsong in turn order, then higher VP.</summary>
        public List<MaelleThreat> Threats(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var seats = b.Seats();
            var cur = IndexOf(seats, b.CurrentSeat());
            var list = new List<(MaelleThreat t, int dist, int vp)>();
            for (var i = 0; i < seats.Count; i++)
            {
                var o = seats[i];
                if (o == me || b.CoalitionPartner(o) == me || b.CoalitionPartner(me) == o) continue;
                var ch = view.CharacterOf(o);
                if (ch == null || !Data.Characters.TryGetValue(ch, out var chRow)) continue;
                if (!Data.WinConditions.TryGetValue(chRow.WinCondition ?? "", out var row)) continue;
                if (view.Act < row.ActMin) continue;
                if (row.OffWithDominance && b.ActiveDominance(o).HasValue) continue;
                if (row.OffInCoalition && b.CoalitionPartner(o).HasValue) continue;
                var banner = view.Countdowns.Contains(o);
                if (!banner && (view.Difficulty == Difficulty.Easy || view.UnitsMissing(o) != 1)) continue;
                var keys = view.KeyClearings(o);
                if (keys.Count == 0) keys = FallbackKeyClearings(view, o, ch);
                var dist = cur < 0 ? i : (i - cur + seats.Count) % seats.Count;
                if (dist == 0) dist = seats.Count;
                list.Add((new MaelleThreat { Seat = o, Banner = banner, KeyClearings = keys }, dist, b.Vp(o)));
            }
            return list.OrderBy(x => x.t.Banner ? 0 : 1).ThenBy(x => x.dist).ThenByDescending(x => x.vp).Select(x => x.t).ToList();
        }

        /// <summary>Key clearings from each win's counterplay for wins that do not report their own (public reads only).</summary>
        public static IReadOnlyList<ClearingId> FallbackKeyClearings(BotView view, SeatId o, string character)
        {
            var b = view.Board;
            var keys = new List<ClearingId>();
            void Add(ClearingId? c) { if (c.HasValue && !keys.Contains(c.Value)) keys.Add(c.Value); }
            var monolith = view.Role(Ids.MapRoles.Monolith);
            switch (character)
            {
                case CharacterIds.Gustave: foreach (var c in view.State.GustaveFlags.OrderBy(c => c.Value)) Add(c); break;
                case CharacterIds.Esquie:
                case CharacterIds.Monoco: Add(b.PawnClearing(o)); break;
                case CharacterIds.Paintress:
                    foreach (var c in b.Clearings().Where(c => b.BuildingsOfType(o, c, PieceTypes.Roost) > 0)) Add(c);
                    Add(monolith);
                    break;
                case CharacterIds.Verso: Add(monolith); break;
                case CharacterIds.Curator: Add(view.Role(Ids.MapRoles.Lumiere)); Add(monolith); break;
                case CharacterIds.Lune: if (view.State.LuneTaggedRelic.HasValue) Add(b.RelicClearing(view.State.LuneTaggedRelic.Value)); break;
                case CharacterIds.WhiteHairedMan: Add(monolith); foreach (var c in view.MonolithNeighbours()) Add(c); break;
                case CharacterIds.Sciel: foreach (var c in b.Clearings().Where(c => b.TokensOfType(o, c, PieceTypes.Sympathy) > 0)) Add(c); break;
                default: Add(b.PawnClearing(o)); break;
            }
            return keys;
        }

        private MaelleAnswer Answer(BotView view, MaelleThreat threat, List<MaelleAction> acts)
        {
            var b = view.Board;
            var me = view.Seat;
            var keys = new HashSet<ClearingId>(threat.KeyClearings);
            var ch = view.CharacterOf(threat.Seat);
            var monolith = view.Role(Ids.MapRoles.Monolith);
            var walkTo = new HashSet<ClearingId>();
            if (monolith.HasValue && (ch == CharacterIds.WhiteHairedMan || (ch == CharacterIds.Verso && !threat.Banner))) walkTo.Add(monolith.Value);
            if (ch == CharacterIds.Sciel && !threat.Banner) walkTo.UnionWith(keys);
            var pawn = b.PawnClearing(me);
            var options = new List<(MaelleAction a, int kind, double cost, int near, int c)>();
            foreach (var a in acts)
            {
                if (a.Verb == MaelleAction.Battle && a.Clearing.HasValue && keys.Contains(a.Clearing.Value) && a.Target == threat.Seat
                    && MayBattle(view, threat.Seat))
                    options.Add((a, 0, 1 + ExpectedHitsTaken(b.Warriors(threat.Seat, a.Clearing.Value)), 0, a.Clearing.Value.Value));
                else if (a.IsMove && a.To.HasValue && walkTo.Contains(a.To.Value))
                    options.Add((a, 1, 1, 1, a.To.Value.Value));
                else if (a.Verb == MaelleAction.Steal && a.Target == threat.Seat)
                    options.Add((a, 2, 1, a.Clearing == pawn ? 0 : 1, a.Clearing?.Value ?? 0));
            }
            if (options.Count == 0) return null;
            var best = options.OrderBy(o => o.kind).ThenBy(o => o.cost).ThenBy(o => o.near).ThenBy(o => o.c).First();
            return new MaelleAnswer { Threat = threat, ActionKey = best.a.Key, Cost = best.cost };
        }

        /// <summary>Her own announce turn: the whole win.maelle test (with its gates) holds on the current board.</summary>
        public static bool IsAnnounceTurn(BotView view)
        {
            var b = view.Board;
            return view.Act >= 2 && !b.CoalitionPartner(view.Seat).HasValue
                   && MaelleRules.Test(b, view.Seat, view.Role(Ids.MapRoles.Lumiere));
        }

        /// <summary>Expected hits the attacker takes: the defender deals the lower of two 0-3 dice, capped by its warriors.</summary>
        public static double ExpectedHitsTaken(int defenderWarriors)
        {
            var e = 0.0;
            for (var k = 1; k <= Math.Min(defenderWarriors, 3); k++) e += Math.Pow((4.0 - k) / 4.0, 2);
            return e;
        }

        // ================================================================= helpers

        /// <summary>Moves from her pawn to every clearing (from a forest: 1 to each clearing around it).</summary>
        public static Dictionary<ClearingId, int> MovesFrom(BotView view)
        {
            var b = view.Board;
            var pawn = b.PawnClearing(view.Seat);
            if (pawn.HasValue) return Graph.Distances(pawn.Value, b.PathNeighbours);
            var result = new Dictionary<ClearingId, int>();
            var forest = b.PawnForest(view.Seat);
            if (!forest.HasValue) return result;
            foreach (var start in b.ForestClearings(forest.Value))
                foreach (var kv in Graph.Distances(start, b.PathNeighbours))
                    if (!result.TryGetValue(kv.Key, out var old) || kv.Value + 1 < old) result[kv.Key] = kv.Value + 1;
            return result;
        }

        private static string MoveToward(BotView view, List<MaelleAction> acts, ClearingId target)
        {
            var dist = Graph.Distances(target, view.Board.PathNeighbours);
            return acts.Where(a => a.IsMove && a.To.HasValue && dist.ContainsKey(a.To.Value))
                .OrderBy(a => dist[a.To.Value]).ThenBy(a => a.To.Value.Value).FirstOrDefault()?.Key;
        }

        private static bool HasPieces(IBoardView b, SeatId s, ClearingId c) => b.Warriors(s, c) + b.Buildings(s, c) + b.Tokens(s, c) > 0;

        private static int IndexOf(IReadOnlyList<SeatId> seats, SeatId s)
        {
            for (var i = 0; i < seats.Count; i++) if (seats[i] == s) return i;
            return -1;
        }
    }
}
