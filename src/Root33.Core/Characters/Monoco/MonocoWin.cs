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

        public override bool Announce(GameContext ctx, SeatId seat) => WheelMissing(ctx.State.MonocoFeet, ctx.Board, seat) == 0;

        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat) && ctx.Board.PawnClearing(seat).HasValue;

        /// <summary>The public Goal Tracker (bot.*.08-spoiler's units): each missing foot, each missing source faction, and the
        /// yes/no part "the Warlord on the map" that the confirm adds.</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat) =>
            WheelMissing(ctx.State.MonocoFeet, ctx.Board, seat) + (ctx.Board.PawnClearing(seat).HasValue ? 0 : 1);

        /// <summary>Missing feet plus missing source factions (one unit each): 0 when the announce test's themed part holds.</summary>
        public static int WheelMissing(IReadOnlyList<FootState> feet, IBoardView board, SeatId seat)
        {
            var missingFeet = feet.Take(Tuning.MonocoFeet).Count(f => !f.Earned) + Math.Max(0, Tuning.MonocoFeet - feet.Count);
            var missingFactions = Math.Max(0, FactionsNeeded(board, seat) - MonocoFeetRules.DistinctFactions(feet));
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
