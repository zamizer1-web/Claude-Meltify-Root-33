using System.Collections.Generic;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Curator
{
    /// <summary>The four parts of Curtain Call's themed test (win.curator), shared by the win and the bot.</summary>
    public static class CuratorParts
    {
        /// <summary>No Paintress roost stands in the Monolith (always true when she is not seated).</summary>
        public static bool RoostGone(IBoardView b, SeatId? paintress, ClearingId monolith) =>
            !paintress.HasValue || !Seated(b, paintress.Value) || b.BuildingsOfType(paintress.Value, monolith, PieceTypes.Roost) == 0;

        /// <summary>TokensOfType(seat, Monolith, 'tunnel') &gt;= tune.curator-monolith-tunnels.</summary>
        public static bool TunnelHeld(IBoardView b, SeatId seat, ClearingId monolith) =>
            b.TokensOfType(seat, monolith, PieceTypes.Tunnel) >= Tuning.CuratorMonolithTunnels;

        /// <summary>Whether any seat has a keep in the clearing.</summary>
        public static bool KeepIn(IBoardView b, ClearingId c)
        {
            foreach (var s in b.Seats()) if (b.TokensOfType(s, c, PieceTypes.Keep) >= 1) return true;
            return false;
        }

        /// <summary>With no keep in Lumière: one of his buildings or tune.curator-lumiere-warriors of his warriors there.</summary>
        public static bool Garrisoned(IBoardView b, SeatId seat, ClearingId lumiere) =>
            KeepIn(b, lumiere) || b.Buildings(seat, lumiere) >= 1 || b.Warriors(seat, lumiere) >= Tuning.CuratorLumiereWarriors;

        /// <summary>Parts of the test not met (0 = the test passes).</summary>
        public static int Missing(IBoardView b, SeatId seat, SeatId? paintress, ClearingId monolith, ClearingId lumiere)
        {
            var n = 0;
            if (!RoostGone(b, paintress, monolith)) n++;
            if (!TunnelHeld(b, seat, monolith)) n++;
            if (b.Ruler(lumiere) != seat) n++;
            if (!Garrisoned(b, seat, lumiere)) n++;
            return n;
        }

        private static bool Seated(IBoardView b, SeatId s)
        {
            foreach (var x in b.Seats()) if (x == s) return true;
            return false;
        }
    }

    /// <summary>
    /// win.curator, Curtain Call (countdown). Gates from the row (WinRules): act &gt;= 3, Vp &gt;= tune.countdown-vp, no active
    /// dominance. Themed test, the same at announce and confirm: no Paintress roost in the Monolith, his tunnel there, he rules
    /// Lumière, and with no keep in Lumière one of his buildings or tune.curator-lumiere-warriors warriors there.
    /// </summary>
    public sealed class CuratorWin : WinConditionBase, IGoalProgress
    {
        public CuratorWin() : base(Ids.WinConditions.Curator) { }

        public override bool Announce(GameContext ctx, SeatId seat)
        {
            var m = ctx.Role(Ids.MapRoles.Monolith);
            var l = ctx.Role(Ids.MapRoles.Lumiere);
            if (!m.HasValue || !l.HasValue) return false;
            return CuratorParts.Missing(ctx.Board, seat, ctx.SeatOf(CharacterIds.Paintress), m.Value, l.Value) == 0;
        }

        /// <summary>"The same test on the current board".</summary>
        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        /// <summary>Goal Tracker units: the roost, the tunnel, the rule of Lumière and its garrison (yes/no parts).</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat)
        {
            var m = ctx.Role(Ids.MapRoles.Monolith);
            var l = ctx.Role(Ids.MapRoles.Lumiere);
            if (!m.HasValue || !l.HasValue) return 4;
            return CuratorParts.Missing(ctx.Board, seat, ctx.SeatOf(CharacterIds.Paintress), m.Value, l.Value);
        }

        /// <summary>Lumière (rule and garrison), then the Monolith (his tunnel).</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var list = new List<ClearingId>();
            var l = ctx.Role(Ids.MapRoles.Lumiere);
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (l.HasValue) list.Add(l.Value);
            if (m.HasValue) list.Add(m.Value);
            return list;
        }
    }
}
