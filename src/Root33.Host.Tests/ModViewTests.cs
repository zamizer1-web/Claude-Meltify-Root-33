using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>The mod's panel (Root33.Host.ModView) over a real game on the Autumn board, with a fake host.</summary>
public class ModViewTests
{
    static readonly SeatId S0 = new(0), S1 = new(1), S2 = new(2), S3 = new(3);

    [Fact]
    public void ModView_PlainRootGame_ShowsNothing()
    {
        var board = HostTable.FourSeats();
        var game = new Root33Game(board, board, ModuleRegistry.Default());
        game.OnGameStart(board.Seats(), Difficulty.Normal, SpoilerMode.Safe);   // no character select: plain Root
        var view = new ModView(game.Context, new FakeHost(board), new ModFeed());
        Assert.False(game.Context.E33Mode);
        Assert.Equal("", view.Text(S0));
        Assert.Equal("", view.Text(null));
    }

    [Fact]
    public void ModView_Header_ShowsTheActAndTheMonolithNumber()
    {
        var t = new HostTable(HostTable.FourSeats().Vp(0, 7).Vp(2, 4));
        var p = t.View.Panel(S0);
        Assert.Null(p.Failure);
        Assert.Equal($"ROOT 33 · {ModText.ActTitle(1, SpoilerMode.Safe)} · Monolith Number {Tuning.MonolithNumberStart - 7}: 33 minus the top score. " +
                     "It falls as the leader scores and reaches 3 when someone has 30 VP.", p.Header);
        Assert.Equal("ROOT 33 · Act I: The Landing · Monolith Number 26: 33 minus the top score. It falls as the leader scores and reaches 3 when someone has 30 VP.", p.Header);
        Assert.Equal(ModText.ActLine(1, SpoilerMode.Safe), p.ActLine);
        var text = t.View.Text(S0);
        Assert.StartsWith(p.Header + Environment.NewLine + "  " + p.ActLine, text);
    }

    [Fact]
    public void ModView_GoalTracker_HasEveryCharactersGoalAndTracker()
    {
        var t = new HostTable(HostTable.FourSeats());
        var goals = t.View.Panel(S2).Goals!;
        Assert.Equal(new[] { S0, S1, S2, S3 }, goals.Select(g => g.Seat));
        Assert.Equal("seat 2 (The Paintress)", goals[1].Who);
        Assert.All(goals, g => Assert.Contains("locked until Act II", g.Goal));
        Assert.StartsWith("The Road to the Monolith (countdown): locked until Act II, needs 10+ VP", goals[0].Goal);
        Assert.StartsWith("Esquie's Rocks Come Home (quest): locked until Act II", goals[3].Goal);
        Assert.StartsWith($"Arm 0/{Tuning.GustaveArmMax} · flags: ", goals[0].Tracker);
        Assert.StartsWith("Portraits ", goals[1].Tracker);
        Assert.StartsWith("Sun: ", goals[2].Tracker);
        Assert.Contains("Sanctuaries", goals[2].Tracker);
        Assert.StartsWith("rocks home 0/4 · Florrie ", goals[3].Tracker);
        var text = t.View.Text(S2);
        Assert.Contains("GOAL TRACKER (public)" + Environment.NewLine + "  seat 1 (Gustave) · The Road to the Monolith", text);
        Assert.Contains(Environment.NewLine + "      Arm 0/", text);
    }

    [Fact]
    public void ModView_Gates_ShowDominanceAndCoalition()
    {
        var board = FakeBoard.Autumn()
            .Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress")
            .Seat(2, EngineId.VagabondRanger, "verso").Seat(3, EngineId.VagabondThief, "maelle")
            .Dominance(0, Suit.Fox).Coalition(2, 1);
        var t = new HostTable(board);
        var goals = t.View.Panel(S0).Goals!;
        Assert.Contains("off while a dominance card is active", goals[0].Goal);
        Assert.Contains("off while in a coalition", goals[2].Goal);
        Assert.DoesNotContain("coalition", goals[3].Goal);
        Assert.StartsWith("Perfection rank D · pawn in the Monolith: no", goals[2].Tracker);
        Assert.StartsWith("family: seat 1 (Gustave) ", goals[3].Tracker);
    }

