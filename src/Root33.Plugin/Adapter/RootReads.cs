using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
#if !NOUNITY && !ROOT_MONO
using tuber.canis.entities;
using tuber.canis.entities.PlayableClasses;
using RBuilding = tuber_canis.data.BuildingTypes;
using RFactions = tuber_canis.data.Factions;
using RItem = tuber_canis.data.ItemTypes;
using RPiece = tuber_canis.data.PieceTypes;
using RPlot = tuber_canis.data.PlotTypes;
using RToken = tuber_canis.data.TokenTypes;
using RFace = tuber_canis.data.TokenFace;
#endif

namespace Root33.Plugin.Adapter
{
#if !NOUNITY && !ROOT_MONO
    /// <summary>
    /// The board reads mapped onto Root's own code (sheets/board_reads.json engineSymbol), behind RootBoard's [Mapped] members.
    /// Every read goes through <see cref="RootGame"/>, so it needs a running Root game and throws otherwise.
    /// </summary>
    internal static class RootReads
    {
        private static readonly RPiece[] WarriorKind = { RPiece.Warrior };
        private static readonly RPiece[] BuildingKind = { RPiece.Building };
        private static readonly RPiece[] TokenKind = { RPiece.Token };

        // ------------------------------------------------------------------ seats and turn

        public static IReadOnlyList<SeatId> Seats() => Enumerable.Range(0, RootGame.SeatCount).Select(i => new SeatId(i)).ToList();

        public static EngineId EngineOf(SeatId seat) => RootGame.EngineOf(RootGame.Player(seat));

        public static bool IsBot(SeatId seat) => RootGame.Player(seat).IsAI;

        public static SeatId CurrentSeat() => RootGame.CurrentSeat();

        /// <summary>TuberPlayerEntity.GamePhase holds tuber_canis.data.Phases (Setup=0, Birdsong=1, Daylight=2, Evening=3);
        /// setup reads as Birdsong, the core has no setup phase.</summary>
        public static TurnPhase Phase() => RootGame.Live.CurrentTurnPlayer().GamePhase switch
        {
            2 => TurnPhase.Daylight,
            3 => TurnPhase.Evening,
            _ => TurnPhase.Birdsong,
        };

        /// <summary>read.round is unconfirmed (TuberPlaymat.OnTurn may count single turns): 0 keeps the core's own round.</summary>
        public static int Round() => 0;

        public static int Vp(SeatId seat) => TuberEntityExtensions.VictoryPoints(RootGame.Player(seat));

        public static Suit? ActiveDominance(SeatId seat)
        {
            var p = RootGame.Player(seat);
            return p.ActivatedDominance ? RootGame.SuitOf(p.DominanceSuit) : null;
        }

        public static SeatId? CoalitionPartner(SeatId seat)
        {
            var p = RootGame.Player(seat);
            for (var i = 0; i < RootGame.SeatCount; i++)
                if (i != seat.Value && p.InDirectCoalitionWith(RootGame.Player(new SeatId(i)))) return new SeatId(i);
            return null;
        }

        public static int HandSize(SeatId seat) => RootGame.Each(RootGame.Player(seat).CardsInHand).Count;

        public static IReadOnlyList<Suit> HandSuits(SeatId seat) =>
            RootGame.Each(RootGame.Player(seat).CardsInHand).Select(c => RootGame.SuitOf(TuberEntityExtensions.Suit(c)))
                .Where(s => s.HasValue).Select(s => s!.Value).ToList();

        // ------------------------------------------------------------------ the map

        public static string MapName() => RootGame.MapName(RootGame.Live.TuberMatchInitData.ChosenMap);

        public static IReadOnlyList<ClearingId> Clearings() => RootGame.Clearings(RootGame.Board.Clearings);

