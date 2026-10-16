using Root33.Core;
using Root33.Core.Sheets;
using Root33.Sim;

namespace Root33.Sim.Tests;

/// <summary>The tabletop runs on the real Root 33 core: forced homes, themed wins, acts, story events, spawns and barks
/// fire through CoreBridge and SimBoard (engineer D).</summary>
public class CoreBridgeTests
{
    static SimGame NewGame(int seed, bool human, params string[] characters) =>
        SimGame.New(characters.Select(c => new SimGame.SeatSpec(c, human)).ToList(), seed);

    static string PublicLog(SimGame g) => string.Join("\n", g.LogFor(null).Select(e => e.Text));

    [Fact]
    public void NewGame_IsAnE33Game_WithSeatTitlesAndForcedHomes()
    {
        var g = NewGame(3, true, "gustave", "paintress");
        Assert.True(g.Bridge.Ctx.E33Mode);
        Assert.Equal("gustave", g.Bridge.Ctx.CharacterOf(new SeatId(0)));
        Assert.Equal("The Paintress", ModText.SeatTitle(g, 1));
        // Gustave's keep goes in Lumière (clearing 1): the only legal keep, so it is placed at once
        Assert.Equal(new[] { 1 }, g.Bridge.ForcedHome(0));
        Assert.Equal(1, g.TokensOf(0, 1, PieceTypes.Keep));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("keep:"));
        g.Choose(0, "sawmill:1");
        g.Choose(0, "workshop:5");
        g.Choose(0, "recruiter:12");
        // the Paintress's first roost goes in the Monolith (clearing 3)
        Assert.Equal(new[] { 3 }, g.Bridge.ForcedHome(1));
        Assert.Equal(1, g.BuildingsOf(1, 3, PieceTypes.Roost));
        Assert.Empty(g.Bridge.Errors);
    }

    [Fact]
    public void SetupComplete_FiresActOneStory_TheActBanner_AndOpeningBarks()
    {
        var g = NewGame(5, false, "gustave", "paintress", "sciel");
        Bots.Run(g, roundCap: 1);
        Assert.True(g.State.SetupDone);
        var log = PublicLog(g);
        Assert.Contains(g.Bridge.Story, s => s.EventId == Ids.StoryEvents.Act1Landing);
        Assert.Contains("Root 33 story (Act I: The Landing)", log);
        Assert.Contains("=== Root 33 · Act I: The Landing ===", log);
        Assert.NotEmpty(g.Bridge.Bubbles);
        var b = g.Bridge.Bubbles[0];
        Assert.Contains($"says: \"{ModText.BarkText(b.BarkId)}\"", log);
        Assert.Empty(g.Bridge.Errors);
    }

    [Fact]
    public void Bubbles_AreQueuedPerSeat_UntilThatSeatsTurnEnds()
    {
        var g = Kit.Table(1, "gustave", "paintress");
        var line = Data.Barks.Values.First(x => x.Character == "paintress").Id;
        g.Bridge.Board.ShowBubble(new SeatId(1), line);
        Assert.Contains(ModText.BarkText(line), g.Bridge.ModView(0));
        Assert.Contains(ModText.BarkText(line), g.Bridge.ModView(1));
        g.Bridge.OnTurnEnd(0);
        Assert.DoesNotContain(ModText.BarkText(line), g.Bridge.ModView(0));
        Assert.Contains(ModText.BarkText(line), g.Bridge.ModView(1));
        // the queue survives a reload (every CLI command reloads)
        var loaded = SimGame.FromJson(g.ToJson());
        Assert.DoesNotContain(ModText.BarkText(line), loaded.Bridge.ModView(0));
        Assert.Contains(ModText.BarkText(line), loaded.Bridge.ModView(1));
    }

    /// <summary>The Paintress meets The Family Portrait in Act II: banner at the end of her turn, win at her next Birdsong.</summary>
    static SimGame PaintressPortraitTable()
    {
        var g = Kit.Table(1, "gustave", "paintress");
        g.Bridge.Ctx.State.GlobalAct = 2;
        g.Seat(1).Vp = 12;
        Kit.Building(g, 1, 3, PieceTypes.Roost);
        Kit.Warriors(g, 1, 3, 2);
        foreach (var suit in new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse })
        {
            int c = g.ClearingIds.First(x => x != 1 && x != 3 && g.SuitOf(x) == suit);
            Kit.Building(g, 1, c, PieceTypes.Roost);
            Kit.Warriors(g, 1, c, 2);
        }
        return g;
    }

    [Fact]
    public void ThemedWin_BannerAtEveningEnd_WinAtNextBirdsong_NamesTheWin()
    {
        var g = PaintressPortraitTable();
        g.Bridge.OnEveningEnd(1);
        Assert.Equal("win.paintress", g.State.Mod.Banners[1]);
        Assert.Contains("COUNTDOWN! seat 2 (The Paintress) announces The Family Portrait", PublicLog(g));
        Assert.Contains("COUNTDOWN", g.Bridge.ModView(0));
        Assert.False(g.State.GameOver);

        var loaded = SimGame.FromJson(g.ToJson());         // the banner survives a reload
        Assert.Equal("win.paintress", loaded.State.Mod.Banners[1]);
        loaded.Bridge.OnBirdsongStart(1);
        Assert.True(loaded.State.GameOver);
        Assert.Equal(new List<int> { 1 }, loaded.State.Winners);
        Assert.Contains("The Family Portrait", loaded.State.GameOverText);
        Assert.Contains("themed win of The Paintress", loaded.State.GameOverText);
        Assert.True(loaded.Bridge.IsGameOver(out var text));
        Assert.Contains("The Family Portrait", text);
        Assert.Empty(loaded.Bridge.Errors);
    }

    [Fact]
    public void ThemedWin_IsLockedInActOne()
    {
        var g = PaintressPortraitTable();
        g.Bridge.Ctx.State.GlobalAct = 1;
        g.Bridge.OnEveningEnd(1);
        Assert.False(g.State.Mod.Banners.ContainsKey(1));
        Assert.Contains("locked until Act II", g.Bridge.ModView(0));
    }

    [Fact]
    public void ActClock_OpensActTwo_ThroughTheSim_WithStoryEventsAndBanner()
    {
        var g = Kit.Table(1, "gustave", "paintress");
        g.Seat(0).Vp = 10;
        g.Bridge.OnTurnEnd(0);
        Assert.Equal(2, g.Bridge.Ctx.Act);
        Assert.Contains(g.Bridge.Story, s => s.EventId == Ids.StoryEvents.Act2UnlockWins);
        Assert.Contains("=== Root 33 · Act II: The Monolith ===", PublicLog(g));
        Assert.Contains("Act II: The Monolith", g.Bridge.ModView(1));
        Assert.Empty(g.Bridge.Errors);
    }

    [Fact]
    public void EsquieSpawns_PlaceFlorriesGuards_OnTheSimBoard()
    {
        var g = NewGame(11, false, "gustave", "esquie");
        Bots.Run(g, roundCap: 0);
        Assert.True(g.State.SetupDone);
        var st = g.Bridge.Ctx.State;
        var florrie = st.EsquieRocks[0];
        Assert.Equal(RockStatus.Guarded, florrie.Status);
        int c = florrie.Clearing!.Value.Value;
        int host = florrie.HostSeat!.Value.Value;
        Assert.Equal(g.Bridge.Ctx.Role(Ids.MapRoles.StoneWaveCliffs)!.Value.Value, c);
        Assert.True(g.Warriors(host, c) >= florrie.GuardsLeft && florrie.GuardsLeft > 0);
        Assert.Contains(g.State.Mod.GuardFloors, x => x.Seat == host && x.Clearing == c && x.Floor == florrie.GuardsLeft);
        Assert.Contains(g.State.Mod.BlockedPlacements, x => x.Seat == host && x.Clearing == c);
        Assert.False(g.CanPlace(host, c));
        Assert.Contains("Florrie at", g.Bridge.ModView(1));
        // Esquie started in a forest touching his Nest
        var forests = g.Bridge.ForcedHome(1);
        Assert.NotNull(forests);
        if (g.Seat(1).PawnForest is int f) Assert.Contains(f, forests!);
        Assert.Empty(g.Bridge.Errors);
    }

    [Fact]
    public void SelectCards_ShowEachBaseCharacter_BySpoilerMode()
    {
        var safe = CoreBridge.SelectCards("safe");
        foreach (var id in ModText.BaseCharacters) Assert.Contains($"[{id}]", safe);
        Assert.Contains("plays the Marquise de Cat", safe);
        Assert.Contains("The Road to the Monolith", safe);
        Assert.DoesNotContain("Aline", safe);
        Assert.DoesNotContain("tune.", safe);
        var finished = CoreBridge.SelectCards("finished");
        Assert.Contains("Aline Dessendre", finished);
        Assert.DoesNotContain("Alicia", finished);            // revealed only in Act III
    }

    [Fact]
    public void CharacterCard_ListsTheRuleChangesInPlainWords()
    {
        var g = Kit.Table(1, "gustave", "paintress", "sciel", "esquie");
        var card = g.Bridge.CharacterCard(0);
        Assert.Contains("THEMED WIN · The Road to the Monolith", card);
        Assert.Contains("Overcharge", card);
        Assert.Contains("Expedition Camp in Lumière", card);
        var esquie = g.Bridge.CharacterCard(3);
        Assert.Contains("Friends Not Foes", esquie);
        foreach (var text in new[] { card, esquie, g.Bridge.CharacterCard(1), g.Bridge.CharacterCard(2) })
        {
            Assert.DoesNotContain("tune.", text);
            Assert.DoesNotContain("patch", text);
            Assert.DoesNotContain("UI", text);
        }
        Assert.DoesNotContain("Twilight:", g.Bridge.CharacterCard(2));   // a v1.1 rule, off in this release
    }

    [Fact]
    public void ModView_ShowsEveryCharactersPublicTracker()
    {
        var g = NewGame(2, false, "gustave", "paintress", "sciel", "esquie");
        Bots.Run(g, roundCap: 2);
        var view = g.Bridge.ModView(0);
        Assert.Contains("GOAL TRACKER", view);
        Assert.Contains("Arm ", view);
        Assert.Contains("Portraits ", view);
        Assert.Contains("Sanctuaries", view);
        Assert.Contains("rocks home", view);
        Assert.Empty(g.Bridge.Errors);
    }

    /// <summary>Every card stays on the table while the core draws extra cards: deck, discard, dominance, hands, play areas,
    /// the Eyrie's Decree and the Alliance's supporters (2 seats: the 4 dominance cards are out).</summary>
    [Theory]
    [InlineData(31, new[] { "gustave", "paintress", "sciel", "esquie" })]
    [InlineData(32, new[] { "sciel", "maelle", "gustave", "paintress" })]
    [InlineData(33, new[] { "gustave", "sciel" })]
    public void CoreRules_KeepEveryCardOnTheTable(int seed, string[] table)
    {
        var g = NewGame(seed, false, table);
        for (int r = 1; r <= 30 && !g.State.GameOver; r++)
        {
            Bots.Run(g, roundCap: r);
            int held = 0;
            for (int s = 0; s < g.SeatCount; s++)
                held += g.Seat(s).Faction switch
                {
                    "eyrie" => Eyrie.DecreeCards(g, s).Count(),
                    "alliance" => Alliance.SupporterCards(g, s).Count(),
                    _ => 0,
                };
            var st = g.State;
            Assert.Equal(54 - (table.Length == 2 ? 4 : 0),
                st.Deck.Count + st.Discard.Count + st.AvailableDominance.Count + st.Seats.Sum(x => x.Hand.Count + x.PlayArea.Count) + held);
        }
        Assert.Empty(g.Bridge.Errors);
    }

    /// <summary>The CLI reloads the save on every command: a game reloaded every few decisions plays on exactly like the
    /// one kept in memory (bark cooldowns, banners, the core's state and the board's own order included).</summary>
    [Fact]
    public void ReloadingEveryFewDecisions_PlaysTheSameGameAsKeepingItInMemory()
    {
        var kept = NewGame(41, false, "gustave", "paintress", "sciel", "esquie");
        var reloaded = SimGame.FromJson(kept.ToJson());
        for (int i = 0; i < 40 && !kept.State.GameOver; i++)
        {
            Bots.Run(kept, maxDecisions: 25);
            Bots.Run(reloaded, maxDecisions: 25);
            reloaded = SimGame.FromJson(reloaded.ToJson());
            Assert.Equal(kept.ToJson(), reloaded.ToJson());
        }
        Assert.Empty(kept.Bridge.Errors);
        Assert.Empty(reloaded.Bridge.Errors);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    public void AllBotGames_RunOnTheCore_WithoutCoreErrors_AndSurviveReloads(int seed)
    {
        var g = NewGame(seed, false, "gustave", "paintress", "sciel", "verso");
        for (int i = 0; i < 6 && !g.State.GameOver; i++)
        {
            Bots.Run(g, roundCap: (i + 1) * 3);
            Assert.Empty(g.Bridge.Errors);
            g = SimGame.FromJson(g.ToJson());
            g.Bridge.ModView(0);
            Assert.Empty(g.Bridge.Errors);
        }
        Assert.True(g.State.GameOver || g.State.Round >= 18);
    }
}
