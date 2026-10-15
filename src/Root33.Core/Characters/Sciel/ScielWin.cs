using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>
    /// win.sciel, Twilight Harvest (countdown). The row's gates (act &gt;= 2, Vp &gt;= tune.countdown-vp, no active dominance)
    /// are applied by <see cref="WinRules"/>; this class holds only the themed tests.
    /// </summary>
    public sealed class ScielWin : WinConditionBase, IGoalProgress
    {
        public ScielWin() : base(Ids.WinConditions.Sciel) { }

        /// <summary>Announce (end of her turn): tune.sciel-sanctuaries Sanctuaries spanning fox, rabbit and mouse, and
        /// tune.sciel-aquafarms Aquafarms on the map.</summary>
        public override bool Announce(GameContext ctx, SeatId seat)
        {
            var s = ScielBoard.Sanctuaries(ctx.Board, seat);
            return s.Count >= Tuning.ScielSanctuaries && ScielBoard.CoversAllSuits(ctx.Board, s)
                   && ScielBoard.Aquafarms(ctx.Board, seat) >= Tuning.ScielAquafarms;
        }

        /// <summary>Confirm (start of her next Birdsong): Lanterns with no enemy building or token (enemy warriors and pawns
        /// that only walked in are ignored), spanning all three suits, and an Aquafarm still standing.</summary>
        public override bool Confirm(GameContext ctx, SeatId seat)
        {
            var s = ScielBoard.HeldLanterns(ctx.Board, seat);
            return s.Count >= Tuning.ScielSanctuaries && ScielBoard.CoversAllSuits(ctx.Board, s)
                   && ScielBoard.Aquafarms(ctx.Board, seat) >= Tuning.ScielAquafarms;
        }

        public int UnitsMissing(GameContext ctx, SeatId seat) => ScielBoard.UnitsMissing(ctx.Board, seat);

        /// <summary>Her Sanctuaries, then her Aquafarm clearings (the parts an answer breaks).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var b = ctx.Board;
            var keys = ScielBoard.Sanctuaries(b, seat);
            keys.AddRange(b.Clearings().Where(c => b.BuildingsOfType(seat, c, PieceTypes.Base) > 0 && !keys.Contains(c)));
            return keys;
        }
    }
}
