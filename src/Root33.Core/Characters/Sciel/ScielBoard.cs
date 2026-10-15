using System.Collections.Generic;
using System.Linq;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>
    /// Board reads shared by Sciel's win, tweaks and bot. Every member takes an <see cref="IBoardView"/>, so the bot can pass
    /// its <see cref="Engine.PublicBoardView"/>: none of these reads is hidden information.
    /// </summary>
    public static class ScielBoard
    {
        /// <summary>The three clearing suits a Sanctuary set must span (win.sciel).</summary>
        public static readonly Suit[] ClearingSuits = { Suit.Fox, Suit.Rabbit, Suit.Mouse };

        /// <summary>Her Lantern (Alliance sympathy token) stands in the clearing.</summary>
        public static bool HasLantern(IBoardView b, SeatId me, ClearingId c) => b.TokensOfType(me, c, PieceTypes.Sympathy) >= 1;

        public static IEnumerable<SeatId> Others(IBoardView b, SeatId me) => b.Seats().Where(s => s != me);

        /// <summary>Other seats' warriors, buildings, tokens and pawns in a clearing (a pawn counts as one piece).</summary>
        public static int EnemyPieces(IBoardView b, SeatId me, ClearingId c) =>
            Others(b, me).Sum(o => b.Warriors(o, c) + b.Buildings(o, c) + b.Tokens(o, c) + (b.PawnClearing(o) == c ? 1 : 0));

        public static int EnemyWarriors(IBoardView b, SeatId me, ClearingId c) => Others(b, me).Sum(o => b.Warriors(o, c));

        public static int EnemyBuildingsAndTokens(IBoardView b, SeatId me, ClearingId c) =>
            Others(b, me).Sum(o => b.Buildings(o, c) + b.Tokens(o, c));

        public static int EnemyTokens(IBoardView b, SeatId me, ClearingId c) => Others(b, me).Sum(o => b.Tokens(o, c));

        public static bool EnemyPawnIn(IBoardView b, SeatId me, ClearingId c) => Others(b, me).Any(o => b.PawnClearing(o) == c);

        /// <summary>Enemy warriors in the clearings joined to <paramref name="c"/> by a path.</summary>
        public static int EnemyWarriorsAdjacent(IBoardView b, SeatId me, ClearingId c) =>
            b.PathNeighbours(c).Sum(n => EnemyWarriors(b, me, n));

        /// <summary>win.sciel announce: her Lantern and, for every other seat, no warrior, building, token or pawn.</summary>
        public static bool IsSanctuary(IBoardView b, SeatId me, ClearingId c) => HasLantern(b, me, c) && EnemyPieces(b, me, c) == 0;

        /// <summary>win.sciel confirm: her Lantern and no other seat's building or token (warriors and pawns are ignored).</summary>
        public static bool IsHeldLantern(IBoardView b, SeatId me, ClearingId c) => HasLantern(b, me, c) && EnemyBuildingsAndTokens(b, me, c) == 0;

        public static List<ClearingId> Lanterns(IBoardView b, SeatId me) => b.Clearings().Where(c => HasLantern(b, me, c)).ToList();

        public static List<ClearingId> Sanctuaries(IBoardView b, SeatId me) => b.Clearings().Where(c => IsSanctuary(b, me, c)).ToList();

        public static List<ClearingId> HeldLanterns(IBoardView b, SeatId me) => b.Clearings().Where(c => IsHeldLantern(b, me, c)).ToList();

        public static HashSet<Suit> SuitsCovered(IBoardView b, IEnumerable<ClearingId> clearings) =>
            new HashSet<Suit>(clearings.SelectMany(b.SuitsOf).Where(s => s != Suit.Bird));

        /// <summary>How many of fox, rabbit and mouse the clearings do not cover.</summary>
        public static int MissingSuits(IBoardView b, IEnumerable<ClearingId> clearings)
        {
            var have = SuitsCovered(b, clearings);
            return ClearingSuits.Count(s => !have.Contains(s));
        }

        public static bool CoversAllSuits(IBoardView b, IEnumerable<ClearingId> clearings) => MissingSuits(b, clearings) == 0;

        /// <summary>Her Aquafarms (Alliance bases) on the map.</summary>
        public static int Aquafarms(IBoardView b, SeatId me) => b.Clearings().Sum(c => b.BuildingsOfType(me, c, PieceTypes.Base));

        /// <summary>An Aquafarm stands in a clearing of this suit (a base always matches its clearing's suit).</summary>
        public static bool HasAquafarmInSuit(IBoardView b, SeatId me, Suit suit) =>
            b.Clearings().Any(c => b.BuildingsOfType(me, c, PieceTypes.Base) > 0 && b.SuitsOf(c).Contains(suit));

        /// <summary>Units the announce test still misses (gates excluded): Sanctuaries or Sanctuary suits short (one new
        /// Sanctuary in a missing suit fixes one of each), plus Aquafarms short.</summary>
        public static int UnitsMissing(IBoardView b, SeatId me)
        {
            var s = Sanctuaries(b, me);
            var sanctuariesShort = System.Math.Max(0, Sheets.Tuning.ScielSanctuaries - s.Count);
            var suitsShort = MissingSuits(b, s);
            var aquafarmsShort = System.Math.Max(0, Sheets.Tuning.ScielAquafarms - Aquafarms(b, me));
            return System.Math.Max(sanctuariesShort, suitsShort) + aquafarmsShort;
        }
    }
}
