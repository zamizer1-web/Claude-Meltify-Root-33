using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Gustave
{
    /// <summary>
    /// Gustave's rule tweaks and saved state: tweak.gustave.keep-in-lumiere (hook.setup-home), tweak.gustave.overcharge
    /// (hook.ui-toggle, hook.battle-start, hook.battle-resolved, hook.turn-end), the flags of win.gustave (hook.setup-complete,
    /// hook.evening-end) and the onLoad rules of state.gustave.*.
    /// </summary>
    public static class GustaveTweaks
    {
        /// <summary>The toggle id of the Overcharge button on his faction board (hook.ui-toggle).</summary>
        public const string OverchargeToggle = "overcharge";
        /// <summary>Assumed legal-action key verb for picking a setup home clearing (home:&lt;clearing&gt;).</summary>
        public const string HomeVerb = "home";

        // ------------------------------------------------------------ tweak.gustave.keep-in-lumiere

        /// <summary>hook.setup-home for his seat. On Autumn the keep may only go in Lumière (do.restrict-home; fallback: a bot
        /// is steered there with do.submit-action, a human is warned). On other maps the keep is free: the engine names the
        /// corner it lands in Lumière and the diagonal corner the Monolith (hook.piece-placed).</summary>
        public static void RestrictHome(GameContext ctx, SeatId seat)
        {
            if (ctx.Board.MapName() != AutumnMap.MapName) return;
            var lumiere = ctx.Role(Ids.MapRoles.Lumiere);
            if (!lumiere.HasValue) return;
            if (ctx.SupportsAction(Ids.BoardActions.RestrictHome))
            {
                ctx.Actions.RestrictHomeChoice(seat, new[] { lumiere.Value });
                return;
            }
            if (ctx.Board.IsBot(seat))
            {
                var key = HomeVerb + ":" + lumiere.Value.Value.ToString(CultureInfo.InvariantCulture);
                if (ctx.Board.LegalActions(seat).Contains(key)) ctx.Actions.SubmitAction(seat, key);
            }
            else ctx.Warnings.Add("Gustave's Expedition Camp (the keep) must go in Lumière (" + lumiere.Value + "); his rules still point there.");
        }

        // ------------------------------------------------------------ flags (win.gustave, state.gustave.flags)

        /// <summary>hook.setup-complete: Lumière starts flagged.</summary>
        public static void EnsureLumiereFlag(GameContext ctx)
        {
            var lumiere = ctx.Role(Ids.MapRoles.Lumiere);
            if (lumiere.HasValue) ctx.State.GustaveFlags.Add(lumiere.Value);
        }

        /// <summary>hook.evening-end, his seat (before the win check): plant the new flags and redraw them.</summary>
        public static List<ClearingId> PlantFlags(GameContext ctx, SeatId gustave)
        {
            EnsureLumiereFlag(ctx);
            var added = GustaveChain.PlantFlags(ctx.Board, ctx.State, gustave);
            Refresh(ctx, gustave);
            return added;
        }

        // ------------------------------------------------------------ tweak.gustave.overcharge

        /// <summary>hook.ui-toggle: arming needs a full Arm; switching off always works. Returns the armed state.</summary>
        public static bool Toggle(GameContext ctx, SeatId gustave, SeatId seat, string toggleId, bool on)
        {
            if (seat != gustave || toggleId != OverchargeToggle) return ctx.State.GustaveOverchargeArmed;
            ctx.State.GustaveOverchargeArmed = on && ctx.State.GustaveArm >= Tuning.GustaveArmMax;
            ctx.Actions.ShowOverlay(Ids.Overlays.GustaveArm, gustave, null, null);
            return ctx.State.GustaveOverchargeArmed;
        }

        /// <summary>hook.battle-start: when he attacks with Overcharge armed (and the Arm still full), +tune.gustave-overcharge-hits
        /// hit in this battle (do.add-hits). Returns true when the extra hit was registered.</summary>
        public static bool BattleStarting(GameContext ctx, SeatId gustave, SeatId attacker)
        {
            if (attacker != gustave || !ctx.State.GustaveOverchargeArmed) return false;
            if (ctx.State.GustaveArm < Tuning.GustaveArmMax) { ctx.State.GustaveOverchargeArmed = false; return false; }
            ctx.Actions.AddHits(gustave, Tuning.GustaveOverchargeHits);
            return true;
        }

        /// <summary>hook.battle-resolved: after a battle he started, an Overcharged battle empties the Arm; otherwise a battle
        /// whose removed list holds a non-Marquise piece adds one charge (capped at tune.gustave-arm-max).</summary>
        public static void BattleResolved(GameContext ctx, SeatId gustave, BattleResult r)
        {
            if (r == null || r.Attacker != gustave) return;
            if (ctx.State.GustaveOverchargeArmed)
            {
                ctx.State.GustaveArm = 0;
                ctx.State.GustaveOverchargeArmed = false;
            }
            else if (r.Removed.Any(p => p.Owner != gustave))
                ctx.State.GustaveArm = System.Math.Min(Tuning.GustaveArmMax, ctx.State.GustaveArm + 1);
            ctx.Actions.ShowOverlay(Ids.Overlays.GustaveArm, gustave, null, null);
        }

        /// <summary>hook.turn-end of his own turn: an unused armed toggle switches off.</summary>
        public static void TurnEnd(GameContext ctx, SeatId gustave, SeatId seatEnding)
        {
            if (seatEnding == gustave) ctx.State.GustaveOverchargeArmed = false;
        }

        // ------------------------------------------------------------ hook.load

        /// <summary>state.gustave.flags: ids not on the map are dropped and Lumière is added back; state.gustave.arm: clamped;
        /// state.gustave.overcharge-armed: dropped below a full Arm; state.gustave.route: not trusted (recomputed at his next Birdsong).</summary>
        public static void RecheckOnLoad(GameContext ctx, SeatId gustave)
        {
            var onMap = new HashSet<ClearingId>(ctx.Board.Clearings());
            ctx.State.GustaveFlags.RemoveWhere(c => !onMap.Contains(c));
            EnsureLumiereFlag(ctx);
            if (ctx.State.GustaveArm < 0) ctx.State.GustaveArm = 0;
            if (ctx.State.GustaveArm > Tuning.GustaveArmMax) ctx.State.GustaveArm = Tuning.GustaveArmMax;
            if (ctx.State.GustaveArm < Tuning.GustaveArmMax) ctx.State.GustaveOverchargeArmed = false;
            ctx.State.GustaveRoute = new ClearingId[0];
            Refresh(ctx, gustave);
        }

        // ------------------------------------------------------------ display

        /// <summary>Redraws the flags (ui.gustave.flags), the Arm (ui.gustave.arm) and the ruled Chain (ui.gustave.road).</summary>
        public static void Refresh(GameContext ctx, SeatId gustave)
        {
            var chain = GustaveChain.Chain(ctx.Board, ctx.State, gustave, ctx.Role(Ids.MapRoles.Lumiere));
            foreach (var c in ctx.Board.Clearings())
            {
                if (ctx.State.GustaveFlags.Contains(c)) ctx.Actions.ShowOverlay(Ids.Overlays.GustaveFlags, gustave, c, null);
                else ctx.Actions.HideOverlay(Ids.Overlays.GustaveFlags, gustave, c, null);
                if (chain.Contains(c)) ctx.Actions.ShowOverlay(Ids.Overlays.GustaveRoad, gustave, c, null);
                else ctx.Actions.HideOverlay(Ids.Overlays.GustaveRoad, gustave, c, null);
            }
            ctx.Actions.ShowOverlay(Ids.Overlays.GustaveArm, gustave, null, null);
        }
    }
}
