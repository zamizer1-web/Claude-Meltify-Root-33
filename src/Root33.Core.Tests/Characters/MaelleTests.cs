using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Maelle;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Every test the Maelle rows name: win.maelle, tweak.maelle.stance-chip and bot.maelle's rules.</summary>
    public class MaelleTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);
        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Maelle, () => new MaelleModule());

        private static TestTable Start(FakeBoard b, int act = 2, Difficulty d = Difficulty.Normal)
        {
            var t = TestTable.Start(b, Registry(), d);
            if (act != 1)
            {
                t.Ctx.ActClock.Restore(act);
                t.State.GlobalAct = t.Ctx.ActClock.Act;
            }
            return t;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));

        /// <summary>Maelle (seat 0, Thief) Allied with a Marquise (1) and an Eyrie (2), pawn in Lumière (1), three
        /// undamaged items (one exhausted) and 10 VP.</summary>
        private static FakeBoard WinBoard(bool bot = false) => FakeBoard.Autumn()
            .Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot)
            .Seat(1, EngineId.Marquise).Seat(2, EngineId.Eyrie)
            .Relationship(0, 1, Relationship.Allied).Relationship(0, 2, Relationship.Allied)
            .Pawn(0, "autumn.01").Item(0, ItemType.Sword).Item(0, ItemType.Boots, exhausted: true).Item(0, ItemType.Hammer)
            .Vp(0, 10);

        private static bool Announces(FakeBoard b, int act = 2) => WinRules.AnnouncePasses(Start(b, act).Ctx, S(0), new MaelleWin());

        private static bool Won(FakeBoard b) => b.Winners.Any(w => w.Key == S(0) && w.Value == Ids.WinConditions.Maelle);

        // ================================================================= win.maelle

        [Fact]
        public void MaelleWin_AnnouncesWithTwoAlliedInLumiere()
        {
            var b = WinBoard();
            var t = Start(b);
            Assert.True(new MaelleWin().Announce(t.Ctx, S(0)));
            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Maelle, b.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            t.PlayTurn(1).PlayTurn(2).PlayTurn(0);
            Assert.True(Won(b));

            // Gates: Act I, 9 VP, a coalition partner. Themed parts: pawn outside Lumière, only 2 undamaged items.
            var act1 = WinBoard();
            Start(act1, 1).PlayTurn(0);
            Assert.False(act1.Banners.ContainsKey(S(0)));
            Assert.False(Announces(WinBoard().Vp(0, 9)));
            Assert.False(Announces(WinBoard().Coalition(0, 1)));
            Assert.False(Announces(WinBoard().Pawn(0, "autumn.05")));
            Assert.False(Announces(WinBoard().ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer, damaged: true)));
            Assert.False(Announces(WinBoard().Relationship(0, 2, Relationship.Friendly)));
        }

        [Fact]
        public void MaelleWin_ConfirmFailsWhenItemsDamagedBelowThree()
        {
            var b = WinBoard();
            var t = Start(b);
            t.PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            // Counterplay: attacked in Lumière, two items damaged before her Birdsong.
            t.PlayTurn(1, x => b.ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots, damaged: true).Item(0, ItemType.Hammer, damaged: true));
            t.PlayTurn(2).PlayTurn(0);
            Assert.False(Won(b));
            Assert.False(b.Banners.ContainsKey(S(0)));
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);
        }

        [Fact]
        public void MaelleWin_SingleFactionNeedsFifteenVp()
        {
            FakeBoard Two(int vp) => FakeBoard.Autumn()
                .Seat(0, EngineId.VagabondThief, CharacterIds.Maelle).Seat(1, EngineId.Marquise)
                .Relationship(0, 1, Relationship.Allied).Pawn(0, "autumn.01")
                .Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer).Vp(0, vp);
            Assert.False(Announces(Two(12)));
            Assert.True(Announces(Two(15)));
            var b = Two(15);
            Start(b).PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Maelle, b.Banners[S(0)]);

            // Three seats with two Vagabonds: still one non-Vagabond faction.
            Assert.False(Announces(Two(12).Seat(2, EngineId.VagabondRanger)));
            Assert.True(Announces(Two(15).Seat(2, EngineId.VagabondRanger)));
        }

        [Fact]
        public void MaelleWin_BlockedByAnyHostile()
        {
            Assert.True(Announces(WinBoard().Seat(3, EngineId.Alliance)));
            Assert.False(Announces(WinBoard().Seat(3, EngineId.Alliance).Relationship(0, 3, Relationship.Hostile)));
            Assert.False(Announces(WinBoard().Relationship(0, 2, Relationship.Hostile)));
        }

        // ================================================================= tweak.maelle.stance-chip

        private static BattleResult Battle(int attacker, int defender) =>
            new BattleResult { Clearing = C(1), Attacker = S(attacker), Defender = S(defender) };

        [Fact]
        public void StanceChip_OffensiveAfterBattle()
        {
            var b = WinBoard();
            var t = Start(b, 1);
            Assert.Equal(MaelleTweaks.None, t.State.MaelleStance);
            t.PlayTurn(1, x => x.Game.OnBattleResolved(Battle(1, 2)));
            Assert.Equal(MaelleTweaks.None, t.State.MaelleStance);
            t.PlayTurn(0, x => x.Game.OnBattleResolved(Battle(0, 1)));
            Assert.Equal(MaelleTweaks.Offensive, t.State.MaelleStance);
            Assert.True(b.OverlayShown(Ids.Overlays.MaelleStance));
            Assert.Contains(b.Bubbles, x => x.Key == S(0) && x.Value == Ids.Barks.Maelle04);
            Assert.Empty(t.State.MaelleStanceTurn);
            Assert.Equal(MaelleTweaks.Offensive, t.SaveAndLoad(Registry()).State.MaelleStance);
        }

        [Fact]
        public void StanceChip_DefensiveAfterAidOrAttacked()
        {
            var t = Start(WinBoard(), 1);
            t.Game.OnAidResolved(S(0), S(1), C(1));
            Assert.Equal(MaelleTweaks.Defensive, t.State.MaelleStance);

            var t2 = Start(WinBoard(), 1);
            t2.PlayTurn(0, x => x.Game.OnBattleResolved(Battle(0, 1)));
            Assert.Equal(MaelleTweaks.Offensive, t2.State.MaelleStance);
            t2.PlayTurn(1, x => x.Game.OnBattleResolved(Battle(1, 0)));
            Assert.Equal(MaelleTweaks.Defensive, t2.State.MaelleStance);
        }

        [Fact]
        public void StanceChip_VirtuoseAfterBattleAndAidSameTurn()
        {
            var t = Start(WinBoard(), 1);
            t.PlayTurn(0, x =>
            {
                x.Game.OnBattleResolved(Battle(0, 1));
                x.Game.OnAidResolved(S(0), S(2), C(1));
            });
            Assert.Equal(MaelleTweaks.Virtuose, t.State.MaelleStance);
            t.PlayTurn(1).PlayTurn(2);
            Assert.Equal(MaelleTweaks.Virtuose, t.State.MaelleStance);
            // The record reset at the end of her turn: a battle alone next turn is Offensive.
            t.PlayTurn(0, x => x.Game.OnBattleResolved(Battle(0, 1)));
            Assert.Equal(MaelleTweaks.Offensive, t.State.MaelleStance);
        }

        // ================================================================= bot.maelle

        private static FakeBoard BotBoard() => FakeBoard.Autumn()
            .Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot: true)
            .Seat(1, EngineId.Marquise).Seat(2, EngineId.Eyrie).Seat(3, EngineId.Alliance);

        private static HashSet<SeatId> Seats(params int[] s) => new HashSet<SeatId>(s.Select(S));

        [Fact]
        public void MaelleBot_PicksTwoNonHostileClosestToAllied()
        {
            Assert.Empty(ModuleRegistry.MissingBotRules(new MaelleBot()));
            var b = BotBoard().Relationship(0, 1, Relationship.Friendly).Vp(1, 5)
                .Relationship(0, 2, Relationship.Amiable).Vp(2, 3)
                .Relationship(0, 3, Relationship.Hostile).Vp(3, 8);
            var t = Start(b, 1);
            Assert.Equal(S(1), new MaelleBot().Family(View(t))[0].Item);
            t.PlayTurn(0);
            Assert.Equal(Seats(1, 2), t.State.MaelleFamily);
        }

        [Fact]
        public void MaelleBot_AvoidsVpLeaderInFamily()
        {
            var b = BotBoard().Relationship(0, 1, Relationship.Allied).Vp(1, 12)
                .Relationship(0, 2, Relationship.Friendly).Vp(2, 4).Vp(3, 2);
            var t = Start(b, 1);
            t.PlayTurn(0);
            Assert.Equal(Seats(2, 3), t.State.MaelleFamily);

            // The leader is still picked when it is the only candidate.
            var two = FakeBoard.Autumn().Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot: true).Seat(1, EngineId.Marquise).Vp(1, 12);
            var t2 = Start(two, 1);
            t2.PlayTurn(0);
            Assert.Equal(Seats(1), t2.State.MaelleFamily);
        }

        [Fact]
        public void MaelleBot_RepicksOnlyPastTwentyTwoVp()
        {
            var b = BotBoard().Vp(1, 15).Relationship(0, 2, Relationship.Friendly).Vp(2, 10)
                .Relationship(0, 3, Relationship.Amiable).Vp(3, 4);
            var t = Start(b, 1);
            t.PlayTurn(0);
            Assert.Equal(Seats(2, 3), t.State.MaelleFamily);
            // Seat 1 is now the closest to Allied, but nobody has passed 22 VP: she keeps her family.
            b.Relationship(0, 1, Relationship.Allied).Vp(1, 12).Vp(2, 22);
            t.PlayTurn(0);
            Assert.Equal(Seats(2, 3), t.State.MaelleFamily);
            // Seat 2 passes 22 VP (and leads): she re-picks.
            b.Vp(2, 23);
            t.PlayTurn(0);
            Assert.Equal(Seats(1, 3), t.State.MaelleFamily);
        }

        /// <summary>Family {1 Friendly, 2 Amiable}; seat 1 at 5 (fox) and 6 (mouse), seat 2 at 12 (mouse) and 9 (fox);
        /// a fox card in hand; her pawn at 8.</summary>
        private static (FakeBoard, TestTable) AidTable(Difficulty d = Difficulty.Normal)
        {
            var b = BotBoard().Relationship(0, 1, Relationship.Friendly).Relationship(0, 2, Relationship.Amiable)
                .Warriors(1, 5, 2).Warriors(1, 6, 2).Warriors(2, 12, 2).Warriors(2, 9, 2)
                .Hand(0, Suit.Fox).Pawn(0, "autumn.08");
            var t = Start(b, 1, d);
            t.State.MaelleFamily = Seats(1, 2);
            return (b, t);
        }

        [Fact]
        public void MaelleBot_AidsFamilyInMatchingSuitClearing()
        {
            var (b, t) = AidTable();
            var bot = new MaelleBot();
            var top = bot.Aid(View(t))[0].Item;
            Assert.Equal(S(2), top.Faction);
            Assert.Equal(C(9), top.Clearing);
            Assert.DoesNotContain(bot.Aid(View(t)), r => r.Item.Clearing == C(12));
            Assert.Equal("move:8:7", bot.ChooseAction(View(t), new[] { "move:8:12", "move:8:6", "move:8:7" }));
            b.Pawn(0, "autumn.09");
            Assert.Equal("aid:9:2", bot.ChooseAction(View(t), new[] { "move:9:7", "aid:9:2" }));
        }

        [Fact]
        public void MaelleBot_TakesSwordsAndBootsFirst()
        {
            Assert.Equal(new[] { ItemType.Sword, ItemType.Boots, ItemType.Hammer },
                MaelleBot.ItemPreference(new[] { ItemType.Hammer, ItemType.Boots, ItemType.Sword }).Select(r => r.Item).ToArray());
            var (b, t) = AidTable();
            b.Pawn(0, "autumn.09").Crafted(2, ItemType.Hammer, ItemType.Boots, ItemType.Sword);
            var bot = new MaelleBot();
            Assert.Equal("aid:9:2:sword", bot.ChooseAction(View(t), new[] { "aid:9:2:hammer", "aid:9:2:boots", "aid:9:2:sword" }));
            Assert.Equal("aid:9:2:boots", bot.ChooseAction(View(t), new[] { "aid:9:2:hammer", "aid:9:2:boots" }));
            Assert.Equal("take:boots", bot.ChooseAction(View(t), new[] { "take:hammer", "take:boots" }));
        }

        [Fact]
        public void MaelleBot_EasyAidsOncePerTurn()
        {
            var bot = new MaelleBot();
            var (b, t) = AidTable(Difficulty.Easy);
            b.Pawn(0, "autumn.09");
            Assert.Equal("aid:9:2", bot.ChooseAction(View(t), new[] { "aid:9:2" }));
            t.Game.OnAidResolved(S(0), S(2), C(9));
            Assert.Empty(bot.Aid(View(t)));
            Assert.Null(bot.ChooseAction(View(t), new[] { "aid:9:2" }));

            var (nb, nt) = AidTable();
            nb.Pawn(0, "autumn.09");
            nt.Game.OnAidResolved(S(0), S(2), C(9));
            Assert.Equal("aid:9:2", bot.ChooseAction(View(nt), new[] { "aid:9:2" }));
        }

        [Fact]
        public void MaelleBot_NoFightsWithNonHostileUnlessNearWin()
        {
            var bot = new MaelleBot();
            var b = BotBoard().Relationship(0, 1, Relationship.Friendly).Vp(1, 20).Warriors(1, 5, 2).Pawn(0, "autumn.05");
            var t = Start(b, 1);
            var legal = new[] { "battle:5:1", "pass" };
            Assert.False(bot.MayBattle(View(t), S(1)));
            Assert.NotEqual("battle:5:1", bot.ChooseAction(View(t), legal));
            b.Vp(1, 30 - Tuning.MaelleThreatVp);
            Assert.True(bot.MayBattle(View(t), S(1)));
            Assert.Equal("battle:5:1", bot.ChooseAction(View(t), legal));
            b.Vp(1, 3).Relationship(0, 1, Relationship.Hostile);
            Assert.True(bot.MayBattle(View(t), S(1)));
        }

        [Fact]
        public void MaelleBot_DisarmsVpLeaderToRefillHand()
        {
            var bot = new MaelleBot();
            var b = BotBoard().Pawn(0, "autumn.05")
                .Warriors(1, 5, 1).Vp(1, 10).Hand(1, Suit.Fox, Suit.Fox)
                .Warriors(2, 6, 1).Vp(2, 10).Hand(2, Suit.Fox, Suit.Fox, Suit.Mouse, Suit.Rabbit)
                .Warriors(3, 12, 1).Vp(3, 20).Hand(3, Suit.Fox, Suit.Fox, Suit.Fox);
            var t = Start(b, 1);
            var ranking = bot.NoFights(View(t));
            Assert.Equal(S(2), ranking[0].Item);
            Assert.DoesNotContain(ranking, r => r.Item == S(3));
            Assert.Equal("move:5:6", bot.ChooseAction(View(t), new[] { "steal:5:1", "move:5:1", "move:5:6" }));
            b.Pawn(0, "autumn.06");
            Assert.Equal("steal:6:2", bot.ChooseAction(View(t), new[] { "steal:6:2", "move:6:5" }));
        }

        private static FakeBoard GoalBoard() => BotBoard()
            .Relationship(0, 1, Relationship.Allied).Relationship(0, 2, Relationship.Allied)
            .Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer).Vp(0, 10).Pawn(0, "autumn.05");

        [Fact]
        public void MaelleBot_GoalModeHeadsToLumiereAndStays()
        {
            var bot = new MaelleBot();
            var b = GoalBoard();
            var t = Start(b);
            Assert.True(bot.InGoalMode(View(t)));
            Assert.Equal(C(1), bot.GoalMode(View(t)).ToClearing);
            Assert.Equal("move:5:1", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1" }));
            b.Pawn(0, "autumn.01");
            Assert.True(bot.GoalMode(View(t)).Stay);
            Assert.Equal("pass", bot.ChooseAction(View(t), new[] { "move:1:5", "move:1:8", "pass" }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:1:5", "move:1:8" }));
            Assert.False(bot.InGoalMode(View(Start(GoalBoard().Relationship(0, 3, Relationship.Hostile)))));
        }

        [Fact]
        public void MaelleBot_RepairsInForestTurnBeforeArriving()
        {
            var bot = new MaelleBot();
            var b = GoalBoard().ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer, damaged: true);
            var t = Start(b);
            Assert.Equal(5, bot.GoalMode(View(t)).ToForest);
            Assert.Equal("slip:f5", bot.ChooseAction(View(t), new[] { "move:5:1", "slip:f5" }));
            b.PawnInForest(0, 5);
            Assert.True(bot.GoalMode(View(t)).Rest);
            Assert.Equal("rest", bot.ChooseAction(View(t), new[] { "move:f5:1", "rest" }));
            b.ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer);
            Assert.Equal("move:f5:1", bot.ChooseAction(View(t), new[] { "move:f5:1", "rest" }));
        }

        [Fact]
        public void MaelleBot_EasySkipsRepairBeforeArriving()
        {
            var bot = new MaelleBot();
            var b = GoalBoard().ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer, damaged: true);
            var t = Start(b, 2, Difficulty.Easy);
            Assert.Equal(C(1), bot.GoalMode(View(t)).ToClearing);
            Assert.Equal("move:5:1", bot.ChooseAction(View(t), new[] { "move:5:1", "slip:f5" }));
        }

        [Fact]
        public void MaelleBot_OneFactionTablePicksOneFamilyAndPushesToFifteenVp()
        {
            var bot = new MaelleBot();
            var b = FakeBoard.Autumn().Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot: true)
                .Seat(1, EngineId.Marquise).Seat(2, EngineId.VagabondRanger)
                .Relationship(0, 1, Relationship.Friendly).Pawn(0, "autumn.05")
                .Item(0, ItemType.Sword).Item(0, ItemType.Boots).Item(0, ItemType.Hammer);
            var t = Start(b);
            Assert.True(bot.TwoPlayer(View(t)));
            t.PlayTurn(0);
            Assert.Equal(Seats(1), t.State.MaelleFamily);

            b.Relationship(0, 1, Relationship.Allied).Vp(0, 12);
            Assert.True(bot.InGoalMode(View(t)));
            Assert.False(bot.GoalModeActive(View(t)));
            Assert.Equal("explore:5", bot.ChooseAction(View(t), new[] { "move:5:1", "explore:5" }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:5:1", "move:5:6" }));
            b.Vp(0, Tuning.Maelle2pVp);
            Assert.True(bot.GoalModeActive(View(t)));
            Assert.Equal("move:5:1", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1" }));
        }

        [Fact]
        public void MaelleBot_SpoilerFightsNonHostileOnlyNearWin()
        {
            var bot = new MaelleBot();
            var b = FakeBoard.Autumn().Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot: true)
                .Seat(1, EngineId.Eyrie, CharacterIds.Paintress).Seat(2, EngineId.Marquise)
                .Building(1, 3, PieceTypes.Roost).Warriors(1, 3, 2).Relationship(0, 1, Relationship.Friendly).Vp(1, 20)
                .Pawn(0, "autumn.03");
            var t = Start(b);
            t.State.GlobalCountdowns.Add(S(1));
            var legal = new[] { "battle:3:1", "steal:3:1" };
            Assert.Equal("steal:3:1", bot.Spoiler(View(t), legal).ActionKey);
            Assert.Equal("steal:3:1", bot.ChooseAction(View(t), legal));
            b.Vp(1, 30 - Tuning.MaelleThreatVp);
            Assert.Equal("battle:3:1", bot.ChooseAction(View(t), legal));
            b.Vp(1, 20).Relationship(0, 1, Relationship.Hostile);
            Assert.Equal("battle:3:1", bot.ChooseAction(View(t), legal));
        }

        [Fact]
        public void MaelleBot_SpoilerWalksIntoMonolithAgainstWhiteHairedMan()
        {
            FakeBoard Board() => FakeBoard.Autumn().Seat(0, EngineId.VagabondThief, CharacterIds.Maelle, bot: true)
                .Seat(1, EngineId.Marquise).Seat(2, EngineId.Corvids, CharacterIds.WhiteHairedMan)
                .Relationship(0, 1, Relationship.Friendly).Warriors(1, 9, 2).Hand(0, Suit.Fox).Pawn(0, "autumn.09");
            var legal = new[] { "aid:9:1", "move:9:7", "move:9:3" };
            var bot = new MaelleBot();
            foreach (var d in new[] { Difficulty.Normal, Difficulty.Hard, Difficulty.Easy })
            {
                var t = Start(Board(), 2, d);
                t.State.MaelleFamily = Seats(1);
                Assert.Equal("aid:9:1", bot.ChooseAction(View(t), legal));
                t.State.GlobalCountdowns.Add(S(2));
                var answer = bot.Spoiler(View(t), legal);
                Assert.Equal("move:9:3", answer.ActionKey);
                // Easy answers a banner only with the actions the plan leaves free.
                Assert.Equal(d == Difficulty.Easy ? "aid:9:1" : "move:9:3", bot.ChooseAction(View(t), legal));
            }
        }
    }
}
