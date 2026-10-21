using Root33.Core;
using Root33.Core.Sheets;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>The mod's names and texts (Root33.Host.ModText): read from the sheets, the same in every host.</summary>
public class ModTextTests
{
    [Fact]
    public void ModText_CharacterNames_FollowTheSpoilerMode()
    {
        Assert.Equal(Data.Characters["paintress"].SafeName, ModText.CharacterName("paintress", SpoilerMode.Safe));
        Assert.Equal(Data.Characters["paintress"].FinishedName, ModText.CharacterName("paintress", SpoilerMode.Finished));
        Assert.Equal("The Paintress", ModText.CharacterName("paintress", SpoilerMode.Safe));
        // Maelle's and the Curator's finished names stay hidden until a story event reveals them
        Assert.Equal(Data.Characters["maelle"].SafeName, ModText.CharacterName("maelle", SpoilerMode.Finished));
        Assert.Equal(Data.Characters["curator"].SafeName, ModText.CharacterName("curator", SpoilerMode.Finished));
        Assert.Equal("nobody", ModText.CharacterName("nobody", SpoilerMode.Safe));
        Assert.Equal("", ModText.CharacterName(null, SpoilerMode.Safe));
    }

    [Fact]
    public void ModText_SeatTitle_PrefersTheTitleRootThirtyThreeGave()
    {
        Assert.Equal("Expedition 33", ModText.SeatTitle("Expedition 33", "gustave", SpoilerMode.Finished));
        Assert.Equal("Gustave", ModText.SeatTitle(null, "gustave", SpoilerMode.Safe));
        Assert.Equal("Gustave", ModText.SeatTitle("", "gustave", SpoilerMode.Safe));
        Assert.Equal("seat 2 (The Paintress)", ModText.SeatLabel(new SeatId(1), "The Paintress"));

        var board = HostTable.FourSeats();
        var host = new FakeHost(board);
        Assert.Equal("Sciel", ModText.SeatTitle(host, new SeatId(2)));
        board.SetSeatTitle(new SeatId(0), "Expedition 33");
        Assert.Equal("Expedition 33", ModText.SeatTitle(host, new SeatId(0)));
        Assert.Equal("seat 1 (Expedition 33)", host.Who(new SeatId(0)));
    }

    [Fact]
    public void ModText_EngineAndClearingNames_ComeFromTheSheets()
    {
        Assert.Equal("Marquise de Cat", ModText.EngineName("marquise"));
        Assert.Equal("Vagabond (Tinker)", ModText.CharacterEngineName("esquie"));
        Assert.Equal("Woodland Alliance", ModText.CharacterEngineName("sciel"));
        Assert.Equal("Eyrie Dynasties", ModText.EngineName(EngineId.Eyrie));
        Assert.Equal("Vagabond (Thief)", ModText.EngineName(EngineId.VagabondThief));
        Assert.Equal("Keepers in Iron", ModText.EngineName(EngineId.Keepers));
        Assert.Equal("Vagabond", ModText.EngineName(EngineId.VagabondOther));
        var row = Data.MapAutumn["autumn.03"];
        Assert.Equal($"{row.ContinentName} [c3, {row.Suit}]", ModText.ClearingName(new ClearingId(3)));
        Assert.Equal("The Monolith [c3, mouse]", ModText.ClearingName(new ClearingId(3)));
        Assert.Equal("c99", ModText.ClearingName(new ClearingId(99)));
    }

    [Fact]
    public void ModText_WinsBarksAndAbilities_HaveFallbacks()
    {
        Assert.Equal("The Family Portrait", ModText.WinTitle("win.paintress", SpoilerMode.Finished));
        Assert.Equal("a themed win", ModText.WinTitle(null, SpoilerMode.Safe));
        Assert.Equal("a themed win", ModText.WinTitle(null, SpoilerMode.Finished));
        Assert.Equal("win.unknown", ModText.WinTitle("win.unknown", SpoilerMode.Safe));
        var bark = Data.Barks.Values.First();
        Assert.Equal(bark.Text, ModText.BarkText(bark.Id));
        Assert.Equal("bark.none", ModText.BarkText("bark.none"));
        Assert.Equal($"seat 1 (Gustave) says: \"{bark.Text}\"", ModText.BubbleLine("seat 1 (Gustave)", bark.Id));
        Assert.Equal("rivers count as paths for its moves", ModText.AbilityText("rivers-as-paths"));
        Assert.Equal("teleport", ModText.AbilityText("teleport"));
    }