        public static IReadOnlyList<Suit> SuitsOf(ClearingId c)
        {
            var node = RootGame.Node(c);
            if (TuberEntityExtensions.IsLostCityClearing(node)) return new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse };
            return RootGame.SuitOf(node.Suit) is { } s ? new[] { s } : Array.Empty<Suit>();
        }

        public static IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) => RootGame.Clearings(RootGame.Node(c).ClearingNeighbors);

        public static IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => RootGame.Clearings(RootGame.Node(c).RiverNeighbors);

        public static bool OnRiver(ClearingId c) => RootGame.Node(c).HasRiver;

        public static IReadOnlyList<ClearingId> Corners() => RootGame.Clearings(RootGame.Board.Corners);

        public static ClearingId? OppositeCorner(ClearingId corner) => RootGame.ClearingOf(RootGame.Board.OppositeCornerFor(RootGame.Node(corner)));

        public static bool HasRuin(ClearingId c) => RootGame.Node(c).HasRuin;

        public static int TotalSlots(ClearingId c) => RootGame.Node(c).BuildableSpots;

        public static int FreeSlots(ClearingId c) => RootGame.Node(c).OpenBuildSlots;

        public static IReadOnlyList<int> ForestsAround(ClearingId c) =>
            RootGame.Each(RootGame.Node(c).ForestNeighbors).Select(f => f.Index).ToList();

        public static IReadOnlyList<ClearingId> ForestClearings(int forest) => RootGame.Clearings(RootGame.Forest(forest).ClearingNeighbors);

        // ------------------------------------------------------------------ pieces

        public static SeatId? Ruler(ClearingId c) => RootGame.SeatOfFaction(RootGame.Node(c).getRulingFaction(false, null));

        public static int Warriors(SeatId seat, ClearingId c) => RootGame.Node(c).CountPieces(RootGame.Player(seat).Faction, WarriorKind);

        public static int Buildings(SeatId seat, ClearingId c) => RootGame.Node(c).CountPieces(RootGame.Player(seat).Faction, BuildingKind);

        public static int Tokens(SeatId seat, ClearingId c) => RootGame.Node(c).CountPieces(RootGame.Player(seat).Faction, TokenKind);

        public static int BuildingsOfType(SeatId seat, ClearingId c, string type) =>
            RootGame.Each(RootGame.Node(c).GetFactionPieces(RootGame.Player(seat).Faction, BuildingKind)).Count(e => Matches(BuildingKey(e), type));

        public static int TokensOfType(SeatId seat, ClearingId c, string type) =>
            RootGame.Each(RootGame.Node(c).GetFactionPieces(RootGame.Player(seat).Faction, TokenKind)).Count(e => Matches(TokenKey(e), type));

        public static ClearingId? PawnClearing(SeatId seat)
        {
            var p = RootGame.Player(seat);
            var vb = p.TryCast<VagabondPlayer>();
            if (vb != null) return RootGame.ClearingOf(vb.VagabondNode);
            var lord = p.TryCast<LordOfTheHundredsPlayer>();
            return lord != null ? RootGame.ClearingOf(lord.GetWarlordClearing()) : null;
        }

        public static int? PawnForest(SeatId seat)
        {
            var node = RootGame.Player(seat).TryCast<VagabondPlayer>()?.VagabondNode;
            return node != null && node.IsForest ? node.Index : null;
        }

        public static int SupplyPieces(SeatId seat, string type)
        {
            var p = RootGame.Player(seat);
            var bare = Bare(type);
            foreach (RBuilding bt in Enum.GetValues(typeof(RBuilding)))
                if (BuildingName(bt) == bare)
                    return RootGame.Children(p.SupplyForBuildingType(bt)).Count(e => Matches(BuildingKey(e), type));
            return RootGame.Children(p.TokensArea).Count(e => Matches(TokenKey(e), type));
        }

        // ------------------------------------------------------------------ Vagabond and crafted items

        public static IReadOnlyList<ItemState> Items(SeatId seat)
        {
            var vb = RootGame.Player(seat).TryCast<VagabondPlayer>();
            if (vb == null) return Array.Empty<ItemState>();
            var list = new List<ItemState>();
            foreach (var item in RootGame.Each(vb.AllItems()))
                if (ItemOf(TuberEntityExtensions.ItemType(item)) is { } type)
                    list.Add(new ItemState(type, TuberEntityExtensions.IsDamaged(item), TuberEntityExtensions.IsExhausted(item)));
            return list;
        }

        /// <summary>Root's relationship zone, read as 0 Indifferent, 1 Amiable, 2 Friendly, 3 Allied (to check in a running
        /// game, M2); IsHostileWith wins.</summary>
        public static Relationship RelationshipOf(SeatId vagabond, SeatId faction)
        {
            var vb = RootGame.Player(vagabond).TryCast<VagabondPlayer>()
                     ?? throw new ArgumentException($"{vagabond} is not a Vagabond", nameof(vagabond));
            var f = RootGame.Player(faction).Faction;
            if (vb.IsHostileWith(f)) return Relationship.Hostile;
            return vb.GetCurrentRelationshipZone(f) switch
            {
                <= 0 => Relationship.Indifferent,
                1 => Relationship.Amiable,
                2 => Relationship.Friendly,
                _ => Relationship.Allied,
            };
        }

        public static IReadOnlyList<ItemType> CraftedItems(SeatId seat) =>
            RootGame.Children(RootGame.Player(seat).GetItemArea()).Select(e => ItemOf(TuberEntityExtensions.ItemType(e)))
                .Where(t => t.HasValue).Select(t => t!.Value).ToList();

        // ------------------------------------------------------------------ faction boards

        public static IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column)
        {
            var e = As<EyrieDynastiesPlayer>(eyrie);
            var area = column switch
            {
                "recruit" => e.RecruitDecree,
                "move" => e.MoveDecree,
                "battle" => e.BattleDecree,
                "build" => e.BuildDecree,
                _ => throw new ArgumentException("unknown Decree column " + column, nameof(column)),
            };
            return CardSuits(area, Suit.Bird);
        }

        public static IReadOnlyList<Suit> Supporters(SeatId alliance) => CardSuits(As<WoodlandAlliancePlayer>(alliance).Supporters, null);

        public static int Officers(SeatId alliance) => RootGame.Children(As<WoodlandAlliancePlayer>(alliance).OfficerPile).Count;

        public static IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column)
        {
            var k = As<KeepersInIronPlayer>(keepers);
            var area = column switch
            {
                "move" => k.MoveRetinue,
                "battle-delve" => k.BattleDelveRetinue,
                "move-recover" => k.MoveOrRecoverRetinue,
                _ => throw new ArgumentException("unknown Retinue column " + column, nameof(column)),
            };
            return CardSuits(area, null);
        }

        public static int BurrowWarriors(SeatId duchy) =>
            As<UndergroundDuchyPlayer>(duchy).Burrow.CountPieces(RFactions.UndergroundDuchy, WarriorKind);

        public static IReadOnlyList<string> SwayedMinisters(SeatId duchy) =>
            RootGame.Each(As<UndergroundDuchyPlayer>(duchy).SwayedMinisters()).Select(m => TuberEntityExtensions.ArchID(m).ToString()).ToList();

        public static string? FaceUpPlot(SeatId corvids, ClearingId c) => Plot(corvids, c, RFace.Up);

        public static string? HiddenPlotType(SeatId corvids, ClearingId c) => Plot(corvids, c, RFace.Down);

        public static Suit? OutcastSuit(SeatId lizards) => RootGame.SuitOf(As<LizardCultPlayer>(lizards).OutcastSuit);

        public static bool OutcastHated(SeatId lizards) => As<LizardCultPlayer>(lizards).OutcastType == tuber_canis.data.OutcastTypes.Hated;

        public static int Acolytes(SeatId lizards) => RootGame.Children(As<LizardCultPlayer>(lizards).Acolytes).Count;

        public static string Mood(SeatId hundreds) =>
            As<LordOfTheHundredsPlayer>(hundreds).GetCurrentMood() is { } mood ? TuberEntityExtensions.ArchID(mood).ToString() : "";

        public static int HoardTrack(SeatId hundreds, string track) => track switch
        {
            "command" => As<LordOfTheHundredsPlayer>(hundreds).GetHoardCommandActions(),
            "prowess" => As<LordOfTheHundredsPlayer>(hundreds).GetHoardProwessActions(),
            _ => throw new ArgumentException("unknown Hoard track " + track, nameof(track)),
        };

        // ------------------------------------------------------------------ helpers

        private static T As<T>(SeatId seat) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase =>
            RootGame.Player(seat).TryCast<T>() ?? throw new ArgumentException($"{seat} is not {typeof(T).Name}", nameof(seat));

        private static List<Suit> CardSuits(Canis.entities.Entity? area, Suit? noSuit)
        {
            var list = new List<Suit>();
            foreach (var card in RootGame.Children(area))
            {
                var s = RootGame.SuitOf(TuberEntityExtensions.Suit(card)) ?? noSuit;
                if (s.HasValue) list.Add(s.Value);
            }
            return list;
        }

        private static string? Plot(SeatId corvids, ClearingId c, RFace face)
        {
            var f = RootGame.Player(corvids).Faction;
            foreach (var e in RootGame.Each(RootGame.Node(c).GetFactionPieces(f, TokenKind)))
                if (TuberEntityExtensions.TokenType(e) == RToken.Plot && TuberEntityExtensions.TokenFace(e) == face)
                    return PlotName(TuberEntityExtensions.PlotType(e));
            return null;
        }

        private static string? PlotName(RPlot plot) => plot switch
        {
            RPlot.Bomb => "bomb",
            RPlot.Snare => "snare",
            RPlot.Extortion => "extortion",
            RPlot.Raid => "raid",
            _ => null,
        };

        /// <summary>A type with its suffix: "base.fox", "garden.mouse", "waystation.tablet"; others bare.</summary>
        internal static string BuildingKey(Canis.entities.Entity e)
        {
            var bt = TuberEntityExtensions.BuildingType(e);
            var name = BuildingName(bt);
            if (bt == RBuilding.Waystation)
                return name + "." + TuberEntityExtensions.RelicType(e).ToString().ToLowerInvariant();
            return RootGame.SuitOf(TuberEntityExtensions.Suit(e)) is { } s && s != Suit.Bird ? name + "." + s.ToString().ToLowerInvariant() : name;
        }

        internal static string TokenKey(Canis.entities.Entity e) => TuberEntityExtensions.TokenType(e).ToString().ToLowerInvariant();

        private static string BuildingName(RBuilding bt) => bt.ToString().ToLowerInvariant();

        private static string Bare(string type)
        {
            var dot = type.IndexOf('.');
            return dot < 0 ? type : type.Substring(0, dot);
        }

        /// <summary>'base' matches 'base.fox'; 'base.fox' matches only itself.</summary>
        private static bool Matches(string key, string wanted) => key == wanted || Bare(key) == wanted;

        private static ItemType? ItemOf(RItem item) => item switch
        {
            RItem.Boot => ItemType.Boots,
            RItem.Sword => ItemType.Sword,
            RItem.Crossbow => ItemType.Crossbow,
            RItem.Hammer => ItemType.Hammer,
            RItem.Torch => ItemType.Torch,
            RItem.Sack => ItemType.Bag,
            RItem.GoldStack => ItemType.Coins,
            RItem.TeaPot => ItemType.Tea,
            _ => null,
        };
    }
