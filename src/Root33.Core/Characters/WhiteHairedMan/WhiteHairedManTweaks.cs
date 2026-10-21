using System;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>
    /// The White-Haired Man's rule tweaks: tweak.white-haired-man.unbeatable-act1 (his module's hook.battle-start and its
    /// do.ignore-first-hit fallback at hook.battle-resolved) and tweak.white-haired-man.monolith-barrier with
    /// state.white-haired-man.barrier-down. The Barrier is a table rule switched by opt.monolith-barrier that applies whether
    /// or not he is seated, so <see cref="Root33Game"/> calls <see cref="BarrierAllows"/> and <see cref="BarrierTurnEnd"/>
    /// itself rather than through his module. He owns no spawns.
    /// </summary>
    public static class WhiteHairedManTweaks
    {
        // ================================================================= Unbeatable in Act I

        /// <summary>Whether a battle qualifies for Unbeatable in Act I: Act I, he attacks or defends, in a Ring clearing.</summary>
        public static bool Unbeatable(GameContext ctx, SeatId whm, SeatId attacker, SeatId defender, ClearingId c)
        {
            if (ctx.Act != 1 || Tuning.WhiteHairedManUnbeatableHits <= 0) return false;
            if (attacker != whm && defender != whm) return false;
            return WhiteHairedManRing.Ring(ctx.Board, ctx.Role(Ids.MapRoles.Monolith)).Contains(c);
        }

        /// <summary>hook.battle-start: during Act I only, his warriors in a Ring clearing ignore the first hit of the battle
        /// (do.ignore-first-hit, tune.white-haired-man-unbeatable-hits times), whether he attacks or defends.</summary>
        public static void UnbeatableAtBattleStart(GameContext ctx, SeatId whm, SeatId attacker, SeatId defender, ClearingId c)
        {
            if (!Unbeatable(ctx, whm, attacker, defender, c) || ctx.Board.Warriors(whm, c) < 1) return;
            if (!ctx.SupportsAction(Ids.BoardActions.IgnoreFirstHit)) return; // the fallback runs at hook.battle-resolved
            for (var i = 0; i < Tuning.WhiteHairedManUnbeatableHits; i++) ctx.Actions.IgnoreFirstHit(whm);
        }

        /// <summary>do.ignore-first-hit's fallback at hook.battle-resolved: return one removed warrior of his per ignored hit to
        /// the clearing from his supply (do.place-warriors). Without do.place-warriors the tweak is left out.</summary>
        public static void UnbeatableFallback(GameContext ctx, SeatId whm, BattleResult r)
        {
            if (r == null || ctx.SupportsAction(Ids.BoardActions.IgnoreFirstHit)) return;
            if (!Unbeatable(ctx, whm, r.Attacker, r.Defender, r.Clearing)) return;
            if (!ctx.SupportsAction(Ids.BoardActions.PlaceWarriors)) return;
            var lost = r.Removed.Count(p => p.Owner == whm && p.Kind == PieceKind.Warrior);
            var back = Math.Min(lost, Tuning.WhiteHairedManUnbeatableHits);
            if (back > 0) ctx.Actions.PlaceWarriors(whm, r.Clearing, back);
        }

        // ================================================================= Monolith Barrier (v1.1, opt.monolith-barrier)

        /// <summary>The Barrier acts only when this game has opt.monolith-barrier on (state.global.monolith-barrier) and the v1.1
        /// update rules are on (<see cref="GameContext.RuleOn"/>; off in the first release).</summary>
        public static bool BarrierOn(GameContext ctx) =>
            ctx.Options.MonolithBarrier && ctx.RuleOn(Ids.RuleTweaks.WhiteHairedManMonolithBarrier);

        /// <summary>The seats that cannot bring the Barrier down: the White-Haired Man and the Paintress, when seated.</summary>
        private static bool Excluded(GameContext ctx, SeatId s) =>
            ctx.SeatOf(CharacterIds.WhiteHairedMan) == s || ctx.SeatOf(CharacterIds.Paintress) == s;

        /// <summary>hook.battle-legal: while opt.monolith-barrier is on and the Barrier is up, no battle may start in the
        /// Monolith. A Strike is not a battle and is not blocked.</summary>
        public static bool BarrierAllows(GameContext ctx, ClearingId c, bool strike)
        {
            if (strike || !BarrierOn(ctx) || ctx.State.WhiteHairedManBarrierDown) return true;
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            return !monolith.HasValue || c != monolith.Value;
        }

        /// <summary>hook.turn-end: the Barrier comes down for the rest of the game once Visages and Sirène are both ruled at the
        /// end of the same turn by seats other than the White-Haired Man and the Paintress (not necessarily the same seat).
        /// A map without one of the two roles never brings it down. Returns true when it came down on this call.</summary>
        public static bool BarrierTurnEnd(GameContext ctx)
        {
            if (!BarrierOn(ctx) || ctx.State.WhiteHairedManBarrierDown) return false;
            var visages = ctx.Role(Ids.MapRoles.Visages);
            var sirene = ctx.Role(Ids.MapRoles.Sirene);
            if (!visages.HasValue || !sirene.HasValue) return false;
            var rv = ctx.Board.Ruler(visages.Value);
            var rs = ctx.Board.Ruler(sirene.Value);
            if (!rv.HasValue || !rs.HasValue || Excluded(ctx, rv.Value) || Excluded(ctx, rs.Value)) return false;
            ctx.State.WhiteHairedManBarrierDown = true;
            return true;
        }

        // ================================================================= overlays and barks

        /// <summary>ui.white-haired-man.ring: the tracker, plus one lit segment per held Ring clearing and the Monolith when its
        /// part holds (shown or hidden on the clearing).</summary>
        public static void RefreshRing(GameContext ctx, SeatId whm)
        {
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!monolith.HasValue) return;
            var p = ctx.SeatOf(CharacterIds.Paintress);
            foreach (var r in WhiteHairedManRing.Ring(ctx.Board, monolith))
            {
                if (WhiteHairedManRing.RingClearingHeld(ctx.Board, whm, p, r)) ctx.Actions.ShowOverlay(Ids.Overlays.WhiteHairedManRing, whm, r, null);
                else ctx.Actions.HideOverlay(Ids.Overlays.WhiteHairedManRing, whm, r, null);
            }
            if (WhiteHairedManRing.MonolithHeld(ctx.Board, whm, p, monolith.Value)) ctx.Actions.ShowOverlay(Ids.Overlays.WhiteHairedManRing, whm, monolith, null);
            else ctx.Actions.HideOverlay(Ids.Overlays.WhiteHairedManRing, whm, monolith, null);
            ctx.Actions.ShowOverlay(Ids.Overlays.WhiteHairedManRing, whm, null, null);
        }

        /// <summary>trigger.monolith-approach (hook.piece-moved): an enemy moves into a Ring clearing where he has pieces, or a
        /// Vagabond pawn moves into the Monolith.</summary>
        public static bool IsMonolithApproach(GameContext ctx, SeatId whm, SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to)
        {
            if (owner == whm || !to.HasValue || from == to) return false;
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!monolith.HasValue) return false;
            var b = ctx.Board;
            if (kind == PieceKind.Pawn && ctx.IsVagabond(owner) && to.Value == monolith.Value) return true;
            return WhiteHairedManRing.Ring(b, monolith).Contains(to.Value)
                   && b.Warriors(whm, to.Value) + b.Buildings(whm, to.Value) + b.Tokens(whm, to.Value) > 0;
        }
    }
}
