using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

public class DeckTests
{
    [Fact]
    public void BaseDeck_Has54CardsWithRootsSuitsAmbushesAndDominance()
    {
        Assert.Equal(54, Cards.All.Count);
        Assert.Equal(14, Cards.All.Count(c => c.Suit == Suit.Fox));
        Assert.Equal(13, Cards.All.Count(c => c.Suit == Suit.Rabbit));
        Assert.Equal(13, Cards.All.Count(c => c.Suit == Suit.Mouse));
        Assert.Equal(14, Cards.All.Count(c => c.Suit == Suit.Bird));
        Assert.Equal(5, Cards.All.Count(c => c.Kind == CardKind.Ambush));
        Assert.Equal(2, Cards.All.Count(c => c.Kind == CardKind.Ambush && c.Suit == Suit.Bird));
        Assert.Equal(4, Cards.All.Count(c => c.Kind == CardKind.Dominance));
        Assert.Equal(3, Cards.All.Count(c => c.Key == "tax-collector"));
        Assert.Equal(new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse }, Cards.All.First(c => c.Key == "tax-collector").Cost);
        Assert.Equal(4, Cards.All.First(c => c.Key == "royal-claim").AnyCost);
    }

    [Fact]
    public void NewGame_ItemSupplyRuinsAndHands()
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("paintress", true), new SimGame.SeatSpec("sciel", true) }, 5);
        Assert.Equal(12, g.State.ItemSupply.Count);
        var ruins = g.State.Clearings.Values.Where(c => c.Ruin).OrderBy(c => c.Id).ToList();
        Assert.Equal(new[] { 7, 8, 9, 10 }, ruins.Select(c => c.Id));   // Root's own Fall layout
        Assert.Equal(new[] { ItemType.Bag, ItemType.Boots, ItemType.Hammer, ItemType.Sword }.OrderBy(x => x), ruins.Select(c => c.RuinItem!.Value).OrderBy(x => x));
        Assert.All(g.State.Seats, s => Assert.Equal(3, s.Hand.Count));
        Assert.Equal(54 - 9, g.State.Deck.Count);
        var two = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("paintress", true) }, 5);
        Assert.DoesNotContain(two.State.Deck.Concat(two.State.Seats.SelectMany(s => s.Hand)), c => Cards.Get(c).Kind == CardKind.Dominance);
    }

    [Fact]
    public void Draw_ReshufflesDiscardWhenDeckRunsOut()
    {
        var g = Kit.Table();
        g.State.Discard.AddRange(g.State.Deck.Skip(2));
        g.State.Deck.RemoveRange(2, g.State.Deck.Count - 2);
        Assert.Equal(4, g.Draw(0, 4));
        Assert.Empty(g.State.Discard);
        Assert.Equal(4, g.Seat(0).Hand.Count);
    }
}

