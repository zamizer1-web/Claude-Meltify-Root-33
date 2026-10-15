using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Clea
{
    /// <summary>
    /// Clea's rule tweaks (tweak.clea.home-flying-manor, tweak.clea.nevron-host) and her state row (state.clea.covered).
    /// Clea has no spawns rows. Everything that must survive a save lives in <see cref="ModState"/>.
    /// </summary>
    public static class CleaTweaks
    {
        // ------------------------------------------------------------ tweak.clea.home-flying-manor

        /// <summary>hook.setup-home for her seat: her home corner is the Flying Manor (do.restrict-home). Fallback when the
        /// plugin cannot restrict the choice: a bot is steered there with do.submit-action, a human is warned that her rules
        /// still point at the Flying Manor.</summary>
        public static void RestrictHome(GameContext ctx, SeatId clea)
        {
            var manor = ctx.Role(Ids.MapRoles.FlyingManor);
            if (!manor.HasValue) return;
            if (ctx.SupportsAction(Ids.BoardActions.RestrictHome))
            {
                ctx.Actions.RestrictHomeChoice(clea, new[] { manor.Value });
                return;
            }
            if (ctx.Board.IsBot(clea))
            {
                var key = CleaAction.HomeKey(manor.Value);
                if (ctx.Board.LegalActions(clea).Contains(key)) ctx.Actions.SubmitAction(clea, key);
            }
            else ctx.Warnings.Add("Clea's home is the Flying Manor (clearing " + manor.Value.Value.ToString(CultureInfo.InvariantCulture)
                + "): her rules still point there, and she still needs her atelier in the Flying Manor.");
        }

        // ------------------------------------------------------------ tweak.clea.nevron-host

        /// <summary>
        /// hook.setup-complete and hook.act-change (after Esquie's module has spawned that act's guards from her supply, since
        /// Esquie's hook order runs first): every guarded rock she hosts gets the host rules for her seat, human or bot, so
        /// Root's move generator, its AI and the human UI keep GuardsLeft warriors there (do.set-guard-floor) and she cannot
        /// build, recruit or place pieces there (do.block-placement). The guards themselves are placed by Esquie's spawns
        /// (do.place-warriors from the Nevron host, who is always Clea when she is seated).
        /// </summary>
        public static void EnforceHostRules(GameContext ctx, SeatId clea)
        {
            foreach (var r in ctx.State.EsquieRocks)
            {
                if (r == null || r.Status != RockStatus.Guarded || r.HostSeat != clea || !r.Clearing.HasValue || r.GuardsLeft <= 0) continue;
                ctx.Actions.SetGuardFloor(clea, r.Clearing.Value, r.GuardsLeft);
                ctx.Actions.BlockPlacement(clea, r.Clearing.Value, true);
            }
        }

        // ------------------------------------------------------------ state.clea.covered

        /// <summary>
        /// hook.evening-end for her seat: trigger.coverage-count reads the previous |state.clea.covered| first, then the row is
        /// rewritten with the clearings she covers now (guards she hosts not counted, the same count as win.clea), and the
        /// public coverage tracker (ui.clea.coverage) is refreshed. The themed-win test reads the board, not this row.
        /// </summary>
        public static void EndOfTurn(GameContext ctx, SeatId clea)
        {
            var now = CleaParts.Covered(ctx.Board, ctx.State, clea);
            var before = ctx.State.CleaCovered?.Count ?? 0;
            if (now.Count > before) ctx.Barks.Say(clea, Ids.BarkTriggers.CoverageCount);
            ctx.State.CleaCovered = now;
            ShowCoverage(ctx, clea);
        }

        /// <summary>ui.clea.coverage: the tracker, and a mark on each clearing she covers now.</summary>
        public static void ShowCoverage(GameContext ctx, SeatId clea)
        {
            var covered = CleaParts.Covered(ctx.Board, ctx.State, clea);
            foreach (var c in ctx.Board.Clearings())
            {
                if (covered.Contains(c)) ctx.Actions.ShowOverlay(Ids.Overlays.CleaCoverage, clea, c, null);
                else ctx.Actions.HideOverlay(Ids.Overlays.CleaCoverage, clea, c, null);
            }
            ctx.Actions.ShowOverlay(Ids.Overlays.CleaCoverage, clea, null, null);
        }

        /// <summary>state.clea.covered onLoad: restored from the save with ids not on the current map dropped; if the save has
        /// none, recomputed from the current board (so no clearing counts as just lost until her next turn ends). The host
        /// rules of the rocks she still guards are sent again.</summary>
        public static void RecheckOnLoad(GameContext ctx, SeatId clea, IReadOnlyCollection<string> loadedKeys)
        {
            if (loadedKeys != null && loadedKeys.Contains(Ids.State.CleaCovered) && ctx.State.CleaCovered != null)
            {
                var map = new HashSet<ClearingId>(ctx.Board.Clearings());
                ctx.State.CleaCovered = new HashSet<ClearingId>(ctx.State.CleaCovered.Where(map.Contains));
            }
            else ctx.State.CleaCovered = CleaParts.Covered(ctx.Board, ctx.State, clea);
            EnforceHostRules(ctx, clea);
            ShowCoverage(ctx, clea);
        }
    }
}
