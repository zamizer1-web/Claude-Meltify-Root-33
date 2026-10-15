using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Paintress
{
    /// <summary>
    /// The Paintress's rule tweaks and saved state:
    /// tweak.paintress.roost-in-monolith (hook.setup-home), tweak.paintress.still-life (hook.battle-start, hook.turn-end),
    /// state.paintress.still-life-used and state.paintress.repaint-avoid (hook.piece-removed, hook.turn-end, hook.load),
    /// and the display of her Portraits (ui.paintress.portraits, ui.paintress.portrait-tracker, ui.paintress.ending).
    /// </summary>
    public static class PaintressTweaks
    {
        /// <summary>Assumed legal-action key for picking a setup home clearing (see <see cref="PaintressActionKey"/>).</summary>
        public const string HomeVerb = "home";

        // ------------------------------------------------------------ tweak.paintress.roost-in-monolith

        /// <summary>hook.setup-home for her seat: her starting roost (Easel) must go in the Monolith (do.restrict-home).
        /// Fallback when the plugin cannot restrict the choice: a bot is steered there with do.submit-action, a human is warned.</summary>
        public static void RestrictHome(GameContext ctx, SeatId seat)
        {
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!monolith.HasValue) return;
            if (ctx.SupportsAction(Ids.BoardActions.RestrictHome))
            {
                ctx.Actions.RestrictHomeChoice(seat, new[] { monolith.Value });
                return;
            }
            if (ctx.Board.IsBot(seat))
            {
                var key = PaintressActionKey.Format(HomeVerb, monolith.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (ctx.Board.LegalActions(seat).Contains(key)) ctx.Actions.SubmitAction(seat, key);
            }
            else ctx.Warnings.Add("The Paintress's first easel must go in the Monolith (" + monolith.Value + "); her rules still point there.");
        }

        // ------------------------------------------------------------ tweak.paintress.still-life (v1.1)

        /// <summary>hook.battle-start: once per turn, when an enemy starts a battle in one of her Living Portrait clearings,
        /// tune.paintress-still-life-warriors warrior moves in from an adjacent clearing before the roll (do.move-warriors;
        /// its fallback leaves Still Life out). Returns the clearing the warrior came from, or null.</summary>
        public static ClearingId? StillLife(GameContext ctx, SeatId paintress, SeatId attacker, ClearingId c)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.PaintressStillLife)) return null; // v1.1: off in the first release
            if (attacker == paintress || ctx.Board.CoalitionPartner(attacker) == paintress) return null;
            if (ctx.State.PaintressStillLifeUsed) return null;
            if (!ctx.SupportsAction(Ids.BoardActions.MoveWarriors)) return null;
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!PaintressPortraits.IsLivingPortrait(ctx.Board, ctx.State, paintress, c, monolith)) return null;

            var n = Tuning.PaintressStillLifeWarriors;
            var from = ctx.Board.PathNeighbours(c)
                .Select(x => (clearing: x, spare: PaintressPortraits.PortraitWarriors(ctx.Board, ctx.State, paintress, x)))
                .Where(x => x.spare >= n)
                .OrderByDescending(x => x.spare).ThenBy(x => x.clearing.Value)
                .Select(x => (ClearingId?)x.clearing).FirstOrDefault();
            if (!from.HasValue) return null;
            if (!ctx.Actions.MoveWarriors(paintress, from.Value, c, n)) return null;
            ctx.State.PaintressStillLifeUsed = true;
            return from;
        }

        /// <summary>hook.turn-end (any seat): Still Life is available again; at the end of her own turn every repaint-avoid
        /// entry loses one turn and leaves at 0.</summary>
        public static void TurnEnd(GameContext ctx, SeatId paintress, SeatId seatEnding)
        {
            ctx.State.PaintressStillLifeUsed = false;
            if (seatEnding != paintress) return;
            var avoid = ctx.State.PaintressRepaintAvoid;
            foreach (var c in avoid.Keys.ToList())
            {
                var left = avoid[c] - 1;
                if (left <= 0) avoid.Remove(c); else avoid[c] = left;
            }
        }

        // ------------------------------------------------------------ state.paintress.repaint-avoid

        /// <summary>hook.piece-removed: one of her easels (a roost outside the Monolith) left the map, so the clearing is
        /// avoided for tune.paintress-repaint-avoid-turns of her turns (bot.paintress.05-repaint).</summary>
        public static bool PortraitRemoved(GameContext ctx, SeatId paintress, RemovedPiece piece, ClearingId c)
        {
            if (piece.Owner != paintress || piece.Kind != PieceKind.Building) return false;
            if (piece.Type == null || !(piece.Type == PieceTypes.Roost || piece.Type.StartsWith(PieceTypes.Roost + ".", System.StringComparison.Ordinal))) return false;
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (monolith.HasValue && c == monolith.Value) return false;
            ctx.State.PaintressRepaintAvoid[c] = Tuning.PaintressRepaintAvoidTurns;
            return true;
        }

        /// <summary>hook.load: still-life-used is restored as is (reset at the next turn end); repaint-avoid entries for
        /// clearings not on the map are dropped and turns are kept within 0..tune.paintress-repaint-avoid-turns.</summary>
        public static void RecheckOnLoad(GameContext ctx)
        {
            var onMap = new HashSet<ClearingId>(ctx.Board.Clearings());
            var avoid = ctx.State.PaintressRepaintAvoid;
            foreach (var c in avoid.Keys.ToList())
            {
                if (!onMap.Contains(c) || avoid[c] <= 0) { avoid.Remove(c); continue; }
                if (avoid[c] > Tuning.PaintressRepaintAvoidTurns) avoid[c] = Tuning.PaintressRepaintAvoidTurns;
            }
        }

        // ------------------------------------------------------------ display

        /// <summary>Redraws the framed portraits over every Living Portrait and the public tracker (display only).</summary>
        public static void RefreshPortraits(GameContext ctx, SeatId paintress)
        {
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            var living = new HashSet<ClearingId>(PaintressPortraits.LivingPortraits(ctx.Board, ctx.State, paintress, monolith));
            foreach (var c in ctx.Board.Clearings())
            {
                if (living.Contains(c)) ctx.Actions.ShowOverlay(Ids.Overlays.PaintressPortraits, paintress, c, null);
                else ctx.Actions.HideOverlay(Ids.Overlays.PaintressPortraits, paintress, c, null);
            }
            ctx.Actions.ShowOverlay(Ids.Overlays.PaintressPortraitTracker, paintress, null, null);
        }
    }
}
