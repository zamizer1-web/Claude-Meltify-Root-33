using Root33.Core;
using Root33.Sim;

namespace Root33.Sim.Tests;

/// <summary>The Vagabond (Law 9): Tinker, Ranger and Thief. Owner: engineer C.</summary>
public class VagabondTests
{
    const ItemType Boots = ItemType.Boots, Sword = ItemType.Sword, Torch = ItemType.Torch, Tea = ItemType.Tea,
        Coins = ItemType.Coins, Bag = ItemType.Bag, Hammer = ItemType.Hammer, Crossbow = ItemType.Crossbow;

    // ------------------------------------------------------------------------------------------ helpers

    static VagabondData D(SimGame g, int seat) => Vagabond.Data(g, seat);

    static void Pawn(SimGame g, int seat, int c) { g.Seat(seat).PawnClearing = c; g.Seat(seat).PawnForest = null; }
    static void PawnInForest(SimGame g, int seat, int forest) { g.Seat(seat).PawnClearing = null; g.Seat(seat).PawnForest = forest; }

    static void SetItems(SimGame g, int seat, params ItemType[] items) => D(g, seat).Items = items.Select(t => new VbItem(t)).ToList();
    static void AddItem(SimGame g, int seat, ItemType t, bool exhausted = false, bool damaged = false) =>
        D(g, seat).Items.Add(new VbItem(t) { Exhausted = exhausted, Damaged = damaged });
    static int Count(SimGame g, int seat, ItemType t, bool? exhausted = null, bool? damaged = null) =>
        D(g, seat).Items.Count(i => i.Type == t && (exhausted == null || i.Exhausted == exhausted) && (damaged == null || i.Damaged == damaged));

    /// <summary>Move a card (by key) into a seat's hand from wherever it is.</summary>
    static int Give(SimGame g, int seat, string key, Suit? suit = null)
    {
        int c = Kit.Card(key, suit);
        g.State.Deck.Remove(c); g.State.Discard.Remove(c);
        foreach (var s in g.State.Seats) s.Hand.Remove(c);
        g.Seat(seat).Hand.Add(c);
        return c;
    }

    static int ToDiscard(SimGame g, string key, Suit? suit = null)
    {
        int c = Kit.Card(key, suit);
        g.State.Deck.Remove(c);
        foreach (var s in g.State.Seats) s.Hand.Remove(c);
        g.State.Discard.Add(c);
        return c;
    }

    /// <summary>Run one frame of the seat's turn in a phase, with a hold frame under it.</summary>
    static void Run(SimGame g, int seat, TurnPhase phase, string kind)
    {
        Kit.Active(g, seat, phase);
        Kit.Hold(g, seat);
        g.Push(new Frame(kind, seat));
        g.Advance();
    }

    static void Daylight(SimGame g, int seat) => Run(g, seat, TurnPhase.Daylight, "vagabond.daylight");

    static void Pick(SimGame g, int seat, string key)
    {
        Assert.NotNull(g.Pending);
        Assert.Equal(seat, g.Pending!.Seat);
        Assert.True(g.Pending.Find(key) != null, $"'{key}' not offered at {g.Pending.Kind}: {string.Join(" | ", g.Pending.Options.Select(o => o.Key))}");
        g.Choose(seat, key);
    }

    static bool Offered(SimGame g, string key) => g.Pending?.Find(key) != null;

    /// <summary>Ask the top frame again after a test changed the state under a pending decision.</summary>
    static void Refresh(SimGame g) { g.State.Pending = null; g.Advance(); }

    // ------------------------------------------------------------------------------------------ setup

    [Fact]
    public void Setup_PawnInAForest_StartingItems_ThreeQuests_IndifferentRelationships()
    {
        var g = SimGame.New(new[] { new SimGame.SeatSpec("gustave", true), new SimGame.SeatSpec("maelle", true) }, 3);
        g.Choose(0, "sawmill:1"); g.Choose(0, "workshop:5"); g.Choose(0, "recruiter:12");
        Assert.Equal("vagabond.setup", g.Pending!.Kind);
        Assert.Equal(7, g.Pending.Options.Count);
        g.Choose(1, "forest:3");
        Assert.Equal(3, g.Seat(1).PawnForest);
        Assert.Null(g.Seat(1).PawnClearing);
        Assert.Equal(new[] { Boots, Torch, Tea, Sword }, D(g, 1).Items.Select(i => i.Type));
        Assert.All(D(g, 1).Items, i => Assert.False(i.Exhausted || i.Damaged));
        Assert.Equal(3, Vagabond.QuestsUp(g).Count);
        Assert.Equal(12, Vagabond.QuestDeck(g).Count);
        Assert.Equal(Relationship.Indifferent, Vagabond.Rel(g, 1, 0));
        Assert.True(g.State.SetupDone);
        Assert.Equal(15, VagabondQuests.All.Count);
        Assert.Equal(5, VagabondQuests.All.Count(q => q.Suit == Suit.Fox));
        Assert.Equal(5, VagabondQuests.All.Count(q => q.Suit == Suit.Mouse));
    }

