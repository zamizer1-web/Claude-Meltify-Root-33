using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Paintress;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Characters
{
    /// <summary>The Paintress: win.paintress, tweak.paintress.*, state.paintress.*, bot.paintress and its rules.
    /// Autumn: the Monolith is 3 (mouse); fox 4, 5, 8, 9; rabbit 1, 2, 10, 11; mouse 3, 6, 7, 12.</summary>
    public class PaintressTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Paintress, () => new PaintressModule());

        /// <summary>Gustave's Marquise at seat 0 (no module: plain Root rules), the Paintress's Eyrie at seat 1.</summary>
        private static FakeBoard Table(bool bot = false) =>
            FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: bot)
                .SupplyOf(1, PieceTypes.Roost, 5);

        /// <summary>The Monolith held with an easel and 3 warriors, and Living Portraits in fox 9, rabbit 11 and mouse 7.</summary>
        private static FakeBoard Family(FakeBoard b, int vp = 10)
        {
            b.Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
             .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 2)
             .Building(1, 11, PieceTypes.Roost).Warriors(1, 11, 2)
             .Building(1, 7, PieceTypes.Roost).Warriors(1, 7, 2)
             .Vp(1, vp);
            return b;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(1));

        // ================================================================= win.paintress

        [Fact]
        public void PaintressWin_AnnouncesWithThreeSuitsAndMonolith()
        {
            var board = Family(Table());
            var t = TestTable.Start(board, Registry());
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            t.PlayTurn(1);
            Assert.Equal(Ids.WinConditions.Paintress, board.Banners[S(1)]);
            Assert.Contains(S(1), t.State.GlobalCountdowns);
            Assert.Empty(board.Winners);

            t.PlayTurn(0).PlayTurn(1);
            Assert.Contains(new KeyValuePair<SeatId, string>(S(1), Ids.WinConditions.Paintress), board.Winners);

            // Two suits are not enough.
            var two = Table();
            Family(two).RemoveBuilding(1, "7", PieceTypes.Roost);
            var t2 = TestTable.Start(two, Registry());
            t2.PlayTurn(0).PlayTurn(1);
            Assert.False(two.Banners.ContainsKey(S(1)));
        }

        [Fact]
        public void PaintressWin_ConfirmFailsAfterPortraitDropsBelowTwoWarriors()
        {
            var board = Family(Table());
            var t = TestTable.Start(board, Registry());
            t.PlayTurn(0).PlayTurn(1);
            Assert.Contains(S(1), t.State.GlobalCountdowns);

            t.PlayTurn(0, during: x => x.Board.Warriors(1, 7, 1)); // a battle thins the mouse Portrait to 1 warrior
            Assert.True(board.Ruler(C(7)) == S(1));                // she still rules it, but it is no Living Portrait
            t.PlayTurn(1);
            Assert.Empty(board.Winners);
            Assert.False(board.Banners.ContainsKey(S(1)));
            Assert.DoesNotContain(S(1), t.State.GlobalCountdowns);
            Assert.Contains("ClearBanner seat1", board.Log);
        }

        [Fact]
        public void PaintressWin_BlockedWithActiveDominance()
        {
            var board = Family(Table()).Vp(0, 10).Dominance(1, Suit.Fox);
            var t = TestTable.Start(board, Registry());
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            t.PlayTurn(1);
            Assert.False(board.Banners.ContainsKey(S(1)));
            Assert.Empty(t.State.GlobalCountdowns);

            board.Dominance(1, null);
            t.PlayTurn(0).PlayTurn(1);
            Assert.Equal(Ids.WinConditions.Paintress, board.Banners[S(1)]);
        }

        [Fact]
        public void PaintressWin_LordsOfTheForestTieCountsAsRule()
        {
            // Fox Portrait 9: her roost + 2 warriors (3) against Gustave's 3 warriors: a tie the Eyrie rules.
            var board = Family(Table()).Warriors(0, 9, 3);
            Assert.Equal(S(1), board.Ruler(C(9)));
            var t = TestTable.Start(board, Registry());
            t.PlayTurn(0).PlayTurn(1);
            Assert.Equal(Ids.WinConditions.Paintress, board.Banners[S(1)]);

            // One more warrior and Gustave rules it: the fox Portrait is gone.
            var lost = Family(Table()).Warriors(0, 9, 4);
            var t2 = TestTable.Start(lost, Registry());
            t2.PlayTurn(0).PlayTurn(1);
            Assert.False(lost.Banners.ContainsKey(S(1)));
        }

        // ================================================================= tweaks

        [Fact]
        public void PaintressSetup_RoostRestrictedToMonolith()
        {
            var board = Table();
            TestTable.Start(board, Registry(), beforeSetup: g =>
            {
                g.OnSetupChoice(S(0));
                g.OnSetupChoice(S(1));
            });
            Assert.Equal(new[] { board.C("The Monolith") }, board.HomeRestrictions[S(1)]);
            Assert.False(board.HomeRestrictions.ContainsKey(S(0)));
        }

        [Fact]
        public void StillLife_MovesOneWarriorBeforeRoll()
        {
            var board = Family(Table()).Warriors(1, 4, 4).Warriors(0, 11, 2);
            var t = TestTable.Start(board, Registry(), updateRules: true);
            t.Game.OnBattleStarting(S(0), S(1), C(11)); // rabbit Portrait 11, next to 3 and 4
            Assert.Equal(2 + Tuning.PaintressStillLifeWarriors, board.Warriors(S(1), C(11)));
            Assert.Equal(4 - Tuning.PaintressStillLifeWarriors, board.Warriors(S(1), C(4)));
            Assert.Contains("MoveWarriors seat1 c4 c11 1", board.Log);
            Assert.True(t.State.PaintressStillLifeUsed);

            // Not in a Living Portrait, or her own battle: nothing moves.
            var b2 = Family(Table()).Warriors(1, 4, 4).Warriors(1, 10, 2).Warriors(0, 10, 1);
            var t2 = TestTable.Start(b2, Registry(), updateRules: true);
            t2.Game.OnBattleStarting(S(0), S(1), C(10));
            t2.Game.OnBattleStarting(S(1), S(0), C(11));
            Assert.DoesNotContain(b2.Log, l => l.StartsWith("MoveWarriors"));

            // The plugin cannot move warriors: Still Life is left out.
            var b3 = Family(Table()).Warriors(1, 4, 4).Unsupported(Ids.BoardActions.MoveWarriors);
            var t3 = TestTable.Start(b3, Registry(), updateRules: true);
            t3.Game.OnBattleStarting(S(0), S(1), C(11));
            Assert.Equal(2, b3.Warriors(S(1), C(11)));
        }

        [Fact]
        public void StillLife_OncePerTurn()
        {
            var board = Family(Table()).Warriors(1, 4, 4).Warriors(1, 2, 3);
            var t = TestTable.Start(board, Registry(), updateRules: true);
            t.PlayTurn(0, during: x =>
            {
                x.Game.OnBattleStarting(S(0), S(1), C(11));
                x.Game.OnBattleStarting(S(0), S(1), C(9)); // second battle, same turn
                Assert.True(x.State.PaintressStillLifeUsed);
            });
            Assert.Single(board.Log.Where(l => l.StartsWith("MoveWarriors")));
            Assert.Equal(2, board.Warriors(S(1), C(9)));
            Assert.False(t.State.PaintressStillLifeUsed); // reset at hook.turn-end

            t.PlayTurn(0, during: x => x.Game.OnBattleStarting(S(0), S(1), C(9)));
            Assert.Equal(2, board.Log.Count(l => l.StartsWith("MoveWarriors")));
            Assert.Equal(3, board.Warriors(S(1), C(9)));
        }

        // ================================================================= bot.paintress.01-site

        [Fact]
        public void PaintressBot_SiteScoreMatchesFormula()
        {
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Warriors(0, 10, 2)
                .Building(0, 4, PieceTypes.Sawmill); // fox corner 4 has no free slot left
            var t = TestTable.Start(board, Registry());
            t.State.EsquieRocks[ModState.Florrie] = new RockState { Status = RockStatus.Guarded, Clearing = C(10), HostSeat = S(0), GuardsLeft = 2 };
            var bot = new PaintressBot();
            var v = View(t);

            Assert.Equal(Tuning.PaintressSiteAdjacent + Tuning.PaintressSiteNearMonolith + 2 * Tuning.PaintressSiteEnemyWarrior + Tuning.PaintressSiteGuardedRock,
                bot.SiteScore(v, C(10)));
            Assert.Equal(Tuning.PaintressSiteAdjacent + Tuning.PaintressSiteNearMonolith, bot.SiteScore(v, C(11)));
            Assert.Equal(Tuning.PaintressSiteNearMonolith, bot.SiteScore(v, C(8)));  // two steps: 3-10-8
            Assert.Equal(0, bot.SiteScore(v, C(5)));                                  // four steps, no neighbour she rules

            var site = bot.Site(v);
            Assert.DoesNotContain(site, r => r.Item == C(3) || r.Item == C(4));
            foreach (var r in site) Assert.Equal(bot.SiteScore(v, r.Item), r.Score);
            for (var i = 1; i < site.Count; i++)
                Assert.True(site[i - 1].Score > site[i].Score || (site[i - 1].Score == site[i].Score && site[i - 1].Item.Value < site[i].Item.Value));
            Assert.Equal(C(9), site[0].Item); // 9 and 11 tie at 5: lower clearing first
        }

        [Fact]
        public void PaintressBot_SiteSkipsSuitsWithLivingPortrait()
        {
            var board = Family(Table(bot: true)).RemoveBuilding(1, "7", PieceTypes.Roost);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var site = bot.Site(View(t));
            Assert.NotEmpty(site);
            Assert.All(site, r => Assert.Contains(Suit.Mouse, board.SuitsOf(r.Item)));
            Assert.Equal(Suit.Mouse, bot.BestMissingSuit(View(t)));

            Family(board.Building(1, 7, PieceTypes.Roost));
            Assert.Empty(bot.Site(View(t)));
        }

        [Fact]
        public void PaintressBot_EasyBarksNextPortraitATurnEarly()
        {
            FakeBoard Board() => Table(bot: true).Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3).Hand(1, Suit.Bird);
            bool Barked(FakeBoard b) => b.Bubbles.Contains(new KeyValuePair<SeatId, string>(S(1), Ids.Barks.Paintress05));

            var easy = Board();
            TestTable.Start(easy, Registry(), Difficulty.Easy).PlayTurn(0).PlayTurn(1);
            Assert.True(Barked(easy)); // a card in hand: Easy names the site a turn early

            var normal = Board();
            TestTable.Start(normal, Registry(), Difficulty.Normal).PlayTurn(0).PlayTurn(1);
            Assert.False(Barked(normal)); // Normal waits for the Build card in the Decree

            var ready = Board().Column(1, DecreeCard.Build, Suit.Bird);
            TestTable.Start(ready, Registry(), Difficulty.Normal).PlayTurn(0).PlayTurn(1);
            Assert.True(Barked(ready));
        }

        // ================================================================= bot.paintress.02-decree

        [Fact]
        public void PaintressBot_DecreePrefersBuildOfBestMissingSuit()
        {
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Warriors(1, 9, 2)                       // she rules fox 9 (free slot, no roost)
                .Warriors(0, 2, 1).Warriors(0, 7, 1)     // 2 and 7 score lower than 9
                .Hand(1, Suit.Bird, Suit.Mouse, Suit.Fox, Suit.Rabbit);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var v = View(t);
            Assert.Equal(Suit.Fox, bot.BestMissingSuit(v));
            var cards = bot.Decree(v);
            Assert.Equal(new DecreeCard(DecreeCard.Build, Suit.Fox), cards[0]);
            Assert.Equal(new DecreeCard(DecreeCard.Recruit, Suit.Bird), cards[1]);

            board.Legal(1, "decree:recruit:bird", "decree:build:fox", "decree:build:rabbit");
            Assert.Equal("decree:build:fox", bot.ChooseAction(v, board.LegalActions(S(1))));
        }

        [Fact]
        public void PaintressBot_PrefersBirdBuildAfterTwoSuits()
        {
            // Fox 9 and rabbit 11 are Living Portraits; she rules mouse 7 (no roost yet).
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 2)
                .Building(1, 11, PieceTypes.Roost).Warriors(1, 11, 2)
                .Warriors(1, 7, 2)
                .Warriors(0, 2, 1)                      // rabbit corner 2 scores below mouse 7
                .Hand(1, Suit.Mouse, Suit.Bird);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            Assert.Equal(new DecreeCard(DecreeCard.Build, Suit.Bird), bot.Decree(View(t))[0]);

            // With one suit done, the suited Build comes first.
            board.Warriors(1, 11, 1);
            Assert.Equal(new DecreeCard(DecreeCard.Build, Suit.Mouse), bot.Decree(View(t))[0]);
        }

        [Fact]
        public void PaintressBot_NeverAddsCardLookaheadSaysMayFail()
        {
            // Best missing suit is fox (9), but she rules no fox clearing: a fox Build would fail. No warriors in supply:
            // a Recruit would fail too.
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Supply(1, 0)
                .Hand(1, Suit.Fox, Suit.Bird);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var v = View(t);
            Assert.Equal(Suit.Fox, bot.BestMissingSuit(v));
            Assert.False(bot.Lookahead(v, new[] { new DecreeCard(DecreeCard.Build, Suit.Fox) }));
            Assert.False(bot.Lookahead(v, new[] { new DecreeCard(DecreeCard.Recruit, Suit.Bird) }));
            Assert.Empty(bot.Decree(v));
            board.Legal(1, "decree:build:fox", "decree:recruit:bird");
            Assert.Null(bot.ChooseAction(v, board.LegalActions(S(1))));

            // A Decree that already holds a card she cannot resolve fails with anything added.
            board.Supply(1, 10).Column(1, DecreeCard.Battle, Suit.Rabbit);
            Assert.False(bot.Lookahead(v, new[] { new DecreeCard(DecreeCard.Recruit, Suit.Bird) }));
            Assert.Empty(bot.Decree(v));
        }

        // ================================================================= bot.paintress.03-garrison

        [Fact]
        public void PaintressBot_GarrisonsPortraitsToTwoWarriors()
        {
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 1)
                .Building(1, 11, PieceTypes.Roost)
                .Building(1, 7, PieceTypes.Roost).Warriors(1, 7, 2);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var v = View(t);
            Assert.Equal(new[] { C(11), C(9) }, bot.Garrison(v).Select(r => r.Item).ToArray());
            Assert.Equal("recruit:11", bot.ChooseAction(v, new[] { "recruit:7", "recruit:9", "recruit:11" }));

            // A hosted guard does not count toward the garrison.
            t.State.EsquieRocks[ModState.Florrie] = new RockState { Status = RockStatus.Guarded, Clearing = C(7), HostSeat = S(1), GuardsLeft = 1 };
            Assert.Contains(C(7), bot.Garrison(View(t)).Select(r => r.Item));
        }

        [Fact]
        public void PaintressBot_KeepsThreeInMonolithAndRecruitsThereUnderThreat()
        {
            var board = Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 2)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 1);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            Assert.Equal(C(3), bot.Garrison(View(t))[0].Item); // fewer than 3 in the Monolith

            board.Warriors(1, "The Monolith", 3);
            Assert.Equal(C(9), bot.Garrison(View(t))[0].Item);  // 3 there and no threat: Portraits first
            Assert.Equal("recruit:9", bot.ChooseAction(View(t), new[] { "recruit:3", "recruit:9" }));

            board.Warriors(0, 10, 2);                           // enemy warriors next to the Monolith
            Assert.True(bot.MonolithThreatened(View(t)));
            Assert.Equal(C(3), bot.Garrison(View(t))[0].Item);
            Assert.Equal("recruit:3", bot.ChooseAction(View(t), new[] { "recruit:9", "recruit:3" }));
        }

        [Fact]
        public void PaintressBot_HardGarrisonsThreePerPortrait()
        {
            FakeBoard Board() => Table(bot: true)
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 2);
            var normal = TestTable.Start(Board(), Registry(), Difficulty.Normal);
            var hard = TestTable.Start(Board(), Registry(), Difficulty.Hard);
            var bot = new PaintressBot();
            Assert.Equal(Tuning.PaintressPortraitWarriors, bot.PortraitGarrison(View(normal)));
            Assert.Equal(Tuning.PaintressHardPortraitWarriors, bot.PortraitGarrison(View(hard)));
            Assert.Empty(bot.Garrison(View(normal)));
            Assert.Equal(new[] { C(9) }, bot.Garrison(View(hard)).Select(r => r.Item).ToArray());
        }

        // ================================================================= bot.paintress.04-finish

        /// <summary>8 VP, Living Portraits fox 9 and rabbit 11, the Monolith held; she rules mouse 7 with 1 warrior.</summary>
        private static FakeBoard FinishBoard() => Table(bot: true)
            .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
            .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 2)
            .Building(1, 11, PieceTypes.Roost).Warriors(1, 11, 2)
            .Warriors(1, 7, 1)
            .Vp(1, Tuning.PaintressGoalVp)
            .Hand(1, Suit.Mouse, Suit.Mouse, Suit.Fox, Suit.Bird);

        [Fact]
        public void PaintressBot_FinishModeAddsExactCardsForThirdPortrait()
        {
            var t = TestTable.Start(FinishBoard(), Registry());
            var bot = new PaintressBot();
            var v = View(t);
            Assert.True(bot.InGoalMode(v));
            var plan = bot.Finish(v);
            Assert.Equal(Suit.Mouse, plan.MissingSuit);
            Assert.Equal(C(7), plan.Target);
            Assert.Equal(new[] { new DecreeCard(DecreeCard.Build, Suit.Mouse), new DecreeCard(DecreeCard.Recruit, Suit.Mouse) }, plan.Cards.ToArray());
            Assert.True(bot.Lookahead(v, plan.Cards));

            // Below the goal VP there is no finish mode.
            t.Board.Vp(1, Tuning.PaintressGoalVp - 1);
            Assert.False(bot.InGoalMode(View(t)));
            Assert.Null(bot.Finish(View(t)));
        }

        [Fact]
        public void PaintressBot_FinishModeBattlesOnlyToRetakeRule()
        {
            var board = FinishBoard().Hand(1)
                .Building(1, 7, PieceTypes.Roost).Warriors(0, 7, 3) // her mouse easel is ruled by Gustave (3 to 2)
                .Warriors(0, 5, 2);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var v = View(t);
            Assert.Equal(new[] { C(7) }, bot.Finish(v).BattleClearings.ToArray());
            Assert.Equal("battle:7:0", bot.ChooseAction(v, new[] { "battle:5:0", "battle:7:0" }));
            Assert.Null(bot.ChooseAction(v, new[] { "battle:5:0" }));
        }

        [Fact]
        public void PaintressBot_EasyAddsOneCardTooManyInFinishMode()
        {
            var bot = new PaintressBot();
            var normal = bot.Finish(View(TestTable.Start(FinishBoard(), Registry(), Difficulty.Normal)));
            var easy = bot.Finish(View(TestTable.Start(FinishBoard(), Registry(), Difficulty.Easy)));
            Assert.Equal(2, normal.Cards.Count);
            Assert.Equal(normal.Cards.Count + 1, easy.Cards.Count);
            Assert.Equal(normal.Cards, easy.Cards.Take(normal.Cards.Count));
            Assert.Equal(new DecreeCard(DecreeCard.Build, Suit.Fox), easy.Cards.Last());
        }

        // ================================================================= bot.paintress.05-repaint

        [Fact]
        public void PaintressBot_RepaintAvoidsSameClearingForTwoTurns()
        {
            var board = Family(Table(bot: true), vp: 0);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();

            board.RemoveBuilding(1, "9", PieceTypes.Roost);
            t.Game.OnPieceRemoved(S(0), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(9));
            Assert.Equal(Tuning.PaintressRepaintAvoidTurns, t.State.PaintressRepaintAvoid[C(9)]);
            Assert.DoesNotContain(bot.Repaint(View(t)), r => r.Item == C(9));
            Assert.Contains(bot.Repaint(View(t)), r => r.Item == C(5)); // the fox Portrait is rebuilt elsewhere

            t.PlayTurn(0);                                   // other seats' turns do not count
            Assert.Equal(Tuning.PaintressRepaintAvoidTurns, t.State.PaintressRepaintAvoid[C(9)]);
            t.PlayTurn(1);
            Assert.Equal(1, t.State.PaintressRepaintAvoid[C(9)]);
            Assert.DoesNotContain(bot.Site(View(t)), r => r.Item == C(9));

            var loaded = t.SaveAndLoad(Registry());          // the countdown survives a save
            Assert.Equal(1, loaded.State.PaintressRepaintAvoid[C(9)]);
            loaded.PlayTurn(0).PlayTurn(1);
            Assert.False(loaded.State.PaintressRepaintAvoid.ContainsKey(C(9)));
            Assert.Contains(bot.Site(new BotView(loaded.Ctx, S(1))), r => r.Item == C(9));

            // An easel in the Monolith is not a Portrait.
            t.Game.OnPieceRemoved(S(0), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(3));
            Assert.False(t.State.PaintressRepaintAvoid.ContainsKey(C(3)));
        }

        // ================================================================= bot.paintress.06-spare-white-haired-man

        [Fact]
        public void PaintressBot_AttacksWhiteHairedManLast()
        {
            var board = Table(bot: true).Seat(2, EngineId.Corvids, "white-haired-man")
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3);
            var t = TestTable.Start(board, Registry());
            var bot = new PaintressBot();
            var v = View(t);
            var ordered = bot.SpareWhiteHairedMan(v, new[] { new BattleTarget(C(5), S(2)), new BattleTarget(C(8), S(0)), new BattleTarget(C(6), S(2)) });
            Assert.Equal(new[] { new BattleTarget(C(8), S(0)), new BattleTarget(C(5), S(2)), new BattleTarget(C(6), S(2)) }, ordered.ToArray());
            Assert.Equal("battle:8:0", bot.ChooseAction(v, new[] { "battle:5:2", "battle:8:0" }));
        }

        // ================================================================= bot.paintress.07-spoiler

        [Fact]
        public void PaintressBot_SpoilerStillAttacksWhiteHairedMan()
        {
            var board = Table(bot: true).Seat(2, EngineId.Corvids, "white-haired-man")
                .Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", 3)
                .Warriors(1, 9, 2).Warriors(2, 9, 1)
                .Warriors(1, 8, 2).Warriors(0, 8, 1)
                .Vp(2, 10);
            var reg = Registry().Register(CharacterIds.WhiteHairedMan, () => new StubModule(CharacterIds.WhiteHairedMan));
            var t = TestTable.Start(board, reg);
            t.State.GlobalAct = 2;
            var bot = new PaintressBot();
            Assert.Null(bot.Spoiler(View(t)));                // no banner, no Goal Tracker: no trigger

            t.State.GlobalCountdowns.Add(S(2));                // his banner is up
            var answer = bot.Spoiler(View(t));
            Assert.Equal(S(2), answer.Threat);
            Assert.Equal(C(9), answer.Clearing);
            Assert.True(answer.PreEmpts);
            Assert.Equal("battle:9:2", bot.ChooseAction(View(t), new[] { "battle:8:0", "battle:9:2" }));
        }

        /// <summary>A stand-in for another character's win that the Goal Tracker shows one unit short, keyed on clearing 10.</summary>
        private sealed class OneStepWin : WinConditionBase, IGoalProgress
        {
            public OneStepWin() : base(Ids.WinConditions.Sciel) { }
            public override bool Announce(GameContext ctx, SeatId seat) => false;
            public int UnitsMissing(GameContext ctx, SeatId seat) => 1;
            public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat) => new[] { new ClearingId(10) };
        }

        private sealed class OneStepModule : CharacterModule
        {
            public override string CharacterId => CharacterIds.Sciel;
            public override IWinCondition Win { get; } = new OneStepWin();
        }

        [Fact]
        public void PaintressBot_SpoilerOwnAnnounceTurnBeforeOneStepThreat()
        {
            FakeBoard Board() => Family(Table(bot: true)).Seat(2, EngineId.Alliance, "sciel")
                .Warriors(1, "The Monolith", 2)                // under the Monolith garrison: step 3 wants a recruit there
                .Warriors(2, 10, 1).Warriors(1, 10, 1)
                .Vp(2, 10);
            ModuleRegistry Reg() => Registry().Register(CharacterIds.Sciel, () => new OneStepModule());
            var legal = new[] { "battle:10:2", "recruit:3" };
            var bot = new PaintressBot();

            // Hard, and this is her own announce turn: the one-step threat waits for the actions the steps leave free.
            var own = TestTable.Start(Board(), Reg(), Difficulty.Hard);
            own.State.GlobalAct = 2;
            Assert.True(bot.OwnAnnounceTurn(View(own)));
            var a = bot.Spoiler(View(own));
            Assert.Equal(S(2), a.Threat);
            Assert.False(a.Banner);
            Assert.False(a.PreEmpts);
            Assert.Equal("recruit:3", bot.ChooseAction(View(own), legal));

            // Hard, not her announce turn (the mouse Portrait is thin): the one-step threat pre-empts.
            var other = TestTable.Start(Board().Warriors(1, 7, 1), Reg(), Difficulty.Hard);
            other.State.GlobalAct = 2;
            Assert.False(bot.OwnAnnounceTurn(View(other)));
            Assert.True(bot.Spoiler(View(other)).PreEmpts);
            Assert.Equal("battle:10:2", bot.ChooseAction(View(other), legal));

            // Normal never pre-empts for a one-step threat; Easy ignores it.
            var normal = TestTable.Start(Board().Warriors(1, 7, 1), Reg(), Difficulty.Normal);
            normal.State.GlobalAct = 2;
            Assert.False(bot.Spoiler(View(normal)).PreEmpts);
            var easy = TestTable.Start(Board().Warriors(1, 7, 1), Reg(), Difficulty.Easy);
            easy.State.GlobalAct = 2;
            Assert.Null(bot.Spoiler(View(easy)));
        }

        // ================================================================= module

        [Fact]
        public void PaintressModule_RegisteredWithEveryBotRule()
        {
            Assert.True(ModuleRegistry.Default().Has(CharacterIds.Paintress));
            Assert.Empty(ModuleRegistry.MissingBotRules(new PaintressBot()));
            Assert.Equal(Ids.BotPlans.Paintress, new PaintressModule().Bot.PlanId);
            Assert.Equal(Ids.WinConditions.Paintress, new PaintressModule().Win.WinId);
        }

        [Fact]
        public void StillLife_OffWithoutUpdateRules()
        {
            var board = Family(Table()).Warriors(1, 4, 4).Warriors(0, 11, 2);
            var t = TestTable.Start(board, Registry());
            Assert.False(t.Ctx.RuleOn(Ids.RuleTweaks.PaintressStillLife)); // builder-questions C1 (a): off in the first release
            t.Game.OnBattleStarting(S(0), S(1), C(11));
            Assert.Equal(2, board.Warriors(S(1), C(11)));
            Assert.Equal(4, board.Warriors(S(1), C(4)));
            Assert.DoesNotContain(board.Log, l => l.StartsWith("MoveWarriors"));
            Assert.False(t.State.PaintressStillLifeUsed);
        }
    }
}