    [Fact]
    public void ModText_ActTitlesAndLines_ComeFromTheActsSheet()
    {
        var act1 = Data.Acts["act1"];
        foreach (var mode in new[] { SpoilerMode.Safe, SpoilerMode.Finished })
        {
            Assert.Equal("Act I: " + act1.Name, ModText.ActTitle(1, mode));
            Assert.Equal("Act 4", ModText.ActTitle(4, mode));   // only the three acts have numerals and names
            var line = ModText.ActLine(1, mode);
            Assert.StartsWith("Themed wins are locked until Act II.", line);
            Assert.Contains($"{ModText.ActTitle(2, mode)} begins at the end of a turn once anyone has {act1.EndsAtVp}+ VP, or after round {act1.EndsAfterRound}.", line);
            Assert.Equal("Lumière is the final battlefield.", ModText.ActLine(3, mode));
            Assert.Equal($"=== Root 33 · {ModText.ActTitle(2, mode)} === {ModText.ActLine(2, mode)}", ModText.ActBannerLine(2, mode));
            Assert.Equal($"Root 33 story ({ModText.ActTitle(1, mode)}): {ModText.StoryText(Ids.StoryEvents.Act1Landing, mode)}",
                ModText.StoryLine(1, Ids.StoryEvents.Act1Landing, mode));
        }
        Assert.Equal("(Root 33 note: check this)", ModText.NoteLine("check this"));
        Assert.Equal("(Root 33 core error in hook.turn-end: boom; the game goes on without it.)", ModText.CoreErrorLine("hook.turn-end", "boom"));
    }

    [Fact]
    public void ModText_Plain_LeavesOutBuilderNotesAndFillsTuningValues()
    {
        var text = "Gustave gains a charge (UI shows a gauge). Up to tune.gustave-arm-max charges. Root's battle gets a patch: the " +
                   "hits go up. If Root cannot do it, the fallback queues it. On other maps it is free. Lumière is autumn.01.";
        Assert.Equal($"Gustave gains a charge. Up to {Tuning.GustaveArmMax} charges. Root's battle gets a patch. Lumière is clearing 1.", ModText.Plain(text));
        Assert.Equal("", ModText.Plain(null));
        Assert.Equal("", ModText.Plain("   "));
        foreach (var w in Data.WinConditions.Values)
            foreach (var t in new[] { ModText.Plain(w.PlayerText), ModText.Plain(w.Counterplay) })
            {
                Assert.DoesNotContain("tune.", t);
                Assert.DoesNotContain("autumn.", t);
            }
    }

    [Fact]
    public void ModText_BannerAndWinLines_NameTheSeatTheHostsWay()
    {
        var board = HostTable.FourSeats().Coalition(3, 2);
        var host = new FakeHost(board);
        var banner = ModText.BannerAnnounced(host, new SeatId(1), "win.paintress");   // FakeHost: safe mode
        Assert.StartsWith("COUNTDOWN! seat 2 (The Paintress) announces The Living Portraits. They win at the start of their next Birdsong if it still holds.", banner);
        Assert.Contains(" To stop it: ", banner);
        Assert.DoesNotContain("The Family Portrait", banner);
        Assert.Equal("the countdown banner of seat 2 (The Paintress) (The Living Portraits) comes down.",
            ModText.BannerCleared(host, new SeatId(1), "win.paintress"));
        Assert.Equal("seat 4 (Esquie) gains a new power: no extra boot to enter a Hostile faction's clearing.",
            ModText.AbilityGained(host, new SeatId(3), "no-hostile-boot"));
        Assert.Equal("seat 4 (Esquie) wins with Esquie's Rocks Come Home, the Root 33 themed win of Esquie (with seat 3 (Sciel) in coalition)",
            ModText.WinLine(host, new[] { new SeatId(3), new SeatId(2) }, "win.esquie"));
        Assert.Equal("", ModText.WinLine(host, Array.Empty<SeatId>(), "win.esquie"));
    }

    [Fact]
    public void ModText_Titles_FollowTheSpoilerMode()
    {
        // Safe mode never shows a title that spoils Clair Obscur's story (Ren and Kit, playtest 1)
        Assert.Equal("The Living Portraits", ModText.WinTitle("win.paintress", SpoilerMode.Safe));
        Assert.Equal("The Family Portrait", ModText.WinTitle("win.paintress", SpoilerMode.Finished));
        Assert.Equal("A Place to Belong", ModText.WinTitle("win.maelle", SpoilerMode.Safe));
        Assert.Equal("A Home in the Canvas", ModText.WinTitle("win.maelle", SpoilerMode.Finished));
        Assert.Equal("Act III: The Final Battlefield", ModText.ActTitle(3, SpoilerMode.Safe));
        Assert.Equal("Act III: The Canvas", ModText.ActTitle(3, SpoilerMode.Finished));
        // a row without a safe title or name reads the same in both modes
        Assert.Equal(Data.WinConditions["win.gustave"].Title, ModText.WinTitle("win.gustave", SpoilerMode.Safe));
        Assert.Equal(ModText.ActTitle(2, SpoilerMode.Finished), ModText.ActTitle(2, SpoilerMode.Safe));
        // the next act's name inside the act banner line follows the mode too
        Assert.Contains("Act III: The Final Battlefield begins", ModText.ActLine(2, SpoilerMode.Safe));
        Assert.Contains("Act III: The Canvas begins", ModText.ActLine(2, SpoilerMode.Finished));
        Assert.StartsWith("=== Root 33 · Act III: The Final Battlefield ===", ModText.ActBannerLine(3, SpoilerMode.Safe));
        Assert.StartsWith("Root 33 story (Act III: The Final Battlefield): ", ModText.StoryLine(3, Ids.StoryEvents.Act3Fall, SpoilerMode.Safe));
        foreach (var w in Data.WinConditions.Values)
            Assert.DoesNotContain("Canvas", ModText.WinTitle(w.Id, SpoilerMode.Safe));
    }

