using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Gustave
{
    /// <summary>
    /// win.gustave, The Road to the Monolith (countdown). Gates from the row (WinRules): act &gt;= 2, Vp &gt;= tune.countdown-vp,
    /// no active dominance. Themed test (<see cref="GustaveChain.RoadTest"/>), the same at announce (after the flags are planted
    /// by the module at hook.evening-end) and at confirm (no new flags).
    /// </summary>
    public sealed class GustaveWin : WinConditionBase, IGoalProgress
    {
        public GustaveWin() : base(Ids.WinConditions.Gustave) { }

        public override bool Announce(GameContext ctx, SeatId seat) =>
            GustaveChain.RoadTest(ctx.Board, ctx.State, seat, ctx.Role(Ids.MapRoles.Lumiere), ctx.Role(Ids.MapRoles.Monolith));

        /// <summary>The same test on the current board and state; no new flags are planted.</summary>
        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        /// <summary>Goal Tracker units: missing Arm pips, missing Chain suits and the Beachhead (yes/no).</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat)
        {
            var chain = GustaveChain.Chain(ctx.Board, ctx.State, seat, ctx.Role(Ids.MapRoles.Lumiere));
            var n = System.Math.Max(0, Tuning.GustaveArmMax - ctx.State.GustaveArm);
            n += System.Math.Max(0, Tuning.GustaveChainSuits - GustaveChain.ChainSuits(ctx.Board, chain).Count);
            if (!GustaveChain.ChainBeachhead(ctx.Board, seat, chain, ctx.Role(Ids.MapRoles.Monolith)).HasValue) n++;
            return n;
        }

        /// <summary>The Chain clearings, thinnest margin of rule first (ties: lower number), with the Beachhead right after the thinnest.</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var chain = GustaveChain.Chain(ctx.Board, ctx.State, seat, ctx.Role(Ids.MapRoles.Lumiere));
            return chain.OrderBy(c => GustaveChain.Margin(ctx.Board, seat, c)).ThenBy(c => c.Value).ToList();
        }
    }
}
