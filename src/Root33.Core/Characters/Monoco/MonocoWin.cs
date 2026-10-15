using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Monoco
{
    /// <summary>
    /// win.monoco (The Bestial Wheel, countdown). Announce: all tune.monoco-feet feet of state.monoco.feet earned, from at
    /// least min(tune.monoco-feet-factions, other seats) distinct factions. Confirm: the same and the Warlord on the map
    /// (read.pawn-clearing). Gates (act, tune.countdown-vp, dominance) are applied by <see cref="WinRules"/>.
    /// </summary>
    public sealed class MonocoWin : WinConditionBase, IGoalProgress
    {
        public MonocoWin() : base(Ids.WinConditions.Monoco) { }

        public override bool Announce(GameContext ctx, SeatId seat) => UnitsMissing(ctx, seat) == 0;

        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat) && ctx.Board.PawnClearing(seat).HasValue;

        /// <summary>Missing feet plus missing source factions (one unit each).</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat)
        {
            var feet = ctx.State.MonocoFeet;
            var missingFeet = feet.Count(f => !f.Earned);
            var missingFactions = Math.Max(0, FactionsNeeded(ctx.Board, seat) - MonocoFeetRules.DistinctFactions(feet));
            return missingFeet + missingFactions;
        }

        /// <summary>The Warlord's clearing (the counterplay: battle the Warlord off the map).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var w = ctx.Board.PawnClearing(seat);
            return w.HasValue ? new[] { w.Value } : new ClearingId[0];
        }

        /// <summary>min(tune.monoco-feet-factions, number of other seats).</summary>
        public static int FactionsNeeded(IBoardView board, SeatId seat) =>
            Math.Min(Tuning.MonocoFeetFactions, board.Seats().Count(s => s != seat));
    }
}
