using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core;

namespace Root33.Host
{
    /// <summary>
    /// The mod's own display state for the display actions (do.show-banner, do.clear-banner, do.set-seat-title,
    /// do.show-overlay, do.hide-overlay), for a host that draws them itself: the plugin's panel shows the banners and titles
    /// before they are drawn on Root's own HUD. Overlays are kept as keys (<see cref="OverlayKey"/>) until the host can anchor
    /// art to Root's clearings and boards. Speech bubbles go to <see cref="ModFeed"/>. The tabletop keeps the same state in
    /// its save (SimState.Mod) instead.
    /// </summary>
    public sealed class ModDisplay
    {
        private readonly SortedDictionary<int, string> _banners = new SortedDictionary<int, string>();
        private readonly SortedDictionary<int, string> _titles = new SortedDictionary<int, string>();
        private readonly List<string> _overlays = new List<string>();

        /// <summary>do.show-banner: true when the banner is new or changed (the host then announces it).</summary>
        public bool ShowBanner(SeatId seat, string winConditionId)
        {
            var changed = !_banners.TryGetValue(seat.Value, out var old) || old != winConditionId;
            _banners[seat.Value] = winConditionId;
            return changed;
        }

        /// <summary>do.clear-banner: the win id of the banner that came down, or null when the seat had none.</summary>
        public string? ClearBanner(SeatId seat)
        {
            if (!_banners.TryGetValue(seat.Value, out var winId)) return null;
            _banners.Remove(seat.Value);
            return winId;
        }

        /// <summary>The banners up now, by seat (for <see cref="IModHost.Banners"/>).</summary>
        public IReadOnlyList<KeyValuePair<SeatId, string>> Banners() =>
            _banners.Select(kv => new KeyValuePair<SeatId, string>(new SeatId(kv.Key), kv.Value)).ToList();

        /// <summary>do.set-seat-title: true when the title changed.</summary>
        public bool SetSeatTitle(SeatId seat, string title)
        {
            var changed = !_titles.TryGetValue(seat.Value, out var old) || old != title;
            _titles[seat.Value] = title;
            return changed;
        }

        /// <summary>The title Root 33 gave a seat, or null (for <see cref="IModHost.TitleOf"/>).</summary>
        public string? TitleOf(SeatId seat) => _titles.TryGetValue(seat.Value, out var t) ? t : null;

        /// <summary>do.show-overlay (kept once per overlay and anchor).</summary>
        public void ShowOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest)
        {
            var key = OverlayKey(overlayId, seat, c, forest);
            if (!_overlays.Contains(key)) _overlays.Add(key);
        }

        /// <summary>do.hide-overlay.</summary>
        public void HideOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest) =>
            _overlays.Remove(OverlayKey(overlayId, seat, c, forest));

        /// <summary>The overlays shown now, as keys, in the order they were shown.</summary>
        public IReadOnlyList<string> Overlays => _overlays;

        public bool OverlayShown(string overlayId) => _overlays.Any(k => k.StartsWith(overlayId + "@", System.StringComparison.Ordinal));

        /// <summary>A new game: no banners, titles or overlays.</summary>
        public void Clear()
        {
            _banners.Clear();
            _titles.Clear();
            _overlays.Clear();
        }

        /// <summary>"ui.esquie.rocks@1/6/" = overlay id, then seat, clearing and "f"+forest anchors (empty when null).</summary>
        public static string OverlayKey(string overlayId, SeatId? seat, ClearingId? c, int? forest) =>
            overlayId + "@" + (seat.HasValue ? seat.Value.Value.ToString(CultureInfo.InvariantCulture) : "") + "/" +
            (c.HasValue ? c.Value.Value.ToString(CultureInfo.InvariantCulture) : "") + "/" +
            (forest.HasValue ? "f" + forest.Value.ToString(CultureInfo.InvariantCulture) : "");
    }
}
