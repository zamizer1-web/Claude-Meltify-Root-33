using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

public class AllianceTests
{
    const int M = 0, A = 1;

    static SimGame Table(TurnPhase phase = TurnPhase.Birdsong)
    {
        var g = Kit.Table(1, "gustave", "sciel");
        Kit.Active(g, A, phase);
        return g;
    }

    static int Support(SimGame g, Suit s) { int c = BKit.Take(g, s); Alliance.D(g, A).Supporters.Add(c); return c; }

    [Fact]
    public void Setup_ThreeSupportersFromTheDeck()
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("sciel", true) }, 3);
        int deck = g.State.Deck.Count;
        g.Choose(0, "sawmill:1"); g.Choose(0, "workshop:5"); g.Choose(0, "recruiter:12");
        Assert.True(g.State.SetupDone);
        Assert.Equal(3, Alliance.D(g, 1).Supporters.Count);
        Assert.Equal(deck - 3, g.State.Deck.Count);
        Assert.Equal(3, g.Seat(1).Hand.Count);
        Assert.Equal(3, g.Faction(1).Supporters(g, 1).Count);
        Assert.DoesNotContain(Cards.Label(Alliance.D(g, 1).Supporters[0]), g.Faction(1).BoardText(g, 1));   // face down
    }

    [Fact]
    public void View_ShowsTheSupportersOnlyToTheAllianceSeat()
    {
        var g = Table();
        Support(g, Suit.Fox); Support(g, Suit.Mouse);
        Assert.Contains("YOUR SUPPORTERS (face down, only you see them; 2): fox x1, mouse x1", Render.View(g, A));
        Assert.DoesNotContain("YOUR SUPPORTERS", Render.View(g, M));
        Assert.Contains("supporters: 2 card(s), face down", Render.View(g, M));
    }

    [Fact]
    public void SpreadSympathy_AnywhereFirst_ThenAdjacent_CostAndVpByTheTrack_MartialLaw()
    {
        var g = Table();
        Support(g, Suit.Fox); Support(g, Suit.Fox); Support(g, Suit.Mouse); int bird = Support(g, Suit.Bird);
        BKit.Run(g, A, new Frame("alliance.birdsong", A));
        Assert.Contains(g.Pending!.Options, o => o.Key == "spread:5");
        Assert.Contains(g.Pending.Options, o => o.Key == "spread:1");      // a bird supporter pays for rabbit
        g.Choose(A, "spread:5");
        Assert.Equal(1, g.TokensOf(A, 5, PieceTypes.Sympathy));
        Assert.Equal(0, g.Seat(A).Vp);                                    // the first token scores 0
        Assert.Contains(bird, Alliance.D(g, A).Supporters);              // exact suit spent before a bird
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key == "spread:12");   // not next to a sympathetic clearing
        g.Choose(A, "spread:6");
        Assert.Equal(1, g.Seat(A).Vp);
        Kit.Warriors(g, M, 1, 3);
        Assert.Equal(1 + 1, Alliance.SpreadCostAt(g, A, 1));             // third token costs 1, +1 martial law
        Assert.Equal(1, Alliance.SpreadCostAt(g, A, 8));
        g.Choose(A, "spread:8");                                          // the last fox supporter
        Assert.Equal(2, g.Seat(A).Vp);
        Assert.Equal(2, Alliance.SpreadCostAt(g, A, 12));
        Assert.Equal(new[] { bird }, Alliance.D(g, A).Supporters);
        Assert.Equal("test.hold", g.Pending!.Kind);                       // nothing affordable: Birdsong ends
    }

    [Fact]
    public void Revolt_RemovesEnemies_ScoresTheirBuildingsAndTokens_PlacesBaseWarriorsAndAnOfficer()
    {
        var g = Table();
        Kit.Token(g, A, 5, PieceTypes.Sympathy);
        Kit.Token(g, A, 8, PieceTypes.Sympathy);
        Support(g, Suit.Fox); Support(g, Suit.Fox); int bird = Support(g, Suit.Bird);
        Kit.Warriors(g, M, 5, 2);
        Kit.Building(g, M, 5, PieceTypes.Sawmill);
        Kit.Token(g, M, 5, PieceTypes.Wood);
        BKit.Run(g, A, new Frame("alliance.birdsong", A));
        Assert.Equal(new[] { "revolt:5", "revolt:8", "done" }, g.Pending!.Options.Select(o => o.Key));
        g.Choose(A, "revolt:5");
        Assert.Equal(0, g.Warriors(M, 5));
        Assert.Equal(0, g.BuildingsOf(M, 5));
        Assert.Equal(0, g.TokensOf(M, 5));
        Assert.Equal(2, g.Seat(A).Vp);                                    // sawmill + wood
        Assert.Equal(1, g.BuildingsOf(A, 5, "base.fox"));
        Assert.Equal(2, g.Warriors(A, 5));                                // two sympathetic fox clearings
        Assert.Equal(1, Alliance.D(g, A).Officers);
        Assert.Equal(10 - 3, g.Seat(A).SupplyWarriors);
        Assert.Equal(new[] { bird }, Alliance.D(g, A).Supporters);
        Assert.Equal(0, g.Supply(A, "base.fox"));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("revolt:"));   // the fox base is out
    }

    [Fact]
    public void Outrage_MovingIntoSympathy_CostsAMatchingCard_OrAFreeSupporterFromTheDeck()
    {
        var g = Table(TurnPhase.Daylight);
        Kit.Active(g, M, TurnPhase.Daylight);
        Kit.Token(g, A, 5, PieceTypes.Sympathy);
        Kit.Warriors(g, M, 1, 4);
        int fox = BKit.ToHand(g, M, BKit.Take(g, Suit.Fox));
        int bird = BKit.ToHand(g, M, BKit.Take(g, Suit.Bird));
        BKit.Run(g, M, Moves.Frame(M, optional: false));
        g.Choose(M, "move:1-5");
        g.Choose(M, "count:2");
        Assert.Equal("alliance.outrage", g.Pending!.Kind);
        Assert.Equal(new[] { $"give:{fox}", $"give:{bird}" }, g.Pending.Options.Select(o => o.Key));
        g.Choose(M, $"give:{fox}");
        Assert.Equal(new[] { fox }, Alliance.D(g, A).Supporters);
        Assert.Equal(new[] { bird }, g.Seat(M).Hand);

        // no matching card: the Alliance draws a supporter from the deck
        g.Seat(M).Hand.Clear();
        int rabbit = BKit.ToHand(g, M, BKit.Take(g, Suit.Rabbit));
        g.State.Pending = null;
        g.Push(Moves.Frame(M, optional: false));
        g.Advance();
        g.Choose(M, "move:1-5");
        g.Choose(M, "count:1");
        Assert.Equal(2, Alliance.D(g, A).Supporters.Count);
        Assert.Equal(new[] { rabbit }, g.Seat(M).Hand);
        Assert.Contains(g.State.Log, e => e.Only == A && e.Text.Contains("hand (Outrage)"));
    }

    [Fact]
    public void Outrage_RemovingSympathyInBattle_AndGuerrillaWar()
    {
        var g = Table(TurnPhase.Daylight);
        Kit.Active(g, M, TurnPhase.Daylight);
        Kit.Token(g, A, 5, PieceTypes.Sympathy);
        Kit.Warriors(g, M, 5, 1);
        int fox = BKit.ToHand(g, M, BKit.Take(g, Suit.Fox));
        Kit.Hold(g, M);
        Battles.Start(g, M, A, 5);
        Kit.Dice(g, 0, 0);
        g.State.Pending = null;
        g.Advance();                                                     // defenseless: 1 hit removes the sympathy
        Assert.Equal(0, g.TokensOf(A, 5));
        Assert.Equal(1, g.Seat(M).Vp);
        Assert.Equal("test.hold", g.Pending!.Kind);                       // one matching card: given at once
        Assert.Equal(new[] { fox }, Alliance.D(g, A).Supporters);

        // Guerrilla War: the defending Alliance takes the higher roll
        Kit.Warriors(g, M, 7, 3);
        Kit.Warriors(g, A, 7, 3);
        g.Push(new Frame("battle", M).Set("def", A).Set("c", 7));
        Kit.Dice(g, 3, 1);
        g.State.Pending = null;
        g.Advance();
        Assert.Equal(0, g.Warriors(M, 7));
        Assert.Equal(2, g.Warriors(A, 7));
    }

    [Fact]
    public void LosingABase_DiscardsItsSupporters_HalfTheOfficers_AndTrimsToFive()
    {
        var g = Table(TurnPhase.Daylight);
        Kit.Active(g, M, TurnPhase.Daylight);
        Kit.Building(g, A, 5, "base.fox");
        var d = Alliance.D(g, A);
        d.Officers = 3; g.Seat(A).SupplyWarriors -= 3;
        Support(g, Suit.Fox); Support(g, Suit.Bird);
        for (int i = 0; i < 4; i++) Support(g, Suit.Rabbit);
        for (int i = 0; i < 3; i++) Support(g, Suit.Mouse);
        Assert.Equal(9, d.Supporters.Count);                              // no limit with a base on the map
        Kit.Hold(g, M);
        g.RemoveBuilding(A, 5, "base.fox", M, "test");
        g.State.Pending = null;
        g.Advance();
        Assert.Equal(1, g.Seat(M).Vp);
        Assert.Equal(1, d.Officers);                                       // 3 -> lose 2
        Assert.Equal(10 - 1, g.Seat(A).SupplyWarriors);
        Assert.Equal(5, d.Supporters.Count);                              // 7 left, trimmed by the default
        Assert.DoesNotContain(d.Supporters, c => Cards.Get(c).Suit is Suit.Fox or Suit.Bird);
        Assert.Equal(1, g.Supply(A, "base.fox"));
    }

    [Fact]
    public void Daylight_Mobilize_Train_AndTheFiveCardLimitWithoutBases()
    {
        var g = Table(TurnPhase.Daylight);
        for (int i = 0; i < 5; i++) Support(g, Suit.Rabbit);
        int fox = BKit.ToHand(g, A, BKit.Take(g, Suit.Fox));
        int mouse = BKit.ToHand(g, A, BKit.Take(g, Suit.Mouse));
        BKit.Run(g, A, new Frame("alliance.daylight", A));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("train:"));   // no base on the map
        g.Choose(A, $"mobilize:{mouse}");
        Assert.Equal(5, Alliance.D(g, A).Supporters.Count);
        Assert.Contains(mouse, g.State.Discard);                          // the stack was full
        Kit.Building(g, A, 8, "base.fox");
        g.State.Pending = null;
        g.Advance();
        g.Choose(A, $"train:{fox}");
        Assert.Equal(1, Alliance.D(g, A).Officers);
        Assert.Equal(9, g.Seat(A).SupplyWarriors);
        Assert.Contains(fox, g.State.Discard);
        g.Choose(A, "end");
        Assert.Equal("test.hold", g.Pending!.Kind);
    }

    [Fact]
    public void Evening_MilitaryOperationsPerOfficer_RecruitAndOrganize_ThenDrawPerBase()
    {
        var g = Table(TurnPhase.Evening);
        Kit.Building(g, A, 5, "base.fox");
        Kit.Warriors(g, A, 5, 2);
        Alliance.D(g, A).Officers = 2; g.Seat(A).SupplyWarriors -= 2;
        BKit.Run(g, A, new Frame("alliance.evening", A));
        Assert.Contains("2 of 2", g.Pending!.Prompt);
        Assert.Contains(g.Pending.Options, o => o.Key == "move");
        g.Choose(A, "recruit:5");
        Assert.Equal(3, g.Warriors(A, 5));
        g.Choose(A, "organize:5");
        Assert.Equal(2, g.Warriors(A, 5));
        Assert.Equal(1, g.TokensOf(A, 5, PieceTypes.Sympathy));
        Assert.Equal(0, g.Seat(A).Vp);
        Assert.Equal("test.hold", g.Pending!.Kind);                       // operations used up; drew 1 + 1 base
        Assert.Equal(2, g.Seat(A).Hand.Count);
    }

    [Fact]
    public void Evening_OpeningMoveAndNotMoving_DoesNotSpendAnOperation()
    {
        // Playtest (Ren, round 3): "Move" then "Do not move" used up one of the three operations.
        var g = Table(TurnPhase.Evening);
        Kit.Building(g, A, 5, "base.fox");
        Kit.Warriors(g, A, 5, 2);
        Alliance.D(g, A).Officers = 2; g.Seat(A).SupplyWarriors -= 2;
        BKit.Run(g, A, new Frame("alliance.evening", A));
        g.Choose(A, "move");
        g.Choose(A, "skip");
        Assert.Contains("2 of 2", g.Pending!.Prompt);                      // nothing spent
        g.Choose(A, "move");
        g.Choose(A, g.Pending!.Options.First(o => o.Key.StartsWith("move:")).Key);
        g.Choose(A, "count:1");
        Assert.Contains("1 of 2", g.Pending!.Prompt);                      // a real move is one operation
    }

    [Fact]
    public void Crafting_WithSympathy_AndSaveLoad()
    {
        var g = Table(TurnPhase.Daylight);
        Kit.Token(g, A, 5, PieceTypes.Sympathy);
        Support(g, Suit.Mouse);
        Alliance.D(g, A).Officers = 1;
        int anvil = BKit.ToHand(g, A, BKit.TakeKey(g, "anvil"));
        Assert.Contains(anvil, Crafting.Craftable(g, A));
        Crafting.Craft(g, A, anvil);
        Assert.Equal(2, g.Seat(A).Vp);
        var back = SimGame.FromJson(g.ToJson());
        Assert.Equal(1, back.Faction(A).Officers(back, A));
        Assert.Equal(new[] { Suit.Mouse }, back.Faction(A).Supporters(back, A));
    }
}

