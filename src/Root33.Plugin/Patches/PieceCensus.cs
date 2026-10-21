#if !NOUNITY && !ROOT_MONO
using System.Collections.Generic;
using Root33.Core;
using Root33.Plugin.Adapter;
using tuber.canis.entities;
using RPiece = tuber_canis.data.PieceTypes;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// Every seat's pieces on the map, counted by clearing, kind and type (Vagabond pawns and the Warlord by their clearing).
    /// Root's removal actions do not say which pieces they took, so hook.piece-removed and hook.battle-resolved compare a
    /// census taken when the removal or battle starts with one taken when it has finished: what is gone was removed there.
    /// </summary>
    internal sealed class PieceCensus
    {
        private readonly Dictionary<(int Clearing, int Seat, PieceKind Kind, string? Type), int> _count =
            new Dictionary<(int, int, PieceKind, string?), int>();

        private static readonly RPiece[] Kinds = { RPiece.Warrior, RPiece.Building, RPiece.Token };

        public static PieceCensus Take()
        {
            var census = new PieceCensus();
            var seats = RootGame.SeatCount;
            foreach (var node in RootGame.Each(RootGame.Board.Clearings))
            {
                if (RootGame.ClearingOf(node) is not { } c) continue;
                for (var s = 0; s < seats; s++)
                {
                    var faction = RootGame.Player(new SeatId(s)).Faction;
                    foreach (var piece in RootGame.Each(node.GetFactionPieces(faction, Kinds)))
                    {
                        switch (TuberEntityExtensions.PieceType(piece))
                        {
                            case RPiece.Warrior: census.Add(c, s, PieceKind.Warrior, null); break;
                            case RPiece.Building: census.Add(c, s, PieceKind.Building, RootReads.BuildingKey(piece)); break;
                            case RPiece.Token: census.Add(c, s, PieceKind.Token, RootReads.TokenKey(piece)); break;
                        }
                    }
                }
            }
            for (var s = 0; s < seats; s++)
                if (RootReads.PawnClearing(new SeatId(s)) is { } at) census.Add(at, s, PieceKind.Pawn, null);
            return census;
        }

        private void Add(ClearingId c, int seat, PieceKind kind, string? type)
        {
            var key = (c.Value, seat, kind, type);
            _count[key] = (_count.TryGetValue(key, out var n) ? n : 0) + 1;
        }

        /// <summary>The pieces counted before and not after, one entry per piece, with their clearing.</summary>
        public List<(ClearingId Clearing, RemovedPiece Piece)> GoneSince(PieceCensus before)
        {
            var gone = new List<(ClearingId, RemovedPiece)>();
            foreach (var kv in before._count)
            {
                var left = _count.TryGetValue(kv.Key, out var n) ? n : 0;
                for (var i = left; i < kv.Value; i++)
                    gone.Add((new ClearingId(kv.Key.Clearing), new RemovedPiece(new SeatId(kv.Key.Seat), kv.Key.Kind, kv.Key.Type!)));
            }
            return gone;
        }
    }
}
#endif
