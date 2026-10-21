using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Paintress
{
    /// <summary>
    /// win.paintress, The Family Portrait (countdown). Gates from the row (WinRules): act &gt;= 2, Vp &gt;= tune.countdown-vp,
    /// no active dominance. Themed test, the same at announce (hook.evening-end) and confirm (hook.birdsong-start, hook.load):
    /// she rules the Monolith with tune.paintress-monolith-roosts roosts there, and her Living Portraits cover fox, rabbit and
    /// mouse (<see cref="PaintressPortraits"/>).
    /// </summary>
    public sealed class PaintressWin : WinConditionBase, IGoalProgress
    {
        public PaintressWin() : base(Ids.WinConditions.Paintress) { }

        public override bool Announce(GameContext ctx, SeatId seat) =>
            PaintressPortraits.FamilyPortrait(ctx.Board, seat, ctx.Role(Ids.MapRoles.Monolith));

        /// <summary>The confirm test is "the same test on the current board".</summary>
        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        /// <summary>Goal Tracker units (gates excluded): the Monolith part (yes/no) and each needed suit without a Living Portrait.</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat)
        {
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            var n = PaintressPortraits.MissingSuits(ctx.Board, seat, monolith).Count;
            if (!PaintressPortraits.HoldsMonolith(ctx.Board, seat, monolith)) n++;
            return n;
        }

        /// <summary>The Monolith and her Living Portraits (met parts to break), then her other easels (where a missing Portrait
        /// would come from).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            var list = new List<ClearingId>();
            if (monolith.HasValue) list.Add(monolith.Value);
            var portraits = PaintressPortraits.LivingPortraits(ctx.Board, seat, monolith);
            list.AddRange(portraits);
            list.AddRange(ctx.Board.Clearings().Where(c => !list.Contains(c) && PaintressPortraits.IsEasel(ctx.Board, seat, c, monolith)));
            return list;
        }
    }
}
