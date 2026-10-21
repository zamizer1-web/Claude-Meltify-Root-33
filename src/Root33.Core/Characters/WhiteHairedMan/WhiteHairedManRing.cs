using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>Corvid plot types as read.corvid-face-up-plot and read.corvid-hidden-plot name them.</summary>
    public static class CorvidPlots
    {
        public const string Bomb = "bomb";
        public const string Snare = "snare";
        public const string Extortion = "extortion";
        public const string Raid = "raid";
    }

    /// <summary>
    /// The Ring around the Monolith (win.white-haired-man, bot.white-haired-man.01-ring-status), worked out from the board each
    /// time and never stored. Everything here is public information (rule, warriors, face-up plots, plot tokens, pawns) plus
    /// state.esquie.rocks, so the win, the public Goal Tracker and the bot all use the same code.
    /// </summary>
    public static class WhiteHairedManRing
    {
        /// <summary>Goal Tracker units when the map has no Monolith yet (roles not assigned): far from any win.</summary>
        public const int NoMonolithUnits = 1000;

        /// <summary>The Ring: the Monolith's path neighbours (read.path-neighbours), or empty without a Monolith.</summary>
        public static IReadOnlyList<ClearingId> Ring(IBoardView board, ClearingId? monolith) =>
            monolith.HasValue ? board.PathNeighbours(monolith.Value) : (IReadOnlyList<ClearingId>)new ClearingId[0];

        /// <summary>His plot tokens in a clearing, face up or face down (read.tokens-of-type 'plot').</summary>
        public static int Plots(IBoardView board, SeatId seat, ClearingId c) => board.TokensOfType(seat, c, PieceTypes.Plot);

        public static bool FaceUpSnare(IBoardView board, SeatId seat, ClearingId c) => board.FaceUpPlot(seat, c) == CorvidPlots.Snare;

        /// <summary>Ruled by no faction other than him or the Paintress (nobody ruling counts).</summary>
        public static bool RuledByHimOrPaintress(IBoardView board, ClearingId c, SeatId seat, SeatId? paintress)
        {
            var r = board.Ruler(c);
            return !r.HasValue || r.Value == seat || (paintress.HasValue && r.Value == paintress.Value);
        }

        /// <summary>One Ring clearing is held: his face-up Snare is there, or at least one of his pieces (warriors, plus plots
        /// face up or down) and no ruler but him or the Paintress.</summary>
        public static bool RingClearingHeld(IBoardView board, SeatId seat, SeatId? paintress, ClearingId r) =>
            FaceUpSnare(board, seat, r)
            || (board.Warriors(seat, r) + Plots(board, seat, r) >= 1 && RuledByHimOrPaintress(board, r, seat, paintress));

        /// <summary>A Vagabond pawn (any seat whose engine is a Vagabond) stands in the clearing.</summary>
        public static bool VagabondIn(IBoardView board, ClearingId c) =>
            board.Seats().Any(v => GameContext.IsVagabondEngine(board.EngineOf(v)) && board.PawnClearing(v) == c);

        /// <summary>The Monolith part: ruled by no faction other than him or the Paintress, and no Vagabond pawn there.</summary>
        public static bool MonolithHeld(IBoardView board, SeatId seat, SeatId? paintress, ClearingId monolith) =>
            RuledByHimOrPaintress(board, monolith, seat, paintress) && !VagabondIn(board, monolith);

        /// <summary>The themed test of Nobody Reaches Her (gates excluded): every Ring clearing held and the Monolith held.</summary>
        public static bool NobodyReachesHer(IBoardView board, SeatId seat, SeatId? paintress, ClearingId? monolith)
        {
            if (!monolith.HasValue) return false;
            return Ring(board, monolith).All(r => RingClearingHeld(board, seat, paintress, r))
                   && MonolithHeld(board, seat, paintress, monolith.Value);
        }

        /// <summary>Goal Tracker units still missing: one per Ring clearing not held, plus one for the Monolith part.</summary>
        public static int UnitsMissing(IBoardView board, SeatId seat, SeatId? paintress, ClearingId? monolith)
        {
            if (!monolith.HasValue) return NoMonolithUnits;
            var n = Ring(board, monolith).Count(r => !RingClearingHeld(board, seat, paintress, r));
            return n + (MonolithHeld(board, seat, paintress, monolith.Value) ? 0 : 1);
        }

        /// <summary>The rule count (warriors + buildings) of the strongest faction in a clearing other than him and the Paintress.</summary>
        public static int StrongestOther(IBoardView board, SeatId seat, SeatId? paintress, ClearingId c)
        {
            var best = 0;
            foreach (var s in board.Seats())
            {
                if (s == seat || (paintress.HasValue && s == paintress.Value)) continue;
                best = Math.Max(best, board.Warriors(s, c) + board.Buildings(s, c));
            }
            return best;
        }

        /// <summary>bot.white-haired-man.01-ring-status: margin(r) = his warriors in r - the rule count of the strongest faction in
        /// r that is not the Paintress.</summary>
        public static int Margin(IBoardView board, SeatId seat, SeatId? paintress, ClearingId r) =>
            board.Warriors(seat, r) - StrongestOther(board, seat, paintress, r);
    }
}
