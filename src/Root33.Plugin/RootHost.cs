using System.Collections.Generic;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Host;
using Root33.Plugin.Adapter;

namespace Root33.Plugin
{
    /// <summary>
    /// What the plugin supplies to Root33.Host (<see cref="IModHost"/>): seats, VP, dominance and coalitions through the
    /// board adapter (so they work as soon as those reads are mapped), characters and the round from the core, titles and
    /// banners from the mod's own display state, Continent names for Autumn's clearings. Seats are named the standard way
    /// ("seat 2 (The Paintress)"); a seat playing plain Root by its faction.
    /// </summary>
    public sealed class RootHost : IModHost
    {
        private readonly RootBoard _board;
        private readonly GameContext _ctx;
        private readonly PluginSettings _settings;

        public RootHost(RootBoard board, GameContext ctx, PluginSettings settings)
        {
            _board = board;
            _ctx = ctx;
            _settings = settings;
        }

        public int SeatCount => _board.Seats().Count;
        public IReadOnlyList<SeatId> TurnOrder() => _ctx.TurnOrder.Count > 0 ? _ctx.TurnOrder : _board.Seats();
        public string? CharacterOf(SeatId seat) => _ctx.CharacterOf(seat);
        public string? TitleOf(SeatId seat) => _board.Display.TitleOf(seat);

        public string Who(SeatId seat) =>
            ModText.SeatLabel(seat, CharacterOf(seat) != null || TitleOf(seat) != null ? ModText.SeatTitle(this, seat) : ModText.EngineName(_board.EngineOf(seat)));

        public int Vp(SeatId seat) => _board.Vp(seat);
        public bool HasActiveDominance(SeatId seat) => _board.ActiveDominance(seat).HasValue;
        public bool InCoalition(SeatId seat) => _board.CoalitionPartner(seat).HasValue;
        public int Round => _ctx.Round;
        public SpoilerMode Spoilers => _ctx.E33Mode ? _ctx.Spoilers : _settings.Spoilers;

        /// <summary>Continent names on Autumn (the only map of the first release); plain numbers on another map.</summary>
        public string ClearingName(ClearingId c)
        {
            bool autumn;
            try { autumn = _board.MapName() == AutumnMap.MapName; }
            catch (NotMappedException) { autumn = true; }
            return autumn ? ModText.ClearingName(c) : "c" + c.Value;
        }

        public IReadOnlyList<KeyValuePair<SeatId, string>> Banners() => _board.Display.Banners();
    }
}