public class BattleTests
{
    [Fact]
    public void Battle_AttackerTakesHigherDefenderLower_EachCappedByWarriors()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 5, 2);
        Kit.Warriors(g, 1, 5, 1);
        Kit.Building(g, 1, 5, PieceTypes.Roost);
        Kit.Dice(g, 1, 3);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        var b = g.State.Log.Select(e => e.Text).ToList();
        Assert.Contains(b, t => t.Contains("Dice: 1 and 3"));
        Assert.Equal(1, g.Warriors(0, 5));           // defender rolled 1, capped at its 1 warrior
        Assert.Equal(0, g.Warriors(1, 5));           // attacker rolled 3, capped at 2: warrior then roost
        Assert.Equal(0, g.BuildingsOf(1, 5));
        Assert.Equal(1, g.Seat(0).Vp);               // 1 VP for the roost
        Assert.Equal("test.hold", g.Pending!.Kind);
        Assert.Null(g.State.Battle);
    }

    [Fact]
    public void Battle_DefenselessDefenderTakesAnExtraHit()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 5, 1);
        Kit.Building(g, 1, 5, PieceTypes.Roost);
        Kit.Dice(g, 0, 0);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.Equal(0, g.BuildingsOf(1, 5));
        Assert.Equal(1, g.Seat(0).Vp);
    }

    [Fact]
    public void Battle_AmbushDealsTwoHitsAndEndsBattleWhenNoAttackersRemain()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 5, 2);   // c5 is fox
        Kit.Warriors(g, 1, 5, 1);
        g.Seat(1).Hand.Add(Kit.Card("ambush", Suit.Fox));
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.Equal(0, g.Warriors(0, 5));
        Assert.Equal(1, g.Warriors(1, 5));
        Assert.Empty(g.Seat(1).Hand);
        Assert.Contains(g.State.Discard, c => c == Kit.Card("ambush", Suit.Fox));
        Assert.Empty(g.TestDice);                     // no dice were needed
    }

    [Fact]
    public void Battle_AttackerMayFoilAnAmbushWithItsOwn()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 5, 2);
        Kit.Warriors(g, 1, 5, 1);
        int theirs = Kit.Card("ambush", Suit.Bird), mine = Kit.Card("ambush", Suit.Fox);
        g.Seat(1).Hand.Add(theirs);
        g.Seat(0).Hand.Add(mine);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.Equal(0, g.Pending!.Seat);
        Assert.Contains(g.Pending.Options, o => o.Key == $"foil:{mine}");
        Kit.Dice(g, 2, 0);
        g.Choose(0, $"foil:{mine}");
        Assert.Equal(2, g.Warriors(0, 5));
        Assert.Equal(0, g.Warriors(1, 5));
    }

    [Fact]
    public void Battle_NonActiveDefenderLosesWarriorsFirstThenItsCheapestPieces()
    {
        var g = Kit.Table();
        Kit.Active(g, 1, TurnPhase.Daylight);
        Kit.Warriors(g, 1, 5, 1);
        Kit.Token(g, 0, 5, PieceTypes.Wood);
        Kit.Building(g, 0, 5, PieceTypes.Sawmill);
        Kit.Building(g, 0, 5, PieceTypes.Workshop);
        Kit.Dice(g, 1, 0);
        Kit.Hold(g, 1);
        Battles.Start(g, 1, 0, 5);
        g.Advance();
        // 1 rolled hit + 1 defenseless: the Marquise (not her turn) loses the wood, then the workshop, by default
        Assert.Equal(0, g.TokensOf(0, 5));
        Assert.Equal(1, g.BuildingsOf(0, 5, PieceTypes.Sawmill));
        Assert.Equal(0, g.BuildingsOf(0, 5, PieceTypes.Workshop));
        Assert.Equal(2, g.Seat(1).Vp);
    }

    [Fact]
    public void Battle_BrutalTacticsAndArmorers()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 5, 3);
        Kit.Warriors(g, 1, 5, 3);
        g.Seat(0).PlayArea.Add(Kit.Card("brutal-tactics"));
        g.Seat(1).PlayArea.Add(Kit.Card("armorers"));
        Kit.Dice(g, 2, 2);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        g.Choose(0, "brutal");
        Assert.Equal(1, g.Seat(1).Vp);             // the defender scores for Brutal Tactics (only "continue" was left: applied at once)
        // the defender's default uses Armorers against 2 rolled hits: only the Brutal Tactics hit lands
        Assert.Equal(2, g.Warriors(1, 5));
        Assert.Equal(1, g.Warriors(0, 5));
        Assert.DoesNotContain(g.Seat(1).PlayArea, c => Cards.Get(c).Key == "armorers");
    }
}

