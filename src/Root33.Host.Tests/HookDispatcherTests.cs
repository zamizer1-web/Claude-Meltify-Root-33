using Root33.Core;
using Root33.Core.Engine;
using Root33.Host;

namespace Root33.Host.Tests;

/// <summary>How hosts call the core (Root33.Host.HookDispatcher): nested hooks queued, questions answered at once, core
/// exceptions recorded and reported once per hook.</summary>
public class HookDispatcherTests
{
    static HookDispatcher NewDispatcher()
    {
        var board = HostTable.FourSeats();
        return new HookDispatcher(new Root33Game(board, board, new ModuleRegistry()));
    }

    [Fact]
    public void HookDispatcher_HooksRaisedInsideAHook_RunRightAfterIt()
    {
        var d = NewDispatcher();
        var order = new List<string>();
        var after = 0;
        d.AfterHook = () => after++;
        d.Call("hook.a", _ =>
        {
            order.Add("a1");
            Assert.True(d.Running);
            d.Call("hook.b", _ => order.Add("b"));
            d.Call("hook.c", _ => order.Add("c"));
            order.Add("a2");
        });
        Assert.Equal(new[] { "a1", "a2", "b", "c" }, order);
        Assert.Equal(1, after);
        Assert.False(d.Running);
    }

    [Fact]
    public void HookDispatcher_Ask_IsAnsweredAtOnce_EvenInsideAHook()
    {
        var d = NewDispatcher();
        bool? inner = null;
        d.Call("hook.a", _ => inner = d.Ask("hook.battle-legal", _ => false, true));
        Assert.False(inner);
        Assert.Equal("move:1", d.Ask<string?>("hook.bot-decision", _ => "move:1", null));
    }

    [Fact]
    public void HookDispatcher_CoreErrors_AreRecorded_AndReportedOncePerHook()
    {
        var d = NewDispatcher();
        var reported = new List<string>();
        d.FirstError = (hook, e) => reported.Add(ModText.CoreErrorLine(hook, e.Message));
        for (var i = 0; i < 2; i++) d.Call("hook.turn-end", _ => throw new InvalidOperationException("boom"));
        d.Call("hook.vp-changed", _ => throw new ArgumentException("bad"));
        Assert.Equal(3, d.Errors.Count);
        Assert.Equal("hook.turn-end: InvalidOperationException: boom", d.Errors[0]);
        Assert.Equal(new[]
        {
            "(Root 33 core error in hook.turn-end: boom; the game goes on without it.)",
            "(Root 33 core error in hook.vp-changed: bad; the game goes on without it.)",
        }, reported);
        var ran = false;
        d.Call("hook.turn-end", _ => ran = true);   // the game goes on
        Assert.True(ran);
    }

    [Fact]
    public void HookDispatcher_Ask_FallsBackWhenTheCoreThrows()
    {
        var d = NewDispatcher();
        Assert.True(d.Ask<bool>("hook.battle-legal", _ => throw new InvalidOperationException(), true));
        Assert.Single(d.Errors);
    }

    [Fact]
    public void HookDispatcher_RunAlone_DropsTheHooksItRaises()
    {
        var d = NewDispatcher();
        var order = new List<string>();
        var after = 0;
        d.AfterHook = () => after++;
        d.RunAlone("hook.load", _ => { order.Add("load"); d.Call("hook.piece-placed", _ => order.Add("placed")); });
        d.Call("hook.round-start", _ => order.Add("round"));
        Assert.Equal(new[] { "load", "round" }, order);
        Assert.Equal(1, after);
    }
}
