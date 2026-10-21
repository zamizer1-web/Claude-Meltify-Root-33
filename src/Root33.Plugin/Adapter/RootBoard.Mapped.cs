using System;
using System.Collections.Generic;
using Root33.Core;
using Root33.Host;

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// The hand-written half of RootBoard: every member mapped onto Root's own code, marked [Mapped("row id")], plus what the
    /// mapped members share. The generated half (RootBoard.cs) holds the rows still to map; after mapping one here, rerun
    /// <c>python3 -I tools/gen_plugin_stubs.py</c> so its stub leaves RootBoard.cs.
    ///
    /// Reads go to <see cref="RootReads"/> (Root's objects through <see cref="RootGame"/>). Actions that change Root's rules
    /// are recorded in <see cref="ModRules"/> for the patches in Patches/ to apply; a themed win goes to <see cref="RootWins"/>.
    /// The six display actions draw on the mod's own panel (<see cref="Display"/>, <see cref="BubbleShown"/>); anchoring them to
    /// Root's views (lotus.ViewManager) waits for the view layers to be read in a running game.
    ///
    /// Still to map (they need a running game, see each row's _unverified): read.legal-actions, read.supply-warriors,
    /// read.eyrie-leader, the relic reads (they need the relic id scheme of do.tag-relic), and the actions that push onto
    /// Root's action stack or filter its setup choices.
    /// </summary>
    public sealed partial class RootBoard
    {
        /// <summary>The mod's own display state (banners, seat titles, overlays) the display actions write and the panel draws.</summary>
        public ModDisplay Display { get; } = new ModDisplay();

        /// <summary>do.show-bubble goes here (Root33Runtime records it in the feed and logs it).</summary>
        public Action<SeatId, string>? BubbleShown { get; set; }

        /// <summary>Reads mapped onto Root so far.</summary>
        public static int MappedReads => ReadRows - UnmappedReads.Length;

        /// <summary>Actions mapped onto Root so far.</summary>
        public static int MappedActions => ActionRows - UnmappedActions.Length;

        // ------------------------------------------------------------------ reads: seats and turn

        [Mapped("read.seats")] public IReadOnlyList<SeatId> Seats() => RootReads.Seats();
        [Mapped("read.seat-engine")] public EngineId EngineOf(SeatId seat) => RootReads.EngineOf(seat);
        [Mapped("read.is-bot")] public bool IsBot(SeatId seat) => RootReads.IsBot(seat);
        [Mapped("read.current-seat")] public SeatId CurrentSeat() => RootReads.CurrentSeat();
        [Mapped("read.phase")] public TurnPhase Phase() => RootReads.Phase();
        [Mapped("read.round")] public int Round() => RootReads.Round();
        [Mapped("read.vp")] public int Vp(SeatId seat) => RootReads.Vp(seat);
        [Mapped("read.active-dominance")] public Suit? ActiveDominance(SeatId seat) => RootReads.ActiveDominance(seat);
        [Mapped("read.coalition-partner")] public SeatId? CoalitionPartner(SeatId seat) => RootReads.CoalitionPartner(seat);
        [Mapped("read.hand-size")] public int HandSize(SeatId seat) => RootReads.HandSize(seat);
        [Mapped("read.hand-suits")] public IReadOnlyList<Suit> HandSuits(SeatId seat) => RootReads.HandSuits(seat);

        // ------------------------------------------------------------------ reads: the map

        [Mapped("read.map-name")] public string MapName() => RootReads.MapName();
        [Mapped("read.clearings")] public IReadOnlyList<ClearingId> Clearings() => RootReads.Clearings();
        [Mapped("read.suits-of")] public IReadOnlyList<Suit> SuitsOf(ClearingId c) => RootReads.SuitsOf(c);
        [Mapped("read.path-neighbours")] public IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) => RootReads.PathNeighbours(c);
        [Mapped("read.river-neighbours")] public IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => RootReads.RiverNeighbours(c);
        [Mapped("read.on-river")] public bool OnRiver(ClearingId c) => RootReads.OnRiver(c);
        [Mapped("read.corners")] public IReadOnlyList<ClearingId> Corners() => RootReads.Corners();
        [Mapped("read.opposite-corner")] public ClearingId? OppositeCorner(ClearingId corner) => RootReads.OppositeCorner(corner);
        [Mapped("read.has-ruin")] public bool HasRuin(ClearingId c) => RootReads.HasRuin(c);
        [Mapped("read.total-slots")] public int TotalSlots(ClearingId c) => RootReads.TotalSlots(c);
        [Mapped("read.free-slots")] public int FreeSlots(ClearingId c) => RootReads.FreeSlots(c);
        [Mapped("read.forests-around")] public IReadOnlyList<int> ForestsAround(ClearingId c) => RootReads.ForestsAround(c);
        [Mapped("read.forest-clearings")] public IReadOnlyList<ClearingId> ForestClearings(int forest) => RootReads.ForestClearings(forest);

        // ------------------------------------------------------------------ reads: pieces

        [Mapped("read.ruler")] public SeatId? Ruler(ClearingId c) => RootReads.Ruler(c);
        [Mapped("read.warriors")] public int Warriors(SeatId seat, ClearingId c) => RootReads.Warriors(seat, c);
        [Mapped("read.buildings")] public int Buildings(SeatId seat, ClearingId c) => RootReads.Buildings(seat, c);
        [Mapped("read.buildings-of-type")] public int BuildingsOfType(SeatId seat, ClearingId c, string type) => RootReads.BuildingsOfType(seat, c, type);
        [Mapped("read.tokens")] public int Tokens(SeatId seat, ClearingId c) => RootReads.Tokens(seat, c);
        [Mapped("read.tokens-of-type")] public int TokensOfType(SeatId seat, ClearingId c, string type) => RootReads.TokensOfType(seat, c, type);
        [Mapped("read.pawn-clearing")] public ClearingId? PawnClearing(SeatId seat) => RootReads.PawnClearing(seat);
        [Mapped("read.pawn-forest")] public int? PawnForest(SeatId seat) => RootReads.PawnForest(seat);
        [Mapped("read.supply-pieces")] public int SupplyPieces(SeatId seat, string type) => RootReads.SupplyPieces(seat, type);

        // ------------------------------------------------------------------ reads: items and faction boards

        [Mapped("read.items")] public IReadOnlyList<ItemState> Items(SeatId seat) => RootReads.Items(seat);
        [Mapped("read.relationship")] public Relationship RelationshipOf(SeatId vagabond, SeatId faction) => RootReads.RelationshipOf(vagabond, faction);
        [Mapped("read.crafted-items")] public IReadOnlyList<ItemType> CraftedItems(SeatId seat) => RootReads.CraftedItems(seat);
        [Mapped("read.eyrie-decree")] public IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column) => RootReads.DecreeColumn(eyrie, column);
        [Mapped("read.alliance-supporters")] public IReadOnlyList<Suit> Supporters(SeatId alliance) => RootReads.Supporters(alliance);
        [Mapped("read.alliance-officers")] public int Officers(SeatId alliance) => RootReads.Officers(alliance);
        [Mapped("read.keepers-retinue")] public IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column) => RootReads.RetinueColumn(keepers, column);
        [Mapped("read.duchy-burrow")] public int BurrowWarriors(SeatId duchy) => RootReads.BurrowWarriors(duchy);
        [Mapped("read.duchy-ministers")] public IReadOnlyList<string> SwayedMinisters(SeatId duchy) => RootReads.SwayedMinisters(duchy);
        [Mapped("read.corvid-face-up-plot")] public string? FaceUpPlot(SeatId corvids, ClearingId c) => RootReads.FaceUpPlot(corvids, c);
        [Mapped("read.corvid-hidden-plot")] public string? HiddenPlotType(SeatId corvids, ClearingId c) => RootReads.HiddenPlotType(corvids, c);
        [Mapped("read.lizard-outcast")] public Suit? OutcastSuit(SeatId lizards) => RootReads.OutcastSuit(lizards);
        [Mapped("read.lizard-hated")] public bool OutcastHated(SeatId lizards) => RootReads.OutcastHated(lizards);
        [Mapped("read.lizard-acolytes")] public int Acolytes(SeatId lizards) => RootReads.Acolytes(lizards);
        [Mapped("read.hundreds-mood")] public string Mood(SeatId hundreds) => RootReads.Mood(hundreds);
        [Mapped("read.hundreds-hoard")] public int HoardTrack(SeatId hundreds, string track) => RootReads.HoardTrack(hundreds, track);

        // ------------------------------------------------------------------ actions on Root's rules

        [Mapped("do.declare-winner")] public void DeclareWinner(SeatId seat, string winConditionId) => RootWins.Declare(seat);
        [Mapped("do.add-hits")] public void AddHits(SeatId seat, int hits) => ModRules.AddHits(seat, hits);
        [Mapped("do.ignore-first-hit")] public void IgnoreFirstHit(SeatId seat) => ModRules.IgnoreFirstHit(seat);

        /// <summary>Root's own AI decides unless a Goal Director overrides it (do.submit-action, M6), so handing a decision
        /// back to Root needs nothing.</summary>
        [Mapped("do.native-ai-step")] public void UseNativeAi(SeatId seat) { }

        // ------------------------------------------------------------------ display actions (the mod's panel)

        [Mapped("do.show-banner")] public void ShowBanner(SeatId seat, string winConditionId) => Display.ShowBanner(seat, winConditionId);
        [Mapped("do.clear-banner")] public void ClearBanner(SeatId seat) => Display.ClearBanner(seat);
        [Mapped("do.show-bubble")] public void ShowBubble(SeatId seat, string barkId) => BubbleShown?.Invoke(seat, barkId);
        [Mapped("do.show-overlay")] public void ShowOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest) => Display.ShowOverlay(overlayId, seat, c, forest);
        [Mapped("do.hide-overlay")] public void HideOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest) => Display.HideOverlay(overlayId, seat, c, forest);
        [Mapped("do.set-seat-title")] public void SetSeatTitle(SeatId seat, string title) => Display.SetSeatTitle(seat, title);
    }
}
