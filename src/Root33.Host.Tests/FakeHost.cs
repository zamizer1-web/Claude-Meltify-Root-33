using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Tests.Support;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>
/// A host for Root33.Host's tests: seats, characters, VP, dominance, coalitions, titles and banners come straight from a
/// <see cref="FakeBoard"/>, seats are named the standard way (<see cref="ModText.SeatLabel"/>), clearings by their
/// Continent names. Set <see cref="FailWho"/> to make naming a seat throw (a read the plugin has not mapped yet).
/// </summary>
public sealed class FakeHost : IModHost
{
    private readonly FakeBoard _b;
    public FakeHost(FakeBoard board) { _b = board; }

    public SpoilerMode Spoilers { get; set; } = SpoilerMode.Safe;
    public int Round { get; set; } = 1;
    public bool FailWho { get; set; }

    public int SeatCount => _b.Seats().Count;
    public IReadOnlyList<SeatId> TurnOrder() => _b.Seats();
    public string? CharacterOf(SeatId seat) => _b.Characters.TryGetValue(seat, out var c) ? c : null;
    public string? TitleOf(SeatId seat) => _b.SeatTitles.TryGetValue(seat, out var t) ? t : null;

    public string Who(SeatId seat)
    {
        if (FailWho) throw new InvalidOperationException("read.seats is not mapped yet");
        return ModText.SeatLabel(seat, ModText.SeatTitle(this, seat));
    }

    public int Vp(SeatId seat) => _b.Vp(seat);
    public bool HasActiveDominance(SeatId seat) => _b.ActiveDominance(seat).HasValue;
    public bool InCoalition(SeatId seat) => _b.CoalitionPartner(seat).HasValue;
    public string ClearingName(ClearingId c) => ModText.ClearingName(c);
    public IReadOnlyList<KeyValuePair<SeatId, string>> Banners() => _b.Banners.OrderBy(kv => kv.Key.Value).ToList();
}

/// <summary>A started game over the Autumn board with the real character modules, a FakeHost, a feed and a view.</summary>
public sealed class HostTable
{
    public HostTable(FakeBoard board, SpoilerMode spoilers = SpoilerMode.Safe)
    {
        Board = board;
        Table = TestTable.Start(board, ModuleRegistry.Default(), spoilers: spoilers);
        Host = new FakeHost(board) { Spoilers = spoilers };
        Feed = new ModFeed();
        View = new ModView(Table.Ctx, Host, Feed);
    }

    public FakeBoard Board { get; }
    public TestTable Table { get; }
    public GameContext Ctx => Table.Ctx;
    public FakeHost Host { get; }
    public ModFeed Feed { get; }
    public ModView View { get; }

    /// <summary>The usual four: Gustave (Marquise), the Paintress (Eyrie, bot), Sciel (Alliance), Esquie (Tinker).</summary>
    public static FakeBoard FourSeats() => FakeBoard.Autumn()
        .Seat(0, EngineId.Marquise, "gustave")
        .Seat(1, EngineId.Eyrie, "paintress", bot: true)
        .Seat(2, EngineId.Alliance, "sciel")
        .Seat(3, EngineId.VagabondTinker, "esquie");
}
