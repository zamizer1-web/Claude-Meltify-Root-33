using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

/// <summary>Card helpers for engineer B's tests: take a specific card out of the deck.</summary>
internal static class BKit
{
    /// <summary>Whether a card is not held by anyone (hand, play area, Decree, Supporters).</summary>
    static bool Free(SimGame g, int id) =>
        !g.State.Seats.Any(s => s.Hand.Contains(id) || s.PlayArea.Contains(id))
        && !Enumerable.Range(0, g.SeatCount).Any(s => (g.Faction(s) is Eyrie && Eyrie.DecreeCards(g, s).Contains(id))
                                                     || (g.Faction(s) is Alliance && Alliance.SupporterCards(g, s).Contains(id)));

    /// <summary>A free card of the suit (item, persistent or favor), out of the deck and discard pile.</summary>
    public static int Take(SimGame g, Suit suit, Func<CardDef, bool>? test = null)
    {
        var c = Cards.All.First(x => x.Suit == suit && (x.Kind is CardKind.Item or CardKind.Persistent or CardKind.Immediate)
                                     && Free(g, x.Id) && (test == null || test(x)));
        g.State.Deck.Remove(c.Id); g.State.Discard.Remove(c.Id);
        return c.Id;
    }

    public static int TakeKey(SimGame g, string key, Suit? suit = null)
    {
        var c = Cards.All.First(x => x.Key == key && (suit == null || x.Suit == suit) && Free(g, x.Id));
        g.State.Deck.Remove(c.Id); g.State.Discard.Remove(c.Id);
        return c.Id;
    }

    public static int ToHand(SimGame g, int seat, int card) { g.Seat(seat).Hand.Add(card); return card; }

    /// <summary>Push a frame over a hold and run until the hold (or another decision) is reached.</summary>
    public static void Run(SimGame g, int seat, Frame f)
    {
        Kit.Hold(g, seat);
        g.Push(f);
        g.State.Pending = null;
        g.Advance();
    }
}

public class EyrieTests
{
    const int M = 0, E = 1;

    static SimGame Table()
    {
        var g = Kit.Table(1, "gustave", "paintress");
        Kit.Active(g, E, TurnPhase.Daylight);
        return g;
    }

    static void Lead(SimGame g, string leader)
    {
        var d = Eyrie.D(g, E);
        d.Leader = leader;
        foreach (var c in Eyrie.Columns) d.Column(c).RemoveAll(x => x < 0);
        d.Column(Eyrie.VizierColumns[leader][0]).Add(Eyrie.Vizier1);
        d.Column(Eyrie.VizierColumns[leader][1]).Add(Eyrie.Vizier2);
    }

