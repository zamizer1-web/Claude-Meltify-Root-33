using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Clea
{
    /// <summary>
    /// The parts of the Nevron Tide test (win.clea), shared by the win, her tweaks and her bot. Everything here is public:
    /// her warriors, the guarded rocks (state.esquie.rocks, shown on the board) and her buildings.
    /// </summary>
    public static class CleaParts
    {
        /// <summary>GuardsLeft of the guarded rocks <paramref name="host"/> hosts in a clearing (tweak.esquie.guards-never-count).</summary>
        public static int HostedGuards(ModState s, SeatId host, ClearingId c) =>
            s.EsquieRocks.Where(r => r != null && r.Status == RockStatus.Guarded && r.HostSeat == host && r.Clearing == c).Sum(r => r.GuardsLeft);

        /// <summary>Whether she hosts a guarded rock in a clearing: she may not build, recruit or place pieces there.</summary>
        public static bool HostsGuardsIn(ModState s, SeatId host, ClearingId c) =>
            s.EsquieRocks.Any(r => r != null && r.Status == RockStatus.Guarded && r.HostSeat == host && r.Clearing == c);

        /// <summary>Her warriors in a clearing minus the guards she hosts there (never below 0).</summary>
        public static int OwnWarriors(IBoardView b, ModState s, SeatId seat, ClearingId c) =>
            Math.Max(0, b.Warriors(seat, c) - HostedGuards(s, seat, c));

        /// <summary>covered = {c : Warriors(seat, c) minus the GuardsLeft of rocks in c whose HostSeat == seat &gt;= 1}.</summary>
        public static HashSet<ClearingId> Covered(IBoardView b, ModState s, SeatId seat) =>
            new HashSet<ClearingId>(b.Clearings().Where(c => OwnWarriors(b, s, seat, c) >= 1));

        /// <summary>BuildingsOfType(seat, Flying Manor, 'garden') &gt;= tune.clea-manor-gardens.</summary>
        public static bool AtelierHeld(IBoardView b, SeatId seat, ClearingId? manor) =>
            manor.HasValue && b.BuildingsOfType(seat, manor.Value, PieceTypes.Garden) >= Tuning.CleaManorGardens;

        /// <summary>The themed test (gates excluded): tune.clea-coverage covered clearings, the Monolith among them, and the
        /// Flying Manor atelier.</summary>
        public static bool TestPasses(IBoardView b, ModState s, SeatId seat, ClearingId? monolith, ClearingId? manor)
        {
            if (!monolith.HasValue || !manor.HasValue) return false;
            var covered = Covered(b, s, seat);
            return covered.Count >= Tuning.CleaCoverage && covered.Contains(monolith.Value) && AtelierHeld(b, seat, manor);
        }

        /// <summary>
        /// Goal Tracker units still missing (gates excluded): one per covered clearing short of tune.clea-coverage, at least one
        /// while the Monolith is not covered (a warrior there is also a covered clearing), and one for a missing atelier.
        /// </summary>
        public static int UnitsMissing(IBoardView b, ModState s, SeatId seat, ClearingId? monolith, ClearingId? manor)
        {
            var covered = Covered(b, s, seat);
            var shortBy = Math.Max(0, Tuning.CleaCoverage - covered.Count);
            var monolithIn = monolith.HasValue && covered.Contains(monolith.Value);
            var coverUnits = monolithIn ? shortBy : Math.Max(1, shortBy);
            return coverUnits + (AtelierHeld(b, seat, manor) ? 0 : 1);
        }
    }

    /// <summary>
    /// win.clea, Nevron Tide (countdown). Gates from the row (<see cref="WinRules"/>): act &gt;= 2, Vp &gt;= tune.countdown-vp, no
    /// active dominance (offWithDominance; not off in a coalition). Themed test, the same at announce and confirm: her
    /// non-guard warriors stand in tune.clea-coverage or more clearings, the Monolith among them, and she has
    /// tune.clea-manor-gardens atelier (garden) in the Flying Manor.
    /// </summary>
    public sealed class CleaWin : WinConditionBase, IGoalProgress
    {
        public CleaWin() : base(Ids.WinConditions.Clea) { }

        public override bool Announce(GameContext ctx, SeatId seat) =>
            CleaParts.TestPasses(ctx.Board, ctx.State, seat, ctx.Role(Ids.MapRoles.Monolith), ctx.Role(Ids.MapRoles.FlyingManor));

        /// <summary>"The same test on the current board".</summary>
        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        public int UnitsMissing(GameContext ctx, SeatId seat) =>
            CleaParts.UnitsMissing(ctx.Board, ctx.State, seat, ctx.Role(Ids.MapRoles.Monolith), ctx.Role(Ids.MapRoles.FlyingManor));

        /// <summary>The counterplay's clearings: the Monolith (while counted), the Flying Manor (its atelier), then every counted
        /// clearing, lone Nevrons first (ties: lower clearing number).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var list = new List<ClearingId>();
            var covered = CleaParts.Covered(ctx.Board, ctx.State, seat);
            var m = ctx.Role(Ids.MapRoles.Monolith);
            var fm = ctx.Role(Ids.MapRoles.FlyingManor);
            if (m.HasValue && covered.Contains(m.Value)) list.Add(m.Value);
            if (fm.HasValue && CleaParts.AtelierHeld(ctx.Board, seat, fm) && !list.Contains(fm.Value)) list.Add(fm.Value);
            list.AddRange(covered.Where(c => !list.Contains(c))
                .OrderBy(c => CleaParts.OwnWarriors(ctx.Board, ctx.State, seat, c)).ThenBy(c => c.Value));
            return list;
        }
    }
}