    [Fact]
    public void ModText_StoryText_IsThePlayerText_NeverTheDesignNotes()
    {
        foreach (var e in Data.StoryEvents.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.PlayerText), e.Id);
            var safe = ModText.StoryText(e.Id, SpoilerMode.Safe);
            var finished = ModText.StoryText(e.Id, SpoilerMode.Finished);
            Assert.Equal(ModText.Plain(e.PlayerText), safe);
            Assert.Equal(ModText.Plain(string.IsNullOrEmpty(e.PlayerTextFinished) ? e.PlayerText : e.PlayerTextFinished), finished);
            foreach (var shown in new[] { safe, finished })
            {
                var plain = ModText.Plain(e.What);
                if (plain.Length > 0) Assert.DoesNotContain(plain, shown);
                Assert.NotEqual(e.What, shown);
                Assert.DoesNotContain("fallback", shown);
                Assert.DoesNotContain("overlay", shown);
                Assert.DoesNotContain("tune.", shown);
            }
        }
        Assert.Equal("The expedition regroups on the shore.", ModText.StoryText(Ids.StoryEvents.Act2Beach, SpoilerMode.Safe));
        Assert.Equal("The Beach: 'This time, the arm held.'", ModText.StoryText(Ids.StoryEvents.Act2Beach, SpoilerMode.Finished));
        Assert.Equal("Maelle: a memory returns.", ModText.StoryText(Ids.StoryEvents.Act3Alicia, SpoilerMode.Safe));
        Assert.Equal("Maelle remembers who she really is: Alicia.", ModText.StoryText(Ids.StoryEvents.Act3Alicia, SpoilerMode.Finished));
        Assert.Equal("Act III begins. Lumière is the final battlefield.", ModText.StoryText(Ids.StoryEvents.Act3Fall, SpoilerMode.Safe));
        Assert.Equal("After the fall at the Monolith, Lumière is the final battlefield.", ModText.StoryText(Ids.StoryEvents.Act3Fall, SpoilerMode.Finished));
        Assert.DoesNotContain("Alicia", ModText.StoryText(Ids.StoryEvents.Act3Alicia, SpoilerMode.Safe));
        // an event without a finished line reads the same in both modes
        Assert.Equal(ModText.StoryText(Ids.StoryEvents.Act1Landing, SpoilerMode.Safe), ModText.StoryText(Ids.StoryEvents.Act1Landing, SpoilerMode.Finished));
        Assert.StartsWith("Expedition 33 comes ashore on the Continent", ModText.StoryText(Ids.StoryEvents.Act1Landing, SpoilerMode.Safe));
        Assert.Equal("story.none", ModText.StoryText("story.none", SpoilerMode.Safe));
    }

    [Fact]
    public void ModText_ActOneLine_SaysTheTurnThatOpensActTwoCannotWarn()
    {
        var act1 = Data.Acts["act1"];
        var line = ModText.ActLine(1, SpoilerMode.Safe);
        Assert.Contains($"a player who reaches {act1.EndsAtVp} VP opens Act II at the end of that turn, " +
                        "and their countdown banner can go up at the end of their next turn at the earliest.", line);
        Assert.DoesNotContain("at the earliest", ModText.ActLine(2, SpoilerMode.Safe));

        // ...and that is what the game does: the Paintress meets her whole test with 10 VP in Act I
        var board = HostTable.FourSeats().Vp(1, act1.EndsAtVp);
        var t = new HostTable(board);
        var monolith = t.Ctx.Role(Ids.MapRoles.Monolith)!.Value;
        board.Building(1, monolith.Value, PieceTypes.Roost).Warriors(1, monolith.Value, 2);
        foreach (var suit in new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse })
        {
            var c = board.Clearings().First(x => x != monolith && board.SuitsOf(x)[0] == suit
                                                 && board.Seats().All(s => board.Warriors(s, x) + board.Buildings(s, x) + board.Tokens(s, x) == 0));
            board.Building(1, c.Value, PieceTypes.Roost).Warriors(1, c.Value, 3);
        }
        Assert.Equal(1, t.Ctx.Act);
        t.Table.PlayTurn(1);                       // her turn ends: win checks first (Act I: locked), then Act II opens
        Assert.Equal(2, t.Ctx.Act);
        Assert.False(board.Banners.ContainsKey(new SeatId(1)));
        t.Table.PlayTurn(1);                       // her next turn: the banner goes up
        Assert.Equal("win.paintress", board.Banners[new SeatId(1)]);
    }
}