    [Fact]
    public void Setup_RoostAndSixWarriorsOppositeTheKeep_ThenALeaderWithItsViziers()
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("paintress", true) }, 3);
        g.Choose(0, "sawmill:1"); g.Choose(0, "workshop:5"); g.Choose(0, "recruiter:12");
        // the only legal home is clearing 3 (opposite the keep), applied at once; the leader is a real choice
        Assert.Equal("eyrie.setup", g.Pending!.Kind);
        Assert.Equal(4, g.Pending.Options.Count);
        Assert.Equal(1, g.BuildingsOf(1, 3, PieceTypes.Roost));
        Assert.Equal(6, g.Warriors(1, 3));
        g.Choose(1, "leader:commander");
        Assert.True(g.State.SetupDone);
        var d = Eyrie.D(g, 1);
        Assert.Equal("commander", g.Faction(1).EyrieLeader(g, 1));
        Assert.Equal(new[] { Eyrie.Vizier1 }, d.Column("move"));
        Assert.Equal(new[] { Eyrie.Vizier2 }, d.Column("battle"));
        Assert.Equal(new[] { Suit.Bird }, g.Faction(1).DecreeColumn(g, 1, "battle"));
        Assert.Equal(6, g.Supply(1, PieceTypes.Roost));
        Assert.Equal(14, g.Seat(1).SupplyWarriors);
    }

    [Fact]
    public void LegalHomes_AnyFreeCornerWithoutAMarquise_ElseOppositeTheKeep()
    {
        var g = Table();
        Assert.Equal(new[] { 1, 2, 3, 4 }, Eyrie.LegalHomes(g, E));
        Kit.Token(g, M, 2, PieceTypes.Keep);
        Assert.Equal(new[] { 4 }, Eyrie.LegalHomes(g, E));
    }

    [Fact]
    public void Birdsong_EmergencyOrders_AddUpToTwoCards_OnlyOneBird_ThenANewRoost()
    {
        var g = Table();
        Kit.Active(g, E, TurnPhase.Birdsong);
        Lead(g, "despot");
        int fox = BKit.Take(g, Suit.Fox);
        g.State.Deck.Add(fox);                                   // top of the deck: Emergency Orders draws it
        Kit.Warriors(g, M, 5, 1);
        BKit.Run(g, E, new Frame("eyrie.birdsong", E));
        Assert.Equal(new[] { fox }, g.Seat(E).Hand);
        Assert.Contains(g.Pending!.Options, o => o.Key == $"add:{fox}:recruit");
        g.Choose(E, $"add:{fox}:battle");
        Assert.Equal(new[] { fox }, Eyrie.D(g, E).Column("battle"));
        // hand empty: no second card; no roost on the map: A New Roost in a clearing with the fewest warriors
        Assert.StartsWith("roost:", g.Pending!.Options[0].Key);
        Assert.DoesNotContain(g.Pending.Options, o => o.Key == "roost:5");   // clearing 5 has a warrior
        g.Choose(E, "roost:6");
        Assert.Equal(1, g.BuildingsOf(E, 6, PieceTypes.Roost));
        Assert.Equal(3, g.Warriors(E, 6));
        Assert.Equal("test.hold", g.Pending!.Kind);

        // two birds in hand: after a bird, the second card may not be a bird
        var g2 = Table();
        Kit.Active(g2, E, TurnPhase.Birdsong);
        Kit.Building(g2, E, 3, PieceTypes.Roost);
        int b1 = BKit.ToHand(g2, E, BKit.Take(g2, Suit.Bird));
        int b2 = BKit.ToHand(g2, E, BKit.Take(g2, Suit.Bird));
        int mouse = BKit.ToHand(g2, E, BKit.Take(g2, Suit.Mouse));
        BKit.Run(g2, E, new Frame("eyrie.birdsong", E));
        g2.Choose(E, $"add:{b1}:move");
        Assert.DoesNotContain(g2.Pending!.Options, o => o.Key.StartsWith($"add:{b2}:"));
        Assert.Contains(g2.Pending.Options, o => o.Key == $"add:{mouse}:build");
        Assert.Contains(g2.Pending.Options, o => o.Key == "done");
        g2.Choose(E, $"add:{mouse}:build");
        Assert.Equal("test.hold", g2.Pending!.Kind);           // a roost is on the map: no new roost
        Assert.Equal(new[] { b2 }, g2.Seat(E).Hand);
    }

    [Fact]
    public void Decree_ResolvesLeftToRight_Charismatic_ThenTurmoilHumiliatesPurgesAndDeposes()
    {
        var g = Table();
        Lead(g, "charismatic");                                  // viziers on Recruit and Battle
        g.Seat(E).Vp = 5;
        Kit.Building(g, E, 3, PieceTypes.Roost);
        Kit.Warriors(g, E, 3, 2);
        Kit.Warriors(g, M, 3, 1);
        int mouse = BKit.Take(g, Suit.Mouse), fox = BKit.Take(g, Suit.Fox);
        var d = Eyrie.D(g, E);
        d.Column("recruit").Add(mouse);
        d.Column("build").Add(fox);                              // no fox clearing to build in: Turmoil
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        Assert.True(g.Pending!.Open);
        Assert.Contains(g.Pending.Options, o => o.Key == "recruit:mouse:3");
        Assert.Contains(g.Pending.Options, o => o.Key == "recruit:bird:3");
        Assert.DoesNotContain(g.Pending.Options, o => o.Key.StartsWith("battle:"));   // left to right
        g.Choose(E, "recruit:mouse:3");
        Assert.Equal(4, g.Warriors(E, 3));                       // Charismatic: two warriors
        g.Choose(E, "recruit:bird:3");
        Assert.Equal(6, g.Warriors(E, 3));
        Assert.Equal(new[] { "battle:bird:3:0" }, g.Pending!.Options.Where(o => !o.Key.StartsWith("card:")).Select(o => o.Key));
        Kit.Dice(g, 2, 1);
        g.Choose(E, "battle:bird:3:0");
        Assert.Equal(0, g.Warriors(M, 3));
        Assert.Equal(5, g.Warriors(E, 3));
        // Build (fox) cannot be resolved: Turmoil
        Assert.Equal("eyrie.turmoil", g.Pending!.Kind);
        Assert.Equal(3, g.Seat(E).Vp);                           // two viziers (birds): -2 VP
        Assert.Contains(mouse, g.State.Discard);
        Assert.Contains(fox, g.State.Discard);
        Assert.Equal(new[] { "charismatic" }, d.FaceDown);
        Assert.Equal(new[] { "leader:builder", "leader:commander", "leader:despot" }, g.Pending.Options.Select(o => o.Key));
        g.Choose(E, "leader:despot");
        Assert.Equal("despot", d.Leader);
        Assert.Equal(new[] { Eyrie.Vizier1 }, d.Column("move"));
        Assert.Equal(new[] { Eyrie.Vizier2 }, d.Column("build"));
        Assert.Empty(d.Column("recruit"));
        Assert.Empty(d.Column("battle"));
        Assert.Equal("test.hold", g.Pending!.Kind);             // Daylight is over
    }

    [Fact]
    public void Turmoil_NewClutch_WhenNoLeaderIsFaceUp()
    {
        var g = Table();
        Lead(g, "builder");
        var d = Eyrie.D(g, E);
        d.FaceDown.AddRange(new[] { "charismatic", "commander", "despot" });
        int bird = BKit.Take(g, Suit.Bird);
        d.Column("build").Add(bird);
        g.Seat(E).Vp = 1;
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        // Recruit vizier: no roost on the map -> Turmoil at once
        Assert.Equal("eyrie.turmoil", g.Pending!.Kind);
        Assert.Equal(0, g.Seat(E).Vp);                           // 3 birds, but VP never go below 0
        Assert.Empty(d.FaceDown);
        Assert.Equal(4, g.Pending.Options.Count);
    }

    [Fact]
    public void DecreeMove_MustMoveFromAMatchingClearing_AndBuildPlacesARoostWhereTheEyrieRules()
    {
        var g = Table();
        Lead(g, "despot");                                       // viziers on Move and Build
        Kit.Warriors(g, E, 3, 2);                                // mouse
        Kit.Warriors(g, E, 9, 1);                                // fox
        Kit.Warriors(g, M, 9, 1);                                // tie in 9: Lords of the Forest
        int mouse = BKit.Take(g, Suit.Mouse);
        Eyrie.D(g, E).Column("move").Add(mouse);
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        g.Choose(E, "move:mouse");
        Assert.Equal("move", g.Pending!.Kind);
        Assert.All(g.Pending.Options, o => Assert.StartsWith("move:3>", o.Key));   // only from the mouse clearing
        Assert.DoesNotContain(g.Pending.Options, o => o.Key == "skip");
        g.Choose(E, "move:3>11");
        g.Choose(E, "count:2");
        Assert.Equal(2, g.Warriors(E, 11));
        g.Choose(E, "move:bird");
        g.Choose(E, "move:9>3");
        Assert.Equal(1, g.Warriors(E, 3));
        // Build (vizier, bird): any clearing the Eyrie rules with a free slot and no roost
        Assert.Contains(g.Pending!.Options, o => o.Key == "build:bird:11");
        Assert.Contains(g.Pending.Options, o => o.Key == "build:bird:3");
        g.Choose(E, "build:bird:11");
        Assert.Equal(1, g.BuildingsOf(E, 11, PieceTypes.Roost));
        Assert.Equal("test.hold", g.Pending!.Kind);
        Assert.Equal(0, g.Seat(E).Vp);
    }

    [Fact]
    public void Commander_ExtraHit_AlsoInABattleOutsideTheDecree()
    {
        // e.g. a Command Warren battle: the Commander's extra hit is the leader's, not the Decree's.
        var g = Table();
        Lead(g, "commander");
        Kit.Warriors(g, E, 5, 3);
        Kit.Warriors(g, M, 5, 3);
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        g.State.Pending = null;
        Battles.Start(g, E, M, 5);
        Kit.Dice(g, 1, 0);
        g.Advance();
        Assert.Equal(1, g.Warriors(M, 5));                       // 1 rolled + 1 Commander
        Assert.Equal(3, g.Warriors(E, 5));
    }

    [Fact]
    public void Commander_DealsAnExtraHitAsAttacker()
    {
        var g = Table();
        Lead(g, "commander");
        Eyrie.D(g, E).Column("move").Clear();
        Kit.Warriors(g, E, 5, 3);
        Kit.Warriors(g, M, 5, 3);
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        Kit.Dice(g, 1, 0);
        g.Choose(E, "battle:bird:5:0");
        Assert.Equal(1, g.Warriors(M, 5));                       // 1 rolled + 1 Commander
        Assert.Equal(3, g.Warriors(E, 5));
        Assert.Empty(g.State.Mod.ExtraHits);
    }

    [Fact]
    public void Despot_ScoresOneMoreForRemovingABuildingInBattle()
    {
        var g = Table();
        Lead(g, "despot");
        int fox = BKit.Take(g, Suit.Fox);
        var d = Eyrie.D(g, E);
        d.Column("move").Clear(); d.Column("build").Clear();
        d.Column("battle").Add(fox);
        Kit.Warriors(g, E, 5, 2);
        Kit.Building(g, M, 5, PieceTypes.Sawmill);
        BKit.Run(g, E, new Frame("eyrie.daylight", E) { Step = 1 });
        Kit.Dice(g, 1, 0);
        g.Choose(E, "battle:fox:5:0");
        Assert.Equal(0, g.BuildingsOf(M, 5));
        Assert.Equal(2, g.Seat(E).Vp);                           // 1 for the building + 1 Despot
    }

    [Fact]
    public void DisdainForTrade_OneVpPerItem_ExceptUnderTheBuilder()
    {
        var g = Table();
        Lead(g, "despot");
        Kit.Building(g, E, 5, PieceTypes.Roost);
        Kit.Building(g, E, 9, PieceTypes.Roost);
        int sword = BKit.ToHand(g, E, BKit.TakeKey(g, "foxfolk-steel"));   // 2 fox, 2 VP
        Assert.Contains(sword, Crafting.Craftable(g, E));
        Crafting.Craft(g, E, sword);
        Assert.Equal(1, g.Seat(E).Vp);
        Assert.Contains(ItemType.Sword, g.Seat(E).CraftedItems);
        Assert.Empty(Crafting.FreePieces(g, E));                 // each roost once per turn

        g.State.Turn.UsedCraftPieces.Clear();
        Lead(g, "builder");
        int anvil = BKit.ToHand(g, E, BKit.TakeKey(g, "anvil"));          // 1 fox, 2 VP
        Crafting.Craft(g, E, anvil);
        Assert.Equal(3, g.Seat(E).Vp);
    }

    [Fact]
    public void Evening_ScoresTheRoostTrack_AndDrawsTheBonus()
    {
        var g = Table();
        Kit.Active(g, E, TurnPhase.Evening);
        foreach (var c in new[] { 3, 5, 7 }) Kit.Building(g, E, c, PieceTypes.Roost);
        Assert.Equal(2, Eyrie.EveningVp(g, E));
        Assert.Equal(1, Eyrie.DrawBonus(g, E));
        BKit.Run(g, E, new Frame("eyrie.evening", E));
        Assert.Equal(2, g.Seat(E).Vp);
        Assert.Equal(2, g.Seat(E).Hand.Count);
        Assert.Equal(new[] { 0, 0, 1, 2, 3, 4, 4, 5 }, Eyrie.RoostVp);
    }

    [Fact]
    public void Board_ShowsLeaderDecreeAndRoosts_AndSurvivesSaveLoad()
    {
        var g = Table();
        Lead(g, "despot");
        int rabbit = BKit.Take(g, Suit.Rabbit);
        Eyrie.D(g, E).Column("recruit").Add(rabbit);
        Kit.Building(g, E, 3, PieceTypes.Roost);
        var text = g.Faction(E).BoardText(g, E);
        Assert.Contains("Despot", text);
        Assert.Contains("Loyal Vizier", text);
        Assert.Contains(Cards.Label(rabbit), text);
        var back = SimGame.FromJson(g.ToJson());
        Assert.Equal("despot", Eyrie.D(back, E).Leader);
        Assert.Equal(new[] { rabbit }, Eyrie.D(back, E).Column("recruit"));
        Assert.Equal(new[] { Suit.Rabbit }, back.Faction(E).DecreeColumn(back, E, "recruit"));
    }
}
