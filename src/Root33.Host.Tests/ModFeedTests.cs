using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>The mod's feed (Root33.Host.ModFeed): the bubble queue with each seat's marks, and the story, act banners and
/// notes it picks up from the core after each hook.</summary>
public class ModFeedTests
{
    static readonly SeatId S0 = new(0), S1 = new(1);

    [Fact]
    public void ModFeed_KeepsTheLast120Bubbles_NumberedInOrder()
    {
        var feed = new ModFeed();
        for (var i = 0; i < 130; i++) feed.AddBubble(new SeatId(i % 2), "bark.x", 1 + i / 10);
        Assert.Equal(130, feed.BubbleCount);
        Assert.Equal(ModFeed.MaxBubbles, feed.Bubbles.Count);
        Assert.Equal(11, feed.Bubbles[0].N);
        Assert.Equal(130, feed.Bubbles[^1].N);
        Assert.Equal(1, feed.Bubbles[^1].Seat);
        Assert.Equal(13, feed.Bubbles[^1].Round);
    }

    [Fact]
    public void ModFeed_SinceLastTurn_StartsAfterTheSeatsOwnTurnEnd()
    {
        var feed = new ModFeed();
        feed.AddBubble(S0, "bark.a", 1);
        feed.AddBubble(S1, "bark.b", 1);
        Assert.Equal(2, feed.SinceLastTurn(S0).Count);
        feed.TurnEnded(S0);
        Assert.Empty(feed.SinceLastTurn(S0));
        Assert.Equal(2, feed.SinceLastTurn(S1).Count);
        feed.AddBubble(S1, "bark.c", 2);
        Assert.Equal(new[] { "bark.c" }, feed.SinceLastTurn(S0).Select(b => b.BarkId));
        Assert.Equal(new[] { "bark.b", "bark.c" }, feed.Latest(2).Select(b => b.BarkId));
    }

    [Fact]
    public void ModFeed_CatchUp_LogsTheActBannerOnce_ThenTheStory_ThenNotes()
    {
        var t = new HostTable(HostTable.FourSeats());
        var lines = new List<string>();
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Equal(ModText.ActBannerLine(1), lines[0]);
        Assert.Equal(1, lines.Count(l => l.StartsWith("=== Root 33 · ")));
        Assert.Contains(ModText.StoryLine(1, Ids.StoryEvents.Act1Landing), lines);
        Assert.Equal(t.Ctx.StoryLog.Count, t.Feed.Story.Count);
        Assert.Equal(1, t.Feed.LastAct);

        lines.Clear();
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Empty(lines);
        t.Ctx.Warnings.Add("a loaded seat changed engine");
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Equal(new[] { "(Root 33 note: a loaded seat changed engine)" }, lines);
    }

    [Fact]
    public void ModFeed_CatchUp_ShowsTheNextActBannerBeforeItsStory()
    {
        var t = new HostTable(HostTable.FourSeats());
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, _ => { });
        t.Board.Vp(0, Tuning.Act1EndVp);
        t.Table.PlayTurn(0);
        Assert.Equal(2, t.Ctx.Act);
        var lines = new List<string>();
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Equal(ModText.ActBannerLine(2), lines[0]);
        Assert.Contains(ModText.StoryLine(2, Ids.StoryEvents.Act2UnlockWins), lines);
        Assert.Equal(2, t.Feed.LastAct);
        Assert.Contains(t.Feed.Story, s => s.Act == 2);
    }

    [Fact]
    public void ModFeed_CatchUp_BeforeSetupEnds_HasNoActBanner()
    {
        var t = new HostTable(HostTable.FourSeats());
        var lines = new List<string>();
        t.Feed.CatchUp(t.Ctx, 0, setupDone: false, lines.Add);
        Assert.DoesNotContain(lines, l => l.StartsWith("=== Root 33"));
        Assert.Contains(ModText.StoryLine(1, Ids.StoryEvents.Act1Landing), lines);
        Assert.All(t.Feed.Story, s => Assert.Equal(0, s.Round));
    }

    [Fact]
    public void ModFeed_SkipSeen_AfterALoad_LogsNothingOld()
    {
        var t = new HostTable(HostTable.FourSeats());
        var lines = new List<string>();
        t.Feed.SkipSeen(t.Ctx);
        t.Feed.CatchUp(t.Ctx, 1, setupDone: true, lines.Add);
        Assert.Equal(new[] { ModText.ActBannerLine(1) }, lines);   // the act banner was never shown in this feed
        Assert.Empty(t.Feed.Story);
    }

    [Fact]
    public void ModFeed_PlainRootGame_LogsNothing()
    {
        var board = HostTable.FourSeats();
        var game = new Root33Game(board, board, ModuleRegistry.Default());
        game.OnGameStart(board.Seats(), Difficulty.Normal, SpoilerMode.Safe);
        game.Context.Warnings.Add("ignored");
        var lines = new List<string>();
        new ModFeed().CatchUp(game.Context, 1, setupDone: true, lines.Add);
        Assert.Empty(lines);
    }
}
