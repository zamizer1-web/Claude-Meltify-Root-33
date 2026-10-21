#if !NOUNITY && !ROOT_MONO
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Root33.Core;
using tuber.canis;
using tuber.canis.entities;
using tuber.canis.entities.PlayableClasses;
using RFactions = tuber_canis.data.Factions;
using RMap = tuber_canis.data.MapLayout;
using RSuits = tuber_canis.data.Suits;
using RVagabond = tuber_canis.data.VagabondCharacters;

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// The live Root game the adapter reads, and the conversions between Root's objects and the core's ids. Patches hand the
    /// match over (<see cref="Track"/>) whenever Root's action stack, setup or save code runs, so nothing here calls Unity and
    /// it works on Root's match thread as well as the main thread.
    ///
    /// Seats are indexes into <c>TuberMatch.Players</c>. Clearings on the Fall map are the core's Autumn numbers through
    /// <see cref="EngineIds"/> (Root's FallBoardLayout index); on other maps a clearing is Root's index plus one. A forest's
    /// number is Root's node index (Fall forests are 12-18).
    /// </summary>
    internal static class RootGame
    {
        private static TuberMatch? _match;

        /// <summary>The match the last patch saw, or null outside a game.</summary>
        public static TuberMatch? Match => _match;

        /// <summary>Remembers the match a patch saw (cheap when it is the same one).</summary>
        public static TuberMatch? Track(Canis.Match? match)
        {
            if (match == null) return _match;
            if (_match != null && _match.Pointer == match.Pointer) return _match;
            var tm = match.TryCast<TuberMatch>();
            if (tm != null) _match = tm;
            return tm;
        }

        public static void Forget() => _match = null;

        public static TuberMatch Live => _match ?? throw new InvalidOperationException("Root 33: no Root game is running");

        public static TuberBoard Board => Live.TuberPlaymat.Board;

        public static bool IsFall => Live.TuberMatchInitData.ChosenMap == RMap.Fall;

        // ------------------------------------------------------------------ seats

        public static int SeatCount => Live.Players.Count;

        public static TuberPlayerEntity Player(SeatId seat)
        {
            var players = Live.Players;
            if (seat.Value < 0 || seat.Value >= players.Count) throw new ArgumentOutOfRangeException(nameof(seat), seat.ToString());
            return players[seat.Value];
        }

        public static SeatId? SeatOf(Il2CppObjectBase? player)
        {
            if (player == null || _match == null) return null;
            var players = _match.Players;
            for (var i = 0; i < players.Count; i++)
                if (players[i].Pointer == player.Pointer) return new SeatId(i);
            return null;
        }

        /// <summary>The seat of a battle side or mover (Root's IHasBoardPieces is a player or a hireling; hirelings have no seat).</summary>
        public static SeatId? SeatOfPieces(IHasBoardPieces? pieces) =>
            pieces == null ? null : SeatOf(pieces.TryCast<TuberPlayerEntity>());

        public static SeatId? SeatOfFaction(RFactions faction)
        {
            if (faction == RFactions.Invalid || _match == null) return null;
            var players = _match.Players;
            for (var i = 0; i < players.Count; i++)
                if (players[i].Faction == faction) return new SeatId(i);
            return null;
        }

        public static SeatId? SeatOfAccount(Canis.utils.ids.AccountID? account)
        {
            if (account == null || _match == null) return null;
            var key = account.ToString();
            var players = _match.Players;
            for (var i = 0; i < players.Count; i++)
                if (players[i].AccountID?.ToString() == key) return new SeatId(i);
            return null;
        }

        public static SeatId CurrentSeat() =>
            SeatOf(Live.CurrentTurnPlayer()) ?? throw new InvalidOperationException("Root 33: Root's current player has no seat");

        public static EngineId EngineOf(TuberPlayerEntity p)
        {
            switch (p.Faction)
            {
                case RFactions.MarquiseDeCat: return EngineId.Marquise;
                case RFactions.EyrieDynasties: return EngineId.Eyrie;
                case RFactions.WoodlandAlliance: return EngineId.Alliance;
                case RFactions.RiverfolkCompany: return EngineId.Riverfolk;
                case RFactions.LizardCult: return EngineId.Lizards;
                case RFactions.UndergroundDuchy: return EngineId.Duchy;
                case RFactions.CorvidConspiracy: return EngineId.Corvids;
                case RFactions.LordOfTheHundreds: return EngineId.Hundreds;
                case RFactions.KeepersInIron: return EngineId.Keepers;
                case RFactions.Vagabond:
                case RFactions.SecondVagabond:
                    var vb = p.TryCast<VagabondPlayer>();
                    var character = vb?.Vagabond == null ? RVagabond.Unknown : TuberEntityExtensions.VagabondCharacter(vb.Vagabond);
                    return character switch
                    {
                        RVagabond.Tinker => EngineId.VagabondTinker,
                        RVagabond.Ranger => EngineId.VagabondRanger,
                        RVagabond.Thief => EngineId.VagabondThief,
                        _ => EngineId.VagabondOther,
                    };
                default:
                    throw new NotSupportedException($"Root 33 has no engine for Root's faction {p.Faction} (Clockwork bots are not Root 33 seats)");
            }
        }

        // ------------------------------------------------------------------ clearings and forests

        public static TuberBoardNode Node(ClearingId c)
        {
            int index;
            if (IsFall)
            {
                var id = EngineIds.RootIdOfAutumnClearing(c) ?? throw new ArgumentOutOfRangeException(nameof(c), c.ToString());
                index = int.Parse(id);
            }
            else index = c.Value - 1;
            return Board.NodeAtIndex(index) ?? throw new ArgumentOutOfRangeException(nameof(c), c.ToString());
        }

        public static ClearingId? ClearingOf(TuberBoardNode? node)
        {
            if (node == null || !node.IsClearing) return null;
            return IsFall ? EngineIds.AutumnClearingFromRoot(node.Index.ToString()) : new ClearingId(node.Index + 1);
        }

        public static TuberBoardNode Forest(int forest) =>
            Board.NodeAtIndex(forest) is { IsForest: true } node ? node : throw new ArgumentOutOfRangeException(nameof(forest), forest.ToString());

        public static List<ClearingId> Clearings(Il2CppSystem.Collections.Generic.IEnumerable<TuberBoardNode>? nodes)
        {
            var list = new List<ClearingId>();
            foreach (var n in Each(nodes))
                if (ClearingOf(n) is { } c) list.Add(c);
            return list;
        }

        // ------------------------------------------------------------------ Root's values as the core's

        public static Suit? SuitOf(RSuits suit) => suit switch
        {
            RSuits.Fox => Suit.Fox,
            RSuits.Rabbit => Suit.Rabbit,
            RSuits.Mouse => Suit.Mouse,
            RSuits.Bird => Suit.Bird,
            _ => null,
        };

        public static string MapName(RMap map) => map switch
        {
            RMap.Fall => "autumn",
            RMap.Winter => "winter",
            RMap.Lake => "lake",
            RMap.Mountain => "mountain",
            _ => "unknown",
        };

        // ------------------------------------------------------------------ IL2CPP collections

        /// <summary>The items of one of Root's IEnumerables, read once into a managed list.</summary>
        public static List<T> Each<T>(Il2CppSystem.Collections.Generic.IEnumerable<T>? source)
        {
            var list = new List<T>();
            if (source == null) return list;
            var e = source.GetEnumerator();
            var step = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (step.MoveNext()) list.Add(e.Current);
            return list;
        }

        public static List<T> Each<T>(Il2CppSystem.Collections.Generic.List<T>? source)
        {
            var list = new List<T>();
            if (source == null) return list;
            for (var i = 0; i < source.Count; i++) list.Add(source[i]);
            return list;
        }

        /// <summary>The child entities of one of Root's areas (hand, supply, decree column...).</summary>
        public static List<Canis.entities.Entity> Children(Canis.entities.Entity? area) => Each(area?.Children);
    }
}
#endif
