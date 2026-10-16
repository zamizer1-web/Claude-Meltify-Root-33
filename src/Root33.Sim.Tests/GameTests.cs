using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

public class MarquiseTests
{
    static SimGame SetUp(int seed = 3)
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("paintress", true) }, seed);
        Assert.Equal("marquise.setup", g.Pending!.Kind);
        g.Choose(0, "keep:1");
        g.Choose(0, "sawmill:1");
        g.Choose(0, "workshop:5");
        g.Choose(0, "recruiter:12");
        return g;
    }

    [Fact]
    public void Setup_KeepGarrisonAndStartingBuildings()
    {
        var g = SetUp();
        Assert.Equal(1, g.TokensOf(0, 1, PieceTypes.Keep));
        foreach (var c in g.ClearingIds) Assert.Equal(c == 3 ? 0 : 1, g.Warriors(0, c));
        Assert.Equal(1, g.BuildingsOf(0, 1, PieceTypes.Sawmill));
        Assert.Equal(1, g.BuildingsOf(0, 5, PieceTypes.Workshop));
        Assert.Equal(1, g.BuildingsOf(0, 12, PieceTypes.Recruiter));
        Assert.Equal(25 - 11, g.Seat(0).SupplyWarriors);
        Assert.Equal(5, g.Supply(0, PieceTypes.Sawmill));
        Assert.True(g.State.SetupDone);
        Assert.Equal(0, g.Seat(0).Vp);
    }

    [Fact]
    public void Turn_BirdsongWood_BuildCostAndVp_Recruit_EveningDraw()
    {
        var g = SetUp();
        // play until the Marquise's first Daylight action decision (the Eyrie stub passes)
        while (g.Pending!.Kind != "marquise.daylight") g.Choose(g.Pending.Seat, g.Pending.Options.Last().Key);
        Assert.Equal(1, g.TokensOf(0, 1, PieceTypes.Wood));            // Birdsong: one wood at the sawmill
        int handBefore = g.Seat(0).Hand.Count;
        g.Choose(0, "build:sawmill:5");                                // second sawmill: 1 wood, 1 VP
        Assert.Equal(0, g.TokensOf(0, 1, PieceTypes.Wood));            // paid from a connected ruled clearing
        Assert.Equal(1, g.Seat(0).Vp);
        g.Choose(0, "recruit");
        Assert.Equal(2, g.Warriors(0, 12));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key == "recruit");   // once per turn
        g.Choose(0, "march");
        g.Choose(0, "move:12>4");
        g.Choose(0, "count:2");
        Assert.Equal(3, g.Warriors(0, 4));
        g.Choose(0, "skip");                                           // second move of the march
        Assert.Contains("0 action(s) left", g.Pending!.Prompt);
        g.Choose(0, "end");
        int drawn = g.Seat(0).Hand.Count - handBefore;
        Assert.True(drawn == 1 || g.Pending?.Kind == "discard");      // 1 card: no recruiter draw bonus yet
    }

    [Fact]
    public void BirdCardsBuyExtraActions_AndOverworkPlacesWood()
    {
        var g = SetUp();
        while (g.Pending!.Kind != "marquise.daylight") g.Choose(g.Pending.Seat, g.Pending.Options.Last().Key);
        int bird = Kit.Card("armorers");
        int rabbit = Kit.Card("cobbler");
        foreach (var c in new[] { bird, rabbit }) { g.State.Deck.Remove(c); g.State.Discard.Remove(c); g.Seat(1).Hand.Remove(c); g.Seat(0).Hand.Add(c); }
        g.State.Pending = null;
        g.Advance();
        Assert.Contains(g.Pending!.Options, o => o.Key == $"extra:{bird}");
        g.Choose(0, $"extra:{bird}");
        Assert.Contains("4 action(s) left", g.Pending!.Prompt);
        g.Choose(0, $"overwork:1:{rabbit}");                          // Lumière is a rabbit clearing
        Assert.Equal(2, g.TokensOf(0, 1, PieceTypes.Wood));
    }

    [Fact]
    public void EveningDrawBonus_FromTheRecruiterTrack()
    {
        var g = Kit.Table();
        Assert.Equal(0, Marquise.DrawBonus(g, 0));
        foreach (var c in new[] { 5, 6, 7 }) Kit.Building(g, 0, c, PieceTypes.Recruiter);
        Assert.Equal(1, Marquise.DrawBonus(g, 0));
        foreach (var c in new[] { 8, 9 }) Kit.Building(g, 0, c, PieceTypes.Recruiter);
        Assert.Equal(2, Marquise.DrawBonus(g, 0));
    }

    [Fact]
    public void FieldHospitals_ReturnRemovedWarriorsToTheKeep()
    {
        var g = Kit.Table();
        Kit.Active(g, 1, TurnPhase.Daylight);
        Kit.Token(g, 0, 1, PieceTypes.Keep);
        Kit.Warriors(g, 0, 5, 2);
        Kit.Warriors(g, 1, 5, 3);
        int fox = Kit.Card("anvil");
        g.Seat(0).Hand.Add(fox);
        Kit.Dice(g, 3, 0);
        Kit.Hold(g, 1);
        Battles.Start(g, 1, 0, 5);
        g.Advance();
        Assert.Equal(0, g.Warriors(0, 5));
        Assert.Equal(2, g.Warriors(0, 1));          // default for a non-active Marquise: 2+ warriors and a matching card
        Assert.DoesNotContain(fox, g.Seat(0).Hand);
    }
}

