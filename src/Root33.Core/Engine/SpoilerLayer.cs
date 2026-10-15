using System.Collections.Generic;
using System.Linq;

namespace Root33.Core.Engine
{
    /// <summary>One seat the spoiler layer must answer.</summary>
    public sealed class SpoilerThreat
    {
        public SpoilerThreat(SeatId seat, bool banner, IReadOnlyList<ClearingId> keyClearings)
        {
            Seat = seat;
            Banner = banner;
            KeyClearings = keyClearings;
        }

        public SeatId Seat { get; }
        /// <summary>True when its countdown banner is up; false for a one-step threat.</summary>
        public bool Banner { get; }
        /// <summary>The public key clearings of its win (<see cref="IGoalProgress.KeyClearings"/>), most important first.</summary>
        public IReadOnlyList<ClearingId> KeyClearings { get; }
        public override string ToString() => Seat + (Banner ? " (banner)" : " (one step)");
    }

    /// <summary>
    /// The shared spoiler layer (layer 3 of every bot, the bot_rules *-spoiler rows; sys.bots). Only the part whose text is the
    /// same in all eleven plans lives here: the TRIGGER, the threat order and when the answer PRE-EMPTS the plan's own steps.
    /// Each character's director turns the chosen threat into its own Root actions (the row's per-character ANSWER and LIMITS).
    /// Everything read here is public (the Goal Tracker, banners, VP, dominance, coalitions), through <see cref="BotView"/>.
    /// </summary>
    public static class SpoilerLayer
    {
        /// <summary>
        /// TRIGGER: <paramref name="other"/> is another seat (the human included), not this bot's coalition partner, whose themed
        /// win is unlocked in the current act and not switched off (offWithDominance / offInCoalition), and that has its banner
        /// up or, when <paramref name="includeOneStep"/>, is one step from its win (the Goal Tracker shows exactly one unit
        /// missing, the tune.countdown-vp gate counted as a unit).
        /// </summary>
        public static bool Threatens(BotView view, SeatId other, bool includeOneStep, out bool banner)
        {
            banner = false;
            if (other == view.Seat) return false;
            var b = view.Board;
            if (b.CoalitionPartner(view.Seat) == other || b.CoalitionPartner(other) == view.Seat) return false;
            var win = view.WinOf(other);
            if (win == null || view.Act < win.ActMin) return false;
            if (win.OffWithDominance && b.ActiveDominance(other).HasValue) return false;
            if (win.OffInCoalition && b.CoalitionPartner(other).HasValue) return false;
            banner = view.Countdowns.Contains(other);
            if (banner) return true;
            return includeOneStep && view.UnitsMissing(other) == 1;
        }

        /// <summary>Whether a seat is one step from its win or has its banner up, as the observer sees it (no difficulty filter).</summary>
        public static bool Threatens(BotView view, SeatId other) => Threatens(view, other, true, out _);

        /// <summary>
        /// Every threat this bot answers, most urgent first: banners before one-step threats; then the seat whose next Birdsong
        /// comes first in turn order from the current seat; then the higher VP; then the lower seat. Easy answers banners only.
        /// </summary>
        public static IReadOnlyList<SpoilerThreat> Threats(BotView view)
        {
            var b = view.Board;
            var seats = b.Seats().ToList();
            var oneStep = view.Difficulty != Difficulty.Easy;
            var found = new List<(SeatId seat, bool banner)>();
            foreach (var s in seats)
                if (Threatens(view, s, oneStep, out var banner)) found.Add((s, banner));
            if (found.Count == 0) return new SpoilerThreat[0];
            var cur = seats.IndexOf(b.CurrentSeat());
            int NextBirdsong(SeatId s)
            {
                var i = seats.IndexOf(s);
                return cur < 0 ? i : (i - cur + seats.Count) % seats.Count;
            }
            return found.OrderByDescending(x => x.banner).ThenBy(x => NextBirdsong(x.seat)).ThenByDescending(x => b.Vp(x.seat))
                .ThenBy(x => x.seat.Value)
                .Select(x => new SpoilerThreat(x.seat, x.banner, view.KeyClearings(x.seat))).ToList();
        }

        /// <summary>The most urgent threat, or null with no trigger (the layer then does nothing).</summary>
        public static SpoilerThreat Top(BotView view) => Threats(view).FirstOrDefault();

        /// <summary>
        /// PRIORITY: the layer takes the actions it needs ahead of the plan's own steps for a banner that is up (unless this bot
        /// can finish a quest win this turn) and, on Hard only, for a one-step threat (unless this is the bot's own announce
        /// turn). Otherwise it answers with the actions the steps leave free. Easy never pre-empts.
        /// </summary>
        public static bool PreEmpts(BotView view, SpoilerThreat threat, bool ownAnnounceTurn = false, bool canFinishQuestThisTurn = false)
        {
            if (threat == null || view.Difficulty == Difficulty.Easy) return false;
            if (threat.Banner) return !canFinishQuestThisTurn;
            return view.Difficulty == Difficulty.Hard && !ownAnnounceTurn;
        }
    }
}
