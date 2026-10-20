using System;
using Root33.Core;
using Root33.Host;

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// The hand-written half of RootBoard: every member mapped onto Root's own code, marked [Mapped("row id")], plus what the
    /// mapped members share. The generated half (RootBoard.cs) holds the rows still to map; after mapping one here, rerun
    /// <c>python3 -I tools/gen_plugin_stubs.py</c> so its stub leaves RootBoard.cs, and write the confirmed symbol into the
    /// sheet's engineSymbol cell (clearing its _unverified entry).
    ///
    /// Nothing is mapped yet. The first ones (docs/PLUGIN-PLAN.md):
    /// <list type="bullet">
    /// <item>M2, the reads the Goal Tracker needs: read.seats, read.seat-engine, read.is-bot, read.current-seat, read.phase,
    /// read.round, read.vp, read.active-dominance, read.coalition-partner, read.map-name, read.clearings, read.suits-of,
    /// read.path-neighbours, read.ruler, read.warriors, read.buildings(-of-type), read.tokens(-of-type), read.pawn-clearing,
    /// read.relationship, read.items. Clearings come back as the core's numbers: <see cref="EngineIds.AutumnClearingFromRoot"/>.</item>
    /// <item>M3, the six display actions, which need no Root code at first: the mod's panel draws them from
    /// <see cref="Display"/> and <see cref="BubbleShown"/> (anchoring them to Root's HUD and portraits comes in M5):
    /// <code>
    /// [Mapped("do.show-banner")]  public void ShowBanner(SeatId seat, string winConditionId) => Display.ShowBanner(seat, winConditionId);
    /// [Mapped("do.clear-banner")] public void ClearBanner(SeatId seat) => Display.ClearBanner(seat);
    /// [Mapped("do.show-bubble")]  public void ShowBubble(SeatId seat, string barkId) => BubbleShown?.Invoke(seat, barkId);
    /// [Mapped("do.show-overlay")] public void ShowOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest) => Display.ShowOverlay(overlayId, seat, c, forest);
    /// [Mapped("do.hide-overlay")] public void HideOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest) => Display.HideOverlay(overlayId, seat, c, forest);
    /// [Mapped("do.set-seat-title")] public void SetSeatTitle(SeatId seat, string title) => Display.SetSeatTitle(seat, title);
    /// </code></item>
    /// </list>
    /// A read maps like this (Root's names are placeholders until Root33.Discover and the probe confirm them):
    /// <code>
    /// [Mapped("read.vp")]
    /// public int Vp(SeatId seat) => Game.Players[seat.Value].Score;   // Game: the live Root object the read comes from
    /// </code>
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
    }
}
