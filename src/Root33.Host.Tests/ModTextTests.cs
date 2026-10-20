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
        Assert.Equal("The Family Portrait", ModText.WinTitle("win.paintress"));
        Assert.Equal("a themed win", ModText.WinTitle(null));
        Assert.Equal("win.unknown", ModText.WinTitle("win.unknown"));
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
        Assert.Equal("Act I: " + act1.Name, ModText.ActTitle(1));
        Assert.Equal("Act 4", ModText.ActTitle(4));   // only the three acts have numerals and names
        var line = ModText.ActLine(1);
        Assert.StartsWith("Themed wins are locked until Act II.", line);
        Assert.Contains($"{ModText.ActTitle(2)} begins at the end of a turn once anyone has {act1.EndsAtVp}+ VP, or after round {act1.EndsAfterRound}.", line);
        Assert.Equal("Lumière is the final battlefield.", ModText.ActLine(3));
        Assert.Equal($"=== Root 33 · {ModText.ActTitle(2)} === {ModText.ActLine(2)}", ModText.ActBannerLine(2));
        Assert.Equal($"Root 33 story ({ModText.ActTitle(1)}): {ModText.StoryText(Ids.StoryEvents.Act1Landing)}",
            ModText.StoryLine(1, Ids.StoryEvents.Act1Landing));
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
        var banner = ModText.BannerAnnounced(host, new SeatId(1), "win.paintress");
        Assert.StartsWith("COUNTDOWN! seat 2 (The Paintress) announces The Family Portrait. They win at the start of their next Birdsong if it still holds.", banner);
        Assert.Contains(" To stop it: ", banner);
        Assert.Equal("the countdown banner of seat 2 (The Paintress) (The Family Portrait) comes down.",
            ModText.BannerCleared(host, new SeatId(1), "win.paintress"));
        Assert.Equal("seat 4 (Esquie) gains a new power: no extra boot to enter a Hostile faction's clearing.",
            ModText.AbilityGained(host, new SeatId(3), "no-hostile-boot"));
        Assert.Equal("seat 4 (Esquie) wins with Esquie's Rocks Come Home, the Root 33 themed win of Esquie (with seat 3 (Sciel) in coalition)",
            ModText.WinLine(host, new[] { new SeatId(3), new SeatId(2) }, "win.esquie"));
        Assert.Equal("", ModText.WinLine(host, Array.Empty<SeatId>(), "win.esquie"));
    }
}
