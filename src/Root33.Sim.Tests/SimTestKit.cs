using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

/// <summary>Builds bare tables for rule tests: setup skipped, empty board, a chosen active seat, and a hold frame.</summary>
public static class Kit
{
    public static SimGame Table(int seed = 1, params string[] characters)
    {
        if (characters.Length == 0) characters = new[] { "gustave", "paintress" };
        var g = SimGame.New(characters.Select(c => new SimGame.SeatSpec(c, true)).ToList(), seed);
        g.State.SetupDone = true;
        g.State.SetupQueue.Clear();
        g.State.Stack.Clear();
        g.State.Pending = null;
        g.State.Round = 1;
        foreach (var c in g.State.Clearings.Values) { c.Warriors.Clear(); c.Buildings.Clear(); c.Tokens.Clear(); }
        foreach (var s in g.State.Seats) s.Hand.Clear();
        Active(g, 0, TurnPhase.Daylight);
        return g;
    }

    /// <summary>Make a seat active in a phase (turn machine parked at that phase's faction step).</summary>
    public static void Active(SimGame g, int seat, TurnPhase phase)
    {
        g.State.Turn = new TurnState { Seat = seat, Phase = phase, Step = phase == TurnPhase.Birdsong ? 2 : phase == TurnPhase.Daylight ? 5 : 7 };
    }

    public static void Warriors(SimGame g, int seat, int c, int n)
    {
        g.Clearing(c).Warriors[seat] = n;
        g.Seat(seat).SupplyWarriors -= n;
    }

    public static void Building(SimGame g, int seat, int c, string type)
    {
        g.Clearing(c).Buildings.Add(new Piece(seat, type));
        g.Seat(seat).Supply[type] = g.Supply(seat, type) - 1;
    }

    public static void Token(SimGame g, int seat, int c, string type)
    {
        g.Clearing(c).Tokens.Add(new Piece(seat, type));
        g.Seat(seat).Supply[type] = g.Supply(seat, type) - 1;
    }

    /// <summary>Push a frame that always asks the active seat to hold, so Advance stops right after the frames above it.</summary>
    public static void Hold(SimGame g, int seat)
    {
        if (!SimGame.Steps.Contains("test.hold"))
            SimGame.Steps.Add("test.hold", (_, f) => new Decision(f.Seat, "hold").Add("hold-a", "hold").Add("hold-b", "hold"), (_, _, _) => { });
        g.Push(new Frame("test.hold", seat));
    }

    public static int Card(string key, Suit? suit = null) =>
        Cards.All.First(c => c.Key == key && (suit == null || c.Suit == suit)).Id;

    public static void Dice(SimGame g, int a, int b) { g.TestDice.Enqueue(a); g.TestDice.Enqueue(b); }
}
