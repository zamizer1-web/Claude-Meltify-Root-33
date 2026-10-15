using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Verso
{
    /// <summary>The parts of win.verso's test, shared by the win, the Goal Tracker and the bot (public board reads only).</summary>
    public static class VersoRules
    {
        /// <summary>Swords with Damaged == false (read.items).</summary>
        public static int UndamagedSwords(IBoardView b, SeatId me) => b.Items(me).Count(i => i.Type == ItemType.Sword && !i.Damaged);

        /// <summary>Rank S, or (a human Verso on Easy) rank tune.verso-easy-rank with tune.verso-easy-swords undamaged swords.</summary>
        public static bool RankQualifies(GameContext ctx, SeatId me)
        {
            var rank = ctx.State.VersoRank;
            if (rank == Tuning.VersoRankS) return true;
            return !ctx.Board.IsBot(me) && ctx.Difficulty == Difficulty.Easy && rank >= Tuning.VersoEasyRank
                   && UndamagedSwords(ctx.Board, me) >= Tuning.VersoEasySwords;
        }

        public static bool InMonolith(GameContext ctx, SeatId me) => InMonolith(ctx.Board, me, ctx.Role(Ids.MapRoles.Monolith));

        public static bool InMonolith(IBoardView b, SeatId me, ClearingId? monolith) =>
            monolith.HasValue && b.PawnClearing(me) == monolith.Value;

        /// <summary>No other seat has warriors, buildings or a pawn in the Monolith (tokens do not count).</summary>
        public static bool MonolithAlone(GameContext ctx, SeatId me) => MonolithAlone(ctx.Board, me, ctx.Role(Ids.MapRoles.Monolith));

        public static bool MonolithAlone(IBoardView b, SeatId me, ClearingId? monolith)
        {
            if (!monolith.HasValue) return false;
            return b.Seats().Where(o => o != me).All(o =>
                b.Warriors(o, monolith.Value) + b.Buildings(o, monolith.Value) == 0 && b.PawnClearing(o) != monolith.Value);
        }
    }

    /// <summary>
    /// win.verso, The Last Note (countdown). Gates (act &gt;= 2, Vp &gt;= tune.countdown-vp, no coalition, no dominance) are
    /// applied by <see cref="WinRules"/>. Announce at the end of his turn: rank qualifies, pawn in the Monolith and no other
    /// seat's warriors, buildings or pawn there. Confirm at his next Birdsong: pawn still in the Monolith and the rank still
    /// qualifies; other pieces there are ignored.
    /// </summary>
    public sealed class VersoWin : WinConditionBase, IGoalProgress
    {
        public VersoWin() : base(Ids.WinConditions.Verso) { }

        public override bool Announce(GameContext ctx, SeatId seat) =>
            VersoRules.RankQualifies(ctx, seat) && VersoRules.InMonolith(ctx, seat) && VersoRules.MonolithAlone(ctx, seat);

        public override bool Confirm(GameContext ctx, SeatId seat) =>
            VersoRules.InMonolith(ctx, seat) && VersoRules.RankQualifies(ctx, seat);

        /// <summary>Goal Tracker units: rank steps short of S (or of the Easy assist), the Monolith, and the Monolith being clear.</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat) =>
            (VersoRules.RankQualifies(ctx, seat) ? 0 : Tuning.VersoRankS - ctx.State.VersoRank)
            + (VersoRules.InMonolith(ctx, seat) ? 0 : 1)
            + (VersoRules.MonolithAlone(ctx, seat) ? 0 : 1);

        /// <summary>The Monolith (where the counterplay battles him), then his pawn's clearing.</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var keys = new List<ClearingId>();
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (monolith.HasValue) keys.Add(monolith.Value);
            var pawn = ctx.Board.PawnClearing(seat);
            if (pawn.HasValue && !keys.Contains(pawn.Value)) keys.Add(pawn.Value);
            return keys;
        }
    }
}
