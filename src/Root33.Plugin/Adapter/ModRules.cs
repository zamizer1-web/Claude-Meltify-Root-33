using System.Collections.Generic;
using Root33.Core;

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// What the core asked Root to change, kept until Root's own code reads it: extra and ignored hits for the battle being
    /// fought (Patches/RulePatches.cs) and themed winners (RootWins). Guarded by a lock: the core writes it from hooks, Root
    /// reads it on its match thread.
    /// </summary>
    internal static class ModRules
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, int> ExtraHits = new Dictionary<int, int>();
        private static readonly HashSet<int> IgnoreFirst = new HashSet<int>();

        public static void AddHits(SeatId seat, int hits)
        {
            lock (Gate) ExtraHits[seat.Value] = (ExtraHits.TryGetValue(seat.Value, out var n) ? n : 0) + hits;
        }

        public static void IgnoreFirstHit(SeatId seat)
        {
            lock (Gate) IgnoreFirst.Add(seat.Value);
        }

        public static int HitsFor(SeatId seat)
        {
            lock (Gate) return ExtraHits.TryGetValue(seat.Value, out var n) ? n : 0;
        }

        public static bool IgnoresFirstHit(SeatId seat)
        {
            lock (Gate) return IgnoreFirst.Contains(seat.Value);
        }

        /// <summary>Hit changes last one battle: cleared when a battle starts and when it has resolved.</summary>
        public static void BattleOver()
        {
            lock (Gate)
            {
                ExtraHits.Clear();
                IgnoreFirst.Clear();
            }
        }

        /// <summary>A new game (or a loaded one) starts with none of the mod's changes.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                ExtraHits.Clear();
                IgnoreFirst.Clear();
            }
        }
    }
}