public class MoveAndRuleTests
{
    [Fact]
    public void Rule_MostWarriorsPlusBuildings_TiesRuleNobody_EyrieWinsTies()
    {
        var g = Kit.Table(1, "gustave", "sciel", "paintress");
        Kit.Warriors(g, 0, 6, 1); Kit.Building(g, 0, 6, PieceTypes.Sawmill);
        Kit.Warriors(g, 1, 6, 2);
        Assert.Null(g.Ruler(6));
        Kit.Token(g, 0, 6, PieceTypes.Wood);       // tokens never count
        Assert.Null(g.Ruler(6));
        Kit.Warriors(g, 1, 7, 1); Kit.Warriors(g, 2, 7, 1);
        Assert.Equal(2, g.Ruler(7));                // Lords of the Forest
        Assert.Null(g.Ruler(9));
    }

    [Fact]
    public void Move_NeedsRuleOfOriginOrDestination_AlongPathsOnly()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 1, 2);
        Kit.Warriors(g, 1, 5, 3);
        Assert.True(Moves.CanMove(g, 0, 1, 5));     // rules the origin
        Assert.False(Moves.CanMove(g, 0, 1, 3));    // no path: forests and the map's middle are not walkable
        Assert.False(Moves.CanMove(g, 0, 1, 6));    // a river is not a path
        Kit.Warriors(g, 0, 1, 0); Kit.Warriors(g, 0, 8, 1); Kit.Warriors(g, 1, 8, 1);
        Assert.False(Moves.CanMove(g, 0, 8, 1));    // rules neither 8 (tie) nor 1 (empty)
        g.State.Mod.Abilities[0] = new List<string> { "rivers-as-paths" };
        Kit.Warriors(g, 0, 1, 2);
        Assert.True(Moves.CanMove(g, 0, 1, 6));
    }

    [Fact]
    public void MoveFrame_MovesTheChosenCount()
    {
        var g = Kit.Table();
        Kit.Warriors(g, 0, 1, 3);
        Kit.Hold(g, 0);
        g.Push(Moves.Frame(0, optional: false));
        g.Advance();
        g.Choose(0, "move:1-5");
        Assert.Equal(new[] { "count:3", "count:2", "count:1" }, g.Pending!.Options.Select(o => o.Key));
        g.Choose(0, "count:2");
        Assert.Equal(1, g.Warriors(0, 1));
        Assert.Equal(2, g.Warriors(0, 5));
    }

    [Fact]
    public void Keep_StopsOtherSeatsPlacing()
    {
        var g = Kit.Table();
        Kit.Token(g, 0, 1, PieceTypes.Keep);
        Assert.Equal(0, g.PlaceWarriors(1, 1, 2));
        Assert.Equal(2, g.PlaceWarriors(1, 5, 2));
        Assert.Equal(1, g.PlaceWarriors(0, 1, 1));
    }
}

public class CraftingTests
{
    [Fact]
    public void Craft_ItemFromSupplyScoresItsVp_EachWorkshopOncePerTurn()
    {
        var g = Kit.Table();
        Kit.Building(g, 0, 5, PieceTypes.Workshop);           // fox
        int anvil = Kit.Card("anvil"), steel = Kit.Card("foxfolk-steel");
        g.Seat(0).Hand.AddRange(new[] { anvil, steel });
        Assert.Equal("not enough crafting pieces of its suits", Crafting.WhyNot(g, 0, steel));
        Crafting.Craft(g, 0, anvil);
        Assert.Equal(2, g.Seat(0).Vp);
        Assert.Contains(ItemType.Hammer, g.Seat(0).CraftedItems);
        Assert.DoesNotContain(ItemType.Hammer, g.State.ItemSupply);
        Assert.Empty(Crafting.FreePieces(g, 0));
        Assert.Contains(anvil, g.State.Discard);
    }

