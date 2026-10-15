using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Lune
{
    /// <summary>One card to add to a Retinue column ('move', 'battle-delve', 'move-recover').</summary>
    public readonly struct RetinueCard : IEquatable<RetinueCard>
    {
        public readonly string Column;
        public readonly Suit Suit;
        public RetinueCard(string column, Suit suit) { Column = column; Suit = suit; }
        public string Key => LuneAction.RetinueKey(Column, Suit);
        public bool Equals(RetinueCard o) => Column == o.Column && Suit == o.Suit;
        public override bool Equals(object obj) => obj is RetinueCard o && Equals(o);
        public override int GetHashCode() => (Column ?? "").GetHashCode() * 7 + (int)Suit;
        public override string ToString() => Key;
    }

    /// <summary>bot.lune.05-escort: how one carried relic goes home.</summary>
    public sealed class EscortPlan
    {
        public const string RecoverFinish = "recover", FlipFinish = "flip", BuildFinish = "build";
        public int Relic;
        public string RelicType;
        public ClearingId From;
        /// <summary>The route, start included; the last clearing is the destination.</summary>
        public List<ClearingId> Route = new List<ClearingId>();
        public ClearingId Destination => Route[Route.Count - 1];
        /// <summary>Recover at a waystation of the relic's type, or flip / build one there first.</summary>
        public string Finish;
        /// <summary>Warriors the convoy moves with (tune.lune-convoy-size, Easy 1).</summary>
        public int ConvoySize;
        /// <summary>Enough warriors stand with the carrier to set off.</summary>
        public bool Ready;
        public override string ToString() => Relic + ": " + string.Join(">", Route) + " " + Finish;
    }

    public sealed class LuneThreat
    {
        public SeatId Seat;
        public bool Banner;
        public IReadOnlyList<ClearingId> KeyClearings = new ClearingId[0];
        public override string ToString() => Seat + (Banner ? " banner" : " one-step");
    }

    public sealed class LuneAnswer
    {
        public LuneThreat Threat;
        public string ActionKey;
        public bool Preempts;
        public double Cost;
        public override string ToString() => ActionKey + (Preempts ? " (pre-empts)" : " (free)");
    }

    /// <summary>
    /// bot.lune: layer 2 of the Lune (Keepers) bot. Root's own AI keeps hit allocation, Ambush and discards and plays every
    /// decision no step claims. Steps in priority order (bot.lune.01..07), each one [BotRule] method that reads only the
    /// <see cref="BotView"/> (public board, her own hand) and returns a ranking or a decision. The plan keeps no state of
    /// its own, so <see cref="PlanTurn"/> writes nothing.
    /// </summary>
    public sealed class LuneBot : IBotDirector
    {
        private const int Far = 1000;

        public string PlanId => Ids.BotPlans.Lune;

        /// <summary>goalModeTrigger (all difficulties): Act II, once the Parents' Journal is tagged.</summary>
        public bool InGoalMode(BotView view) => view.Act >= 2 && view.State.LuneTaggedRelic.HasValue;

        public void PlanTurn(BotView view, ModState state) { }

        // ================================================================= decision

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var acts = LuneAction.ParseAll(legalActions);
            var finish = FinishingRecover(view, acts);
            if (finish != null) return finish.Key;
            var spoiler = Spoiler(view, legalActions);
            if (spoiler != null && spoiler.Preempts) return spoiler.ActionKey;
            return StepAction(view, acts) ?? spoiler?.ActionKey;
        }

        /// <summary>Steps 1-6 in order; the first that claims the decision takes it.</summary>
        public string StepAction(BotView view, List<LuneAction> acts)
        {
            var b = view.Board;
            var goals = Goals(view);
            var goalSet = new HashSet<int>(goals);

            // 1/2: recover a goal relic where she stands.
            var rec = acts.FirstOrDefault(a => a.Verb == LuneAction.Recover && a.Relic.HasValue && goalSet.Contains(a.Relic.Value));
            if (rec != null) return rec.Key;

            // 3: Retinue cards for the route.
            foreach (var card in Retinue(view))
                if (acts.Any(a => a.Key == card.Key)) return card.Key;

            // 4 + 6: delve goal relics only (the Journal only with its escort); duplicates are skipped.
            foreach (var g in goals)
            {
                if (!b.RelicForest(g).HasValue) continue;
                if (g == view.State.LuneTaggedRelic && !Delve(view)) continue;
                var delve = acts.FirstOrDefault(a => a.Verb == LuneAction.Delve && a.Relic == g);
                if (delve != null) return delve.Key;
            }

            // 5: escort carriers home.
            foreach (var plan in Escort(view, Carried(view, acts)))
            {
                var k = EscortStep(view, plan, acts);
                if (k != null) return k;
            }

            // 1/2: march toward the nearest goal forest without stripping a convoy.
            foreach (var g in goals)
            {
                var forest = b.RelicForest(g);
                if (!forest.HasValue) continue;
                var touching = new HashSet<ClearingId>(b.ForestClearings(forest.Value));
                var path = LuneRelics.ShortestPath(b, MyWarriorClearings(view), touching.Contains);
                if (path == null || path.Count < 2) break;
                var mv = acts.Where(a => a.Verb == LuneAction.Move && a.Clearing == path[0] && a.To == path[1] && KeepsConvoy(view, path[0], a.Count))
                    .OrderByDescending(a => a.Count).FirstOrDefault();
                if (mv != null) return mv.Key;
                break;
            }
            return null;
        }

        /// <summary>The goal relics of the current act: step 1 in Act I, step 2 from Act II.</summary>
        public List<int> Goals(BotView view) => (view.Act >= 2 ? Act2Goals(view) : Act1(view)).Select(r => r.Item).ToList();

        // ================================================================= step 1: Act I relics

        /// <summary>
        /// In Act I: relics in forests whose type she has not recovered, nearest first (path distance from her nearest
        /// warriors to a clearing touching the relic's forest); ties: higher value among face-up relics only (face-down
        /// relics rank below known values), then lower relic id. Empty from Act II.
        /// </summary>
        [BotRule("bot.lune.01-act1")]
        public List<Ranked<int>> Act1(BotView view)
        {
            if (view.Act >= 2) return new List<Ranked<int>>();
            var b = view.Board;
            var missing = LuneRelics.MissingTypes(b, view.Seat);
            var dist = LuneRelics.DistancesFrom(b, MyWarriorClearings(view));
            var cands = LuneRelics.ForestRelics(b).Where(r => b.RelicType(r) is string t && missing.Contains(t));
            return Rank.Descending(cands, r => -ForestDistance(b, dist, b.RelicForest(r).Value) * 10.0 + KnownValue(b, r), r => r);
        }

        // ================================================================= step 2: Act II goals

        /// <summary>
        /// From Act II: the Parents' Journal (in its forest, or wherever it is carried) and every forest relic of a missing
        /// type, closer first (path steps from her nearest warrior group to a clearing touching the relic's forest, or to the
        /// relic's clearing while it is carried); ties: the Journal, then lower relic id. Empty in Act I.
        /// </summary>
        [BotRule("bot.lune.02-act2-goals")]
        public List<Ranked<int>> Act2Goals(BotView view)
        {
            if (view.Act < 2) return new List<Ranked<int>>();
            var b = view.Board;
            var me = view.Seat;
            var tag = view.State.LuneTaggedRelic;
            var dist = LuneRelics.DistancesFrom(b, MyWarriorClearings(view));
            var missing = LuneRelics.MissingTypes(b, me);
            var cands = new List<int>();
            if (tag.HasValue && !b.RecoveredRelics(me).Contains(tag.Value) && (b.RelicForest(tag.Value).HasValue || b.RelicClearing(tag.Value).HasValue))
                cands.Add(tag.Value);
            cands.AddRange(LuneRelics.ForestRelics(b).Where(r => r != tag && b.RelicType(r) is string t && missing.Contains(t)));
            return Rank.Descending(cands, r => -GoalDistance(b, dist, r) * 10.0 + (r == tag ? 1 : 0), r => r);
        }

        // ================================================================= step 3: Retinue

        /// <summary>
        /// Move and Delve cards from her hand for the route to the top goal's forest: one Move card per clearing the
        /// shortest path leaves from (in path order) and a Delve card for the clearing touching the forest, matching each
        /// clearing's suit (a bird when no suited card matches); a card the Retinue already holds is not added twice. The
        /// one-turn lookahead drops a card she could not resolve: a Move needs her warriors (now, or brought by the cards
        /// added before it) in a matching clearing; a Delve needs them in a matching clearing beside a forest holding a relic.
        /// </summary>
        [BotRule("bot.lune.03-retinue")]
        public List<RetinueCard> Retinue(BotView view)
        {
            var result = new List<RetinueCard>();
            var b = view.Board;
            var me = view.Seat;
            var goal = Goals(view).Where(r => b.RelicForest(r).HasValue).Select(r => (int?)r).FirstOrDefault();
            if (!goal.HasValue) return result;
            var touching = new HashSet<ClearingId>(b.ForestClearings(b.RelicForest(goal.Value).Value));
            var mine = MyWarriorClearings(view);
            var path = LuneRelics.ShortestPath(b, mine, touching.Contains);
            if (path == null) return result;

            var hand = b.HandSuits(me).ToList();
            var existing = new[] { LuneAction.MoveColumn, LuneAction.BattleDelveColumn }.ToDictionary(c => c, c => b.RetinueColumn(me, c).ToList());
            var positions = new HashSet<ClearingId>(mine);
            for (var i = 0; i < path.Count; i++)
            {
                var c = path[i];
                var column = i < path.Count - 1 ? LuneAction.MoveColumn : LuneAction.BattleDelveColumn;
                var held = existing[column].Where(s => Matches(b, c, s)).Select(s => (Suit?)s).FirstOrDefault();
                if (held.HasValue)
                {
                    existing[column].Remove(held.Value);
                    if (column == LuneAction.MoveColumn) positions.Add(path[i + 1]);
                    continue;
                }
                var suit = hand.Where(s => s != Suit.Bird && b.SuitsOf(c).Contains(s)).Select(s => (Suit?)s).FirstOrDefault()
                           ?? (hand.Contains(Suit.Bird) ? Suit.Bird : (Suit?)null);
                if (!suit.HasValue) continue;
                var card = new RetinueCard(column, suit.Value);
                if (!Lookahead(view, card, positions)) continue;
                hand.Remove(suit.Value);
                result.Add(card);
                if (column == LuneAction.MoveColumn) positions.Add(path[i + 1]);
            }
            return result;
        }

        /// <summary>The one-turn lookahead for one card, given the clearings her warriors will stand in when it resolves.</summary>
        public bool Lookahead(BotView view, RetinueCard card, IEnumerable<ClearingId> positions)
        {
            var b = view.Board;
            var at = positions.Where(p => Matches(b, p, card.Suit)).ToList();
            if (card.Column == LuneAction.MoveColumn) return at.Count > 0;
            if (card.Column == LuneAction.BattleDelveColumn)
                return at.Any(p => b.ForestsAround(p).Any(f => b.RelicsInForest(f).Count > 0));
            return at.Count > 0;
        }

        // ================================================================= step 4: Delve the Journal

        /// <summary>She may delve the Journal's forest only with tune.lune-delve-escort or more of her warriors in the
        /// clearings touching that forest.</summary>
        [BotRule("bot.lune.04-delve")]
        public bool Delve(BotView view)
        {
            var b = view.Board;
            var tag = view.State.LuneTaggedRelic;
            var forest = tag.HasValue ? b.RelicForest(tag.Value) : null;
            if (!forest.HasValue) return false;
            return b.ForestClearings(forest.Value).Sum(c => b.Warriors(view.Seat, c)) >= Tuning.LuneDelveEscort;
        }

        // ================================================================= step 5: Escort

        /// <summary>
        /// Convoys for the carried relics: the shortest path to a waystation showing the relic's type that passes no clearing
        /// where one enemy seat's warriors outnumber the convoy; if none, the shortest such path to a clearing where she can
        /// flip her waystation or build one (a free slot). The convoy is tune.lune-convoy-size warriors or more (all her
        /// warriors with the carrier), Easy single carriers. The Journal first, then the shorter route; Hard runs two convoys.
        /// </summary>
        [BotRule("bot.lune.05-escort")]
        public List<EscortPlan> Escort(BotView view, IEnumerable<int> carried)
        {
            var plans = new List<EscortPlan>();
            foreach (var r in (carried ?? new int[0]).Distinct())
            {
                var p = Route(view, r);
                if (p != null) plans.Add(p);
            }
            var tag = view.State.LuneTaggedRelic;
            return plans.OrderBy(p => p.Relic == tag ? 0 : 1).ThenBy(p => p.Route.Count).ThenBy(p => p.Relic)
                .Take(Convoys(view)).ToList();
        }

        /// <summary>Convoys at once: two on Hard, otherwise one.</summary>
        public int Convoys(BotView view) => view.Difficulty == Difficulty.Hard ? 2 : 1;

        /// <summary>Minimum warriors moving with a carrier: tune.lune-convoy-size, Easy 1.</summary>
        public int ConvoySize(BotView view) => view.Difficulty == Difficulty.Easy ? 1 : Tuning.LuneConvoySize;

        /// <summary>One carried relic's route home (see <see cref="Escort"/>), or null when it is not carried by her or no
        /// route exists.</summary>
        public EscortPlan Route(BotView view, int relic)
        {
            var b = view.Board;
            var me = view.Seat;
            var from = b.RelicClearing(relic);
            var type = b.RelicType(relic);
            if (!from.HasValue || type == null || b.Warriors(me, from.Value) <= 0) return null;
            var size = ConvoySize(view);
            var escort = view.Difficulty == Difficulty.Easy ? 1 : Math.Max(size, b.Warriors(me, from.Value));
            bool Allowed(ClearingId c) => !b.Seats().Any(s => s != me && b.Warriors(s, c) > escort);
            var start = new[] { from.Value };
            var route = LuneRelics.ShortestPath(b, start, c => b.BuildingsOfType(me, c, PieceTypes.Waystation + "." + type) > 0, Allowed);
            var finish = EscortPlan.RecoverFinish;
            if (route == null)
            {
                route = LuneRelics.ShortestPath(b, start, c => b.BuildingsOfType(me, c, PieceTypes.Waystation) > 0 || b.FreeSlots(c) > 0, Allowed);
                if (route == null) return null;
                finish = b.BuildingsOfType(me, route[route.Count - 1], PieceTypes.Waystation) > 0 ? EscortPlan.FlipFinish : EscortPlan.BuildFinish;
            }
            return new EscortPlan
            {
                Relic = relic, RelicType = type, From = from.Value, Route = route, Finish = finish, ConvoySize = size,
                Ready = b.Warriors(me, from.Value) >= size,
            };
        }

        /// <summary>The legal action that advances one convoy: finish at the destination, gather escorts, or move the group
        /// (largest group first; Easy single carriers).</summary>
        public string EscortStep(BotView view, EscortPlan plan, List<LuneAction> acts)
        {
            if (plan.Route.Count == 1)
            {
                var done = plan.Finish == EscortPlan.RecoverFinish
                    ? acts.FirstOrDefault(a => a.Verb == LuneAction.Recover && a.Clearing == plan.From && a.Relic == plan.Relic)
                    : acts.FirstOrDefault(a => a.Verb == plan.Finish && a.Clearing == plan.From && (a.RelicType == null || a.RelicType == plan.RelicType));
                return done?.Key;
            }
            if (!plan.Ready)
                return acts.Where(a => a.Verb == LuneAction.Move && a.To == plan.From && a.Clearing.HasValue && KeepsConvoy(view, a.Clearing.Value, a.Count))
                    .OrderByDescending(a => a.Count).FirstOrDefault()?.Key;
            var next = plan.Route[1];
            var moves = acts.Where(a => a.Verb == LuneAction.Move && a.Clearing == plan.From && a.To == next && a.Count >= plan.ConvoySize
                                        && (a.Relics.Count == 0 || a.Relics.Contains(plan.Relic)));
            moves = view.Difficulty == Difficulty.Easy ? moves.OrderBy(a => a.Count) : moves.OrderByDescending(a => a.Count);
            return moves.FirstOrDefault()?.Key;
        }

        /// <summary>Relics her warriors carry that the bot can name: the Parents' Journal and the relics named in her legal
        /// action keys (no read lists carried relics).</summary>
        public List<int> Carried(BotView view, IEnumerable<LuneAction> acts)
        {
            var b = view.Board;
            var ids = new List<int>();
            var tag = view.State.LuneTaggedRelic;
            if (tag.HasValue) ids.Add(tag.Value);
            ids.AddRange(acts.SelectMany(a => a.Relics));
            return ids.Distinct().Where(r => b.RelicClearing(r) is ClearingId c && b.Warriors(view.Seat, c) > 0).OrderBy(r => r).ToList();
        }

        /// <summary>Moving <paramref name="count"/> warriors out of a clearing keeps a carried Journal's convoy at its size.</summary>
        public bool KeepsConvoy(BotView view, ClearingId from, int count)
        {
            var tag = view.State.LuneTaggedRelic;
            if (!tag.HasValue || view.Board.RelicClearing(tag.Value) != from) return true;
            return view.Board.Warriors(view.Seat, from) - count >= ConvoySize(view);
        }

        // ================================================================= step 6: Skip duplicates

        /// <summary>True when this relic is skipped: its type is already recovered, it is not the Journal, and a goal relic
        /// (the Journal or a missing type) can still be reached by a route step 5 accepts. With no goal reachable she needs
        /// VP and duplicates are normal Keepers play.</summary>
        [BotRule("bot.lune.06-duplicates")]
        public bool Duplicates(BotView view, int relicId)
        {
            var b = view.Board;
            var type = b.RelicType(relicId);
            if (type == null || relicId == view.State.LuneTaggedRelic) return false;
            if (!LuneRelics.RecoveredTypes(b, view.Seat).Contains(type)) return false;
            return GoalReachable(view);
        }

        /// <summary>A goal relic is reachable from her warriors through clearings where no enemy seat outnumbers a convoy.</summary>
        public bool GoalReachable(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var size = ConvoySize(view);
            var dist = LuneRelics.DistancesFrom(b, MyWarriorClearings(view), c => !b.Seats().Any(s => s != me && b.Warriors(s, c) > size));
            foreach (var g in Goals(view))
            {
                var at = b.RelicClearing(g);
                if (at.HasValue && dist.ContainsKey(at.Value)) return true;
                var f = b.RelicForest(g);
                if (f.HasValue && b.ForestClearings(f.Value).Any(dist.ContainsKey)) return true;
            }
            return false;
        }

        // ================================================================= step 7: Spoiler layer

        /// <summary>
        /// The shared spoiler layer. Lune answers with Battle and Move cards added to the Retinue for the key clearing (only
        /// when the lookahead says she can resolve them), or the battle / move those cards resolve into; convoys keep their
        /// escort size. Breaking a met part (a battle) comes before blocking; then the cheapest (actions + expected hits
        /// taken), the nearest, the lower clearing. Pre-empts the steps for a banner (not on Easy) and, on Hard, for a
        /// one-step threat, unless she can finish her own quest win this turn (which always comes first).
        /// </summary>
        [BotRule("bot.lune.07-spoiler")]
        public LuneAnswer Spoiler(BotView view, IReadOnlyList<string> legal = null)
        {
            var acts = LuneAction.ParseAll(legal ?? view.Board.LegalActions(view.Seat));
            if (acts.Count == 0) return null;
            var finishing = CanFinishQuest(view, acts);
            foreach (var threat in Threats(view))
            {
                var answer = Answer(view, threat, acts);
                if (answer == null) continue;
                answer.Preempts = !finishing && (threat.Banner ? view.Difficulty != Difficulty.Easy : view.Difficulty == Difficulty.Hard);
                return answer;
            }
            return null;
        }

        /// <summary>Other seats (not her coalition partner) whose win is unlocked in the current act and not switched off,
        /// with a banner up or one step away (Easy: banners only); banners first, then the next Birdsong in turn order, then
        /// higher VP.</summary>
        public List<LuneThreat> Threats(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var seats = b.Seats();
            var cur = IndexOf(seats, b.CurrentSeat());
            var list = new List<(LuneThreat t, int dist, int vp)>();
            for (var i = 0; i < seats.Count; i++)
            {
                var o = seats[i];
                if (o == me || b.CoalitionPartner(o) == me || b.CoalitionPartner(me) == o) continue;
                var ch = view.CharacterOf(o);
                if (ch == null || !Data.Characters.TryGetValue(ch, out var chRow)) continue;
                if (!Data.WinConditions.TryGetValue(chRow.WinCondition ?? "", out var row)) continue;
                if (!WinRules.IsUnlocked(view.Act, row)) continue;
                if (row.OffWithDominance && b.ActiveDominance(o).HasValue) continue;
                if (row.OffInCoalition && b.CoalitionPartner(o).HasValue) continue;
                var banner = view.Countdowns.Contains(o);
                if (!banner && (view.Difficulty == Difficulty.Easy || view.UnitsMissing(o) != 1)) continue;
                var keys = view.KeyClearings(o);
                if (keys.Count == 0) keys = FallbackKeyClearings(view, o, ch);
                var dist = cur < 0 ? i : (i - cur + seats.Count) % seats.Count;
                if (dist == 0) dist = seats.Count;
                list.Add((new LuneThreat { Seat = o, Banner = banner, KeyClearings = keys }, dist, b.Vp(o)));
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
                case CharacterIds.Paintress:
                    foreach (var c in b.Clearings().Where(c => b.BuildingsOfType(o, c, PieceTypes.Roost) > 0)) Add(c);
                    Add(monolith);
                    break;
                case CharacterIds.Verso: Add(monolith); break;
                case CharacterIds.Maelle:
                case CharacterIds.Curator: Add(view.Role(Ids.MapRoles.Lumiere)); Add(monolith); break;
                case CharacterIds.WhiteHairedMan: Add(monolith); foreach (var c in view.MonolithNeighbours()) Add(c); break;
                case CharacterIds.Sciel: foreach (var c in b.Clearings().Where(c => b.TokensOfType(o, c, PieceTypes.Sympathy) > 0)) Add(c); break;
                case CharacterIds.Clea: Add(monolith); break;
                default: Add(b.PawnClearing(o)); break;
            }
            return keys;
        }

        private LuneAnswer Answer(BotView view, LuneThreat threat, List<LuneAction> acts)
        {
            var b = view.Board;
            var me = view.Seat;
            var keys = threat.KeyClearings;
            var options = new List<(LuneAction a, int kind, double cost, int near, int c)>();
            foreach (var a in acts)
            {
                if (a.Verb == LuneAction.Battle && a.Target == threat.Seat && a.Clearing.HasValue && keys.Contains(a.Clearing.Value))
                    options.Add((a, 0, 1 + ExpectedHitsTaken(b.Warriors(threat.Seat, a.Clearing.Value)), 0, a.Clearing.Value.Value));
                else if (a.Verb == LuneAction.Move && a.To.HasValue && keys.Contains(a.To.Value) && a.Clearing.HasValue && KeepsConvoy(view, a.Clearing.Value, a.Count))
                    options.Add((a, 1, 1, 1, a.To.Value.Value));
                else if (a.Verb == LuneAction.Retinue && a.Suit.HasValue && a.Column == LuneAction.BattleDelveColumn)
                {
                    var k = keys.Where(c => b.Warriors(me, c) > 0 && HasPieces(b, threat.Seat, c) && Matches(b, c, a.Suit.Value))
                        .OrderBy(c => ExpectedHitsTaken(b.Warriors(threat.Seat, c))).ThenBy(c => c.Value).Select(c => (ClearingId?)c).FirstOrDefault();
                    if (k.HasValue) options.Add((a, 0, 1 + ExpectedHitsTaken(b.Warriors(threat.Seat, k.Value)), 0, k.Value.Value));
                }
                else if (a.Verb == LuneAction.Retinue && a.Suit.HasValue && a.Column == LuneAction.MoveColumn)
                {
                    var from = b.Clearings().Where(c => b.Warriors(me, c) > 0 && Matches(b, c, a.Suit.Value) && KeepsConvoy(view, c, 1)
                                                        && b.PathNeighbours(c).Any(keys.Contains))
                        .OrderBy(c => c.Value).Select(c => (ClearingId?)c).FirstOrDefault();
                    if (from.HasValue)
                    {
                        var to = b.PathNeighbours(from.Value).Where(keys.Contains).OrderBy(c => c.Value).First();
                        options.Add((a, 1, 1, 1, to.Value));
                    }
                }
            }
            if (options.Count == 0) return null;
            var best = options.OrderBy(o => o.kind).ThenBy(o => o.cost).ThenBy(o => o.near).ThenBy(o => o.c).First();
            return new LuneAnswer { Threat = threat, ActionKey = best.a.Key, Cost = best.cost };
        }

        /// <summary>Her quest win can be finished this turn: the gates pass and the test holds, or one legal Recover completes it.</summary>
        public bool CanFinishQuest(BotView view, IEnumerable<LuneAction> acts) =>
            LuneRelics.TestMet(view.Board, view.Seat, view.State.LuneTaggedRelic) && GatesPass(view) || FinishingRecover(view, acts) != null;

        /// <summary>The legal Recover that completes win.lune this turn (gates included), or null.</summary>
        public LuneAction FinishingRecover(BotView view, IEnumerable<LuneAction> acts)
        {
            var tag = view.State.LuneTaggedRelic;
            if (!tag.HasValue || !GatesPass(view)) return null;
            var b = view.Board;
            var have = b.RecoveredRelics(view.Seat);
            foreach (var a in acts.Where(a => a.Verb == LuneAction.Recover && a.Relic.HasValue))
            {
                var set = have.Concat(new[] { a.Relic.Value }).Distinct().ToList();
                var types = set.Select(b.RelicType).Where(t => t != null && LuneRelics.Types.Contains(t)).Distinct().Count();
                if (set.Contains(tag.Value) && types >= Tuning.LuneRelicTypes) return a;
            }
            return null;
        }

        private static bool GatesPass(BotView view)
        {
            var row = Data.WinConditions[Ids.WinConditions.Lune];
            return view.Act >= row.ActMin && !(row.OffWithDominance && view.Board.ActiveDominance(view.Seat).HasValue)
                   && !(row.OffInCoalition && view.Board.CoalitionPartner(view.Seat).HasValue) && view.Board.Vp(view.Seat) >= row.VpMin;
        }

        /// <summary>Expected hits the attacker takes: the defender deals the lower of two 0-3 dice, capped by its warriors.</summary>
        public static double ExpectedHitsTaken(int defenderWarriors)
        {
            var e = 0.0;
            for (var k = 1; k <= Math.Min(defenderWarriors, 3); k++) e += Math.Pow((4.0 - k) / 4.0, 2);
            return e;
        }

        // ================================================================= helpers

        public static List<ClearingId> MyWarriorClearings(BotView view) =>
            view.Board.Clearings().Where(c => view.Board.Warriors(view.Seat, c) > 0).OrderBy(c => c.Value).ToList();

        private static bool Matches(IBoardView b, ClearingId c, Suit s) => s == Suit.Bird || b.SuitsOf(c).Contains(s);

        private static bool HasPieces(IBoardView b, SeatId s, ClearingId c) =>
            b.Warriors(s, c) + b.Buildings(s, c) + b.Tokens(s, c) > 0 || b.PawnClearing(s) == c;

        private static int ForestDistance(IBoardView b, Dictionary<ClearingId, int> dist, int forest)
        {
            var ds = b.ForestClearings(forest).Where(dist.ContainsKey).Select(c => dist[c]).ToList();
            return ds.Count == 0 ? Far : ds.Min();
        }

        private static int GoalDistance(IBoardView b, Dictionary<ClearingId, int> dist, int relic)
        {
            var at = b.RelicClearing(relic);
            if (at.HasValue) return dist.TryGetValue(at.Value, out var d) ? d : Far;
            var f = b.RelicForest(relic);
            return f.HasValue ? ForestDistance(b, dist, f.Value) : Far;
        }

        private static double KnownValue(IBoardView b, int relic) => b.RelicFaceUp(relic) && b.RelicValue(relic) is int v ? v : 0;

        private static int IndexOf(IReadOnlyList<SeatId> seats, SeatId s)
        {
            for (var i = 0; i < seats.Count; i++) if (seats[i] == s) return i;
            return -1;
        }
    }
}
