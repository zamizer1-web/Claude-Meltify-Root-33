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

        /// <summary>No-spawn fallback: tune.esquie-guard-spawns is 0 or the plugin cannot place neutral guards.</summary>
        public static bool NoSpawnFallback(GameContext ctx) =>
            Tuning.EsquieGuardSpawns <= 0 || !ctx.SupportsAction(Ids.BoardActions.PlaceGuards);

        /// <summary>Whether he has an undamaged sword: every battle of his needs one (tweak.esquie.neutral-guards).</summary>
        public static bool HasUndamagedSword(IBoardView b, SeatId me) => b.Items(me).Any(it => it.Type == ItemType.Sword && !it.Damaged);

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
            SpawnGuarded(ctx, me, Florrie, Ids.MapRoles.StoneWaveCliffs);
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
            if (act >= 3) SpawnGuarded(ctx, me, Urrie, Ids.MapRoles.EsquiesNest);
        }

        /// <summary>spawn.florrie / spawn.urrie: the rock appears loose; its guards take post now if they are active
        /// (<see cref="ActivateGuards"/>), otherwise when he collects his second rock.</summary>
        private static void SpawnGuarded(GameContext ctx, SeatId me, int i, string roleId)
        {
            var r = ctx.State.EsquieRocks[i];
            var c = ctx.Role(roleId);
            if (r.Status != RockStatus.NotSpawned || !c.HasValue) return;
            r.Clearing = c;
            r.HostSeat = null;
            r.GuardsLeft = 0;
            r.Status = RockStatus.Loose;
            ctx.Actions.ShowOverlay(Ids.Overlays.EsquieRocks, me, c, null);
            if (ctx.State.EsquieGuardsActive) PostGuards(ctx, me, i);
        }

        /// <summary>Neutral guards take post on a loose Florrie or Urrie (none under the no-spawn fallback).</summary>
        private static void PostGuards(GameContext ctx, SeatId me, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            if (r.Status != RockStatus.Loose || !r.Clearing.HasValue || NoSpawnFallback(ctx)) return;
            var guards = i == Florrie ? Tuning.EsquieGuardsFlorrie : Tuning.EsquieGuardsUrrie;
            var placed = ctx.Actions.PlaceGuards(r.Clearing.Value, guards);
            if (placed <= 0) return;
            r.GuardsLeft = Math.Min(placed, guards);
            r.Status = RockStatus.Guarded;
            ctx.Actions.ShowOverlay(Badge(i), me, r.Clearing, null);
        }

        /// <summary>tune.esquie-guards-after-rocks: once he holds that many rocks the guards take post, once, on every Florrie
        /// or Urrie still on the map (state.esquie.guards-active).</summary>
        public static void ActivateGuards(GameContext ctx, SeatId me)
        {
            if (ctx.State.EsquieGuardsActive || HeldCount(ctx.State) < Tuning.EsquieGuardsAfterRocks) return;
            GuardsTakePost(ctx, me);
        }

        /// <summary>The guards take post now: state.esquie.guards-active, and guards on every Florrie or Urrie still loose.</summary>
        public static void GuardsTakePost(GameContext ctx, SeatId me)
        {
            ctx.State.EsquieGuardsActive = true;
            foreach (var i in new[] { Florrie, Urrie }) PostGuards(ctx, me, i);
        }

        private static string Badge(int i) => i == Florrie ? Ids.Overlays.EsquieLampmasterBadge : Ids.Overlays.EsquieFrancoisBadge;

        private static void MakeLoose(GameContext ctx, SeatId me, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            if (r.Status == RockStatus.Guarded) ctx.Actions.HideOverlay(Badge(i), me, r.Clearing, null);
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
            if (r.Status == RockStatus.Guarded) MakeLoose(ctx, me, i);
            ctx.Actions.HideOverlay(Ids.Overlays.EsquieRocks, me, r.Clearing, null);
            r.Status = RockStatus.Held;
            r.Clearing = null;
            r.HostSeat = null;
            r.GuardsLeft = 0;
            GrantPower(ctx, me, i);
            ActivateGuards(ctx, me);
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
                SpawnGuarded(ctx, me, Urrie, Ids.MapRoles.EsquiesNest);
        }

        /// <summary>A loose rock in clearing c may be picked up now (Corvid block on Dorrie; no-spawn fallback needs a free clearing).</summary>
        public static bool CanPickUp(GameContext ctx, SeatId me, int i, ClearingId c)
        {
            if (i == Dorrie && CorvidWarriorsIn(ctx.Board, c)) return false;
            if ((i == Florrie || i == Urrie) && NoSpawnFallback(ctx) && ctx.State.EsquieGuardsActive && OtherWarriorsIn(ctx.Board, me, c)) return false;
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

        /// <summary>Under the no-spawn fallback, once the guards would be active: index of an unclaimed (loose) Florrie or Urrie in
        /// a clearing, or -1.</summary>
        public static int FallbackRockAt(GameContext ctx, ClearingId c)
        {
            if (!NoSpawnFallback(ctx) || !ctx.State.EsquieGuardsActive) return -1;
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

        /// <summary>tweak.esquie.belly-bounce under the no-spawn fallback (no guards): in his battles in a clearing with an
        /// unclaimed Florrie or Urrie an undamaged Bag counts as tune.esquie-belly-bounce-swords sword, however many Bags he has.
        /// With guards, <see cref="GuardBattleStarting"/> applies it.</summary>
        public static void BellyBounce(GameContext ctx, SeatId me, SeatId attacker, SeatId defender, ClearingId c)
        {
            if (attacker != me && defender != me) return;
            if (FallbackRockAt(ctx, c) < 0) return;
            if (HasUndamagedBag(ctx.Board, me)) ctx.Actions.AddMaxHits(me, Tuning.EsquieBellyBounceSwords);
        }

        /// <summary>tweak.esquie.never-mad: he battles no faction while there are guards (his battles are guard battles,
        /// <see cref="CanBattleGuards"/>) and never Strikes. Under the no-spawn fallback (no guards) he may battle only in a
        /// clearing with an unclaimed Florrie or Urrie, against any faction's warriors there.</summary>
        public static bool NeverMadAllows(GameContext ctx, SeatId defender, ClearingId c, bool strike) =>
            !strike && FallbackRockAt(ctx, c) >= 0 && ctx.Board.Warriors(defender, c) > 0;

        /// <summary>tweak.esquie.neutral-guards and never-mad (hook.guard-battle-legal): only Esquie battles the guards of a
        /// guarded rock, and only with an undamaged sword.</summary>
        public static bool CanBattleGuards(GameContext ctx, SeatId me, SeatId attacker, ClearingId c) =>
            attacker == me && GuardedRockAt(ctx.State, c) >= 0 && HasUndamagedSword(ctx.Board, me);

        /// <summary>hook.guard-battle-start: tweak.esquie.belly-bounce against the guards.</summary>
        public static void GuardBattleStarting(GameContext ctx, SeatId me, SeatId attacker, ClearingId c)
        {
            if (attacker != me || GuardedRockAt(ctx.State, c) < 0) return;
            if (HasUndamagedBag(ctx.Board, me)) ctx.Actions.AddMaxHits(me, Tuning.EsquieBellyBounceSwords);
        }

        /// <summary>hook.guard-battle-resolved, his own guard battles only: GuardsLeft of the rock there becomes the guards still
        /// standing (read.guards), never more than before; at 0 the rock is loose.</summary>
        public static void GuardBattleResolved(GameContext ctx, SeatId me, SeatId attacker, ClearingId c)
        {
            if (attacker != me) return;   // nobody else may fight them, so nobody else can clear them for him
            var i = GuardedRockAt(ctx.State, c);
            if (i >= 0) SyncGuards(ctx, me, i);
        }

        private static void SyncGuards(GameContext ctx, SeatId me, int i)
        {
            var r = ctx.State.EsquieRocks[i];
            r.GuardsLeft = Math.Max(0, Math.Min(r.GuardsLeft, r.Clearing.HasValue ? ctx.Board.Guards(r.Clearing.Value) : 0));
            if (r.GuardsLeft == 0) MakeLoose(ctx, me, i);
        }

        // ------------------------------------------------------------ load

        /// <summary>state.esquie.rocks / decoy / target onLoad.</summary>
        public static void Loaded(GameContext ctx, SeatId me)
        {
            var rocks = ctx.State.EsquieRocks;
            for (var i = 0; i < rocks.Length; i++)
            {
                if (rocks[i].Status == RockStatus.Held) GrantPower(ctx, me, i);
                if (rocks[i].Status == RockStatus.Guarded) SyncGuards(ctx, me, i);
            }
            if (HeldCount(ctx.State) >= Tuning.EsquieGuardsAfterRocks) ctx.State.EsquieGuardsActive = true;
            if (ctx.Act >= 2) ctx.State.EsquieDecoy = false;
            ctx.State.EsquieTarget = null;
        }
    }
}