    [Fact]
    public void Craft_PersistentNoDuplicates_ItemNeedsSupply_FavorClearsSuit()
    {
        var g = Kit.Table();
        foreach (var c in new[] { 5, 8, 9 }) Kit.Building(g, 0, c, PieceTypes.Workshop);   // three fox workshops
        var armorers = Cards.All.Where(c => c.Key == "armorers").Select(c => c.Id).ToList();
        g.Seat(0).Hand.AddRange(armorers);
        Crafting.Craft(g, 0, armorers[0]);
        Assert.Equal("you already have it", Crafting.WhyNot(g, 0, armorers[1]));
        g.State.ItemSupply.RemoveAll(i => i == ItemType.Crossbow);
        int xbow = Kit.Card("crossbow", Suit.Mouse);
        g.Seat(0).Hand.Add(xbow);
        Assert.Equal("no Crossbow left in the item supply", Crafting.WhyNot(g, 0, xbow));

        var g2 = Kit.Table();
        foreach (var c in new[] { 5, 8, 9 }) Kit.Building(g2, 0, c, PieceTypes.Workshop);
        Kit.Warriors(g2, 1, 4, 2); Kit.Building(g2, 1, 4, PieceTypes.Roost);    // c4 is fox
        Kit.Warriors(g2, 1, 2, 2);                                               // c2 is rabbit
        int favor = Kit.Card("favor-of-the-foxes");
        g2.Seat(0).Hand.Add(favor);
        Kit.Hold(g2, 0);
        Crafting.Craft(g2, 0, favor);
        g2.Advance();
        Assert.Equal(0, g2.Warriors(1, 4));
        Assert.Equal(0, g2.BuildingsOf(1, 4));
        Assert.Equal(2, g2.Warriors(1, 2));
        Assert.Equal(1, g2.Seat(0).Vp);
    }
}

public class CardTextTests
{
    [Fact]
    public void Describe_RoyalClaimInHand_SaysCraftItFirst_BeforeItsEffect()
    {
        // Kit held Royal Claim all game and was never offered it: a persistent card works only once crafted.
        int claim = Kit.Card("royal-claim");
        Assert.Equal($"{Cards.Label(claim)} - persistent effect; craft it first (cost: any+any+any+any), then it stays in front of you: " +
                     "In Birdsong, you may discard this card to score one point per clearing you rule.", Cards.Describe(claim));
        foreach (var c in Cards.All.Where(c => c.Kind is CardKind.Persistent or CardKind.Immediate))
        {
            var d = Cards.Describe(c.Id);
            var craft = d.IndexOf($"craft it first (cost: {Cards.CostText(c)})", StringComparison.Ordinal);
            Assert.True(craft > 0, d);
            Assert.True(craft < d.IndexOf(c.Text, StringComparison.Ordinal), d);
        }
        Assert.Contains("one-time effect; craft it first (cost: fox+fox+fox): Immediately upon crafting", Cards.Describe(Kit.Card("favor-of-the-foxes")));
        // items, ambushes and dominance cards are not effect cards
        Assert.Equal($"{Cards.Label(Kit.Card("anvil"))} - item Hammer, 2 VP; craft: fox. Craft a Hammer.", Cards.Describe(Kit.Card("anvil")));
        foreach (var c in Cards.All.Where(c => c.Kind is CardKind.Item or CardKind.Ambush or CardKind.Dominance))
            Assert.DoesNotContain("craft it first", Cards.Describe(c.Id));

        // the hand in 'view' reads the same; the crafting option names the cost without repeating "craft it first"
        var g = Kit.Table();
        g.Seat(0).Hand.Add(claim);
        Assert.Contains("  " + Cards.Describe(claim), Render.View(g, 0));
        Assert.DoesNotContain("craft it first", Cards.DescribeToCraft(claim));
        Assert.Contains("cost: any+any+any+any", Cards.DescribeToCraft(claim));
    }
}

