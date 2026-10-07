using System.Collections.Generic;

namespace Root33.Core
{
    /// <summary>
    /// The announce-then-confirm timing every countdown win shares (dominance timing):
    /// the owner's test passes at the end of their turn, the table sees a banner, and the owner wins
    /// at the start of their next Birdsong only if the confirm test still passes. Otherwise the banner clears.
    /// </summary>
    public sealed class Countdown
    {
        private readonly Dictionary<SeatId, string> _announced = new Dictionary<SeatId, string>();

        /// <summary>Seats with a banner up, and the win they announced.</summary>
        public IReadOnlyDictionary<SeatId, string> Announced => _announced;

        public bool IsAnnounced(SeatId seat) => _announced.ContainsKey(seat);

        /// <summary>End of the owner's turn: put the banner up when the announce test passed, clear it otherwise.
        /// Returns true when a new banner went up this call.</summary>
        public bool EndOfTurn(SeatId seat, string winId, bool announceTestPassed)
        {
            if (!announceTestPassed)
            {
                _announced.Remove(seat);
                return false;
            }
            var isNew = !_announced.ContainsKey(seat);
            _announced[seat] = winId;
            return isNew;
        }

        /// <summary>Start of the owner's Birdsong: the result of a pending countdown.</summary>
        public CountdownResult StartOfBirdsong(SeatId seat, bool confirmTestPassed)
        {
            if (!_announced.TryGetValue(seat, out var winId)) return CountdownResult.None;
            _announced.Remove(seat);
            return confirmTestPassed ? CountdownResult.Win(winId) : CountdownResult.Cleared(winId);
        }

        /// <summary>Clears every banner (new game or load).</summary>
        public void Reset() => _announced.Clear();

        /// <summary>Restores banners from a save.</summary>
        public void Restore(IEnumerable<KeyValuePair<SeatId, string>> banners)
        {
            _announced.Clear();
            foreach (var kv in banners) _announced[kv.Key] = kv.Value;
        }
    }

    public readonly struct CountdownResult
    {
        public readonly CountdownOutcome Outcome;
        public readonly string WinId;
        private CountdownResult(CountdownOutcome outcome, string winId) { Outcome = outcome; WinId = winId; }
        public static readonly CountdownResult None = new CountdownResult(CountdownOutcome.None, null);
        public static CountdownResult Win(string winId) => new CountdownResult(CountdownOutcome.Win, winId);
        public static CountdownResult Cleared(string winId) => new CountdownResult(CountdownOutcome.Cleared, winId);
    }

    public enum CountdownOutcome { None, Win, Cleared }
}