public class SaveLoadTests
{
    [Fact]
    public void SaveLoad_RoundTripsAndContinuesIdentically()
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", false), new SimGame.SeatSpec("paintress", false), new SimGame.SeatSpec("esquie", false) }, 11);
        Bots.Run(g, maxDecisions: 150);
        var json = g.ToJson();
        var h = SimGame.FromJson(json);
        Assert.Equal(json, h.ToJson());
        Bots.Run(g, maxDecisions: 200);
        Bots.Run(h, maxDecisions: 200);
        Assert.Equal(g.ToJson(), h.ToJson());

        var path = Path.Combine(Path.GetTempPath(), "root33-sim-" + Guid.NewGuid().ToString("N") + ".json");
        try { g.Save(path); Assert.Equal(g.ToJson(), SimGame.Load(path).ToJson()); }
        finally { File.Delete(path); }
    }
}

public class RobustnessTests
{
    static readonly string[][] Tables =
    {
        new[] { "gustave", "paintress", "sciel", "esquie" },
        new[] { "gustave", "paintress", "verso", "maelle" },
        new[] { "gustave", "sciel" },
        new[] { "paintress", "gustave", "maelle" },
        new[] { "esquie", "gustave", "verso" },
        new[] { "sciel", "maelle", "gustave", "paintress" },
    };

    [Fact]
    public void HundredSeededBotGames_FinishOrReachTheRoundCap_WithoutGettingStuck()
    {
        const int roundCap = 30;
        int finished = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            var table = Tables[seed % Tables.Length];
            var g = SimGame.New(table.Select(c => new SimGame.SeatSpec(c, false)).ToList(), seed);
            Bots.Run(g, maxDecisions: 200_000, roundCap: roundCap);
            if (g.State.GameOver) { finished++; Assert.Null(g.Pending); Assert.NotEmpty(g.State.GameOverText!); }
            else
            {
                Assert.NotNull(g.Pending);
                Assert.True(g.State.Round > roundCap, $"seed {seed}: stopped in round {g.State.Round} at {g.Pending!.Kind}");
                Assert.NotEmpty(g.Pending!.Options);
            }
            Assert.All(g.State.Seats, s => Assert.InRange(s.Vp, 0, 60));
            Assert.Equal(54 - (table.Length == 2 ? 4 : 0),
                g.State.Deck.Count + g.State.Discard.Count + g.State.AvailableDominance.Count + g.State.Seats.Sum(s => s.Hand.Count + s.PlayArea.Count));
        }
        Assert.True(finished > 0, "no bot game reached a win");
    }
}

public class CliTests
{
    [Fact]
    public void Cli_NewViewDoStatus_EndWithTheStatusLine()
    {
        var path = Path.Combine(Path.GetTempPath(), "root33-cli-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var w = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "new", "--save", path, "--seed", "7", "--spoiler", "safe", "--seats", "gustave:human,paintress:bot,sciel:bot,esquie:bot" }, w));
            Assert.StartsWith("STATUS: round 0 (setup) · seat 1 (gustave) to act", Last(w));
            w = new StringWriter();
            Program.Run(new[] { "view", "--save", path, "--seat", "1" }, w);
            Assert.Contains("YOUR HAND (3)", w.ToString());
            Assert.Contains("keep:1", w.ToString());
            w = new StringWriter();
            Assert.Equal(1, Program.Run(new[] { "do", "--save", path, "--seat", "2", "1" }, w));
            Assert.Contains("not seat 2's", w.ToString());
            w = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "do", "--save", path, "--seat", "1", "keep:1" }, w));
            Assert.Contains("garrisons a warrior", w.ToString());
            Assert.StartsWith("STATUS:", Last(w));
            w = new StringWriter();
            Program.Run(new[] { "status", "--save", path }, w);
            Assert.StartsWith("STATUS:", Last(w));
            w = new StringWriter();
            Program.Run(new[] { "rules", "--save", path, "--seat", "1" }, w);
            Assert.Contains("MARQUISE DE CAT", w.ToString());
            w = new StringWriter();
            Program.Run(new[] { "log", "--save", path, "--since", "3" }, w);
            Assert.DoesNotContain("You drew", w.ToString());
            w = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "select", "--spoiler", "finished" }, w));
            Assert.Contains("Woodland Alliance", w.ToString());
        }
        finally { foreach (var f in new[] { path, path + ".lock" }) if (File.Exists(f)) File.Delete(f); }
    }

    [Theory]
    [InlineData("gustave:human,gustave:human")]
    [InlineData("gustave:human,lune:human")]
    [InlineData("esquie:human,verso:human,maelle:human")]
    [InlineData("gustave:human")]
    public void ParseSeats_RejectsIllegalTables(string spec) => Assert.Throws<SimUsageException>(() => Program.ParseSeats(spec));

    static string Last(StringWriter w) => w.ToString().TrimEnd().Split('\n').Last().Trim();
}
