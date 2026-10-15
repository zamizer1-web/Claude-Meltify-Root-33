using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Verso
{
    /// <summary>A battle Verso could start (bot.verso.01-climb's filter): where, against whom, the hits he expects to take.</summary>
    public sealed class VersoTarget
    {
        public ClearingId Clearing;
        public SeatId Defender;
        public double ExpectedHits;
        public bool PossibleAmbush;
        public override string ToString() => Defender + "@" + Clearing + " (" + ExpectedHits + ")";
    }

    /// <summary>bot.verso.02-repair: Get Back Up (Hideout) when Root offers it, else walk into a forest, else stay in the
    /// forest he is in and end the turn there (the Evening rest).</summary>
    public sealed class RepairStep
    {
        public bool Hideout = true;
        public int? ToForest;
        public bool Stay;
        public override string ToString() => Stay ? "stay" : ToForest.HasValue ? "f" + ToForest : "hideout";
    }

    /// <summary>bot.verso.04-assault's test on the current board.</summary>
    public sealed class AssaultPlan
    {
        /// <summary>Other seats' warriors + buildings + tokens in the Monolith.</summary>
        public int PiecesToClear;
        public double ExpectedRemovals;
        public double ExpectedDamage;
        /// <summary>Rank A or S from Act II (Easy: tune.verso-easy-assault-swords undamaged swords), removals &gt;= pieces and
        /// damage &lt; tune.verso-bot-assault-max-damage. He enters the Monolith only while this holds.</summary>
        public bool Go;
        /// <summary>Go, and the assault can end with him there at rank S: he is at S, or there are pieces whose clean removal
        /// raises him (at rank A an empty Monolith is not worth the boots, as he may not end the turn there).</summary>
        public bool Enter;
        /// <summary>Rank S and no other faction's pieces (warriors, buildings, tokens, pawns) left in the Monolith.</summary>
        public bool EndTurnThere;
        public override string ToString() => (Go ? "go" : "wait") + " " + ExpectedRemovals + "/" + PiecesToClear + " dmg " + ExpectedDamage;
    }

    public sealed class VersoThreat
    {
        public SeatId Seat;
        public bool Banner;
        public IReadOnlyList<ClearingId> KeyClearings = new ClearingId[0];
        public override string ToString() => Seat + (Banner ? " banner" : " one-step");
    }

    public sealed class VersoAnswer
    {
        public VersoThreat Threat;
        public string ActionKey;
        public bool Preempts;
        public double Cost;
        public override string ToString() => ActionKey + (Preempts ? " (pre-empts)" : " (free)");
    }

    /// <summary>
    /// bot.verso: layer 2 of the Verso bot (Ranger Vagabond). Root's own AI keeps hit allocation, Ambush and discards and
    /// plays every decision no step claims (<see cref="ChooseAction"/> returns null). Steps in priority order
    /// (bot.verso.01..07), each one [BotRule] method that reads only the <see cref="BotView"/> (public board, the public
    /// rank and rocks) and returns a ranking or a decision. The plan keeps nothing in <see cref="ModState"/>.
    /// Battles always pass <see cref="MayBattle"/>: none while an item is damaged (step 2), step 4's test in the Monolith at
    /// rank A or S from Act II, step 1's filter below rank A and (step 3) while approaching.
    /// </summary>
    public sealed class VersoBot : IBotDirector
    {
        /// <summary>Faces of a Root battle die are 0-3.</summary>
        private const int DieFaces = 4;
        /// <summary>Hits an Ambush card deals (bot.verso.04-assault adds 2 per battle against a possible Ambush).</summary>
        public const int AmbushHits = 2;
        private const double Eps = 1e-9;

        public string PlanId => Ids.BotPlans.Verso;

        /// <summary>Act II, the act win.verso unlocks (its actMin).</summary>
        private static int ActTwo => Data.WinConditions[Ids.WinConditions.Verso].ActMin;

        /// <summary>goalModeTrigger (all difficulties): rank A or above in Act II or later. Below A he climbs.</summary>
        public bool InGoalMode(BotView view) => view.State.VersoRank >= Tuning.VersoPerfectionRank && view.Act >= ActTwo;

        /// <summary>bot.verso keeps no plan state (no state.json row): every step reads the board each decision.</summary>
        public void PlanTurn(BotView view, ModState state) { }

        // ================================================================= decision

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0) return null;
            var acts = VersoAction.ParseAll(legalActions).Where(a => Allowed(view, a)).ToList();
            if (acts.Count == 0) return null;
            var spoiler = Spoiler(view, legalActions);
            if (spoiler != null && spoiler.Preempts) return spoiler.ActionKey;
            return StepAction(view, acts) ?? spoiler?.ActionKey;
        }

        /// <summary>The steps' 'never' and 'only': battles must pass <see cref="MayBattle"/>; at rank A or S from Act II he
        /// enters the Monolith only while step 4's test passes, ends the turn there only at rank S with no other pieces, and
        /// does not walk out once he can.</summary>
        public bool Allowed(BotView view, VersoAction a)
        {
            if (a.IsBattle) return a.Clearing.HasValue && a.Target.HasValue && MayBattle(view, a.Clearing.Value, a.Target.Value);
            var m = Monolith(view);
            if (!m.HasValue || !InGoalMode(view)) return true;
            if (view.Board.PawnClearing(view.Seat) != m.Value) return !(a.IsMove && a.To == m.Value) || Assault(view).Go;
            var plan = Assault(view);
            if (a.IsEnd) return plan.EndTurnThere;
            if (a.IsMove) return !plan.EndTurnThere;
            return true;
        }

        /// <summary>No battle while an item is damaged (step 2); in the Monolith at rank A or S from Act II only while step 4's
        /// test passes; elsewhere step 1's filter below rank A and while approaching (step 3). At rank A or S in Act I no step
        /// names a filter.</summary>
        public bool MayBattle(BotView view, ClearingId c, SeatId defender)
        {
            if (Damaged(view.Board, view.Seat)) return false;
            if (InGoalMode(view) && Monolith(view) == c) return Assault(view).Go;
            if (view.State.VersoRank < Tuning.VersoPerfectionRank || InGoalMode(view)) return Target(view, c, defender) != null;
            return true;
        }

        /// <summary>Steps 1-6 in order; the first that claims the decision takes it.</summary>
        public string StepAction(BotView view, List<VersoAction> acts)
        {
            var b = view.Board;
            var me = view.Seat;
            var pawn = b.PawnClearing(me);
            var m = Monolith(view);

            // 1 Climb: the best safe battle where he stands.
            foreach (var t in Climb(view))
            {
                var k = BattleKey(acts, t.Item);
                if (k != null) return k;
            }

            // 2 Repair: Get Back Up, or end the turn in a forest.
            var repair = Repair(view);
            if (repair != null)
            {
                var k = (repair.Hideout ? acts.FirstOrDefault(a => a.IsHideout) : null)
                        ?? (repair.ToForest.HasValue ? MoveToForest(acts, repair.ToForest.Value) : null)
                        ?? (repair.Stay ? acts.FirstOrDefault(a => a.IsEnd) : null);
                if (k != null) return k.Key;
            }

            if (InGoalMode(view) && m.HasValue)
            {
                var plan = Assault(view);
                if (pawn != m.Value)
                {
                    // 4 Assault: walk in once the test passes; 3 Approach until then (and on the way).
                    var enter = plan.Enter ? acts.FirstOrDefault(a => a.IsMove && a.To == m.Value) : null;
                    if (enter != null) return enter.Key;
                    var k = ApproachAction(view, acts, pawn);
                    if (k != null) return k;
                }
                else
                {
                    // 4 Assault: end the turn at rank S alone, else clear warriors then buildings, else walk out.
                    if (plan.EndTurnThere)
                    {
                        var end = acts.FirstOrDefault(a => a.IsEnd);
                        if (end != null) return end.Key;
                    }
                    var battle = acts.Where(a => a.IsBattle && a.Clearing == m.Value && a.Target.HasValue)
                        .OrderByDescending(a => b.Warriors(a.Target.Value, m.Value)).ThenBy(a => a.Target.Value.Value).FirstOrDefault();
                    if (battle != null) return battle.Key;
                    if (!plan.EndTurnThere)
                    {
                        var k = ApproachAction(view, acts, pawn);
                        if (k != null) return k;
                    }
                }
            }

            // 5 Bail: below rank A in the Monolith, into a forest.
            var bail = Bail(view);
            if (bail.HasValue)
            {
                var mv = MoveToForest(acts, bail.Value);
                if (mv != null) return mv.Key;
            }

            // 6 Reunion: walk into Esquie's clearing.
            var esquie = Reunion(view);
            if (esquie.HasValue && pawn != esquie.Value)
            {
                var k = MoveToward(view, acts, esquie.Value);
                if (k != null) return k;
            }
            return null;
        }

        // ================================================================= step 1: Climb

        /// <summary>
        /// Below rank A, the battles he may start where his pawn stands: expected hits taken under
        /// tune.verso-bot-max-expected-hits (undefended buildings and tokens) or a lone defending warrior (allowed by name);
        /// against a possible Ambush (tune.verso-bot-ambush-hand cards) only with tune.verso-bot-ambush-swords undamaged swords
        /// (Easy ignores Ambush risk). Hostile factions first, then the VP leader, then the rest; then fewest expected hits;
        /// ties: lower clearing number.
        /// </summary>
        [BotRule("bot.verso.01-climb")]
        public List<Ranked<VersoTarget>> Climb(BotView view)
        {
            var pawn = view.Board.PawnClearing(view.Seat);
            if (view.State.VersoRank >= Tuning.VersoPerfectionRank || !pawn.HasValue) return new List<Ranked<VersoTarget>>();
            return SafeTargets(view, pawn.Value);
        }

        /// <summary>Step 1's filter and order for the battles in one clearing (also step 3's farming).</summary>
        public List<Ranked<VersoTarget>> SafeTargets(BotView view, ClearingId c)
        {
            var b = view.Board;
            var me = view.Seat;
            var others = b.Seats().Where(o => o != me).ToList();
            var leader = others.Count == 0 ? 0 : others.Max(o => b.Vp(o));
            return others.Select(o => Target(view, c, o)).Where(t => t != null)
                .Select(t => new Ranked<VersoTarget>(t, Tier(b, me, t.Defender, leader) * 100 - t.ExpectedHits))
                .OrderByDescending(r => r.Score).ThenBy(r => r.Item.Clearing.Value).ThenBy(r => r.Item.Defender.Value).ToList();
        }

        /// <summary>Step 1's filter for one defender in one clearing, or null when the battle is not safe.</summary>
        public VersoTarget Target(BotView view, ClearingId c, SeatId defender)
        {
            var b = view.Board;
            var me = view.Seat;
            if (defender == me) return null;
            var warriors = b.Warriors(defender, c);
            if (warriors + b.Buildings(defender, c) + b.Tokens(defender, c) == 0) return null;
            var defending = DefendingWarriors(b, defender, c);
            var expected = ExpectedHitsTaken(defending);
            var lone = warriors == 1 && defending == 1;
            if (expected >= Tuning.VersoBotMaxExpectedHits && !lone) return null;
            var ambush = PossibleAmbush(b, defender);
            if (ambush && view.Difficulty != Difficulty.Easy && VersoRules.UndamagedSwords(b, me) < Tuning.VersoBotAmbushSwords) return null;
            return new VersoTarget { Clearing = c, Defender = defender, ExpectedHits = expected, PossibleAmbush = ambush };
        }

        private static int Tier(IBoardView b, SeatId me, SeatId o, int leader) =>
            b.RelationshipOf(me, o) == Relationship.Hostile ? 2 : b.Vp(o) == leader ? 1 : 0;

        // ================================================================= step 2: Repair

        /// <summary>While any item is damaged he starts no battle (<see cref="MayBattle"/>) and repairs: Get Back Up (Hideout)
        /// if Root offers it, else the lowest-numbered forest touching his clearing, else (in a forest) end the turn there.
        /// Null when nothing is damaged.</summary>
        [BotRule("bot.verso.02-repair")]
        public RepairStep Repair(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            if (!Damaged(b, me)) return null;
            var step = new RepairStep();
            if (b.PawnForest(me).HasValue) step.Stay = true;
            else
            {
                var pawn = b.PawnClearing(me);
                if (pawn.HasValue) step.ToForest = LowestForest(b, pawn.Value);
            }
            return step;
        }

        // ================================================================= step 3: Approach

        /// <summary>
        /// At rank A or above in Act II or later: clearings (not the Monolith) by path distance to the nearest clearing next to
        /// the Monolith (nearest first, without walking through it); next to it, the clearings with a step 1 farming target
        /// first; ties: lower clearing number. Empty outside goal mode.
        /// </summary>
        [BotRule("bot.verso.03-approach")]
        public List<Ranked<ClearingId>> Approach(BotView view)
        {
            var m = Monolith(view);
            if (!InGoalMode(view) || !m.HasValue) return new List<Ranked<ClearingId>>();
            var dist = NextToMonolithDistances(view.Board, m.Value);
            return Rank.Clearings(dist.Keys, c => -dist[c] + (dist[c] == 0 && SafeTargets(view, c).Count > 0 ? 0.5 : 0));
        }

        /// <summary>Next to the Monolith: farm a safe target here. Otherwise the legal move to the best-ranked clearing that
        /// beats where he stands.</summary>
        private string ApproachAction(BotView view, List<VersoAction> acts, ClearingId? pawn)
        {
            var score = Approach(view).ToDictionary(r => r.Item, r => r.Score);
            if (score.Count == 0) return null;
            var own = double.NegativeInfinity;
            if (pawn.HasValue && score.TryGetValue(pawn.Value, out var here))
            {
                own = here;
                if (here >= 0)
                    foreach (var t in SafeTargets(view, pawn.Value))
                    {
                        var k = BattleKey(acts, t.Item);
                        if (k != null) return k;
                    }
            }
            return acts.Where(a => a.IsMove && a.To.HasValue && score.ContainsKey(a.To.Value) && score[a.To.Value] > own)
                .OrderByDescending(a => score[a.To.Value]).ThenBy(a => a.To.Value.Value).FirstOrDefault()?.Key;
        }

        private static Dictionary<ClearingId, int> NextToMonolithDistances(IBoardView b, ClearingId m)
        {
            var result = new Dictionary<ClearingId, int>();
            IEnumerable<ClearingId> Around(ClearingId c) => b.PathNeighbours(c).Where(n => n != m);
            foreach (var n in b.PathNeighbours(m))
                foreach (var kv in Graph.Distances(n, Around))
                    if (!result.TryGetValue(kv.Key, out var d) || kv.Value < d) result[kv.Key] = kv.Value;
            result.Remove(m);
            return result;
        }

        // ================================================================= step 4: Assault

        /// <summary>
        /// Pieces to clear = other seats' warriors + buildings + tokens in the Monolith. One battle per ready sword, seats with
        /// warriors first: each removes E[min(higher of two 0-3 dice, undamaged swords)] + tune.verso-perfection-hits, +1 when
        /// the defender has no warriors left (capped by that defender's pieces), and costs E[min(lower die, its warriors)], +2
        /// against a possible Ambush. Go when removals &gt;= pieces and damage &lt; tune.verso-bot-assault-max-damage, at rank A
        /// or S from Act II; Easy also waits for tune.verso-easy-assault-swords undamaged swords.
        /// </summary>
        [BotRule("bot.verso.04-assault")]
        public AssaultPlan Assault(BotView view)
        {
            var plan = new AssaultPlan();
            var m = Monolith(view);
            if (!m.HasValue) return plan;
            var b = view.Board;
            var me = view.Seat;
            var others = b.Seats().Where(o => o != me).ToList();
            plan.PiecesToClear = others.Sum(o => Pieces(b, o, m.Value));
            var swords = VersoRules.UndamagedSwords(b, me);
            var ready = b.Items(me).Count(i => i.Type == ItemType.Sword && !i.Damaged && !i.Exhausted);
            var dealt = ExpectedHitsDealt(swords) + Tuning.VersoPerfectionHits;
            var defenders = others.Where(o => Pieces(b, o, m.Value) > 0)
                .OrderByDescending(o => b.Warriors(o, m.Value) > 0).ThenBy(o => o.Value)
                .Select(o => new Defender
                {
                    Warriors = b.Warriors(o, m.Value),
                    Others = b.Buildings(o, m.Value) + b.Tokens(o, m.Value),
                    Ambush = PossibleAmbush(b, o),
                }).ToList();
            for (var i = 0; i < ready; i++)
            {
                var d = defenders.FirstOrDefault(x => x.Warriors + x.Others > Eps);
                if (d == null) break;
                var w = (int)Math.Ceiling(d.Warriors - Eps);
                plan.ExpectedDamage += ExpectedHitsTaken(w) + (d.Ambush ? AmbushHits : 0);
                var hits = dealt + (w == 0 ? 1 : 0);
                var onWarriors = Math.Min(hits, d.Warriors);
                d.Warriors -= onWarriors;
                var onOthers = Math.Min(hits - onWarriors, d.Others);
                d.Others -= onOthers;
                plan.ExpectedRemovals += onWarriors + onOthers;
            }
            var swordsOk = view.Difficulty != Difficulty.Easy || swords >= Tuning.VersoEasyAssaultSwords;
            plan.Go = InGoalMode(view) && swordsOk && plan.ExpectedRemovals >= plan.PiecesToClear - Eps
                      && plan.ExpectedDamage < Tuning.VersoBotAssaultMaxDamage;
            var rankS = view.State.VersoRank >= Tuning.VersoRankS;
            plan.Enter = plan.Go && (rankS || plan.PiecesToClear > 0);
            plan.EndTurnThere = rankS && plan.PiecesToClear == 0 && !others.Any(o => b.PawnClearing(o) == m.Value);
            return plan;
        }

        private sealed class Defender
        {
            public double Warriors;
            public double Others;
            public bool Ambush;
        }

        // ================================================================= step 5: Bail

        /// <summary>From Act II, below rank A with his pawn in the Monolith: the forest touching it (one move), the lower
        /// forest number first. Null otherwise.</summary>
        [BotRule("bot.verso.05-bail")]
        public int? Bail(BotView view)
        {
            var m = Monolith(view);
            if (view.Act < ActTwo || view.State.VersoRank >= Tuning.VersoPerfectionRank || !VersoRules.InMonolith(view.Board, view.Seat, m)) return null;
            return LowestForest(view.Board, m.Value);
        }

        // ================================================================= step 6: Reunion

        /// <summary>In Act II or later, with Esquie seated and Soarrie on Verso's card (withVerso): Esquie's clearing when his
        /// ready boots (undamaged, not exhausted) reach it this turn. Null otherwise.</summary>
        [BotRule("bot.verso.06-reunion")]
        public ClearingId? Reunion(BotView view)
        {
            if (view.Act < ActTwo) return null;
            var esquie = view.SeatOf(CharacterIds.Esquie);
            if (!esquie.HasValue || esquie.Value == view.Seat) return null;
            var soarrie = view.State.EsquieRocks[ModState.Soarrie];
            if (soarrie.Status != RockStatus.WithVerso || (soarrie.HostSeat.HasValue && soarrie.HostSeat.Value != view.Seat)) return null;
            var b = view.Board;
            var target = b.PawnClearing(esquie.Value);
            if (!target.HasValue) return null;
            var boots = b.Items(view.Seat).Count(i => i.Type == ItemType.Boots && !i.Damaged && !i.Exhausted);
            return MovesFrom(view).TryGetValue(target.Value, out var d) && d <= boots ? target : null;
        }

        // ================================================================= step 7: Spoiler layer

        /// <summary>
        /// The shared spoiler layer, bound by the steps above (<see cref="Allowed"/>): his fights keep step 1's filter below
        /// rank A and step 4's test in the Monolith. Answers: battle the threat in a key clearing (breaks a met part), else
        /// walk his pawn in (the Monolith against the White-Haired Man; a would-be Sanctuary before Sciel's banner); the
        /// cheapest, ties nearest then the lower clearing. Pre-empts the steps for a banner (not on Easy) and, on Hard, for a
        /// one-step threat unless this is his own announce turn. Verso has no quest win, so a banner always pre-empts.
        /// </summary>
        [BotRule("bot.verso.07-spoiler")]
        public VersoAnswer Spoiler(BotView view, IReadOnlyList<string> legal = null)
        {
            var acts = VersoAction.ParseAll(legal ?? view.Board.LegalActions(view.Seat)).Where(a => Allowed(view, a)).ToList();
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

        /// <summary>Other seats (never his coalition partner) whose win is unlocked and not switched off, with a banner up or
        /// one step away (Easy: banners only); banners first, then the next Birdsong in turn order, then higher VP.</summary>
        public List<VersoThreat> Threats(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var seats = b.Seats();
            var cur = IndexOf(seats, b.CurrentSeat());
            var list = new List<(VersoThreat t, int dist, int vp)>();
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
                list.Add((new VersoThreat { Seat = o, Banner = banner, KeyClearings = keys }, dist, b.Vp(o)));
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
                case CharacterIds.Curator: Add(view.Role(Ids.MapRoles.Lumiere)); Add(monolith); break;
                case CharacterIds.Lune: if (view.State.LuneTaggedRelic.HasValue) Add(b.RelicClearing(view.State.LuneTaggedRelic.Value)); break;
                case CharacterIds.WhiteHairedMan: Add(monolith); foreach (var c in view.MonolithNeighbours()) Add(c); break;
                case CharacterIds.Sciel: foreach (var c in b.Clearings().Where(c => b.TokensOfType(o, c, PieceTypes.Sympathy) > 0)) Add(c); break;
                default: Add(b.PawnClearing(o)); break;
            }
            return keys;
        }

        private VersoAnswer Answer(BotView view, VersoThreat threat, List<VersoAction> acts)
        {
            var b = view.Board;
            var keys = new HashSet<ClearingId>(threat.KeyClearings);
            var ch = view.CharacterOf(threat.Seat);
            var monolith = Monolith(view);
            var walkTo = new HashSet<ClearingId>();
            if (monolith.HasValue && ch == CharacterIds.WhiteHairedMan) walkTo.Add(monolith.Value);
            if (ch == CharacterIds.Sciel && !threat.Banner) walkTo.UnionWith(keys);
            var pawn = b.PawnClearing(view.Seat);
            var options = new List<(VersoAction a, int kind, double cost, int near, int c)>();
            foreach (var a in acts)
            {
                if (a.IsBattle && a.Clearing.HasValue && a.Target == threat.Seat && keys.Contains(a.Clearing.Value))
                    options.Add((a, 0,
                        1 + ExpectedHitsTaken(DefendingWarriors(b, threat.Seat, a.Clearing.Value)) + (PossibleAmbush(b, threat.Seat) ? AmbushHits : 0),
                        a.Clearing == pawn ? 0 : 1, a.Clearing.Value.Value));
                else if (a.IsMove && a.To.HasValue && walkTo.Contains(a.To.Value))
                    options.Add((a, 1, 1, 1, a.To.Value.Value));
            }
            if (options.Count == 0) return null;
            var best = options.OrderBy(o => o.kind).ThenBy(o => o.cost).ThenBy(o => o.near).ThenBy(o => o.c).First();
            return new VersoAnswer { Threat = threat, ActionKey = best.a.Key, Cost = best.cost };
        }

        /// <summary>His own announce turn: the whole win.verso announce test (with its gates) holds on the current board; bots
        /// always need rank S.</summary>
        public static bool IsAnnounceTurn(BotView view)
        {
            var b = view.Board;
            var me = view.Seat;
            var m = view.Role(Ids.MapRoles.Monolith);
            var row = Data.WinConditions[Ids.WinConditions.Verso];
            return view.Act >= row.ActMin && b.Vp(me) >= row.VpMin && !b.CoalitionPartner(me).HasValue
                   && view.State.VersoRank >= Tuning.VersoRankS && VersoRules.InMonolith(b, me, m) && VersoRules.MonolithAlone(b, me, m);
        }

        // ================================================================= dice and helpers

        /// <summary>Expected hits the attacker takes: the defender deals the lower of two 0-3 dice, capped by its warriors.</summary>
        public static double ExpectedHitsTaken(int defenderWarriors)
        {
            var e = 0.0;
            for (var k = 1; k <= Math.Min(defenderWarriors, DieFaces - 1); k++) e += Math.Pow((double)(DieFaces - k) / DieFaces, 2);
            return e;
        }

        /// <summary>Expected hits he rolls: the higher of two 0-3 dice, capped by his undamaged swords.</summary>
        public static double ExpectedHitsDealt(int undamagedSwords)
        {
            var e = 0.0;
            for (var k = 1; k <= Math.Min(undamagedSwords, DieFaces - 1); k++) e += 1 - Math.Pow((double)k / DieFaces, 2);
            return e;
        }

        /// <summary>A defender's warriors in the clearing, plus a Vagabond defender's undamaged swords when its pawn is there.</summary>
        public static int DefendingWarriors(IBoardView b, SeatId defender, ClearingId c) =>
            b.Warriors(defender, c) + (b.PawnClearing(defender) == c ? VersoRules.UndamagedSwords(b, defender) : 0);

        /// <summary>A defender holding tune.verso-bot-ambush-hand or more cards (public hand size).</summary>
        public static bool PossibleAmbush(IBoardView b, SeatId defender) => b.HandSize(defender) >= Tuning.VersoBotAmbushHand;

        private static bool Damaged(IBoardView b, SeatId me) => b.Items(me).Any(i => i.Damaged);

        private static int Pieces(IBoardView b, SeatId o, ClearingId c) => b.Warriors(o, c) + b.Buildings(o, c) + b.Tokens(o, c);

        private static ClearingId? Monolith(BotView view) => view.Role(Ids.MapRoles.Monolith);

        private static int? LowestForest(IBoardView b, ClearingId c) =>
            b.ForestsAround(c).OrderBy(f => f).Select(f => (int?)f).FirstOrDefault();

        private static string BattleKey(List<VersoAction> acts, VersoTarget t) =>
            acts.FirstOrDefault(a => a.IsBattle && a.Clearing == t.Clearing && a.Target == t.Defender)?.Key;

        private static VersoAction MoveToForest(List<VersoAction> acts, int forest) =>
            acts.FirstOrDefault(a => a.IsMove && a.ToForest == forest)
            ?? acts.Where(a => a.IsMove && a.ToForest.HasValue).OrderBy(a => a.ToForest.Value).FirstOrDefault();

        /// <summary>Moves from his pawn to every clearing (from a forest: 1 to each clearing around it).</summary>
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

        private static string MoveToward(BotView view, List<VersoAction> acts, ClearingId target)
        {
            var dist = Graph.Distances(target, view.Board.PathNeighbours);
            return acts.Where(a => a.IsMove && a.To.HasValue && dist.ContainsKey(a.To.Value))
                .OrderBy(a => dist[a.To.Value]).ThenBy(a => a.To.Value.Value).FirstOrDefault()?.Key;
        }

        private static int IndexOf(IReadOnlyList<SeatId> seats, SeatId s)
        {
            for (var i = 0; i < seats.Count; i++) if (seats[i] == s) return i;
            return -1;
        }
    }
}