/// <summary>Seeded all-bot games with the Eyrie and the Alliance (no Vagabond): no exception, no stuck decision, cards conserved.</summary>
public class AllianceEyrieBotGameTests
{
    static readonly string[][] Tables =
    {
        new[] { "gustave", "paintress", "sciel" },
        new[] { "paintress", "sciel" },
        new[] { "sciel", "gustave", "paintress" },
        new[] { "paintress", "gustave" },
    };

    [Fact]
    public void SeededBotGames_WithEyrieAndAlliance_RunCleanly()
    {
        int finished = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var table = Tables[seed % Tables.Length];
            var g = SimGame.New(table.Select(c => new SimGame.SeatSpec(c, false)).ToList(), seed);
            Bots.Run(g, maxDecisions: 200_000, roundCap: 30);
            if (g.State.GameOver) finished++;
            else Assert.True(g.State.Round > 30, $"seed {seed}: stopped in round {g.State.Round} at {g.Pending?.Kind}");
            int held = Enumerable.Range(0, g.SeatCount).Sum(s => g.Faction(s) switch
            {
                Eyrie => Eyrie.DecreeCards(g, s).Count(),
                Alliance => Alliance.SupporterCards(g, s).Count(),
                _ => 0,
            });
            Assert.Equal(54 - (table.Length == 2 ? 4 : 0),
                g.State.Deck.Count + g.State.Discard.Count + g.State.AvailableDominance.Count + held + g.State.Seats.Sum(s => s.Hand.Count + s.PlayArea.Count));
            Assert.All(g.State.Seats, s => Assert.InRange(s.Vp, 0, 60));
        }
        Assert.True(finished > 0, "no bot game with the Eyrie and the Alliance reached a win");
    }
}