    [Fact]
    public void ModView_CountdownBanner_ShowsUnderTheActLineAndOnTheGoalRow()
    {
        var t = new HostTable(HostTable.FourSeats());
        t.Board.ShowBanner(S1, "win.paintress");
        var p = t.View.Panel(S0);
        Assert.Equal(new[] { ModText.BannerAnnounced(t.Host, S1, "win.paintress") }, p.Banners);
        Assert.EndsWith("COUNTDOWN ANNOUNCED", p.Goals![1].Goal);
        Assert.DoesNotContain("COUNTDOWN ANNOUNCED", p.Goals[0].Goal);
        Assert.Contains("  COUNTDOWN! seat 2 (The Paintress) announces The Living Portraits", t.View.Text(S0));   // safe mode
    }

    [Fact]
    public void ModView_SpeechBubbles_ShowSinceTheViewersLastTurn()
    {
        var t = new HostTable(HostTable.FourSeats());
        var line = Data.Barks.Values.First(b => b.Character == "paintress");
        t.Feed.AddBubble(S1, line.Id, 2);
        var shown = $"  r2 seat 2 (The Paintress): \"{line.Text}\"";
        Assert.Contains("SPEECH BUBBLES SINCE YOUR LAST TURN" + Environment.NewLine + shown, t.View.Text(S0));
        t.Feed.TurnEnded(S0);
        Assert.DoesNotContain("SPEECH BUBBLES", t.View.Text(S0));
        Assert.Contains(shown, t.View.Text(S1));
        Assert.Contains("LATEST SPEECH BUBBLES" + Environment.NewLine + shown, t.View.Text(null));
    }

    [Fact]
    public void ModView_Spectator_SeesTheLatestTwelveBubbles()
    {
        var t = new HostTable(HostTable.FourSeats());
        var line = Data.Barks.Values.First(b => b.Character == "gustave").Id;
        for (var i = 1; i <= 15; i++) t.Feed.AddBubble(S0, line, i);
        var p = t.View.Panel(null);
        Assert.Equal(ModView.SpectatorBubbles, p.Bubbles!.Count);
        Assert.StartsWith("r4 ", p.Bubbles[0]);
        Assert.StartsWith("r15 ", p.Bubbles[^1]);
        Assert.Equal(15, t.View.Panel(S1).Bubbles!.Count);
    }

