using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Esquie
{
    /// <summary>bot.esquie.01-target / 04-endgame: the rock he chases and where he walks.</summary>
    public sealed class EsquieTargetPlan
    {
        /// <summary>The rock index (0-3) he chases, or null (no claimable rock within range, or Easy's decoy walk).</summary>
        public int? Rock;
        /// <summary>Where he walks: the rock's clearing, Verso's clearing (withVerso), the Monolith (heldByRuler) or the Nest (decoy).</summary>
        public ClearingId? Destination;
        /// <summary>Path distance from his pawn to <see cref="Destination"/> (<see cref="EsquieBot.Unreachable"/> when none).</summary>
        public int Distance = EsquieBot.Unreachable;
        /// <summary>Easy, Act I: the one walk to the François decoy at the Nest.</summary>
        public bool Decoy;
        /// <summary>Soarrie held by the Monolith's ruler and no card of the Monolith's suit in hand: collect one first.</summary>
        public bool NeedsMonolithCard;
        /// <summary>Every claimable rock, best first (also those out of range).</summary>
        public List<Ranked<int>> Ranking = new List<Ranked<int>>();
    }

    /// <summary>bot.esquie.02-gear.</summary>
    public sealed class EsquieGearOrder
    {
        /// <summary>tune.esquie-bot-repair-damaged or more damaged items: end the move in a forest.</summary>
        public bool Repair;
        /// <summary>He could deal no hits in his (guarded) target's clearing: get a sword instead of attacking.</summary>
        public bool SeekSword;
        /// <summary>Craft a sword now (a sword is craftable, and he seeks one or plays Hard).</summary>
        public bool CraftSword;
        /// <summary>No sword craftable: Aid this faction, the nearest holding a crafted sword...</summary>
        public SeatId? AidFor;
        /// <summary>...in this clearing (its nearest clearing with pieces; ties lower number).</summary>
        public ClearingId? AidAt;
    }

    /// <summary>bot.esquie.03-hunt: whether he enters his target's guarded clearing and battles the neutral guards.</summary>
    public sealed class EsquieHuntOrder
    {
        public int Rock;
        public ClearingId Clearing;
        public int GuardsLeft;
        /// <summary>Guard battles he may still take this turn.</summary>
        public int Battles;
        public int MaxHits;
        public double ExpectedHits;
        public bool AllItemsUndamaged;
        public bool HasSword;
        public bool Go;
    }

    /// <summary>bot.esquie.06-spoiler: the chosen threat and where his pawn answers it (null: he has no pawn answer).</summary>
    public sealed class EsquieSpoilerAnswer
    {
        public SeatId Threat;
        public bool Banner;
        public bool PreEmpts;
        public ClearingId? Destination;
    }

    /// <summary>
    /// bot.esquie: Esquie's Goal Director (Tinker Vagabond). One public [BotRule] method per bot_rules row, asked in order by
    /// <see cref="ChooseAction"/>; the spoiler layer pre-empts them for a banner that is up (and on Hard a one-step threat)
    /// unless he can finish his own quest win this turn. Rule methods read only <see cref="BotView"/>; he never battles to
    /// spoil (Never Mad) and only moves his pawn.
    /// </summary>
    public sealed class EsquieBot : IBotDirector
    {
        public const int Unreachable = int.MaxValue / 4;

        /// <summary>Guard battles taken this turn (transient: reset at his Birdsong; a load mid-turn starts it again at 0).</summary>
        private int _guardBattlesThisTurn;

        public string PlanId => Ids.BotPlans.Esquie;

        /// <summary>goalModeTrigger: always target-driven; goal mode while a claimable rock is within range or in the endgame.</summary>
        public bool InGoalMode(BotView view)
        {
            if (Endgame(view) != null) return true;
            var plan = Target(view);
            return plan.Rock.HasValue || plan.Decoy;
        }

        public void PlanTurn(BotView view, ModState state)
        {
            _guardBattlesThisTurn = 0;
            state.EsquieTarget = Plan(view).Rock;
        }

        /// <summary>The plan he follows this turn: the endgame's last rock, otherwise step 1's target.</summary>
        public EsquieTargetPlan Plan(BotView view) => Endgame(view) ?? Target(view);

        // ------------------------------------------------------------ helpers

        private static RockState[] Rocks(BotView v) => v.State.EsquieRocks;
        public static int HeldCount(BotView v) => Rocks(v).Count(r => r.Status == RockStatus.Held);
        private static IReadOnlyList<ItemState> Items(BotView v) => v.Board.Items(v.Seat);

        public static bool TorchReady(BotView v) => Items(v).Any(i => i.Type == ItemType.Torch && !i.Damaged && !i.Exhausted);

        public static bool CorvidWarriorsIn(BotView v, ClearingId c) => EsquieTweaks.CorvidWarriorsIn(v.Board, c);

        /// <summary>Path distances from a clearing (paths only).</summary>
        private static Dictionary<ClearingId, int> From(BotView v, ClearingId c) => Graph.Distances(c, x => v.Board.PathNeighbours(x));

        /// <summary>Path distance from his pawn: from a forest, from the nearest clearing touching it, +1.</summary>
        public static int Distance(BotView v, ClearingId to)
        {
            var b = v.Board;
            var dist = From(v, to);
            int D(ClearingId c) => dist.TryGetValue(c, out var d) ? d : Unreachable;
            var p = b.PawnClearing(v.Seat);
            if (p.HasValue) return D(p.Value);
            var f = b.PawnForest(v.Seat);
            if (!f.HasValue) return Unreachable;
            var best = b.ForestClearings(f.Value).Select(D).DefaultIfEmpty(Unreachable).Min();
            return best >= Unreachable ? Unreachable : best + 1;
        }

        /// <summary>Where a rock is reached: its clearing, Verso's clearing (withVerso; none while Verso is in a forest) or the
        /// Monolith (heldByRuler). Null for a rock not spawned or held.</summary>
        public static ClearingId? Destination(BotView v, int i)
        {
            var r = Rocks(v)[i];
            switch (r.Status)
            {
                case RockStatus.Loose:
                case RockStatus.Hidden:
                case RockStatus.Guarded:
                    return r.Clearing;
                case RockStatus.WithVerso:
                    return r.HostSeat.HasValue ? v.Board.PawnClearing(r.HostSeat.Value) : null;
                case RockStatus.HeldByRuler:
                    return v.Role(Ids.MapRoles.Monolith);
                default:
                    return null;
            }
        }

        /// <summary>His max hits in a clearing: undamaged swords, plus tune.esquie-belly-bounce-swords for an undamaged Bag in a
        /// guarded-rock clearing.</summary>
        public static int MaxHits(BotView v, ClearingId c)
        {
            var swords = Items(v).Count(i => i.Type == ItemType.Sword && !i.Damaged);
            var bounce = EsquieTweaks.GuardedRockAt(v.State, c) >= 0 && EsquieTweaks.HasUndamagedBag(v.Board, v.Seat) ? Tuning.EsquieBellyBounceSwords : 0;
            return swords + bounce;
        }

        /// <summary>E[min(higher of two 0-3 dice, max hits)]: his expected hits in one battle as the attacker.</summary>
        public static double ExpectedRollHits(int maxHits)
        {
            double e = 0;
            for (var a = 0; a < 4; a++) for (var b = 0; b < 4; b++) e += Math.Min(Math.Max(a, b), Math.Max(0, maxHits));
            return e / 16.0;
        }

        private static bool HasCardFor(BotView v, ClearingId c)
        {
            var hand = v.Board.HandSuits(v.Seat);
            return hand.Contains(Suit.Bird) || v.Board.SuitsOf(c).Any(s => hand.Contains(s));
        }

        private static bool NeedsMonolithCard(BotView v, int i)
        {
            if (i != EsquieTweaks.Soarrie || Rocks(v)[i].Status != RockStatus.HeldByRuler) return false;
            var m = v.Role(Ids.MapRoles.Monolith);
            return m.HasValue && v.Board.Ruler(m.Value).HasValue && !HasCardFor(v, m.Value);
        }

        /// <summary>The cost of bot.esquie.01-target: path distance from his pawn + tune.esquie-bot-guard-weight x GuardsLeft - his
        /// max hits there (Soarrie: distance to Verso's clearing or to the Monolith). <see cref="Unreachable"/> when it cannot be reached.</summary>
        public static int TargetCost(BotView v, int i)
        {
            var dest = Destination(v, i);
            if (!dest.HasValue) return Unreachable;
            var d = Distance(v, dest.Value);
            if (d >= Unreachable) return Unreachable;
            return d + Tuning.EsquieBotGuardWeight * Rocks(v)[i].GuardsLeft - MaxHits(v, dest.Value);
        }

        /// <summary>Tier of a claimable rock in step 1's order (0 loose, 1 Dorrie with a torch ready, 2 guarded rocks and Soarrie),
        /// or -1 when he cannot claim it now.</summary>
        private static int Tier(BotView v, int i)
        {
            var r = Rocks(v)[i];
            var dest = Destination(v, i);
            if (!dest.HasValue) return -1;
            if (i == EsquieTweaks.Dorrie && (r.Status == RockStatus.Loose || r.Status == RockStatus.Hidden) && CorvidWarriorsIn(v, dest.Value)) return -1;
            switch (r.Status)
            {
                case RockStatus.Loose: return 0;
                case RockStatus.Hidden: return i == EsquieTweaks.Dorrie && TorchReady(v) ? 1 : -1;
                case RockStatus.Guarded:
                case RockStatus.WithVerso:
                case RockStatus.HeldByRuler: return 2;
                default: return -1;
            }
        }

        private static EsquieTargetPlan PlanFor(BotView v, int i, EsquieTargetPlan plan)
        {
            plan.Rock = i;
            plan.Destination = Destination(v, i);
            plan.Distance = plan.Destination.HasValue ? Distance(v, plan.Destination.Value) : Unreachable;
            plan.NeedsMonolithCard = NeedsMonolithCard(v, i);
            return plan;
        }

        // ------------------------------------------------------------ 01 Target

        /// <summary>bot.esquie.01-target: loose rocks by path distance, then Dorrie when an undamaged, unexhausted torch is ready
        /// and no Corvid warriors are in Old Lumière, then guarded rocks and Soarrie by cost; ties Florrie, Dorrie, Soarrie, Urrie.
        /// He chases the best one within tune.esquie-bot-rock-range moves. Easy walks to the François decoy at the Nest once in
        /// Act I while state.esquie.decoy is true.</summary>
        [BotRule("bot.esquie.01-target")]
        public EsquieTargetPlan Target(BotView view)
        {
            var plan = new EsquieTargetPlan();
            var claimable = new List<(int rock, int tier, int cost, int dist)>();
            for (var i = 0; i < Rocks(view).Length; i++)
            {
                var tier = Tier(view, i);
                if (tier < 0) continue;
                var dest = Destination(view, i).Value;
                var dist = Distance(view, dest);
                if (dist >= Unreachable) continue;
                claimable.Add((i, tier, tier == 2 ? TargetCost(view, i) : dist, dist));
            }
            plan.Ranking = Rank.Descending(claimable, x => -(x.tier * 1000.0 + x.cost), x => x.rock).Select(x => new Ranked<int>(x.Item.rock, x.Score)).ToList();

            if (view.Difficulty == Difficulty.Easy && view.Act <= 1 && view.State.EsquieDecoy)
            {
                var nest = view.Role(Ids.MapRoles.EsquiesNest);
                if (nest.HasValue)
                {
                    plan.Decoy = true;
                    plan.Destination = nest;
                    plan.Distance = Distance(view, nest.Value);
                    return plan;
                }
            }
            var inRange = plan.Ranking.Select(r => r.Item).Where(i => claimable.First(x => x.rock == i).dist <= Tuning.EsquieBotRockRange).ToList();
            if (inRange.Count > 0) PlanFor(view, inRange[0], plan);
            return plan;
        }

        // ------------------------------------------------------------ 02 Gear

        /// <summary>bot.esquie.02-gear: repair in a forest at tune.esquie-bot-repair-damaged damaged items (before any guard
        /// battle); with a guarded target and no undamaged sword (a guard battle needs one), craft a sword if one is craftable,
        /// otherwise Aid the nearest faction holding a crafted sword (ties lower clearing number). Hard crafts a sword as soon as
        /// one is craftable.</summary>
        [BotRule("bot.esquie.02-gear")]
        public EsquieGearOrder Gear(BotView view)
        {
            var b = view.Board;
            var order = new EsquieGearOrder { Repair = Items(view).Count(i => i.Damaged) >= Tuning.EsquieBotRepairDamaged };
            var plan = Plan(view);
            if (plan.Rock.HasValue && Rocks(view)[plan.Rock.Value].Status == RockStatus.Guarded)
                order.SeekSword = !EsquieTweaks.HasUndamagedSword(b, view.Seat);
            var craftable = EsquieAction.ParseAll(b.LegalActions(view.Seat)).Any(a => a.Verb == EsquieAction.Craft && a.Item == ItemType.Sword);
            order.CraftSword = craftable && (order.SeekSword || view.Difficulty == Difficulty.Hard);
            if (order.SeekSword && !craftable)
            {
                var best = b.Seats().Where(s => s != view.Seat && b.CraftedItems(s).Contains(ItemType.Sword))
                    .SelectMany(s => b.Clearings().Where(c => b.Warriors(s, c) + b.Buildings(s, c) + b.Tokens(s, c) > 0)
                        .Select(c => (seat: s, c, d: Distance(view, c))))
                    .Where(x => x.d < Unreachable)
                    .OrderBy(x => x.d).ThenBy(x => x.c.Value).ThenBy(x => x.seat.Value).ToList();
                if (best.Count > 0)
                {
                    order.AidFor = best[0].seat;
                    order.AidAt = best[0].c;
                }
            }
            return order;
        }

        // ------------------------------------------------------------ 03 Hunt

        /// <summary>bot.esquie.03-hunt: for a guarded target, whether he enters and battles the neutral guards: only with every
        /// item undamaged and an undamaged sword, and when the expected hits of the guard battles he may still take this turn (tune.esquie-bot-guard-battles,
        /// Hard tune.esquie-hard-guard-battles, minus <paramref name="guardBattlesTaken"/>) reach GuardsLeft - 1. Null when the
        /// target is not a guarded rock.</summary>
        [BotRule("bot.esquie.03-hunt")]
        public EsquieHuntOrder Hunt(BotView view, int guardBattlesTaken = 0)
        {
            var plan = Plan(view);
            if (!plan.Rock.HasValue) return null;
            var r = Rocks(view)[plan.Rock.Value];
            if (r.Status != RockStatus.Guarded || !r.Clearing.HasValue) return null;
            var perTurn = view.Difficulty == Difficulty.Hard ? Tuning.EsquieHardGuardBattles : Tuning.EsquieBotGuardBattles;
            var o = new EsquieHuntOrder
            {
                Rock = plan.Rock.Value,
                Clearing = r.Clearing.Value,
                GuardsLeft = r.GuardsLeft,
                Battles = Math.Max(0, perTurn - guardBattlesTaken),
                MaxHits = MaxHits(view, r.Clearing.Value),
                AllItemsUndamaged = Items(view).All(i => !i.Damaged),
                HasSword = EsquieTweaks.HasUndamagedSword(view.Board, view.Seat),
            };
            o.ExpectedHits = o.Battles * ExpectedRollHits(o.MaxHits);
            o.Go = o.AllItemsUndamaged && o.HasSword && o.Battles > 0 && o.MaxHits > 0 && o.ExpectedHits >= o.GuardsLeft - 1;
            return o;
        }

        // ------------------------------------------------------------ 04 Endgame

        /// <summary>bot.esquie.04-endgame: with tune.esquie-endgame-rocks rocks held, the last rock is the target whatever its
        /// distance (shortest path to it, to Verso's clearing or to the Monolith). Null before the endgame.</summary>
        [BotRule("bot.esquie.04-endgame")]
        public EsquieTargetPlan Endgame(BotView view)
        {
            var held = HeldCount(view);
            if (held < Tuning.EsquieEndgameRocks || held >= Tuning.EsquieRocks) return null;
            var rocks = Rocks(view);
            for (var i = 0; i < rocks.Length; i++)
            {
                if (rocks[i].Status == RockStatus.Held || rocks[i].Status == RockStatus.NotSpawned) continue;
                var plan = PlanFor(view, i, new EsquieTargetPlan());
                plan.Ranking = new List<Ranked<int>> { new Ranked<int>(i, -plan.Distance) };
                return plan;
            }
            return null;
        }

        // ------------------------------------------------------------ 05 Vagabond VP

        /// <summary>bot.esquie.05-vp: true when no claimable rock is within tune.esquie-bot-rock-range moves (and he is not in the
        /// endgame or on Easy's decoy walk): the step claims no decision and Root's Vagabond AI plays for VP.</summary>
        [BotRule("bot.esquie.05-vp")]
        public bool VagabondVp(BotView view)
        {
            if (Endgame(view) != null) return false;
            var plan = Target(view);
            return !plan.Rock.HasValue && !plan.Decoy;
        }

        // ------------------------------------------------------------ 06 Spoiler layer

        /// <summary>Whether he can complete win.esquie this turn: gates pass, and the last missing rock can be claimed this turn
        /// within the moves his undamaged, unexhausted boots allow (a guarded last rock never counts).</summary>
        public bool CanFinishQuestThisTurn(BotView view)
        {
            var b = view.Board;
            var row = Data.WinConditions[Ids.WinConditions.Esquie];
            if (view.Act < row.ActMin) return false;
            if (row.OffInCoalition && b.CoalitionPartner(view.Seat).HasValue) return false;
            if (row.OffWithDominance && b.ActiveDominance(view.Seat).HasValue) return false;
            var held = HeldCount(view);
            if (held >= Tuning.EsquieRocks) return true;
            if (held != Tuning.EsquieRocks - 1) return false;
            var reach = Items(view).Count(i => i.Type == ItemType.Boots && !i.Damaged && !i.Exhausted);
            var rocks = Rocks(view);
            for (var i = 0; i < rocks.Length; i++)
            {
                var r = rocks[i];
                if (r.Status == RockStatus.Held) continue;
                var dest = Destination(view, i);
                if (!dest.HasValue || Distance(view, dest.Value) > reach) return false;
                switch (r.Status)
                {
                    case RockStatus.Loose:
                        return Tier(view, i) == 0;
                    case RockStatus.Hidden:
                        return Tier(view, i) == 1;
                    case RockStatus.WithVerso:
                        return true;
                    case RockStatus.HeldByRuler:
                        return !b.Ruler(dest.Value).HasValue || HasCardFor(view, dest.Value);
                    default:
                        return false;
                }
            }
            return false;
        }

        /// <summary>bot.esquie.06-spoiler: the most urgent threat (banners first, then the next Birdsong in turn order, then higher
        /// VP) and his pawn answer: the Monolith against the White-Haired Man, the Monolith before Verso's banner, the nearest
        /// Sanctuary before Sciel's banner; no answer otherwise (Never Mad: he never attacks to spoil). Pre-empts the steps for a
        /// banner (Normal, Hard) and on Hard for a one-step threat, unless he can finish his own quest win this turn. Easy answers
        /// banners only, with free actions. Null with no trigger.</summary>
        [BotRule("bot.esquie.06-spoiler")]
        public EsquieSpoilerAnswer Spoiler(BotView view)
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
            var finish = CanFinishQuestThisTurn(view);
            var pre = !finish && view.Difficulty != Difficulty.Easy && (t.banner || view.Difficulty == Difficulty.Hard);
            return new EsquieSpoilerAnswer { Threat = t.seat, Banner = t.banner, PreEmpts = pre, Destination = finish ? null : SpoilerDestination(view, t.seat, t.banner) };
        }

        private static ClearingId? SpoilerDestination(BotView view, SeatId threat, bool banner)
        {
            var who = view.CharacterOf(threat);
            var monolith = view.Role(Ids.MapRoles.Monolith);
            if (who == CharacterIds.WhiteHairedMan) return monolith;
            if (who == CharacterIds.Verso && !banner) return monolith;
            if (who == CharacterIds.Sciel && !banner)
                return view.KeyClearings(threat).Select(c => (c, d: Distance(view, c))).Where(x => x.d < Unreachable)
                    .OrderBy(x => x.d).ThenBy(x => x.c.Value).Select(x => (ClearingId?)x.c).FirstOrDefault();
            return null;
        }

        // ------------------------------------------------------------ ChooseAction

        public string ChooseAction(BotView view, IReadOnlyList<string> legalActions)
        {
            var acts = EsquieAction.ParseAll(legalActions ?? new string[0]);
            var b = view.Board;
            var here = b.PawnClearing(view.Seat);
            var guarded = new HashSet<ClearingId>(Rocks(view).Where(r => r.Status == RockStatus.Guarded && r.Clearing.HasValue).Select(r => r.Clearing.Value));

            // 06 pre-empting: a banner up (or on Hard a one-step threat), unless his own quest win can finish this turn.
            var spoiler = Spoiler(view);
            if (spoiler != null && spoiler.PreEmpts && spoiler.Destination.HasValue)
            {
                var k = here == spoiler.Destination ? Stay(acts) : Toward(view, acts, spoiler.Destination.Value, guarded);
                if (k != null) return k;
            }

            var plan = Plan(view);

            // 02 gear.
            var gear = Gear(view);
            if (gear.Repair)
            {
                if (b.PawnForest(view.Seat).HasValue) return Stay(acts);
                var k = IntoForest(view, acts, plan.Destination);
                if (k != null) return k;
            }
            if (gear.CraftSword || gear.SeekSword || view.Difficulty == Difficulty.Hard)
            {
                var k = acts.FirstOrDefault(a => a.Verb == EsquieAction.Craft && a.Item == ItemType.Sword)?.Key;
                if (k != null) return k;
            }
            if (gear.SeekSword && gear.AidFor.HasValue && gear.AidAt.HasValue)
            {
                var k = AidKey(acts, here, gear.AidFor.Value, ItemType.Sword) ?? Toward(view, acts, gear.AidAt.Value, guarded);
                if (k != null) return k;
            }

            // 03 hunt.
            var hunt = gear.SeekSword ? null : Hunt(view, _guardBattlesThisTurn);
            if (hunt != null && hunt.Go)
            {
                if (here == hunt.Clearing)
                {
                    var k = acts.FirstOrDefault(a => a.Verb == EsquieAction.Battle && a.AgainstGuards && a.Clearing == hunt.Clearing)?.Key;
                    if (k != null)
                    {
                        _guardBattlesThisTurn++;
                        return k;
                    }
                }
                else
                {
                    var allowed = new HashSet<ClearingId>(guarded);
                    allowed.Remove(hunt.Clearing);
                    var k = Toward(view, acts, hunt.Clearing, allowed);
                    if (k != null) return k;
                }
            }

            // 01 / 04: walk to the target (Explore Dorrie, Aid the Monolith's ruler for Soarrie, collect a card first).
            if (plan.Destination.HasValue && (plan.Rock.HasValue || plan.Decoy))
            {
                var dest = plan.Destination.Value;
                if (plan.Rock.HasValue && here == dest)
                {
                    var r = Rocks(view)[plan.Rock.Value];
                    if (plan.Rock.Value == EsquieTweaks.Dorrie && r.Status == RockStatus.Hidden)
                    {
                        var k = acts.FirstOrDefault(a => a.Verb == EsquieAction.Explore && (!a.Clearing.HasValue || a.Clearing == dest))?.Key;
                        if (k != null) return k;
                    }
                    if (plan.Rock.Value == EsquieTweaks.Soarrie && r.Status == RockStatus.HeldByRuler && b.Ruler(dest).HasValue)
                    {
                        var k = AidKey(acts, here, b.Ruler(dest).Value, null);
                        if (k != null) return k;
                    }
                }
                if (plan.NeedsMonolithCard)
                {
                    var k = acts.FirstOrDefault(a => a.Verb == EsquieAction.DayLabor)?.Key;
                    if (k != null) return k;
                }
                if (here != dest)
                {
                    var k = Toward(view, acts, dest, guarded);
                    if (k != null) return k;
                }
            }

            // 05: no claimable rock in range: Root's Vagabond AI plays for VP; the spoiler layer may use the free move.
            if (spoiler != null && !spoiler.PreEmpts && spoiler.Destination.HasValue && here != spoiler.Destination)
            {
                var k = Toward(view, acts, spoiler.Destination.Value, guarded);
                if (k != null) return k;
            }
            return null;
        }

        private static string Stay(IEnumerable<EsquieAction> acts) => acts.FirstOrDefault(a => a.IsStay)?.Key;

        private static string AidKey(IEnumerable<EsquieAction> acts, ClearingId? here, SeatId friend, ItemType? item)
        {
            var aids = acts.Where(a => a.Verb == EsquieAction.Aid && a.Target == friend && (!a.Clearing.HasValue || a.Clearing == here)).ToList();
            return (item.HasValue ? aids.FirstOrDefault(a => a.Item == item) : null)?.Key ?? aids.FirstOrDefault()?.Key;
        }

        /// <summary>The legal move that brings his pawn strictly closer to <paramref name="dest"/>, never into a clearing of
        /// <paramref name="forbidden"/> (guarded-rock clearings he is not hunting, even when one is the destination); ties lower
        /// clearing number.</summary>
        private static string Toward(BotView view, IEnumerable<EsquieAction> acts, ClearingId dest, ICollection<ClearingId> forbidden)
        {
            var dist = From(view, dest);
            var now = Distance(view, dest);
            return acts.Where(a => a.IsMove && a.To.HasValue && !forbidden.Contains(a.To.Value))
                .Select(a => (a, d: dist.TryGetValue(a.To.Value, out var d) ? d : Unreachable))
                .Where(x => x.d < now).OrderBy(x => x.d).ThenBy(x => x.a.To.Value.Value).Select(x => x.a.Key).FirstOrDefault();
        }

        /// <summary>A legal move into a forest, the one nearest <paramref name="dest"/> (ties lower forest number).</summary>
        private static string IntoForest(BotView view, IEnumerable<EsquieAction> acts, ClearingId? dest)
        {
            var dist = dest.HasValue ? From(view, dest.Value) : new Dictionary<ClearingId, int>();
            int ForestDist(int f) => dest.HasValue
                ? view.Board.ForestClearings(f).Select(c => dist.TryGetValue(c, out var d) ? d : Unreachable).DefaultIfEmpty(Unreachable).Min()
                : 0;
            return acts.Where(a => a.IsMove && a.ToForest.HasValue).OrderBy(a => ForestDist(a.ToForest.Value)).ThenBy(a => a.ToForest.Value)
                .Select(a => a.Key).FirstOrDefault();
        }

        /// <summary>tweak.esquie.start-near-nest: of the allowed forests touching the Nest, the one closest to Stone Wave Cliffs
        /// (ties lower forest number).</summary>
        public static int? StartForest(BotView view)
        {
            var nest = view.Role(Ids.MapRoles.EsquiesNest);
            if (!nest.HasValue) return null;
            var cliffs = view.Role(Ids.MapRoles.StoneWaveCliffs);
            var dist = cliffs.HasValue ? From(view, cliffs.Value) : new Dictionary<ClearingId, int>();
            return view.Board.ForestsAround(nest.Value)
                .OrderBy(f => view.Board.ForestClearings(f).Select(c => dist.TryGetValue(c, out var d) ? d : Unreachable).DefaultIfEmpty(Unreachable).Min())
                .ThenBy(f => f).Select(f => (int?)f).FirstOrDefault();
        }
    }
}
