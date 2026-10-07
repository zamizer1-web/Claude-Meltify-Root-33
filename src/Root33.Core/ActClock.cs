namespace Root33.Core
{
    /// <summary>
    /// Which act of the year the game is in. An act ends when any seat reaches its VP mark or after its last round,
    /// whichever comes first; the last act never ends. Acts only move forward.
    /// </summary>
    public sealed class ActClock
    {
        private readonly int[] _endsAtVp;
        private readonly int[] _endsAfterRound;

        /// <param name="endsAtVp">per act (index 0 = act 1): the VP mark that ends it; 0 = never</param>
        /// <param name="endsAfterRound">per act: the last round of it; 0 = never</param>
        public ActClock(int[] endsAtVp, int[] endsAfterRound)
        {
            _endsAtVp = endsAtVp;
            _endsAfterRound = endsAfterRound;
            Act = 1;
        }

        public int Act { get; private set; }
        public int LastAct => _endsAtVp.Length;

        /// <summary>Call whenever VP change and at the end of each round. Returns how many acts were advanced
        /// (a big swing can skip straight through an act).</summary>
        public int Update(int highestVp, int completedRounds)
        {
            var advanced = 0;
            while (Act < LastAct)
            {
                var i = Act - 1;
                var byVp = _endsAtVp[i] > 0 && highestVp >= _endsAtVp[i];
                var byRound = _endsAfterRound[i] > 0 && completedRounds >= _endsAfterRound[i];
                if (!byVp && !byRound) break;
                Act++;
                advanced++;
            }
            return advanced;
        }

        /// <summary>Restores the act from a save (clamped to the valid range).</summary>
        public void Restore(int act) => Act = act < 1 ? 1 : act > LastAct ? LastAct : act;
    }
}
