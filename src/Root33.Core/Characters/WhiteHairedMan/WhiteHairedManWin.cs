using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>
    /// win.white-haired-man, Nobody Reaches Her (countdown). Gates from the row (<see cref="WinRules"/>): act &gt;= 2,
    /// Vp &gt;= tune.countdown-vp, no active dominance (offWithDominance; offInCoalition is false). Themed test, the same at
    /// announce (hook.evening-end) and confirm (hook.birdsong-start, hook.load): every Ring clearing (the Monolith's path
    /// neighbours) holds his face-up Snare, or one of his pieces with no ruler but him or the Paintress; and the Monolith is
    /// ruled by no faction but him or the Paintress and holds no Vagabond pawn (<see cref="WhiteHairedManRing"/>).
    /// </summary>
    public sealed class WhiteHairedManWin : WinConditionBase, IGoalProgress
    {
        public WhiteHairedManWin() : base(Ids.WinConditions.WhiteHairedMan) { }

        private static SeatId? Paintress(GameContext ctx) => ctx.SeatOf(CharacterIds.Paintress);

        public override bool Announce(GameContext ctx, SeatId seat) =>
            WhiteHairedManRing.NobodyReachesHer(ctx.Board, ctx.State, seat, Paintress(ctx), ctx.Role(Ids.MapRoles.Monolith));

        /// <summary>The confirm test is "the same test on the current board".</summary>
        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        /// <summary>Goal Tracker units (gates excluded): each Ring clearing not held, and the Monolith part (yes/no).</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat) =>
            WhiteHairedManRing.UnitsMissing(ctx.Board, ctx.State, seat, Paintress(ctx), ctx.Role(Ids.MapRoles.Monolith));

        /// <summary>Where his win is answered, met parts first: the held Ring clearings an out-stack or an Exposure guess can
        /// break (fewest of his pieces first), the Monolith (the Vagabond walk-in), the Ring clearings a face-up Snare holds,
        /// then the Ring clearings not held yet (to keep them unheld).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var b = ctx.Board;
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!monolith.HasValue) return new ClearingId[0];
            var p = Paintress(ctx);
            var ring = WhiteHairedManRing.Ring(b, monolith);
            var held = ring.Where(r => WhiteHairedManRing.RingClearingHeld(b, ctx.State, seat, p, r)).ToList();
            var list = new List<ClearingId>();
            list.AddRange(held.Where(r => !WhiteHairedManRing.FaceUpSnare(b, seat, r))
                .OrderBy(r => WhiteHairedManRing.OwnWarriors(b, ctx.State, seat, r) + WhiteHairedManRing.Plots(b, seat, r)).ThenBy(r => r.Value));
            list.Add(monolith.Value);
            list.AddRange(held.Where(r => WhiteHairedManRing.FaceUpSnare(b, seat, r)).OrderBy(r => r.Value));
            list.AddRange(ring.Where(r => !held.Contains(r)).OrderBy(r => r.Value));
            return list;
        }
    }
}
