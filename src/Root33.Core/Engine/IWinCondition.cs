using System;
using System.Collections.Generic;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    public enum WinKind { Countdown, Quest }

    /// <summary>
    /// A character's themed win (one win_conditions row). Implement only the themed part of the tests: the framework
    /// (<see cref="WinRules"/>) applies the row's gates itself, at announce, confirm and quest time:
    /// act &gt;= actMin, Vp(seat) &gt;= vpMin, no active dominance when offWithDominance, no coalition partner when offInCoalition.
    /// Countdown wins: <see cref="Announce"/> runs at the end of the owner's turn (hook.evening-end, after every module's
    /// evening-end tweaks); a pass puts the seat in state.global.countdowns and shows the banner; <see cref="Confirm"/> runs
    /// at the start of the owner's next Birdsong (hook.birdsong-start, before anything else) and on load.
    /// Quest wins: <see cref="QuestComplete"/> runs at the end of the owner's turn and wins at once.
    /// </summary>
    public interface IWinCondition
    {
        /// <summary>The win_conditions row.</summary>
        WinConditionDef Row { get; }
        string WinId { get; }
        WinKind Kind { get; }
        int ActMin { get; }
        int VpMin { get; }
        bool OffWithDominance { get; }
        bool OffInCoalition { get; }

        /// <summary>Countdown: the themed announce test (gates excluded).</summary>
        bool Announce(GameContext ctx, SeatId seat);
        /// <summary>Countdown: the themed confirm test (gates excluded).</summary>
        bool Confirm(GameContext ctx, SeatId seat);
        /// <summary>Quest: the themed quest test (gates excluded).</summary>
        bool QuestComplete(GameContext ctx, SeatId seat);
    }

    /// <summary>Reads kind and gates from the sheet row; override the tests that apply to the row's kind.</summary>
    public abstract class WinConditionBase : IWinCondition
    {
        protected WinConditionBase(string winId)
        {
            if (!Data.WinConditions.TryGetValue(winId, out var row)) throw new ArgumentException("no win_conditions row " + winId);
            Row = row;
        }

        public WinConditionDef Row { get; }
        public string WinId => Row.Id;
        public WinKind Kind => Row.Kind == "quest" ? WinKind.Quest : WinKind.Countdown;
        public int ActMin => Row.ActMin;
        public int VpMin => Row.VpMin;
        public bool OffWithDominance => Row.OffWithDominance;
        public bool OffInCoalition => Row.OffInCoalition;

        public virtual bool Announce(GameContext ctx, SeatId seat) => false;
        public virtual bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);
        public virtual bool QuestComplete(GameContext ctx, SeatId seat) => false;
    }

    /// <summary>The shared gates and timing of every themed win (sys.win-conditions).</summary>
    public static class WinRules
    {
        /// <summary>Whether a win row is unlocked in an act (act &gt;= actMin): no themed win in Act I, the Curator's from Act III.</summary>
        public static bool IsUnlocked(int act, WinConditionDef row) => act >= row.ActMin;

        /// <summary>The row's gates for a seat on the current board.</summary>
        public static bool GatesPass(GameContext ctx, SeatId seat, IWinCondition win)
        {
            if (ctx.Act < win.ActMin) return false;
            if (win.OffWithDominance && ctx.Board.ActiveDominance(seat).HasValue) return false;
            if (win.OffInCoalition && ctx.Board.CoalitionPartner(seat).HasValue) return false;
            if (win.VpMin > 0 && ctx.Board.Vp(seat) < win.VpMin) return false;
            return true;
        }

        public static bool AnnouncePasses(GameContext ctx, SeatId seat, IWinCondition win) =>
            win.Kind == WinKind.Countdown && GatesPass(ctx, seat, win) && win.Announce(ctx, seat);

        public static bool ConfirmPasses(GameContext ctx, SeatId seat, IWinCondition win) =>
            win.Kind == WinKind.Countdown && GatesPass(ctx, seat, win) && win.Confirm(ctx, seat);

        public static bool QuestPasses(GameContext ctx, SeatId seat, IWinCondition win) =>
            win.Kind == WinKind.Quest && GatesPass(ctx, seat, win) && win.QuestComplete(ctx, seat);

        /// <summary>hook.evening-end: the owner's themed-win test. Countdown: banner up (or cleared) through the shared
        /// <see cref="Countdown"/>; quest: declare the win at once. Returns true when a quest win was declared.</summary>
        public static bool EndOfTurn(GameContext ctx, SeatId seat, IWinCondition win)
        {
            if (ctx.GameOver || win == null) return false;
            if (win.Kind == WinKind.Quest)
            {
                if (!QuestPasses(ctx, seat, win)) return false;
                ctx.DeclareWinner(seat, win.WinId);
                return true;
            }
            var passed = AnnouncePasses(ctx, seat, win);
            var wasUp = ctx.Countdown.IsAnnounced(seat);
            ctx.Countdown.EndOfTurn(seat, win.WinId, passed);
            if (passed)
            {
                ctx.State.GlobalCountdowns.Add(seat);
                ctx.Actions.ShowBanner(seat, win.WinId);
            }
            else
            {
                ctx.State.GlobalCountdowns.Remove(seat);
                if (wasUp) ctx.Actions.ClearBanner(seat);
            }
            return false;
        }

        /// <summary>hook.birdsong-start (first thing): a pending countdown either wins or clears. Returns the outcome.</summary>
        public static CountdownOutcome StartOfBirdsong(GameContext ctx, SeatId seat, IWinCondition win)
        {
            if (ctx.GameOver || !ctx.Countdown.IsAnnounced(seat)) return CountdownOutcome.None;
            var confirmed = win != null && ConfirmPasses(ctx, seat, win);
            var result = ctx.Countdown.StartOfBirdsong(seat, confirmed);
            ctx.State.GlobalCountdowns.Remove(seat);
            ctx.Actions.ClearBanner(seat);
            if (result.Outcome == CountdownOutcome.Win) ctx.DeclareWinner(seat, result.WinId);
            return result.Outcome;
        }
    }
}
