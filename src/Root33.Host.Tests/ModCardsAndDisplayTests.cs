using System.Text.Json;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>Character cards (ModCards), the display state a host keeps (ModDisplay) and the start-up facts (SheetsInfo).</summary>
public class ModCardsAndDisplayTests
{
    [Fact]
    public void ModCards_SelectCards_ListThePickableCharacters_BySpoilerMode()
    {
        var safe = ModCards.SelectCards(SpoilerMode.Safe, Data.Characters.Keys);
        Assert.StartsWith("ROOT 33 · choose your characters (spoiler mode: safe).", safe);
        Assert.Contains("[gustave] Gustave · plays the Marquise de Cat", safe);
        Assert.Contains("[esquie] Esquie · plays the Vagabond (Tinker)", safe);
        Assert.DoesNotContain("[clea]", safe);                       // not selectable in safe mode
        Assert.DoesNotContain("tune.", safe);
        Assert.EndsWith("Every character can also win the Root way: 30 VP, or a dominance card." + Environment.NewLine, safe);
        var finished = ModCards.SelectCards(SpoilerMode.Finished, Data.Characters.Keys);
        Assert.Contains("[clea] Clea · plays the Lizard Cult", finished);
        Assert.Contains("[paintress] Aline Dessendre · plays the Eyrie Dynasties", finished);
        Assert.Contains("[maelle] Maelle · plays the Vagabond (Thief)", finished);   // revealed only in Act III
    }

    [Fact]
    public void ModCards_SelectCards_ShowTheWinTitlesInTheSpoilerModesWords()
    {
        var safe = ModCards.SelectCards(SpoilerMode.Safe, Data.Characters.Keys);
        Assert.Contains("  How they win: The Living Portraits (countdown win). ", safe);
        Assert.Contains("  How they win: A Place to Belong (countdown win). ", safe);
        Assert.DoesNotContain("The Family Portrait", safe);
        Assert.DoesNotContain("A Home in the Canvas", safe);
        var finished = ModCards.SelectCards(SpoilerMode.Finished, Data.Characters.Keys);
        Assert.Contains("  How they win: The Family Portrait (countdown win). ", finished);
        Assert.Contains("  How they win: A Home in the Canvas (countdown win). ", finished);
        Assert.DoesNotContain("The Living Portraits (countdown win)", finished);
    }

    [Fact]
    public void ModCards_CharacterCard_ShowsTheWinTitleInTheGamesSpoilerMode()
    {
        var safe = new HostTable(HostTable.FourSeats(), SpoilerMode.Safe);
        Assert.Contains("THEMED WIN · The Living Portraits: ", ModCards.CharacterCard(safe.Ctx, safe.Host, new SeatId(1)));
        var finished = new HostTable(HostTable.FourSeats(), SpoilerMode.Finished);
        Assert.Contains("THEMED WIN · The Family Portrait: ", ModCards.CharacterCard(finished.Ctx, finished.Host, new SeatId(1)));
    }

    [Fact]
    public void ModCards_EsquieAndScielCards_NameTheirTermsAndHowToFindTheRocks()
    {
        var t = new HostTable(HostTable.FourSeats());
        var sciel = ModCards.CharacterCard(t.Ctx, t.Host, new SeatId(2));
        Assert.Contains("Her Lanterns are her sympathy tokens and her Aquafarms are her Alliance bases.", sciel);
        Assert.Contains("A Sanctuary is a Lantern clearing with no enemy pieces at all", sciel);
        Assert.Contains("still have no enemy building or token", sciel);
        Assert.Contains("To stop her, place a building or token in one of those clearings, or remove her Aquafarms.", sciel);
        var esquie = ModCards.CharacterCard(t.Ctx, t.Host, new SeatId(3));
        Assert.Contains("Collect all four of Esquie's lost rocks and he wins at the end of any of his turns, no VP needed (not while in a coalition).", esquie);
        Assert.Contains("explore the Old Lumière ruin to find it (if the White-Haired Man is playing, not while his warriors stand there)", esquie);
        Assert.Contains("like every Vagabond battle, needs an undamaged sword", esquie);
        Assert.Contains("Collected rocks are never lost.", esquie);
        var winLine = esquie.Split('\n').Single(l => l.StartsWith("THEMED WIN", StringComparison.Ordinal));
        Assert.DoesNotContain(" home", winLine);                                   // the old text never said what 'home' meant
    }

    [Fact]
    public void ModGlossary_HasTheModsWords_InTheSpoilerModesWords()
    {
        var terms = new[]
        {
            "Act", "Themed win", "Countdown banner", "Goal Tracker", "Monolith Number", "Speech bubbles", "Expedition Flag",
            "Gustave's Arm and Overcharge", "Living Portrait", "Lantern", "Aquafarm", "Sanctuary", "Esquie's rocks", "Guards",
            "Perfection rank", "Maelle's relationships", "Maelle's stances",
        };
        foreach (var mode in new[] { SpoilerMode.Safe, SpoilerMode.Finished })
        {
            var entries = ModGlossary.Entries(mode);
            foreach (var term in terms) Assert.Contains(entries, e => e.Term == term);
            var text = ModGlossary.Text(mode);
            Assert.StartsWith($"ROOT 33 WORDS (the mod's own terms, spoiler mode: {ModCards.ModeName(mode)})", text);
            Assert.Equal(entries.Count + 1, text.Split('\n').Length);
            Assert.Contains("  Lantern: Sciel's sympathy token.", text);
            Assert.Contains("  Aquafarm: Sciel's Alliance base.", text);
            Assert.DoesNotContain("tune.", text);
        }
        var safe = ModGlossary.Text(SpoilerMode.Safe);
        foreach (var spoiler in new[] { "Aline", "The Canvas", "The Family Portrait", "A Home in the Canvas", "Renoir", "Alicia" })
            Assert.DoesNotContain(spoiler, safe);
        Assert.Contains("Act III: The Final Battlefield", safe);
        Assert.Contains("one of The Paintress's roosts", safe);
        var finished = ModGlossary.Text(SpoilerMode.Finished);
        Assert.Contains("Act III: The Canvas", finished);
        Assert.Contains("one of Aline Dessendre's roosts", finished);
        Assert.Contains("Verso (fox), Clea (rabbit), Renoir (mouse)", finished);
        Assert.Contains("The Family Portrait needs one", finished);
    }

