using System.Collections.Generic;

namespace Root33.Core.Engine
{
    /// <summary>
    /// Optional, for the shared spoiler layer (bot_rules *-spoiler rows) and the public Goal Tracker: a win condition that
    /// implements it tells how far a seat is from its themed test and which clearings hold the parts already met.
    /// Only public information may be used (everything here is shown on the Goal Tracker). Wins that do not implement it
    /// are answered by the spoiler layer only once their banner is up.
    /// </summary>
    public interface IGoalProgress
    {
        /// <summary>Units of the themed test (announce or quest, gates excluded) that are still missing: 0 = met,
        /// 1 = one step away. The spoiler layer adds the tune.countdown-vp gate as one more unit itself.</summary>
        int UnitsMissing(GameContext ctx, SeatId seat);

        /// <summary>Clearings that hold the met parts of the test (the answer's key clearings), most important first.</summary>
        IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat);
    }
}
