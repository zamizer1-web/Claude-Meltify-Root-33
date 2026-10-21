using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Paintress
{
    /// <summary>
    /// Living Portraits, worked out at each check and never stored (win.paintress): a roost outside the Monolith, in a
    /// clearing she rules (read.ruler, so Lords of the Forest ties count), with tune.paintress-portrait-warriors or more of her
    /// warriors. Everything here reads only public board facts, so the bot may use it too.
    /// </summary>
    public static class PaintressPortraits
    {
        /// <summary>The three suits a Family Portrait needs, in tracker order.</summary>
        public static readonly IReadOnlyList<Suit> NeededSuits = new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse };

        /// <summary>Display labels, one per suit, in the order of <see cref="NeededSuits"/> (finished mode; display only).</summary>
        public static readonly IReadOnlyList<string> Labels = new[] { "Verso", "Clea", "Renoir" };

        /// <summary>Her warriors in a clearing that count for a Portrait (all of them: Esquie's neutral guards are nobody's).</summary>
        public static int PortraitWarriors(IBoardView board, SeatId seat, ClearingId c) => board.Warriors(seat, c);

        /// <summary>A roost of hers outside the Monolith (an easel that can be a Portrait), whatever its warriors and rule.</summary>
        public static bool IsEasel(IBoardView board, SeatId seat, ClearingId c, ClearingId? monolith) =>
            (!monolith.HasValue || c != monolith.Value) && board.BuildingsOfType(seat, c, PieceTypes.Roost) >= 1;

        public static bool IsLivingPortrait(IBoardView board, SeatId seat, ClearingId c, ClearingId? monolith) =>
            IsEasel(board, seat, c, monolith)
            && board.Ruler(c) == seat
            && PortraitWarriors(board, seat, c) >= Tuning.PaintressPortraitWarriors;

        /// <summary>Every Living Portrait on the board, by clearing number.</summary>
        public static List<ClearingId> LivingPortraits(IBoardView board, SeatId seat, ClearingId? monolith) =>
            board.Clearings().Where(c => IsLivingPortrait(board, seat, c, monolith)).OrderBy(c => c.Value).ToList();

        /// <summary>The needed suits (fox, rabbit, mouse) covered by the given Portraits (a Lost City gives all three).</summary>
        public static HashSet<Suit> CoveredSuits(IBoardView board, IEnumerable<ClearingId> portraits)
        {
            var set = new HashSet<Suit>();
            foreach (var c in portraits)
                foreach (var s in board.SuitsOf(c))
                    if (NeededSuits.Contains(s)) set.Add(s);
            return set;
        }

        public static HashSet<Suit> CoveredSuits(IBoardView board, SeatId seat, ClearingId? monolith) =>
            CoveredSuits(board, LivingPortraits(board, seat, monolith));

        /// <summary>The needed suits still lacking a Living Portrait, in tracker order.</summary>
        public static List<Suit> MissingSuits(IBoardView board, SeatId seat, ClearingId? monolith)
        {
            var covered = CoveredSuits(board, seat, monolith);
            return NeededSuits.Where(s => !covered.Contains(s)).ToList();
        }

        /// <summary>She rules the Monolith and has tune.paintress-monolith-roosts roosts there.</summary>
        public static bool HoldsMonolith(IBoardView board, SeatId seat, ClearingId? monolith) =>
            monolith.HasValue && board.Ruler(monolith.Value) == seat
            && board.BuildingsOfType(seat, monolith.Value, PieceTypes.Roost) >= Tuning.PaintressMonolithRoosts;

        /// <summary>The themed part of win.paintress (announce and confirm alike): the Monolith held with an easel, and Living
        /// Portraits in a fox, a rabbit and a mouse clearing. Gates (act, VP, dominance) are applied by WinRules.</summary>
        public static bool FamilyPortrait(IBoardView board, SeatId seat, ClearingId? monolith) =>
            HoldsMonolith(board, seat, monolith) && MissingSuits(board, seat, monolith).Count == 0;

        /// <summary>Display only: one Living Portrait per suit labelled Verso, Clea and Renoir (lowest clearing first, each
        /// clearing used once).</summary>
        public static List<KeyValuePair<string, ClearingId>> LabelledPortraits(IBoardView board, SeatId seat, ClearingId? monolith)
        {
            var portraits = LivingPortraits(board, seat, monolith);
            var used = new HashSet<ClearingId>();
            var result = new List<KeyValuePair<string, ClearingId>>();
            for (var i = 0; i < NeededSuits.Count; i++)
            {
                var pick = portraits.Where(c => !used.Contains(c) && board.SuitsOf(c).Contains(NeededSuits[i])).Select(c => (ClearingId?)c).FirstOrDefault();
                if (!pick.HasValue) continue;
                used.Add(pick.Value);
                result.Add(new KeyValuePair<string, ClearingId>(Labels[i], pick.Value));
            }
            return result;
        }
    }
}
