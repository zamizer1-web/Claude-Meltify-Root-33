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