    [Fact]
    public void ModCards_CharacterCard_ListsTheWinAndRuleChangesInPlainWords()
    {
        var t = new HostTable(HostTable.FourSeats());
        var card = ModCards.CharacterCard(t.Ctx, t.Host, new SeatId(0));
        Assert.StartsWith("ROOT 33 CHARACTER: Gustave, playing the Marquise de Cat.", card);
        Assert.Contains("THEMED WIN · The Road to the Monolith: ", card);
        Assert.Contains("  How the table stops it: ", card);
        Assert.Contains("ROOT 33 RULE CHANGES:" + Environment.NewLine + "  - ", card);
        Assert.Contains("Overcharge", card);
        foreach (var s in t.Board.Seats())
        {
            var text = ModCards.CharacterCard(t.Ctx, t.Host, s);
            Assert.DoesNotContain("tune.", text);
            Assert.DoesNotContain("patch", text);
        }
        foreach (var v11 in Data.RuleTweaks.Values.Where(r => r.Version == "v1.1" && r.Character == "sciel"))
            Assert.DoesNotContain(v11.Name + ":", ModCards.CharacterCard(t.Ctx, t.Host, new SeatId(2)));
    }

    [Fact]
    public void ModCards_CharacterCard_IsEmptyForAPlainRootSeat()
    {
        var board = FakeBoardWithPlainSeat();
        var t = new HostTable(board);
        Assert.Equal("", ModCards.CharacterCard(t.Ctx, t.Host, new SeatId(1)));
    }

    static Root33.Core.Tests.Support.FakeBoard FakeBoardWithPlainSeat() =>
        Root33.Core.Tests.Support.FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie);

    [Fact]
    public void ModDisplay_KeepsBannersTitlesAndOverlays()
    {
        var d = new ModDisplay();
        Assert.True(d.ShowBanner(new SeatId(2), "win.sciel"));
        Assert.False(d.ShowBanner(new SeatId(2), "win.sciel"));
        Assert.True(d.ShowBanner(new SeatId(0), "win.gustave"));
        Assert.Equal(new[] { 0, 2 }, d.Banners().Select(kv => kv.Key.Value));
        Assert.Equal("win.sciel", d.ClearBanner(new SeatId(2)));
        Assert.Null(d.ClearBanner(new SeatId(2)));

        Assert.Null(d.TitleOf(new SeatId(0)));
        Assert.True(d.SetSeatTitle(new SeatId(0), "Expedition 33"));
        Assert.False(d.SetSeatTitle(new SeatId(0), "Expedition 33"));
        Assert.Equal("Expedition 33", d.TitleOf(new SeatId(0)));

        d.ShowOverlay("ui.esquie.rocks", new SeatId(1), new ClearingId(6), null);
        d.ShowOverlay("ui.esquie.rocks", new SeatId(1), new ClearingId(6), null);
        d.ShowOverlay("ui.global.title", null, null, null);
        d.ShowOverlay("ui.lune.journal", null, null, 3);
        Assert.Equal(new[] { "ui.esquie.rocks@1/6/", "ui.global.title@//", "ui.lune.journal@//f3" }, d.Overlays);
        Assert.True(d.OverlayShown("ui.global.title"));
        d.HideOverlay("ui.global.title", null, null, null);
        Assert.False(d.OverlayShown("ui.global.title"));
        d.Clear();
        Assert.Empty(d.Banners());
        Assert.Empty(d.Overlays);
        Assert.Null(d.TitleOf(new SeatId(0)));
    }

    [Fact]
    public void SheetsInfo_CountsEveryRowOfEverySheet()
    {
        var root = RepoRoot();
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sheets", "_schema.json")));
        int sheets = 0, rows = 0;
        foreach (var sheet in schema.RootElement.GetProperty("sheets").EnumerateObject())
        {
            sheets++;
            var file = sheet.Value.GetProperty("file").GetString()!;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sheets", file)));
            rows += doc.RootElement.GetProperty("rows").GetArrayLength();
        }
        Assert.Equal(sheets, SheetsInfo.SheetCount);
        Assert.Equal(rows, SheetsInfo.RowCount);
        Assert.Matches(@"^\d+\.\d+\.\d+", SheetsInfo.CoreVersion);
    }

    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "sheets", "_schema.json"))) return dir.FullName;
        throw new DirectoryNotFoundException("sheets/_schema.json not found above " + AppContext.BaseDirectory);
    }
}
