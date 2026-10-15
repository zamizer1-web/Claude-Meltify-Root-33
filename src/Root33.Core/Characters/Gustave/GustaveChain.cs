using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Gustave
{
    /// <summary>
    /// Gustave's road (win.gustave): Expedition Flags (state.gustave.flags), the Chain of ruled flagged clearings connected
    /// to Lumière by paths, the Beachheads next to the Monolith and the three-suit test. Shared by the win, the tweaks and the bot.
    /// </summary>
    public static class GustaveChain
    {
        /// <summary>The suits the Chain must cover (birds never count).</summary>
        public static readonly IReadOnlyList<Suit> ChainSuitList = new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse };

        /// <summary>hook.evening-end, his seat: every clearing he rules holding at least tune.gustave-flag-buildings of his
        /// buildings gets a permanent flag. Returns the clearings flagged now.</summary>
        public static List<ClearingId> PlantFlags(IBoardView b, ModState s, SeatId seat)
        {
            var added = new List<ClearingId>();
            foreach (var c in b.Clearings().OrderBy(c => c.Value))
                if (WouldFlag(b, seat, c) && s.GustaveFlags.Add(c)) added.Add(c);
            return added;
        }

        /// <summary>Whether the end-of-turn flag step would flag <paramref name="c"/> now.</summary>
        public static bool WouldFlag(IBoardView b, SeatId seat, ClearingId c) =>
            b.Ruler(c) == seat && b.Buildings(seat, c) >= Tuning.GustaveFlagBuildings;

        /// <summary>The Chain: breadth-first from Lumière along paths (not rivers) through clearings that are flagged
        /// (<paramref name="isFlagged"/>) and ruled by <paramref name="seat"/>. Empty when Lumière itself fails.</summary>
        public static HashSet<ClearingId> Chain(IBoardView b, SeatId seat, ClearingId? lumiere, Func<ClearingId, bool> isFlagged)
        {
            if (!lumiere.HasValue) return new HashSet<ClearingId>();
            return Graph.ConnectedWithin(lumiere.Value, c => b.PathNeighbours(c), c => isFlagged(c) && b.Ruler(c) == seat);
        }

        public static HashSet<ClearingId> Chain(IBoardView b, ModState s, SeatId seat, ClearingId? lumiere) =>
            Chain(b, seat, lumiere, c => s.GustaveFlags.Contains(c));

        /// <summary>The Chain as it will stand after this turn's flag step (ruled clearings with his building count as flagged).</summary>
        public static HashSet<ClearingId> ChainAfterFlags(IBoardView b, ModState s, SeatId seat, ClearingId? lumiere) =>
            Chain(b, seat, lumiere, c => s.GustaveFlags.Contains(c) || WouldFlag(b, seat, c));

        /// <summary>Fox, rabbit and mouse covered by the Chain (a Lost City gives all three).</summary>
        public static HashSet<Suit> ChainSuits(IBoardView b, IEnumerable<ClearingId> chain)
        {
            var suits = new HashSet<Suit>();
            foreach (var c in chain)
                foreach (var s in b.SuitsOf(c))
                    if (ChainSuitList.Contains(s)) suits.Add(s);
            return suits;
        }

        /// <summary>Beachheads: the path neighbours of the Monolith.</summary>
        public static IReadOnlyList<ClearingId> Beachheads(IBoardView b, ClearingId? monolith) =>
            monolith.HasValue ? b.PathNeighbours(monolith.Value) : (IReadOnlyList<ClearingId>)new ClearingId[0];

        /// <summary>The lowest-numbered Chain clearing next to the Monolith holding tune.gustave-beachhead-buildings of his buildings.</summary>
        public static ClearingId? ChainBeachhead(IBoardView b, SeatId seat, ICollection<ClearingId> chain, ClearingId? monolith) =>
            Beachheads(b, monolith).Where(chain.Contains).Where(c => b.Buildings(seat, c) >= Tuning.GustaveBeachheadBuildings)
                .OrderBy(c => c.Value).Select(c => (ClearingId?)c).FirstOrDefault();

        /// <summary>The themed test of win.gustave (gates excluded): Arm full, and the Chain reaches a Beachhead holding his
        /// building and covers tune.gustave-chain-suits suits.</summary>
        public static bool RoadTest(IBoardView b, ModState s, SeatId seat, ClearingId? lumiere, ClearingId? monolith, bool countPendingFlags = false)
        {
            if (s.GustaveArm != Tuning.GustaveArmMax) return false;
            var chain = countPendingFlags ? ChainAfterFlags(b, s, seat, lumiere) : Chain(b, s, seat, lumiere);
            if (chain.Count == 0) return false;
            if (!ChainBeachhead(b, seat, chain, monolith).HasValue) return false;
            return ChainSuits(b, chain).Count >= Tuning.GustaveChainSuits;
        }

        /// <summary>margin(c) = his warriors + buildings in c minus the most warriors + buildings any other seat has there.</summary>
        public static int Margin(IBoardView b, SeatId seat, ClearingId c)
        {
            var mine = b.Warriors(seat, c) + b.Buildings(seat, c);
            var other = 0;
            foreach (var s in b.Seats())
                if (s != seat) other = Math.Max(other, b.Warriors(s, c) + b.Buildings(s, c));
            return mine - other;
        }
    }
}