public class CraftingStepTests
{
    [Fact]
    public void CraftingStep_WithNothingCraftable_SaysSoInOnePrivateLine()
    {
        var g = Kit.Table();
        Kit.Building(g, 0, 5, PieceTypes.Workshop);            // one fox workshop
        g.Seat(0).Hand.Add(Kit.Card("foxfolk-steel"));           // needs fox+fox
        Kit.Hold(g, 0);
        g.Push(Crafting.Frame(0));
        g.Advance();
        Assert.Equal("test.hold", g.Pending!.Kind);
        Assert.Contains(g.LogFor(0), e => e.Text == "Crafting: nothing in your hand can be crafted now (free crafting pieces: 1 fox), so crafting is skipped this turn.");
        Assert.DoesNotContain(g.LogFor(null), e => e.Text.StartsWith("Crafting:"));   // it speaks of the hand: only that seat reads it

        var empty = Kit.Table();
        Kit.Hold(empty, 0);
        empty.Push(Crafting.Frame(0));
        empty.Advance();
        Assert.Contains(empty.LogFor(0), e => e.Text == "Crafting: your hand is empty (no free crafting pieces), so crafting is skipped this turn.");
    }

    [Fact]
    public void CraftingStep_AfterCraftingOrAsTheVagabondsAction_StaysQuiet()
    {
        var g = Kit.Table();
        Kit.Building(g, 0, 5, PieceTypes.Workshop);
        int anvil = Kit.Card("anvil");
        g.Seat(0).Hand.Add(anvil);
        Kit.Hold(g, 0);
        g.Push(Crafting.Frame(0));
        g.Advance();
        Assert.Equal("craft", g.Pending!.Kind);
        g.Choose(0, "craft:" + anvil);
        Assert.Equal("test.hold", g.Pending!.Kind);             // crafted: nothing more to say
        Assert.DoesNotContain(g.LogFor(0), e => e.Text.StartsWith("Crafting:"));

        var once = Kit.Table();
        Kit.Hold(once, 0);
        once.Push(Crafting.Frame(0, once: true));
        once.Advance();
        Assert.DoesNotContain(once.LogFor(0), e => e.Text.StartsWith("Crafting:"));
    }
}

public class DominanceTests
{
    [Fact]
    public void Dominance_ActivatesAt10Vp_StopsScoring_WinsAtBirdsong()
    {
        var g = Kit.Table(1, "gustave", "paintress", "sciel");
        int dom = Kit.Card("dominance", Suit.Mouse);
        g.Seat(0).Hand.Add(dom);
        g.Seat(0).Vp = 9;
        Kit.Hold(g, 0);
        g.Push(new Frame("marquise.daylight", 0) { Step = 2 }.Set("actions", 0));   // an open Daylight decision
        g.Advance();
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key == $"card:dominance:{dom}");
        g.Seat(0).Vp = 10;
        g.State.Pending = null;
        g.Advance();
        Assert.Contains(g.Pending!.Options, o => o.Key == $"card:dominance:{dom}");
        g.Choose(0, $"card:dominance:{dom}");
        Assert.Equal(dom, g.Seat(0).Dominance);
        g.AddVp(0, 3, "test");
        Assert.Equal(10, g.Seat(0).Vp);
        foreach (var c in new[] { 6, 7, 12 }) Kit.Warriors(g, 0, c, 1);   // the three mouse clearings
        g.CheckDominanceWin(0);
        Assert.True(g.State.GameOver);
        Assert.Equal(new[] { 0 }, g.State.Winners);
    }

    [Fact]
    public void BirdDominance_NeedsTwoOppositeCorners()
    {
        var g = Kit.Table(1, "gustave", "paintress", "sciel");
        g.Seat(0).Dominance = Kit.Card("dominance", Suit.Bird);
        Kit.Warriors(g, 0, 1, 1); Kit.Warriors(g, 0, 2, 1);
        g.CheckDominanceWin(0);
        Assert.False(g.State.GameOver);
        Kit.Warriors(g, 0, 3, 1);
        g.CheckDominanceWin(0);
        Assert.True(g.State.GameOver);
    }

    [Fact]
    public void ThirtyVp_WinsAtOnce()
    {
        var g = Kit.Table();
        g.Seat(1).Vp = 29;
        g.AddVp(1, 1, "test");
        Assert.True(g.State.GameOver);
        Assert.Equal(new[] { 1 }, g.State.Winners);
        Assert.Contains("30 victory points", g.State.GameOverText);
    }
}