    [Fact]
    public void StartingItems_ByCharacter()
    {
        var g = Kit.Table(1, "esquie", "verso", "gustave");
        Assert.Equal(new[] { Boots, Torch, Bag, Hammer }, D(g, 0).Items.Select(i => i.Type));     // Tinker
        Assert.Equal(new[] { Boots, Torch, Crossbow, Sword }, D(g, 1).Items.Select(i => i.Type)); // Ranger
        Assert.Equal("Tinker", Vagabond.Character(g, 0));
        Assert.Equal("Ranger", Vagabond.Character(g, 1));
        // two Vagabonds: no relationship marker for each other, one shared quest row (Law 9.7.2)
        Assert.False(Vagabond.HasMarker(g, 0, 1));
        Assert.True(Vagabond.HasMarker(g, 0, 2));
        Assert.Equal(0, Vagabond.QuestHolder(g));
        Assert.Same(Vagabond.QuestsUp(g), D(g, 0).QuestsUp);
        Assert.Empty(D(g, 1).QuestsUp);
    }

    // ------------------------------------------------------------------------------------------ Birdsong

    [Fact]
    public void Refresh_ThreePlusTwoPerTea_PlayerChoosesWhenShort_ThenSlip()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        SetItems(g, 1, Tea);                                                // 1 tea on its track: refresh 5
        foreach (var t in new[] { Boots, Boots, Boots, Sword, Torch, Hammer }) AddItem(g, 1, t, exhausted: true);
        AddItem(g, 1, Crossbow, exhausted: true, damaged: true);
        Run(g, 1, TurnPhase.Birdsong, "vagabond.birdsong");
        Assert.Equal("vagabond.birdsong", g.Pending!.Kind);
        foreach (var k in new[] { "sword-exhausted", "torch-exhausted", "boots-exhausted", "boots-exhausted", "boots-exhausted" })
            Pick(g, 1, "refresh:" + k);
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: true));               // the sixth item stays face down
        Assert.Equal(1, Count(g, 1, Crossbow, exhausted: true));
        // slip from Spring Meadows (c5): its paths, its forest, or stay
        Assert.Equal(new[] { "slip:c:1", "slip:c:6", "slip:f:5", "stay" }, g.Pending!.Options.Select(o => o.Key));
        Pick(g, 1, "slip:f:5");
        Assert.Equal(5, g.Seat(1).PawnForest);
        Assert.Null(g.Seat(1).PawnClearing);
    }

    [Fact]
    public void Refresh_AllWhenTheyFit_DamagedFaceDownItemsWithWhatIsLeft_SlipBetweenForests()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        PawnInForest(g, 1, 1);
        SetItems(g, 1);
        AddItem(g, 1, Boots, exhausted: true);
        AddItem(g, 1, Sword, exhausted: true, damaged: true);
        Run(g, 1, TurnPhase.Birdsong, "vagabond.birdsong");
        Assert.All(D(g, 1).Items, i => Assert.False(i.Exhausted));
        Assert.True(D(g, 1).Items.Single(i => i.Type == Sword).Damaged);
        // forest 1 touches 3, 4, 10, 11 and is one path away from forests 2 and 3
        Assert.Equal(new[] { "slip:c:3", "slip:c:4", "slip:c:10", "slip:c:11", "slip:f:2", "slip:f:3", "stay" }, g.Pending!.Options.Select(o => o.Key));
        Assert.Equal(new[] { 2, 3 }, Vagabond.AdjacentForests(g, 1));
        Assert.Equal(new[] { 2, 5 }, Vagabond.AdjacentForests(g, 4));
    }

    // ------------------------------------------------------------------------------------------ Daylight: move

    [Fact]
    public void Move_OneBoot_OneMoreIntoHostileWarriors_FromAForestOnlyIntoTouchingClearings()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        PawnInForest(g, 1, 4);                                              // forest 4 touches 1, 8, 12
        SetItems(g, 1, Boots, Boots, Torch);
        D(g, 1).Relations[0] = Relationship.Hostile;
        Kit.Warriors(g, 0, 8, 1);
        Daylight(g, 1);
        var moves = g.Pending!.Options.Where(o => o.Key.StartsWith("move:")).Select(o => o.Key).ToList();
        Assert.Equal(new[] { "move:1", "move:8", "move:12" }, moves);
        Assert.Contains("2 boots", g.Pending.Find("move:8")!.Text);
        Pick(g, 1, "move:8");
        Assert.Equal(8, g.Seat(1).PawnClearing);
        Assert.Null(g.Seat(1).PawnForest);
        Assert.Equal(0, Vagabond.Ready(g, 1, Boots));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("move:"));
    }

    [Fact]
    public void Move_NoHostileBootAbility_AndAlliedWarriorsMayFollow()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        SetItems(g, 1, Boots, Torch);
        g.State.Mod.Abilities[1] = new List<string> { "no-hostile-boot" };
        D(g, 1).Relations[0] = Relationship.Allied;
        Kit.Warriors(g, 0, 5, 3);
        Daylight(g, 1);
        Pick(g, 1, "move:1");
        Assert.Equal("vagabond.ally-move", g.Pending!.Kind);
        Pick(g, 1, "ally:0:2");
        Assert.Equal(1, g.Seat(1).PawnClearing);
        Assert.Equal(2, g.Warriors(0, 1));
        Assert.Equal(1, g.Warriors(0, 5));
        Assert.Equal(1, Vagabond.MoveCost(g, 1, 5));
    }

    [Fact]
    public void FullRemoval_AFavorCraftedInTheVagabondsClearing_DamagesThreeItems()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        var fox = g.ClearingIds.Where(c => g.SuitOf(c) == Suit.Fox).Take(3).ToList();
        foreach (var c in fox) Kit.Building(g, 0, c, PieceTypes.Workshop);
        Pawn(g, 1, fox[0]);
        Kit.Active(g, 0, TurnPhase.Daylight);
        var favor = Kit.Card("favor-of-the-foxes");
        g.Seat(0).Hand.Add(favor);
        Crafting.Craft(g, 0, favor);
        g.State.Pending = null; g.Advance();
        // Law 9.2.2.I: three items to damage (outside the Vagabond's turn the default order picks them)
        for (int i = 0; i < 3 && g.Pending?.Kind == "vagabond.hits"; i++) g.Choose(1, g.Pending.Options[0].Key);
        Assert.Equal(3, Vagabond.DamagedCount(g, 1));
    }

    // ------------------------------------------------------------------------------------------ battle

    [Fact]
    public void Battle_SwordsCapHits_HitsDamageItems_HostileThenInfamy()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        Kit.Warriors(g, 0, 5, 3);
        Kit.Building(g, 0, 5, PieceTypes.Workshop);
        Kit.Dice(g, 3, 2);
        Daylight(g, 1);
        Pick(g, 1, "battle:5:0");
        Assert.Equal("vagabond.hits", g.Pending!.Kind);                     // Marquise rolled 2: two items to damage
        Assert.Equal(2, g.Warriors(0, 5));                                  // 1 undamaged sword: 1 hit only
        Assert.Equal(Relationship.Hostile, Vagabond.Rel(g, 1, 0));
        Assert.Equal(0, g.Seat(1).Vp);                                      // the warrior that made them Hostile scores nothing
        Assert.Equal("damage:sword-exhausted", g.Pending.Options[0].Key);
        Pick(g, 1, "damage:tea");
        Pick(g, 1, "damage:torch");
        Assert.Equal(2, Vagabond.DamagedCount(g, 1));
        Assert.Equal(1, Count(g, 1, Sword, exhausted: true, damaged: false));

        AddItem(g, 1, Sword); AddItem(g, 1, Sword);
        Refresh(g);
        Kit.Dice(g, 3, 0);
        Pick(g, 1, "battle:5:0");
        Assert.Equal(0, g.Warriors(0, 5));
        Assert.Equal(0, g.BuildingsOf(0, 5));
        Assert.Equal(4, g.Seat(1).Vp);                                      // 2 warriors (Infamy) + workshop (1 + Infamy)
        Assert.Equal("vagabond.daylight", g.Pending!.Kind);
    }

    [Fact]
    public void Defender_WithoutUndamagedSwordIsDefenseless_DefaultDamagesFaceDownThenCheapItems_ExtraHitsIgnored()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        SetItems(g, 1, Boots, Torch);
        AddItem(g, 1, Tea, exhausted: true);
        Kit.Warriors(g, 0, 5, 2);
        Kit.Dice(g, 1, 0);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.True(Vagabond.Data(g, 1).Items.Single(i => i.Type == Tea).Damaged);     // face down first
        Assert.True(Vagabond.Data(g, 1).Items.Single(i => i.Type == Torch).Damaged);   // then torch before boots
        Assert.False(Vagabond.Data(g, 1).Items.Single(i => i.Type == Boots).Damaged);
        Assert.Contains(g.State.Log, e => e.Text.Contains("decided by the default"));

        Kit.Dice(g, 3, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.Equal(3, Vagabond.DamagedCount(g, 1));
        Assert.Contains(g.State.Log, e => e.Text.Contains("hits ignored"));
        Assert.Equal(Relationship.Indifferent, Vagabond.Rel(g, 1, 0));    // the Vagabond removed nothing
    }

    [Fact]
    public void Defender_RemovingAWarriorMakesTheAttackerHostile_BattleRecordsDamagedItems()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        Kit.Warriors(g, 0, 5, 2);
        Kit.Dice(g, 1, 1);
        Kit.Hold(g, 0);
        Battles.Start(g, 0, 1, 5);
        g.Advance();
        Assert.Equal(1, g.Warriors(0, 5));
        Assert.Equal(Relationship.Hostile, Vagabond.Rel(g, 1, 0));
        Assert.Equal(1, Vagabond.DamagedCount(g, 1));
        Assert.True(D(g, 1).Items.Single(i => i.Type == Tea).Damaged);     // default: cheapest face-up item
        Assert.Equal(0, g.Seat(1).Vp);
        Assert.Null(g.State.Battle);
    }

    [Fact]
    public void Battle_NoteBattleFillsDamagedBeforeAndAfter()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        AddItem(g, 1, Boots, damaged: true);
        g.State.Battle = new BattleState { Attacker = 0, Defender = 1, Clearing = 5 };
        Vagabond.NoteBattle(g, 1);
        D(g, 1).Items[0].Damaged = true;
        Vagabond.NoteBattle(g, 1);
        Assert.Equal(1, g.State.Battle.DamagedBefore[1]);
        Assert.Equal(2, g.State.Battle.DamagedAfter[1]);
    }

    [Fact]
    public void Battle_WithAlliedWarriors_TheyAddHitsAndMayTakeHits_TooManyMakesThemHostile()
    {
        var g = Kit.Table(1, "gustave", "maelle", "verso");
        Pawn(g, 1, 5);
        Pawn(g, 2, 5);
        D(g, 1).Relations[0] = Relationship.Allied;
        Kit.Warriors(g, 0, 5, 3);
        Kit.Dice(g, 3, 1);
        Daylight(g, 1);
        Assert.True(Offered(g, "battle:5:2:0"));
        Assert.False(Offered(g, "battle:5:0:0"));                           // never with the same faction
        Pick(g, 1, "battle:5:2:0");
        // 1 sword + 3 Allied warriors: 3 hits on the Ranger (all its undamaged items but one)
        Assert.Equal(3, Vagabond.DamagedCount(g, 2));
        Assert.Equal("vagabond.hits", g.Pending!.Kind);
        Pick(g, 1, "ally:0");
        Assert.Equal(2, g.Warriors(0, 5));
        Assert.Equal(Relationship.Hostile, Vagabond.Rel(g, 1, 0));          // 1 Allied hit > 0 item hits
        Assert.Null(D(g, 1).BattleAlly);
    }

    // ------------------------------------------------------------------------------------------ explore, aid, quest

    [Fact]
    public void Explore_TorchTakesTheRuinItem_OneVp_RuinRemoved()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 8);
        var item = g.Clearing(8).RuinItem!.Value;
        int slots = g.FreeSlots(8);
        Daylight(g, 1);
        Pick(g, 1, "explore");
        Assert.False(g.Clearing(8).Ruin);
        Assert.Null(g.Clearing(8).RuinItem);
        Assert.Equal(slots + 1, g.FreeSlots(8));
        Assert.Equal(1, g.Seat(1).Vp);
        Assert.Equal(1, Count(g, 1, Torch, exhausted: true));
        Assert.Contains(D(g, 1).Items, i => i.Type == item && !i.Exhausted);
        Assert.False(Offered(g, "explore"));
    }

    [Fact]
    public void Aid_GivesAMatchingCard_TakesACraftedItem_RelationshipOneTwoTwo_ThenTwoVpPerAid()
    {
        var g = Kit.Table(1, "gustave", "esquie");
        Pawn(g, 1, 5);                                                      // fox clearing
        SetItems(g, 1, Boots, Boots, Boots, Bag, Bag, Bag, Torch, Hammer);
        Kit.Warriors(g, 0, 5, 1);
        g.Seat(0).CraftedItems.Add(Sword);
        var fox = new[] { "anvil", "root-tea", "travel-gear", "protection-racket", "foxfolk-steel" }.Select(k => Give(g, 1, k, Suit.Fox)).ToList();
        fox.Add(Give(g, 1, "armorers"));                                    // a bird card is wild
        Give(g, 1, "cobbler");                                              // a rabbit card cannot be given here
        Daylight(g, 1);
        Assert.Contains("1 more Aid", g.Pending!.Find("aid:0")!.Text);
        Pick(g, 1, "aid:0");
        Assert.Equal(6, g.Pending!.Options.Count);
        Pick(g, 1, $"give:{fox[0]}");
        Pick(g, 1, "exhaust:boots");
        Pick(g, 1, "take:sword");
        Assert.Contains(fox[0], g.Seat(0).Hand);
        Assert.Empty(g.Seat(0).CraftedItems);
        Assert.Equal(1, Count(g, 1, Sword));
        Assert.Equal(Relationship.Amiable, Vagabond.Rel(g, 1, 0));
        Assert.Equal(1, g.Seat(1).Vp);

        void Aid(int card, string item)
        {
            Pick(g, 1, "aid:0");
            if (g.Pending!.Kind == "vagabond.aid" && Offered(g, $"give:{card}")) Pick(g, 1, $"give:{card}");
            Pick(g, 1, "exhaust:" + item);
        }
        Aid(fox[1], "boots");
        Assert.Equal(Relationship.Amiable, Vagabond.Rel(g, 1, 0));         // 1 of 2
        Aid(fox[2], "boots");
        Assert.Equal(Relationship.Friendly, Vagabond.Rel(g, 1, 0));
        Assert.Equal(3, g.Seat(1).Vp);

        g.State.Stack.Clear(); g.State.Pending = null;                     // a new turn: Aid counts restart
        Daylight(g, 1);
        Aid(fox[3], "bag");
        Assert.Equal(Relationship.Friendly, Vagabond.Rel(g, 1, 0));
        Aid(fox[4], "bag");
        Assert.Equal(Relationship.Allied, Vagabond.Rel(g, 1, 0));
        Assert.Equal(5, g.Seat(1).Vp);
        Aid(fox[5], "bag");
        Assert.Equal(7, g.Seat(1).Vp);                                     // Aiding an Allied faction: 2 VP
    }

    [Fact]
    public void Aid_HostileFactionOnlyForItems()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        D(g, 1).Relations[0] = Relationship.Hostile;
        Kit.Warriors(g, 0, 5, 1);
        int card = Give(g, 1, "anvil");
        Daylight(g, 1);
        Assert.Contains("Hostile", g.Pending!.Find("aid:0")!.Text);
        Pick(g, 1, "aid:0");
        Pick(g, 1, "exhaust:boots");
        Assert.Equal(Relationship.Hostile, Vagabond.Rel(g, 1, 0));
        Assert.Equal(0, g.Seat(1).Vp);
        Assert.Contains(card, g.Seat(0).Hand);
    }

    [Fact]
    public void Quest_MatchingSuit_ExhaustsTwoItems_VpPerCompletedQuestOfThatSuit_OrDrawTwo_NewQuestTurnedUp()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);                                                      // fox
        SetItems(g, 1, Torch, Hammer, Tea, Boots, Tea, Boots, Torch, Tea);
        Vagabond.QuestsUp(g).Clear();
        Vagabond.QuestsUp(g).AddRange(new[] { 3, 1, 5 });                  // Repair a Shed, Errand (fox); Guard Duty (rabbit)
        Vagabond.QuestDeck(g).Clear();
        Vagabond.QuestDeck(g).AddRange(new[] { 9, 4, 0 });                 // drawn from the end: Fundraising, Give a Speech, ...
        Daylight(g, 1);
        Assert.True(Offered(g, "quest:3"));
        Assert.True(Offered(g, "quest:1"));
        Assert.False(Offered(g, "quest:5"));
        Pick(g, 1, "quest:3");
        Assert.Equal("vagabond.quest", g.Pending!.Kind);
        Pick(g, 1, "reward:vp");
        Assert.Equal(1, g.Seat(1).Vp);
        Assert.Equal(new[] { 1, 5, 0 }, Vagabond.QuestsUp(g));
        Assert.Equal(new[] { 3 }, D(g, 1).Completed);
        Assert.Equal(1, Count(g, 1, Torch, exhausted: true));
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: true));
        Pick(g, 1, "quest:1");
        Pick(g, 1, "reward:vp");
        Assert.Equal(3, g.Seat(1).Vp);                                     // second fox quest: 2 VP
        Assert.Equal(new[] { 5, 0, 4 }, Vagabond.QuestsUp(g));
        Assert.True(Offered(g, "quest:4"));                                // Give a Speech: torch + tea
        Assert.False(Offered(g, "quest:0"));                               // Fundraising needs coins
    }

    [Fact]
    public void Quest_DrawTwoInsteadOfVp()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        SetItems(g, 1, Torch, Hammer);
        Vagabond.QuestsUp(g).Clear();
        Vagabond.QuestsUp(g).Add(3);
        Daylight(g, 1);
        Pick(g, 1, "quest:3");
        Pick(g, 1, "reward:draw");
        Assert.Equal(0, g.Seat(1).Vp);
        Assert.Equal(2, g.Seat(1).Hand.Count);
        Assert.Single(Vagabond.QuestsUp(g));                                // the completed quest is replaced from the deck
        Assert.Equal(11, Vagabond.QuestDeck(g).Count);
    }

    // ------------------------------------------------------------------------------------------ strike, repair, craft

    [Fact]
    public void Strike_RemovesAWarrior_ThenABuildingWhenNoWarriorsAreLeft_NoInfamyOutsideBattle()
    {
        var g = Kit.Table(1, "gustave", "verso");
        Pawn(g, 1, 5);
        AddItem(g, 1, Crossbow);
        Kit.Warriors(g, 0, 5, 1);
        Kit.Building(g, 0, 5, PieceTypes.Sawmill);
        Daylight(g, 1);
        Assert.False(Offered(g, "strike:0:building:sawmill"));
        Pick(g, 1, "strike:0:warrior");
        Assert.Equal(0, g.Warriors(0, 5));
        Assert.Equal(Relationship.Hostile, Vagabond.Rel(g, 1, 0));
        Assert.Equal(0, g.Seat(1).Vp);
        Pick(g, 1, "strike:0:building:sawmill");
        Assert.Equal(0, g.BuildingsOf(0, 5));
        Assert.Equal(1, g.Seat(1).Vp);                                     // the building's point, no Infamy (not in battle)
        Assert.Equal(2, Count(g, 1, Crossbow, exhausted: true));
    }

    [Fact]
    public void Repair_HammerMovesADamagedItemBack_KeepingItsSide()
    {
        var g = Kit.Table(1, "gustave", "esquie");
        Pawn(g, 1, 5);
        AddItem(g, 1, Sword, exhausted: true, damaged: true);
        Daylight(g, 1);
        Pick(g, 1, "repair:sword-damaged-exhausted");
        var sword = D(g, 1).Items.Single(i => i.Type == Sword);
        Assert.False(sword.Damaged);
        Assert.True(sword.Exhausted);
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: true));
    }

    [Fact]
    public void Craft_HammersMatchTheClearing_ItemComesFaceUp_MultiSuitCardsCannotBeCrafted()
    {
        var g = Kit.Table(1, "gustave", "esquie");
        Pawn(g, 1, 5);                                                      // fox clearing, one hammer
        int anvil = Give(g, 1, "anvil");
        int tax = Give(g, 1, "tax-collector");
        Give(g, 1, "travel-gear", Suit.Fox);                               // costs a rabbit: not here
        Daylight(g, 1);
        Pick(g, 1, "craft");
        Assert.Equal(new[] { $"craft:{anvil}", "done" }, g.Pending!.Options.Where(o => !o.Key.StartsWith("card:")).Select(o => o.Key));
        Pick(g, 1, $"craft:{anvil}");
        Assert.Equal(2, g.Seat(1).Vp);
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: true));
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: false));
        Assert.DoesNotContain(ItemType.Hammer, g.State.ItemSupply);
        Assert.Contains(tax, g.Seat(1).Hand);
        Assert.Empty(g.Seat(1).CraftedItems);
    }

    // ------------------------------------------------------------------------------------------ special actions

    [Fact]
    public void Thief_Steal_TakesARandomCardFromAPlayerInTheClearing()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        Kit.Warriors(g, 0, 5, 1);
        Give(g, 0, "anvil"); Give(g, 0, "cobbler");
        Daylight(g, 1);
        Pick(g, 1, "steal:0");
        Assert.Single(g.Seat(0).Hand);
        Assert.Single(g.Seat(1).Hand);
        Assert.Equal(1, Count(g, 1, Torch, exhausted: true));
        Assert.Contains(g.State.Log, e => e.Only == 1 && e.Text.StartsWith("You stole"));
    }

    [Fact]
    public void Tinker_DayLabor_TakesAMatchingOrBirdCardFromTheDiscard()
    {
        var g = Kit.Table(1, "gustave", "esquie");
        Pawn(g, 1, 5);
        int anvil = ToDiscard(g, "anvil"), cobbler = ToDiscard(g, "cobbler"), armorers = ToDiscard(g, "armorers");
        Daylight(g, 1);
        Pick(g, 1, "day-labor");
        Assert.Equal(new[] { $"take:{anvil}", $"take:{armorers}" }, g.Pending!.Options.Select(o => o.Key));
        Pick(g, 1, $"take:{anvil}");
        Assert.Contains(anvil, g.Seat(1).Hand);
        Assert.DoesNotContain(anvil, g.State.Discard);
        Assert.Contains(cobbler, g.State.Discard);
    }

    [Fact]
    public void Ranger_Hideout_RepairsThree_ThenDaylightEnds()
    {
        var g = Kit.Table(1, "gustave", "verso");
        PawnInForest(g, 1, 2);
        SetItems(g, 1, Torch);
        foreach (var t in new[] { Boots, Crossbow, Sword, Hammer }) AddItem(g, 1, t, damaged: true);
        Daylight(g, 1);
        Pick(g, 1, "hideout");
        Pick(g, 1, "repair:boots-damaged");
        Pick(g, 1, "repair:sword-damaged");
        Pick(g, 1, "repair:crossbow-damaged");
        Assert.Equal(1, Vagabond.DamagedCount(g, 1));
        Assert.True(D(g, 1).Items.Single(i => i.Type == Hammer).Damaged);
        Assert.Equal("test.hold", g.Pending!.Kind);                          // Daylight is over
    }

    // ------------------------------------------------------------------------------------------ Evening

    [Fact]
    public void Evening_RestInAForest_DrawOnePlusCoins_ItemLimitSixPlusTwoPerBag()
    {
        var g = Kit.Table(1, "gustave", "verso");
        PawnInForest(g, 1, 2);
        SetItems(g, 1, Coins, Coins, Boots, Boots, Boots, Boots, Sword, Sword, Torch);
        AddItem(g, 1, Hammer, exhausted: true, damaged: true);
        Assert.Equal(8, Vagabond.CountedItems(g, 1));                     // coins on their track do not count
        Run(g, 1, TurnPhase.Evening, "vagabond.evening");
        Assert.Equal(0, Vagabond.DamagedCount(g, 1));
        Assert.Equal(1, Count(g, 1, Hammer, exhausted: false));            // rested: repaired and face up
        Assert.Equal(3, g.Seat(1).Hand.Count);                              // 1 + 2 coins
        Assert.Equal("vagabond.evening", g.Pending!.Kind);
        Assert.False(Offered(g, "drop:coins"));                             // track items are not in the Satchel
        Pick(g, 1, "drop:boots");
        Pick(g, 1, "drop:boots");
        Assert.Equal(6, Vagabond.CountedItems(g, 1));
        Assert.Equal("test.hold", g.Pending!.Kind);
    }

    [Fact]
    public void ItemLimit_BagsOnTheirTrack_AndTracksHoldThree()
    {
        var g = Kit.Table(1, "gustave", "esquie");
        SetItems(g, 1, Bag, Bag, Bag, Bag, Tea, Tea, Tea, Tea);
        Assert.Equal(12, Vagabond.ItemLimit(g, 1));
        Assert.Equal(3, Vagabond.OnTrack(g, 1, Tea));
        Assert.Equal(2, Vagabond.CountedItems(g, 1));                     // the fourth bag and tea are in the Satchel
        D(g, 1).Items[0].Exhausted = true;
        Assert.Equal(12, Vagabond.ItemLimit(g, 1));                         // the fourth bag moves onto the track
        D(g, 1).Items[1].Exhausted = true;
        Assert.Equal(10, Vagabond.ItemLimit(g, 1));
    }

    // ------------------------------------------------------------------------------------------ coalition, save

    [Fact]
    public void Coalition_FourPlayers_TenVp_WithTheFewestVpPlayer_HostileBecomesIndifferent()
    {
        var g = Kit.Table(1, "gustave", "paintress", "sciel", "maelle");
        Pawn(g, 3, 5);
        g.Seat(0).Vp = 5; g.Seat(1).Vp = 3; g.Seat(2).Vp = 3; g.Seat(3).Vp = 12;
        int dom = Give(g, 3, "dominance", Suit.Fox);
        D(g, 3).Relations[2] = Relationship.Hostile;
        Daylight(g, 3);
        Assert.False(Offered(g, $"card:dominance:{dom}"));                  // no normal activation
        Assert.True(Offered(g, $"open:coalition:{dom}:1"));
        Assert.False(Offered(g, $"open:coalition:{dom}:0"));
        Pick(g, 3, $"open:coalition:{dom}:2");
        Assert.Equal(2, g.Seat(3).CoalitionWith);
        Assert.Equal(dom, g.Seat(3).Dominance);
        Assert.Contains(dom, g.Seat(3).PlayArea);
        Assert.Equal(Relationship.Indifferent, Vagabond.Rel(g, 3, 2));
        g.AddVp(3, 2, "test");
        Assert.Equal(12, g.Seat(3).Vp);
        Assert.Equal(new[] { 2, 3 }, g.WithPartners(2));
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("open:coalition"));
    }

    [Fact]
    public void Coalition_NotInThreePlayerGames()
    {
        var g = Kit.Table(1, "gustave", "paintress", "maelle");
        Pawn(g, 2, 5);
        g.Seat(2).Vp = 12;
        Give(g, 2, "dominance", Suit.Fox);
        Daylight(g, 2);
        Assert.DoesNotContain(g.Pending!.Options, o => o.Key.StartsWith("open:"));
    }

    [Fact]
    public void SaveLoad_KeepsItemsRelationshipsAndQuests()
    {
        var g = Kit.Table(1, "gustave", "maelle");
        Pawn(g, 1, 5);
        AddItem(g, 1, Hammer, exhausted: true, damaged: true);
        D(g, 1).Relations[0] = Relationship.Friendly;
        D(g, 1).Completed.Add(7);
        var h = SimGame.FromJson(g.ToJson());
        Assert.Equal(Relationship.Friendly, Vagabond.Rel(h, 1, 0));
        Assert.Equal(new[] { 7 }, D(h, 1).Completed);
        Assert.Equal(Vagabond.QuestsUp(g), Vagabond.QuestsUp(h));
        Assert.Contains(D(h, 1).Items, i => i.Type == Hammer && i.Exhausted && i.Damaged);
        Assert.Equal(5, h.Seat(1).PawnClearing);
        Assert.Equal(g.ToJson(), h.ToJson());
    }

    [Fact]
    public void BoardAndRulesText_DescribeTheVagabond()
    {
        var g = Kit.Table(1, "gustave", "verso");
        PawnInForest(g, 1, 2);
        var board = g.Faction(1).BoardText(g, 1);
        Assert.Contains("forest 2", board);
        Assert.Contains("face-up quests", board);
        Assert.Contains("Indifferent", board);
        Assert.Contains("Hideout", g.Faction(1).RulesText(g, 1));
        Assert.Equal(4, g.Faction(1).Items(g, 1).Count);
    }

    [Fact]
    public void BotGamesWithVagabonds_ScoreAndNeverGetStuck()
    {
        int vagabondVp = 0;
        foreach (var (table, seed) in new[] { (new[] { "maelle", "gustave", "verso" }, 5), (new[] { "esquie", "gustave", "paintress", "sciel" }, 6), (new[] { "verso", "gustave" }, 7) })
        {
            var g = SimGame.New(table.Select(c => new SimGame.SeatSpec(c, false)).ToList(), seed);
            Bots.Run(g, maxDecisions: 200_000, roundCap: 20);
            Assert.True(g.State.GameOver || g.Pending != null);
            vagabondVp += g.State.Seats.Where(s => s.Faction == "vagabond").Sum(s => s.Vp);
            foreach (var s in g.SeatsOf("vagabond"))
                Assert.True(g.Seat(s).PawnClearing != null ^ g.Seat(s).PawnForest != null);
        }
        Assert.True(vagabondVp > 0);
    }
}