#else
    /// <summary>The cloud compile check (no Root assemblies): every read needs Root.</summary>
    internal static class RootReads
    {
        private static NotSupportedException NoRoot() => new NotSupportedException("Root 33: board reads need Root (BepInEx 6 IL2CPP build)");
        public static IReadOnlyList<SeatId> Seats() => throw NoRoot();
        public static EngineId EngineOf(SeatId seat) => throw NoRoot();
        public static bool IsBot(SeatId seat) => throw NoRoot();
        public static SeatId CurrentSeat() => throw NoRoot();
        public static TurnPhase Phase() => throw NoRoot();
        public static int Round() => 0;
        public static int Vp(SeatId seat) => throw NoRoot();
        public static Suit? ActiveDominance(SeatId seat) => throw NoRoot();
        public static SeatId? CoalitionPartner(SeatId seat) => throw NoRoot();
        public static int HandSize(SeatId seat) => throw NoRoot();
        public static IReadOnlyList<Suit> HandSuits(SeatId seat) => throw NoRoot();
        public static string MapName() => throw NoRoot();
        public static IReadOnlyList<ClearingId> Clearings() => throw NoRoot();
        public static IReadOnlyList<Suit> SuitsOf(ClearingId c) => throw NoRoot();
        public static IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) => throw NoRoot();
        public static IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => throw NoRoot();
        public static bool OnRiver(ClearingId c) => throw NoRoot();
        public static IReadOnlyList<ClearingId> Corners() => throw NoRoot();
        public static ClearingId? OppositeCorner(ClearingId corner) => throw NoRoot();
        public static bool HasRuin(ClearingId c) => throw NoRoot();
        public static int TotalSlots(ClearingId c) => throw NoRoot();
        public static int FreeSlots(ClearingId c) => throw NoRoot();
        public static IReadOnlyList<int> ForestsAround(ClearingId c) => throw NoRoot();
        public static IReadOnlyList<ClearingId> ForestClearings(int forest) => throw NoRoot();
        public static SeatId? Ruler(ClearingId c) => throw NoRoot();
        public static int Warriors(SeatId seat, ClearingId c) => throw NoRoot();
        public static int Buildings(SeatId seat, ClearingId c) => throw NoRoot();
        public static int Tokens(SeatId seat, ClearingId c) => throw NoRoot();
        public static int BuildingsOfType(SeatId seat, ClearingId c, string type) => throw NoRoot();
        public static int TokensOfType(SeatId seat, ClearingId c, string type) => throw NoRoot();
        public static ClearingId? PawnClearing(SeatId seat) => throw NoRoot();
        public static int? PawnForest(SeatId seat) => throw NoRoot();
        public static int SupplyPieces(SeatId seat, string type) => throw NoRoot();
        public static IReadOnlyList<ItemState> Items(SeatId seat) => throw NoRoot();
        public static Relationship RelationshipOf(SeatId vagabond, SeatId faction) => throw NoRoot();
        public static IReadOnlyList<ItemType> CraftedItems(SeatId seat) => throw NoRoot();
        public static IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column) => throw NoRoot();
        public static IReadOnlyList<Suit> Supporters(SeatId alliance) => throw NoRoot();
        public static int Officers(SeatId alliance) => throw NoRoot();
        public static IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column) => throw NoRoot();
        public static int BurrowWarriors(SeatId duchy) => throw NoRoot();
        public static IReadOnlyList<string> SwayedMinisters(SeatId duchy) => throw NoRoot();
        public static string? FaceUpPlot(SeatId corvids, ClearingId c) => throw NoRoot();
        public static string? HiddenPlotType(SeatId corvids, ClearingId c) => throw NoRoot();
        public static Suit? OutcastSuit(SeatId lizards) => throw NoRoot();
        public static bool OutcastHated(SeatId lizards) => throw NoRoot();
        public static int Acolytes(SeatId lizards) => throw NoRoot();
        public static string Mood(SeatId hundreds) => throw NoRoot();
        public static int HoardTrack(SeatId hundreds, string track) => throw NoRoot();
    }
#endif
}
