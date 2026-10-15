using System;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Esquie
{
    /// <summary>
    /// Esquie's rule tweaks (tweak.esquie.*) and spawns (spawn.florrie, spawn.dorrie, spawn.soarrie, spawn.urrie,
    /// spawn.francois-decoy). Everything lives in state.esquie.rocks / state.esquie.decoy; nothing in fields.
    /// </summary>
    public static class EsquieTweaks
    {
        public const int Florrie = 0, Dorrie = 1, Soarrie = 2, Urrie = 3;

        /// <summary>do.grant-ability keys, as the board_actions row names them.</summary>
        public const string AbilityRiversAsPaths = "rivers-as-paths", AbilityNoHostileBoot = "no-hostile-boot", AbilityGlide = "glide";

        // ------------------------------------------------------------ queries

        public static int HeldCount(ModState s) => s.EsquieRocks.Count(r => r.Status == RockStatus.Held);

        /// <summary>Index of the guarded rock in a clearing, or -1.</summary>
        public static int GuardedRockAt(ModState s, ClearingId c)
        {
            for (var i = 0; i < s.EsquieRocks.Length; i++)
                if (s.EsquieRocks[i].Status == RockStatus.Guarded && s.EsquieRocks[i].Clearing == c) return i;
            return -1;
        }

        /// <summary>tweak.esquie.guards-never-count: GuardsLeft of the guarded rocks <paramref name="host"/> hosts in a clearing.</summary>
        public static int HostedGuards(ModState s, SeatId host, ClearingId c) =>
            s.EsquieRocks.Where(r => r.Status == RockStatus.Guarded && r.HostSeat == host && r.Clearing == c).Sum(r => r.GuardsLeft);

        /// <summary>A host's warriors in a clearing that count toward its own themed win (guards excluded).</summary>
        public static int OwnWarriors(GameContext ctx, SeatId host, ClearingId c) =>
            Math.Max(0, ctx.Board.Warriors(host, c) - HostedGuards(ctx.State, host, c));

        /// <summary>No-spawn fallback: tune.esquie-guard-spawns is 0 or the plugin cannot place another seat's warriors.</summary>
        public static bool NoSpawnFallback(GameContext ctx) =>
            Tuning.EsquieGuardSpawns <= 0 || !ctx.SupportsAction(Ids.BoardActions.PlaceWarriors);

        public static bool CorvidWarriorsIn(IBoardView b, ClearingId c) =>
            b.Seats().Any(s => b.EngineOf(s) == EngineId.Corvids && b.Warriors(s, c) >= 1);

        private static bool OtherWarriorsIn(IBoardView b, SeatId me, ClearingId c) => b.Seats().Any(s => s != me && b.Warriors(s, c) > 0);

        /// <summary>Where a not-yet-held rock can be reached: its clearing, Verso's clearing (withVerso) or the Monolith (heldByRuler).</summary>
        public static ClearingId? RockSite(GameContext ctx, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            if (r.Status == RockStatus.WithVerso) return r.HostSeat.HasValue ? ctx.Board.PawnClearing(r.HostSeat.Value) : null;
            if (r.Status == RockStatus.HeldByRuler) return ctx.Role(Ids.MapRoles.Monolith);
            return r.Status == RockStatus.Held || r.Status == RockStatus.NotSpawned ? null : r.Clearing;
        }

        /// <summary>The Nevron host: Clea if seated; otherwise the non-Marquise, non-Vagabond faction with the most warriors in
        /// supply (ties: lowest VP, then lowest seat); otherwise the Marquise.</summary>
        public static SeatId? ChooseHost(GameContext ctx)
        {
            var clea = ctx.SeatOf(CharacterIds.Clea);
            if (clea.HasValue) return clea;
            var b = ctx.Board;
            var cands = b.Seats().Where(s => b.EngineOf(s) != EngineId.Marquise && !ctx.IsVagabond(s)).ToList();
            if (cands.Count > 0)
                return cands.OrderByDescending(s => b.SupplyWarriors(s)).ThenBy(s => b.Vp(s)).ThenBy(s => s.Value).First();
            foreach (var s in b.Seats()) if (b.EngineOf(s) == EngineId.Marquise) return s;
            return null;
        }

        // ------------------------------------------------------------ setup and acts

        /// <summary>tweak.esquie.start-near-nest.</summary>
        public static void StartNearNest(GameContext ctx, SeatId me)
        {
            var nest = ctx.Role(Ids.MapRoles.EsquiesNest);
            if (nest.HasValue) ctx.Actions.RestrictStartForest(me, ctx.Board.ForestsAround(nest.Value));
        }

        /// <summary>hook.setup-complete: Florrie under her guards, the François decoy at the Nest.</summary>
        public static void Setup(GameContext ctx, SeatId me)
        {
            SpawnGuarded(ctx, me, Florrie, Ids.MapRoles.StoneWaveCliffs, Tuning.EsquieGuardsFlorrie);
            var nest = ctx.Role(Ids.MapRoles.EsquiesNest);
            if (ctx.Act <= 1 && nest.HasValue)
            {
                ctx.State.EsquieDecoy = true;
                ctx.Actions.ShowOverlay(Ids.Overlays.EsquieFrancoisDecoy, me, nest, null);
            }
        }

        /// <summary>hook.act-change: Dorrie and Soarrie from Act II, Urrie from Act III; the decoy leaves at Act II.</summary>
        public static void ActChanged(GameContext ctx, SeatId me, int act)
        {
            var rocks = ctx.State.EsquieRocks;
            if (act >= 2)
            {
                RemoveDecoy(ctx, me);
                var d = rocks[Dorrie];
                var ol = ctx.Role(Ids.MapRoles.OldLumiere);
                if (d.Status == RockStatus.NotSpawned && ol.HasValue)
                {
                    d.Clearing = ol;
                    d.Status = ctx.Board.HasRuin(ol.Value) ? RockStatus.Hidden : RockStatus.Loose;
                    ctx.Actions.ShowOverlay(Ids.Overlays.EsquieRocks, me, ol, null);
                }
                var s = rocks[Soarrie];
                if (s.Status == RockStatus.NotSpawned)
                {
                    var verso = ctx.SeatOf(CharacterIds.Verso);
                    s.Clearing = null;
                    s.HostSeat = verso;
                    s.Status = verso.HasValue ? RockStatus.WithVerso : RockStatus.HeldByRuler;
                }
            }
            if (act >= 3) SpawnGuarded(ctx, me, Urrie, Ids.MapRoles.EsquiesNest, Tuning.EsquieGuardsUrrie);
        }

        private static void SpawnGuarded(GameContext ctx, SeatId me, int i, string roleId, int guards)
        {
            var r = ctx.State.EsquieRocks[i];
            var c = ctx.Role(roleId);
            if (r.Status != RockStatus.NotSpawned || !c.HasValue) return;
            r.Clearing = c;
            r.HostSeat = null;
            r.GuardsLeft = 0;
            r.Status = RockStatus.Loose;
            ctx.Actions.ShowOverlay(Ids.Overlays.EsquieRocks, me, c, null);
            if (NoSpawnFallback(ctx)) return;
            var host = ChooseHost(ctx);
            var placed = host.HasValue ? ctx.Actions.PlaceWarriors(host.Value, c.Value, guards) : 0;
            if (placed <= 0) return;
            r.HostSeat = host;
            r.GuardsLeft = Math.Min(placed, guards);
            r.Status = RockStatus.Guarded;
            ctx.Actions.ShowOverlay(Badge(i), host, c, null);
            SendGuardRules(ctx, r);
        }

        private static string Badge(int i) => i == Florrie ? Ids.Overlays.EsquieLampmasterBadge : Ids.Overlays.EsquieFrancoisBadge;

        /// <summary>tweak.esquie.guard-floor and tweak.esquie.guard-no-placement for one guarded rock.</summary>
        private static void SendGuardRules(GameContext ctx, RockState r)
        {
            if (r.Status != RockStatus.Guarded || !r.HostSeat.HasValue || !r.Clearing.HasValue) return;
            ctx.Actions.SetGuardFloor(r.HostSeat.Value, r.Clearing.Value, r.GuardsLeft);
            ctx.Actions.BlockPlacement(r.HostSeat.Value, r.Clearing.Value, true);
        }

        private static void MakeLoose(GameContext ctx, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            if (r.HostSeat.HasValue && r.Clearing.HasValue)
            {
                ctx.Actions.SetGuardFloor(r.HostSeat.Value, r.Clearing.Value, 0);
                ctx.Actions.BlockPlacement(r.HostSeat.Value, r.Clearing.Value, false);
                ctx.Actions.HideOverlay(Badge(i), r.HostSeat, r.Clearing, null);
            }
            r.Status = RockStatus.Loose;
            r.HostSeat = null;
            r.GuardsLeft = 0;
        }

        private static void RemoveDecoy(GameContext ctx, SeatId me)
        {
            if (!ctx.State.EsquieDecoy) return;
            ctx.State.EsquieDecoy = false;
            ctx.Actions.HideOverlay(Ids.Overlays.EsquieFrancoisDecoy, me, ctx.Role(Ids.MapRoles.EsquiesNest), null);
        }

        // ------------------------------------------------------------ claims

        private static void Claim(GameContext ctx, SeatId me, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            if (r.Status == RockStatus.Held) return;
            if (r.Status == RockStatus.Guarded) MakeLoose(ctx, i);
            ctx.Actions.HideOverlay(Ids.Overlays.EsquieRocks, me, r.Clearing, null);
            r.Status = RockStatus.Held;
            r.Clearing = null;
            r.HostSeat = null;
            r.GuardsLeft = 0;
            GrantPower(ctx, me, i);
            CheckEarlyUrrie(ctx, me);
        }

        /// <summary>The v1.1 rule_tweaks row of each rock's power, in rock order (Urrie's power is presentation only).</summary>
        private static readonly string[] PowerRows =
            { Ids.RuleTweaks.EsquieFlorrieSwim, Ids.RuleTweaks.EsquieDorrieCoral, Ids.RuleTweaks.EsquieSoarrieGlide };

        /// <summary>tweak.esquie.florrie-swim / dorrie-coral / soarrie-glide (v1.1): do.grant-ability, only while the update
        /// rules are on (<see cref="GameContext.RuleOn"/>), and left out when the plugin cannot grant abilities. Either way the
        /// rocks still count for the win.</summary>
        private static void GrantPower(GameContext ctx, SeatId me, int i)
        {
            if (i < PowerRows.Length && !ctx.RuleOn(PowerRows[i])) return;
            if (!ctx.SupportsAction(Ids.BoardActions.GrantAbility)) return;
            if (i == Florrie) ctx.Actions.GrantAbility(me, AbilityRiversAsPaths);
            else if (i == Dorrie) ctx.Actions.GrantAbility(me, AbilityNoHostileBoot);
            else if (i == Soarrie) ctx.Actions.GrantAbility(me, AbilityGlide);
        }

        /// <summary>spawn.urrie: as soon as Esquie holds the other three rocks.</summary>
        public static void CheckEarlyUrrie(GameContext ctx, SeatId me)
        {
            var rocks = ctx.State.EsquieRocks;
            if (rocks[Urrie].Status == RockStatus.NotSpawned && rocks[Florrie].Status == RockStatus.Held &&
                rocks[Dorrie].Status == RockStatus.Held && rocks[Soarrie].Status == RockStatus.Held)
                SpawnGuarded(ctx, me, Urrie, Ids.MapRoles.EsquiesNest, Tuning.EsquieGuardsUrrie);
        }

        /// <summary>A loose rock in clearing c may be picked up now (Corvid block on Dorrie; no-spawn fallback needs a free clearing).</summary>
        public static bool CanPickUp(GameContext ctx, SeatId me, int i, ClearingId c)
        {
            if (i == Dorrie && CorvidWarriorsIn(ctx.Board, c)) return false;
            if ((i == Florrie || i == Urrie) && NoSpawnFallback(ctx) && OtherWarriorsIn(ctx.Board, me, c)) return false;
            return true;
        }

        /// <summary>hook.action-resolved (any seat): Dorrie turns loose once Old Lumière's ruin is empty; after Esquie's own
        /// actions he picks up every loose rock in his pawn's clearing, and a heldByRuler Soarrie in an unruled Monolith.</summary>
        public static void AfterAction(GameContext ctx, SeatId me, SeatId actor)
        {
            var rocks = ctx.State.EsquieRocks;
            var d = rocks[Dorrie];
            if (d.Status == RockStatus.Hidden && d.Clearing.HasValue && !ctx.Board.HasRuin(d.Clearing.Value)) d.Status = RockStatus.Loose;
            if (actor != me) return;
            var p = ctx.Board.PawnClearing(me);
            if (!p.HasValue) return;
            for (var i = 0; i < rocks.Length; i++)
            {
                var r = rocks[i];
                if (r.Status == RockStatus.Loose && r.Clearing == p && CanPickUp(ctx, me, i, p.Value)) Claim(ctx, me, i);
                else if (i == Soarrie && r.Status == RockStatus.HeldByRuler && ctx.Role(Ids.MapRoles.Monolith) == p &&
                         !ctx.Board.Ruler(p.Value).HasValue) Claim(ctx, me, i);
            }
        }

        /// <summary>hook.explore-resolved: Esquie's Explore of Old Lumière claims Dorrie (loose instead while Corvid warriors are
        /// there); another Vagabond's Explore that empties the ruin makes it loose.</summary>
        public static void ExploreResolved(GameContext ctx, SeatId me, SeatId vagabond, ClearingId c)
        {
            var d = ctx.State.EsquieRocks[Dorrie];
            if (ctx.Act < 2 || d.Clearing != c || (d.Status != RockStatus.Hidden && d.Status != RockStatus.Loose)) return;
            if (vagabond == me)
            {
                if (CorvidWarriorsIn(ctx.Board, c)) d.Status = RockStatus.Loose;
                else Claim(ctx, me, Dorrie);
            }
            else if (!ctx.Board.HasRuin(c)) d.Status = RockStatus.Loose;
        }

        /// <summary>hook.aid-resolved: Aiding the Monolith's ruler while in the Monolith claims a heldByRuler Soarrie.</summary>
        public static void AidResolved(GameContext ctx, SeatId me, SeatId vagabond, SeatId receiver, ClearingId c)
        {
            var s = ctx.State.EsquieRocks[Soarrie];
            if (vagabond != me || s.Status != RockStatus.HeldByRuler) return;
            if (ctx.Role(Ids.MapRoles.Monolith) == c && ctx.Board.PawnClearing(me) == c && ctx.Board.Ruler(c) == receiver) Claim(ctx, me, Soarrie);
        }

        /// <summary>hook.evening-end for Esquie: tweak.verso.soarrie-handoff, carried out here as part of spawn.soarrie (Esquie's
        /// pawn in Verso's clearing: Soarrie leaves Verso's card and Verso scores), then the early-Urrie check.</summary>
        public static void EveningEnd(GameContext ctx, SeatId me)
        {
            var s = ctx.State.EsquieRocks[Soarrie];
            if (s.Status == RockStatus.WithVerso && s.HostSeat.HasValue)
            {
                var verso = s.HostSeat.Value;
                var mine = ctx.Board.PawnClearing(me);
                if (mine.HasValue && mine == ctx.Board.PawnClearing(verso))
                {
                    Claim(ctx, me, Soarrie);
                    ctx.ScoreVp(verso, Tuning.VersoSoarrieVp);
                    ctx.Actions.HideOverlay(Ids.Overlays.EsquieRocks, verso, null, null);
                }
            }
            CheckEarlyUrrie(ctx, me);
        }

        /// <summary>hook.piece-moved: Esquie entering the Nest in Act I while the decoy is up.</summary>
        public static void PieceMoved(GameContext ctx, SeatId me, SeatId owner, PieceKind kind, ClearingId? to)
        {
            if (owner != me || kind != PieceKind.Pawn || ctx.Act != 1 || !ctx.State.EsquieDecoy) return;
            if (!to.HasValue || ctx.Role(Ids.MapRoles.EsquiesNest) != to) return;
            ctx.Actions.ShowBubble(me, Ids.Barks.Esquie02);
            RemoveDecoy(ctx, me);
        }

        // ------------------------------------------------------------ battle tweaks

        /// <summary>Under the no-spawn fallback: index of an unclaimed (loose) Florrie or Urrie in a clearing, or -1.</summary>
        public static int FallbackRockAt(GameContext ctx, ClearingId c)
        {
            if (!NoSpawnFallback(ctx)) return -1;
            foreach (var i in new[] { Florrie, Urrie })
            {
                var r = ctx.State.EsquieRocks[i];
                if (r.Status == RockStatus.Loose && r.Clearing == c) return i;
            }
            return -1;
        }

        /// <summary>A clearing where Esquie's rock rules apply to battles: a guarded rock, or (no-spawn fallback) an unclaimed
        /// Florrie or Urrie.</summary>
        public static bool IsRockBattleClearing(GameContext ctx, ClearingId c) => GuardedRockAt(ctx.State, c) >= 0 || FallbackRockAt(ctx, c) >= 0;

        /// <summary>Whether he has an undamaged Bag (Belly Bounce).</summary>
        public static bool HasUndamagedBag(IBoardView b, SeatId me) => b.Items(me).Any(it => it.Type == ItemType.Bag && !it.Damaged);

        /// <summary>tweak.esquie.belly-bounce: an undamaged Bag counts as tune.esquie-belly-bounce-swords sword in a guarded-rock
        /// clearing (under the no-spawn fallback, in a clearing with an unclaimed Florrie or Urrie), however many Bags he has.</summary>
        public static void BellyBounce(GameContext ctx, SeatId me, SeatId attacker, SeatId defender, ClearingId c)
        {
            if (attacker != me && defender != me) return;
            if (!IsRockBattleClearing(ctx, c)) return;
            if (HasUndamagedBag(ctx.Board, me)) ctx.Actions.AddMaxHits(me, Tuning.EsquieBellyBounceSwords);
        }

        /// <summary>tweak.esquie.never-mad: only the host's warriors, in a guarded-rock clearing. Under the no-spawn fallback
        /// (no host) only in a clearing with an unclaimed Florrie or Urrie, against any faction's warriors there.</summary>
        public static bool NeverMadAllows(GameContext ctx, SeatId defender, ClearingId c)
        {
            var i = GuardedRockAt(ctx.State, c);
            if (i >= 0) return ctx.State.EsquieRocks[i].HostSeat == defender;
            return FallbackRockAt(ctx, c) >= 0 && ctx.Board.Warriors(defender, c) > 0;
        }

        /// <summary>tweak.esquie.friends-not-foes: Root's hostility change is skipped for battles in a clearing with a guarded
        /// rock (keyed to the clearing, so any Vagabond battling there).</summary>
        public static bool FriendsNotFoesAllows(ModState s, ClearingId battleClearing) => GuardedRockAt(s, battleClearing) < 0;

        /// <summary>hook.piece-removed: every host warrior removed from a guarded rock's clearing costs a guard, in a battle
        /// (whoever fought it) or any other way (Strike, Bomb, Revolt). This is the only place guards are counted, so a removal
        /// is counted once; hook.battle-resolved counts nothing.</summary>
        public static void PieceRemoved(GameContext ctx, RemovedPiece piece, ClearingId c)
        {
            if (piece.Kind != PieceKind.Warrior) return;
            var rocks = ctx.State.EsquieRocks;
            for (var i = 0; i < rocks.Length; i++)
            {
                var r = rocks[i];
                if (r.Status != RockStatus.Guarded || r.Clearing != c || r.HostSeat != piece.Owner) continue;
                r.GuardsLeft = Math.Max(0, r.GuardsLeft - 1);
                if (r.GuardsLeft == 0) MakeLoose(ctx, i);
                else SendGuardRules(ctx, r);
            }
        }

        // ------------------------------------------------------------ load

        /// <summary>state.esquie.rocks / decoy / target onLoad.</summary>
        public static void Loaded(GameContext ctx, SeatId me)
        {
            var rocks = ctx.State.EsquieRocks;
            for (var i = 0; i < rocks.Length; i++)
            {
                var r = rocks[i];
                if (r.Status == RockStatus.Held) GrantPower(ctx, me, i);
                if (r.Status != RockStatus.Guarded) continue;
                var now = r.HostSeat.HasValue && r.Clearing.HasValue ? ctx.Board.Warriors(r.HostSeat.Value, r.Clearing.Value) : 0;
                r.GuardsLeft = Math.Min(r.GuardsLeft, now);
                if (r.GuardsLeft <= 0) MakeLoose(ctx, i);
                else SendGuardRules(ctx, r);
            }
            if (ctx.Act >= 2) ctx.State.EsquieDecoy = false;
            ctx.State.EsquieTarget = null;
        }
    }
}