    [Fact]
    public void ModView_StorySoFar_ListsTheStoryEventsTheFeedCaughtUp()
    {
        var t = new HostTable(HostTable.FourSeats());
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, _ => { });
        Assert.Contains(t.Feed.Story, s => s.EventId == Ids.StoryEvents.Act1Landing);
        var text = t.View.Text(S0);
        Assert.Contains("STORY SO FAR" + Environment.NewLine + $"  {ModText.ActTitle(1, SpoilerMode.Safe)}: {ModText.StoryText(Ids.StoryEvents.Act1Landing, SpoilerMode.Safe)}", text);
        Assert.Contains("  Act I: The Landing: Expedition 33 comes ashore on the Continent, battered but alive.", text);
    }

    [Fact]
    public void ModView_FinishedMode_LabelsThePaintressPortraits()
    {
        var board = HostTable.FourSeats();
        var t = new HostTable(board, SpoilerMode.Finished);
        var monolith = t.Ctx.Role(Ids.MapRoles.Monolith)!.Value;
        board.Building(1, monolith.Value, PieceTypes.Roost).Warriors(1, monolith.Value, 2);
        var fox = board.Clearings().First(c => c != monolith && board.SuitsOf(c)[0] == Suit.Fox
                                               && board.Seats().All(s => board.Warriors(s, c) + board.Buildings(s, c) == 0));
        board.Building(1, fox.Value, PieceTypes.Roost).Warriors(1, fox.Value, 3);
        var tracker = t.View.Panel(S0).Goals![1].Tracker;
        Assert.Matches(@"^Portraits 1/3: \w+ at ", tracker);
        Assert.Contains(" at " + ModText.ClearingName(fox), tracker);
        Assert.Contains("holds the Monolith with an easel: yes", tracker);
    }

    [Fact]
    public void ModView_ReadFailure_KeepsWhatWasShownAndNamesTheError()
    {
        var t = new HostTable(HostTable.FourSeats());
        t.Host.FailWho = true;
        var p = t.View.Panel(S0);
        Assert.IsType<InvalidOperationException>(p.Failure);
        Assert.NotNull(p.Header);
        Assert.Empty(p.Goals!);
        Assert.Null(p.Bubbles);
        Assert.EndsWith("GOAL TRACKER (public)" + Environment.NewLine + "(Root 33 view unavailable: read.seats is not mapped yet)", t.View.Text(S0));
    }

    /// <summary>Plays every seat once (Act I -> II -> III by VP) and catches the feed up, so all three acts' events fired.</summary>
    static (HostTable Table, List<string> Lines) ToActThree(SpoilerMode mode)
    {
        var t = new HostTable(FakeBoard.Autumn()
            .Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress")
            .Seat(2, EngineId.VagabondTinker, "esquie").Seat(3, EngineId.VagabondThief, "maelle")
            .Vp(0, Tuning.Act2EndVp), mode);
        var lines = new List<string>();
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        t.Table.PlayTurn(0);
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        t.Table.PlayTurn(1);
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Equal(3, t.Ctx.Act);
        return (t, lines);
    }

    [Theory]
    [InlineData(SpoilerMode.Safe)]
    [InlineData(SpoilerMode.Finished)]
    public void ModView_StorySoFar_ShowsPlayerText_NeverTheDesignNotes(SpoilerMode mode)
    {
        var (t, lines) = ToActThree(mode);
        var p = t.View.Panel(S0);
        Assert.Equal(ModView.StoryShown, p.Story!.Count);
        var shown = t.Feed.LatestStory(ModView.StoryShown).ToList();
        for (var i = 0; i < shown.Count; i++)
            Assert.Equal($"{ModText.ActTitle(shown[i].Act, mode)}: {ModText.StoryText(shown[i].EventId, mode)}", p.Story[i]);
        var text = t.View.Text(S0) + "\n" + string.Join("\n", lines);
        foreach (var e in Data.StoryEvents.Values)
        {
            Assert.DoesNotContain(e.What, text);
            var plain = ModText.Plain(e.What);
            if (plain.Length > 0) Assert.DoesNotContain(plain, text);
        }
        foreach (var r in t.Feed.Story)
            Assert.Contains(ModText.StoryLine(r.Act, r.EventId, mode), lines);
        if (mode == SpoilerMode.Safe)
        {
            Assert.Contains("  Act III: The Final Battlefield: Act III begins. Lumière is the final battlefield.", text);
            Assert.Contains("Maelle: a memory returns.", text);
            Assert.DoesNotContain("Alicia", text);
            Assert.DoesNotContain("The Canvas", text);
            Assert.DoesNotContain(Data.StoryEvents[Ids.StoryEvents.Act2Beach].PlayerTextFinished, text);   // a finished-only event
        }
        else
        {
            Assert.Contains("  Act III: The Canvas: After the fall at the Monolith, Lumière is the final battlefield.", text);
            Assert.Contains("Maelle remembers who she really is: Alicia.", text);
            Assert.Contains("Root 33 story (Act II: The Monolith): The Beach: 'This time, the arm held.'", lines);
        }
    }

    [Fact]
    public void ModView_TitlesInTheHeaderGoalsAndBanners_FollowTheSpoilerMode()
    {
        foreach (var mode in new[] { SpoilerMode.Safe, SpoilerMode.Finished })
        {
            var (t, _) = ToActThree(mode);
            t.Board.ShowBanner(S1, "win.paintress");
            var p = t.View.Panel(S0);
            var portrait = mode == SpoilerMode.Safe ? "The Living Portraits" : "The Family Portrait";
            var home = mode == SpoilerMode.Safe ? "A Place to Belong" : "A Home in the Canvas";
            Assert.StartsWith($"ROOT 33 · Act III: {(mode == SpoilerMode.Safe ? "The Final Battlefield" : "The Canvas")} · ", p.Header);
            Assert.StartsWith(portrait + " (countdown): ", p.Goals![1].Goal);
            Assert.StartsWith(home + " (countdown): ", p.Goals[3].Goal);
            Assert.StartsWith($"COUNTDOWN! seat 2 ({ModText.SeatTitle(t.Host, S1)}) announces {portrait}.", p.Banners![0]);
            if (mode == SpoilerMode.Safe)
                foreach (var spoiler in new[] { "The Family Portrait", "A Home in the Canvas", "The Canvas", "Aline" })
                    Assert.DoesNotContain(spoiler, t.View.Text(S0));
        }
    }
}
