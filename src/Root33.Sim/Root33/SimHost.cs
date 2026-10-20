using Root33.Core;
using Root33.Host;

namespace Root33.Sim;

/// <summary>
/// What the tabletop supplies to Root33.Host (<see cref="IModHost"/>) so the mod's texts read the same here as in Root:
/// seats in the sim's turn order, the sim's own seat records (character, VP, dominance, coalition), the seat titles and
/// countdown banners SimBoard keeps in State.Mod, and the sim's names for seats ("seat 2 (The Paintress)") and clearings.
/// </summary>
public sealed class SimHost : IModHost
{
    private readonly SimGame _g;
    public SimHost(SimGame g) { _g = g; }

    private SeatState Of(SeatId seat) => _g.Seat(seat.Value);

    public int SeatCount => _g.SeatCount;
    public IReadOnlyList<SeatId> TurnOrder() => _g.TurnOrder().Select(s => new SeatId(s)).ToArray();
    public string? CharacterOf(SeatId seat) => Of(seat).Character;
    public string? TitleOf(SeatId seat) => _g.State.Mod.SeatTitles.TryGetValue(seat.Value, out var t) ? t : null;
    public string Who(SeatId seat) => Render.Who(_g, seat.Value);
    public int Vp(SeatId seat) => Of(seat).Vp;
    public bool HasActiveDominance(SeatId seat) => Of(seat).Dominance != null;
    public bool InCoalition(SeatId seat) => Of(seat).CoalitionWith != null;
    public int Round => _g.State.Round;
    public SpoilerMode Spoilers => SpoilersOf(_g.State.Spoiler);
    public string ClearingName(ClearingId c) => _g.ClearingName(c.Value);

    public IReadOnlyList<KeyValuePair<SeatId, string>> Banners() =>
        _g.State.Mod.Banners.Select(kv => new KeyValuePair<SeatId, string>(new SeatId(kv.Key), kv.Value)).ToList();

    /// <summary>The sim's spoiler text ("safe", "finished") as the core's mode: anything but "finished" is safe.</summary>
    public static SpoilerMode SpoilersOf(string spoiler) => spoiler == "finished" ? SpoilerMode.Finished : SpoilerMode.Safe;
}
