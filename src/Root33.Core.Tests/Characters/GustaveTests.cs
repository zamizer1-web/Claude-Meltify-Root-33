using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Gustave;
using Root33.Core.Characters.Paintress;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Gustave: win.gustave, tweak.gustave.*, state.gustave.*, bot.gustave and its rules.
    /// Autumn: Lumière is 1 (rabbit), the Monolith 3; Beachheads 9 (fox), 10 and 11 (rabbit); paths 1-5, 1-8, 1-12, 8-6, 8-7,
    /// 8-10, 8-12, 12-4, 10-4, 7-2, 7-9, 9-2.</summary>
    public class GustaveTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);
        private static ClearingId[] Cs(params int[] ids) => ids.Select(C).ToArray();

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Gustave, () => new GustaveModule());

        private static FakeBoard Table(bool bot = false) =>
            FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave", bot: bot).Seat(1, EngineId.Eyrie, "paintress")
                .SupplyOf(0, PieceTypes.Sawmill, 6).SupplyOf(0, PieceTypes.Workshop, 6).SupplyOf(0, PieceTypes.Recruiter, 6);

        /// <summary>The road 1 (rabbit) - 12 (mouse) - 8 (fox) - 10 (rabbit Beachhead), each ruled with a building.</summary>
        private static FakeBoard Road(FakeBoard b, int vp = 10)
        {
            b.Warriors(0, 1, 2).Building(0, 1, PieceTypes.Sawmill)
             .Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill)
             .Warriors(0, 8, 2).Building(0, 8, PieceTypes.Workshop)
             .Warriors(0, 10, 2).Building(0, 10, PieceTypes.Recruiter)
             .Vp(0, vp);
            return b;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));
        private static GustaveBot Bot(TestTable t) => ((GustaveModule)t.Game.ModuleOf(S(0))).Director;

        private static BattleResult Battle(int attacker, int defender, int clearing, params RemovedPiece[] removed)
        {
            var r = new BattleResult { Clearing = C(clearing), Attacker = S(attacker), Defender = S(defender) };
            r.Removed.AddRange(removed);
            return r;
        }

        // ================================================================= win.gustave

        [Fact]
        public void GustaveWin_AnnouncesWithRuledThreeSuitChainToBeachhead()
        {
            var board = Road(Table());
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            Assert.Equal(new HashSet<ClearingId>(Cs(1, 8, 10, 12)), t.State.GustaveFlags);
            t.PlayTurn(1).PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Gustave, board.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            Assert.Empty(board.Winners);

            t.PlayTurn(1).PlayTurn(0);
            Assert.Contains(new KeyValuePair<SeatId, string>(S(0), Ids.WinConditions.Gustave), board.Winners);

            // A Chain of two suits (no mouse clearing) does not announce.
            var two = Table();
            Road(two).Warriors(0, 12, 0).RemoveBuilding(0, "12", PieceTypes.Sawmill);
            var t2 = TestTable.Start(two, Registry());
            t2.State.GustaveArm = Tuning.GustaveArmMax;
            t2.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.False(two.Banners.ContainsKey(S(0)));

            // An Arm short of full does not announce.
            var arm = Road(Table());
            var t3 = TestTable.Start(arm, Registry());
            t3.State.GustaveArm = Tuning.GustaveArmMax - 1;
            t3.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.False(arm.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void GustaveWin_ConfirmFailsAfterChainClearingFlips()
        {
            var board = Road(Table());
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);

            // The Eyrie flips rule of 8 before his Birdsong.
            t.PlayTurn(1, during: x => x.Board.Warriors(1, 8, 5));
            t.PlayTurn(0);
            Assert.Empty(board.Winners);
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.Contains(C(8), t.State.GustaveFlags);
        }

        [Fact]
        public void GustaveWin_BlockedInActOneAndBelowTenVp()
        {
            // Act I: everything met, no banner.
            var board = Road(Table());
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            Assert.Equal(1, t.Ctx.Act);
            t.PlayTurn(0);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.Empty(t.State.GlobalCountdowns);

            // Act II at 9 VP: no banner; at 10 VP it goes up.
            var low = Road(Table(), vp: 9).Vp(1, 10);
            var t2 = TestTable.Start(low, Registry());
            t2.State.GustaveArm = Tuning.GustaveArmMax;
            t2.PlayTurn(0).PlayTurn(1);
            Assert.Equal(2, t2.Ctx.Act);
            t2.PlayTurn(0);
            Assert.False(low.Banners.ContainsKey(S(0)));
            low.Vp(0, Tuning.CountdownVp);
            t2.PlayTurn(1).PlayTurn(0);
            Assert.True(low.Banners.ContainsKey(S(0)));

            // An active dominance switches it off.
            var dom = Road(Table()).Dominance(0, Suit.Fox).Vp(1, 10);
            var t3 = TestTable.Start(dom, Registry());
            t3.State.GustaveArm = Tuning.GustaveArmMax;
            t3.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.False(dom.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void GustaveWin_FlaggedButUnruledClearingBreaksChain()
        {
            // The road 1 - 8 (fox) - 7 (mouse) - 9 (fox Beachhead).
            var board = Table().Vp(0, 10)
                .Warriors(0, 1, 2).Building(0, 1, PieceTypes.Sawmill)
                .Warriors(0, 8, 2).Building(0, 8, PieceTypes.Workshop)
                .Warriors(0, 7, 2).Building(0, 7, PieceTypes.Sawmill)
                .Warriors(0, 9, 2).Building(0, 9, PieceTypes.Recruiter);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.PlayTurn(0);
            Assert.Contains(C(8), t.State.GustaveFlags);
            t.PlayTurn(1, during: x => x.Board.Warriors(1, 8, 5));
            t.PlayTurn(0);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.Contains(C(8), t.State.GustaveFlags);
            Assert.Equal(new HashSet<ClearingId>(Cs(1)), GustaveChain.Chain(board, t.State, S(0), C(1)));

            // Lumière lost: the Chain is empty even though everything else holds.
            board.Warriors(1, 8, 0).Warriors(1, 1, 5);
            Assert.Empty(GustaveChain.Chain(board, t.State, S(0), C(1)));
        }

        // ================================================================= tweak.gustave.overcharge

        [Fact]
        public void Overcharge_AddsHitAndEmptiesArm()
        {
            var board = Road(Table()).Warriors(1, 5, 2).Warriors(0, 5, 3);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.Game.OnToggle(S(0), GustaveTweaks.OverchargeToggle, true);
            Assert.True(t.State.GustaveOverchargeArmed);
            t.Game.OnBattleStarting(S(0), S(1), C(5));
            Assert.Equal(Tuning.GustaveOverchargeHits, board.ExtraHits[S(0)]);
            t.Game.OnBattleResolved(Battle(0, 1, 5, new RemovedPiece(S(1), PieceKind.Warrior, null)));
            Assert.Equal(0, t.State.GustaveArm);
            Assert.False(t.State.GustaveOverchargeArmed);

            // Without Overcharge a battle he starts that removes an enemy piece charges the Arm; his own losses or defending do not.
            t.Game.OnBattleResolved(Battle(0, 1, 5, new RemovedPiece(S(1), PieceKind.Warrior, null)));
            Assert.Equal(1, t.State.GustaveArm);
            t.Game.OnBattleResolved(Battle(0, 1, 5, new RemovedPiece(S(0), PieceKind.Warrior, null)));
            Assert.Equal(1, t.State.GustaveArm);
            t.Game.OnBattleResolved(Battle(1, 0, 5, new RemovedPiece(S(1), PieceKind.Warrior, null)));
            Assert.Equal(1, t.State.GustaveArm);
        }

        [Fact]
        public void Overcharge_UnavailableBelowFullArm()
        {
            var board = Road(Table()).Warriors(1, 5, 2).Warriors(0, 5, 3);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax - 1;
            t.Game.OnToggle(S(0), GustaveTweaks.OverchargeToggle, true);
            Assert.False(t.State.GustaveOverchargeArmed);
            t.Game.OnBattleStarting(S(0), S(1), C(5));
            Assert.False(board.ExtraHits.ContainsKey(S(0)));
            t.Game.OnBattleResolved(Battle(0, 1, 5, new RemovedPiece(S(1), PieceKind.Warrior, null)));
            Assert.Equal(Tuning.GustaveArmMax, t.State.GustaveArm);
            t.Game.OnBattleResolved(Battle(0, 1, 5, new RemovedPiece(S(1), PieceKind.Warrior, null)));
            Assert.Equal(Tuning.GustaveArmMax, t.State.GustaveArm);

            // A load drops an armed toggle below a full Arm and clamps the Arm.
            t.State.GustaveOverchargeArmed = true;
            var loaded = t.SaveAndLoad(Registry(), text => text.Replace(Ids.State.GustaveArm + "=3", Ids.State.GustaveArm + "=1"));
            Assert.Equal(1, loaded.State.GustaveArm);
            Assert.False(loaded.State.GustaveOverchargeArmed);
            var clamped = loaded.SaveAndLoad(Registry(), text => text.Replace(Ids.State.GustaveArm + "=1", Ids.State.GustaveArm + "=9"));
            Assert.Equal(Tuning.GustaveArmMax, clamped.State.GustaveArm);
            Assert.Contains(C(1), clamped.State.GustaveFlags);
        }

        [Fact]
        public void Overcharge_DisarmsUnusedAtTurnEnd()
        {
            var board = Road(Table());
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            var armedDuring = false;
            t.PlayTurn(0, during: x =>
            {
                x.Game.OnToggle(S(0), GustaveTweaks.OverchargeToggle, true);
                armedDuring = x.State.GustaveOverchargeArmed;
            });
            Assert.True(armedDuring);
            Assert.False(t.State.GustaveOverchargeArmed);
            Assert.Equal(Tuning.GustaveArmMax, t.State.GustaveArm);
        }

        // ================================================================= tweak.gustave.keep-in-lumiere

        [Fact]
        public void GustaveSetup_KeepCornerBecomesLumiere()
        {
            var board = FakeBoard.Autumn().MapNamed("winter").Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress");
            var t = TestTable.Start(board, Registry(), beforeSetup: g =>
            {
                g.OnSetupChoice(S(0));
                board.Token(0, 2, PieceTypes.Keep);
                g.OnPiecePlaced(S(0), PieceKind.Token, PieceTypes.Keep, C(2));
            });
            Assert.False(board.HomeRestrictions.ContainsKey(S(0)));
            Assert.Equal(C(2), t.Ctx.Role(Ids.MapRoles.Lumiere));
            Assert.Contains(C(2), t.State.GustaveFlags);
        }

        [Fact]
        public void GustaveSetup_MonolithIsDiagonalAndHasNoExpeditioners()
        {
            var board = FakeBoard.Autumn().MapNamed("winter").Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress");
            var t = TestTable.Start(board, Registry(), beforeSetup: g =>
            {
                board.Token(0, 4, PieceTypes.Keep);
                g.OnPiecePlaced(S(0), PieceKind.Token, PieceTypes.Keep, C(4));
                // Normal Marquise setup: a warrior everywhere except the clearing diagonally opposite the keep.
                foreach (var c in board.Clearings().Where(c => c != board.OppositeCorner(C(4)).Value)) board.Warriors(0, c, 1);
            });
            var monolith = t.Ctx.Role(Ids.MapRoles.Monolith);
            Assert.Equal(board.OppositeCorner(C(4)), monolith);
            Assert.Equal(C(4), t.Ctx.Role(Ids.MapRoles.Lumiere));
            var empty = board.Clearings().Where(c => board.Warriors(S(0), c) == 0).ToList();
            Assert.Equal(new[] { monolith.Value }, empty);
        }

        [Fact]
        public void GustaveSetup_AutumnKeepRestrictedToLumiere()
        {
            var board = Table();
            var t = TestTable.Start(board, Registry(), beforeSetup: g => { g.OnSetupChoice(S(0)); g.OnSetupChoice(S(1)); });
            Assert.Equal(new[] { C(1) }, board.HomeRestrictions[S(0)]);
            Assert.False(board.HomeRestrictions.ContainsKey(S(1)));
            Assert.Equal(C(3), t.Ctx.Role(Ids.MapRoles.Monolith));

            // Fallback: no do.restrict-home; a human is warned.
            var human = Table().Unsupported(Ids.BoardActions.RestrictHome);
            var t2 = TestTable.Start(human, Registry(), beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.False(human.HomeRestrictions.ContainsKey(S(0)));
            Assert.NotEmpty(t2.Ctx.Warnings);
        }

        // ================================================================= bot.gustave

        [Fact]
        public void GustaveBot_RouteCostFollowsRuleFlagsAndSlots()
        {
            var board = Table(bot: true)
                .Warriors(0, 1, 2)
                .Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill)
                .Warriors(0, 8, 2)
                .Warriors(1, 5, 3)
                .Building(1, 10, PieceTypes.Roost).Building(1, 10, PieceTypes.Roost)
                .Warriors(0, 11, 3).Building(1, 11, PieceTypes.Roost).Building(1, 11, PieceTypes.Roost);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveFlags.Add(C(11));
            var bot = Bot(t);
            var v = View(t);
            Assert.Equal(0, bot.RouteCost(v, C(1)));                                       // ruled and flagged
            Assert.Equal(0, bot.RouteCost(v, C(12)));                                      // ruled with his building (flagged tonight)
            Assert.Equal(Tuning.GustaveRouteCostRuled, bot.RouteCost(v, C(8)));            // ruled, unflagged
            Assert.Equal(Tuning.GustaveRouteCostUnruled + 3, bot.RouteCost(v, C(5)));      // enemy warriors
            Assert.Equal(Tuning.GustaveRouteCostUnruled, bot.RouteCost(v, C(7)));          // empty
            Assert.Equal(Tuning.GustaveRouteCostUnruled + Tuning.GustaveRouteCostFull, bot.RouteCost(v, C(10)));  // full of enemy buildings
            Assert.Equal(Tuning.GustaveRouteCostFull, bot.RouteCost(v, C(11)));            // flagged and ruled but full
            Assert.Empty(ModuleRegistry.MissingBotRules(bot));
        }

        [Fact]
        public void GustaveBot_RouteIsCheapestThreeSuitPathToBeachhead()
        {
            var board = Table(bot: true)
                .Warriors(0, 1, 2)
                .Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill)
                .Warriors(0, 8, 2)
                .Warriors(0, 10, 2);
            var t = TestTable.Start(board, Registry());
            var bot = Bot(t);
            var plan = bot.Route(View(t));
            // 1-8-10 costs the same but misses mouse; 1-12-4-10 costs 3.
            Assert.Equal(Cs(1, 12, 8, 10), plan.Path);
            Assert.Equal(2, plan.Cost);
            bot.PlanTurn(View(t), t.State);
            Assert.Equal(Cs(1, 12, 8, 10), t.State.GustaveRoute);

            // His Birdsong plans it too.
            t.State.GustaveRoute = new ClearingId[0];
            t.PlayTurn(0);
            Assert.Equal(Cs(1, 12, 8, 10), t.State.GustaveRoute);
        }

        [Fact]
        public void GustaveBot_HardReplansRouteAroundBlockade()
        {
            FakeBoard Board() => Table(bot: true)
                .Warriors(0, 1, 2)
                .Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill)
                .Warriors(0, 8, 2)
                .Warriors(0, 10, 2);

            var hard = Board();
            var t = TestTable.Start(hard, Registry(), Difficulty.Hard);
            Bot(t).PlanTurn(View(t), t.State);
            Assert.Equal(Cs(1, 12, 8, 10), t.State.GustaveRoute);
            hard.Warriors(1, 8, 6);
            t.Game.OnActionResolved(S(1));
            Assert.Equal(Cs(1, 12, 4, 10), t.State.GustaveRoute);

            var normal = Board();
            var n = TestTable.Start(normal, Registry(), Difficulty.Normal);
            Bot(n).PlanTurn(View(n), n.State);
            normal.Warriors(1, 8, 6);
            n.Game.OnActionResolved(S(1));
            Assert.Equal(Cs(1, 12, 8, 10), n.State.GustaveRoute);
        }

        private static FakeBoard LostEight(FakeBoard b) =>
            b.Warriors(0, 1, 4).Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill)
             .Warriors(0, 8, 1).Warriors(1, 8, 3);

        [Fact]
        public void GustaveBot_RetakesLostFlaggedRouteClearingFirst()
        {
            var board = LostEight(Table(bot: true));
            var t = TestTable.Start(board, Registry());
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            t.State.GustaveFlags.Add(C(8));
            var hold = Bot(t).Hold(View(t));
            Assert.Equal(C(8), hold[0].Clearing);
            Assert.Equal(3, hold[0].WarriorsNeeded);
            Assert.Equal(C(1), hold[0].MarchFrom);
            var pick = Bot(t).ChooseAction(View(t), new[] { "build:12:workshop", "battle:8:1", "move:12:8:2", "move:1:8:3" });
            Assert.Equal("move:1:8:3", pick);
        }

        [Fact]
        public void GustaveBot_EasyDoesNotRetakeLostChainClearings()
        {
            var board = LostEight(Table(bot: true));
            var t = TestTable.Start(board, Registry(), Difficulty.Easy);
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            t.State.GustaveFlags.Add(C(8));
            Assert.Empty(Bot(t).Hold(View(t)));
            Assert.NotEqual("move:1:8:3", Bot(t).ChooseAction(View(t), new[] { "move:1:8:3" }));
        }

        [Fact]
        public void GustaveBot_HardKeepsRuleMarginTwoOnChain()
        {
            FakeBoard Board() => Table(bot: true)
                .Warriors(0, 1, 3)
                .Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill).Warriors(1, 12, 2)
                .Warriors(0, 8, 2).Building(0, 8, PieceTypes.Workshop);
            var t = TestTable.Start(Board(), Registry(), Difficulty.Hard);
            t.State.GustaveFlags.UnionWith(Cs(12, 8));
            var hold = Bot(t).Hold(View(t));
            Assert.Single(hold);
            Assert.Equal(C(12), hold[0].Clearing);
            Assert.True(hold[0].TopUp);
            Assert.Equal(Tuning.GustaveHardRuleMargin - 1, hold[0].WarriorsNeeded);

            var n = TestTable.Start(Board(), Registry(), Difficulty.Normal);
            n.State.GustaveFlags.UnionWith(Cs(12, 8));
            Assert.Empty(Bot(n).Hold(View(n)));
        }

        [Fact]
        public void GustaveBot_BuildsInNextUnflaggedRouteClearing()
        {
            var board = Table(bot: true)
                .Warriors(0, 1, 2).Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill).Warriors(0, 8, 2)
                .SupplyOf(0, PieceTypes.Sawmill, 5).SupplyOf(0, PieceTypes.Recruiter, 5);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            t.State.GustaveFlags.Add(C(12));
            var builds = Bot(t).Build(View(t));
            Assert.Equal(C(8), builds[0].Clearing);
            Assert.Equal(PieceTypes.Workshop, builds[0].Type);
            Assert.Equal("build:8:workshop", Bot(t).ChooseAction(View(t), new[] { "build:7:workshop", "build:8:workshop" }));

            // No sawmill among the connected ruled clearings: a sawmill first.
            var bare = Table(bot: true).Warriors(0, 1, 2).Warriors(0, 8, 2);
            var t2 = TestTable.Start(bare, Registry());
            t2.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.Equal(PieceTypes.Sawmill, Bot(t2).Build(View(t2))[0].Type);
        }

        [Fact]
        public void GustaveBot_BuildsBeachheadAsSoonAsRuled()
        {
            var board = Table(bot: true)
                .Warriors(0, 1, 2).Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill).Warriors(0, 8, 2).Warriors(0, 10, 2)
                .SupplyOf(0, PieceTypes.Sawmill, 5);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            t.State.GustaveFlags.Add(C(12));
            var builds = Bot(t).Build(View(t));
            Assert.Equal(C(10), builds[0].Clearing);
            Assert.True(builds[0].Beachhead);
            Assert.Equal("build:10:workshop", Bot(t).ChooseAction(View(t), new[] { "build:8:workshop", "build:10:workshop" }));
        }

        [Fact]
        public void GustaveBot_EasyBuildsOnRouteOncePerTurn()
        {
            // 12 already got a building this turn (ruled, unflagged, his building): Easy makes no second Route build.
            FakeBoard Built() => Table(bot: true)
                .Warriors(0, 1, 2).Warriors(0, 12, 2).Building(0, 12, PieceTypes.Sawmill).Warriors(0, 8, 2);
            var t = TestTable.Start(Built(), Registry(), Difficulty.Easy);
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.Empty(Bot(t).Build(View(t)));
            var n = TestTable.Start(Built(), Registry(), Difficulty.Normal);
            n.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.Contains(Bot(n).Build(View(n)), o => o.Clearing == C(8));

            // Two candidates: Easy plans only one.
            var two = Table(bot: true).Warriors(0, 1, 2).Warriors(0, 12, 2).Warriors(0, 8, 2);
            var e = TestTable.Start(two, Registry(), Difficulty.Easy);
            e.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.Single(Bot(e).Build(View(e)));
        }

        [Fact]
        public void GustaveBot_TakesWinnableBattleWhileArmNotFull()
        {
            var board = Table(bot: true).Warriors(0, 5, 3).Warriors(1, 5, 1).Warriors(0, 6, 1).Warriors(1, 6, 1);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = 1;
            var legal = new[] { "battle:6:1", "battle:5:1" };
            var charge = Bot(t).Charge(View(t));
            Assert.Empty(charge);   // reads legal actions from the board: none set yet
            board.Legal(0, legal);
            charge = Bot(t).Charge(View(t));
            Assert.Single(charge);  // 1 against 1 in 6 is not winnable
            Assert.Equal(new GustaveBattle(C(5), S(1)), charge[0].Item);
            Assert.Equal("battle:5:1", Bot(t).ChooseAction(View(t), legal));

            t.State.GustaveArm = Tuning.GustaveArmMax;
            Assert.Empty(Bot(t).Charge(View(t)));
        }

        [Fact]
        public void GustaveBot_ChargePrefersEnemyPiecesOnRoute()
        {
            var board = Table(bot: true)
                .Warriors(0, 8, 3).Warriors(1, 8, 2)
                .Warriors(0, 5, 5).Warriors(1, 5, 1).Building(1, 5, PieceTypes.Roost)
                .Legal(0, "battle:5:1", "battle:8:1");
            var t = TestTable.Start(board, Registry());
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            var charge = Bot(t).Charge(View(t));
            Assert.Equal(2, charge.Count);
            Assert.Equal(C(8), charge[0].Item.Clearing);
            Assert.Equal("battle:8:1", Bot(t).ChooseAction(View(t), new[] { "battle:5:1", "battle:8:1" }));
        }

        [Fact]
        public void GustaveBot_OverchargesOnlyForBeachheadOrLumiere()
        {
            var board = Table(bot: true).Warriors(0, 1, 1).Warriors(0, 10, 3).Warriors(1, 10, 2).Warriors(0, 8, 3).Warriors(1, 8, 2);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            var bot = Bot(t);
            Assert.True(bot.Overcharge(View(t), C(10), S(1)));
            Assert.False(bot.Overcharge(View(t), C(8), S(1)));
            Assert.False(bot.Overcharge(View(t), C(1), S(1)));   // he still rules Lumière
            board.Warriors(1, 1, 4);
            Assert.True(bot.Overcharge(View(t), C(1), S(1)));    // retake Lumière
            t.State.GustaveArm = Tuning.GustaveArmMax - 1;
            Assert.False(bot.Overcharge(View(t), C(10), S(1)));

            // Through the hooks: the bot arms it at battle start for the Beachhead only.
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.Game.OnBattleStarting(S(0), S(1), C(8));
            Assert.False(board.ExtraHits.ContainsKey(S(0)));
            t.Game.OnBattleStarting(S(0), S(1), C(10));
            Assert.Equal(Tuning.GustaveOverchargeHits, board.ExtraHits[S(0)]);
        }

        [Fact]
        public void GustaveBot_NoOverchargeOnExpectedAnnounceTurn()
        {
            var board = Road(Table(bot: true)).Warriors(1, 10, 1);
            var t = TestTable.Start(board, Registry());
            t.State.GlobalAct = 2;
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            var bot = Bot(t);
            Assert.True(bot.AnnounceTurn(View(t)));
            Assert.False(bot.Overcharge(View(t), C(10), S(1)));
            t.Game.OnBattleStarting(S(0), S(1), C(10));
            Assert.False(board.ExtraHits.ContainsKey(S(0)));

            board.Vp(0, Tuning.CountdownVp - 1);
            Assert.False(bot.AnnounceTurn(View(t)));
            Assert.True(bot.Overcharge(View(t), C(10), S(1)));
        }

        [Fact]
        public void GustaveBot_EasyNeverOvercharges()
        {
            var board = Table(bot: true).Warriors(0, 10, 3).Warriors(1, 10, 2).Warriors(1, 1, 4);
            var t = TestTable.Start(board, Registry(), Difficulty.Easy);
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.False(Bot(t).Overcharge(View(t), C(10), S(1)));
            Assert.False(Bot(t).Overcharge(View(t), C(1), S(1)));
            t.Game.OnBattleStarting(S(0), S(1), C(10));
            Assert.False(board.ExtraHits.ContainsKey(S(0)));
            Assert.False(t.State.GustaveOverchargeArmed);
        }

        [Fact]
        public void GustaveBot_GoalModeAtEightVpOrRouteCostFour()
        {
            // 8 VP, nothing ruled.
            var vp = TestTable.Start(Table(bot: true).Vp(0, Tuning.GustaveGoalVp), Registry());
            Assert.True(Bot(vp).GoalMode(View(vp)));
            Assert.True(Bot(vp).InGoalMode(View(vp)));

            // 7 VP and an expensive Route.
            var far = TestTable.Start(Table(bot: true).Vp(0, Tuning.GustaveGoalVp - 1).Warriors(0, 1, 1), Registry());
            Assert.False(Bot(far).GoalMode(View(far)));

            // 7 VP and a Route of cost 3 (1-12-8-10 ruled, unflagged).
            FakeBoard Near(int v) => Table(bot: true).Vp(0, v).Warriors(0, 1, 1).Warriors(0, 12, 1).Warriors(0, 8, 1).Warriors(0, 10, 1);
            var near = TestTable.Start(Near(Tuning.GustaveGoalVp - 1), Registry());
            Assert.True(Bot(near).Route(View(near)).Cost <= Tuning.GustaveGoalRouteCost);
            Assert.True(Bot(near).GoalMode(View(near)));

            // Easy: only at 12 VP.
            var easy = TestTable.Start(Near(Tuning.GustaveGoalVp), Registry(), Difficulty.Easy);
            Assert.False(Bot(easy).GoalMode(View(easy)));
            var easy12 = TestTable.Start(Near(Tuning.GustaveEasyGoalVp), Registry(), Difficulty.Easy);
            Assert.True(Bot(easy12).GoalMode(View(easy12)));
        }

        [Fact]
        public void GustaveBot_GoalModeSpendsBirdsOnRouteAndSkipsOverwork()
        {
            // Goal mode with Route work left (8 is ruled and unflagged) but no legal build: spend a bird for an extra action.
            var board = Table(bot: true).Vp(0, Tuning.GustaveGoalVp).Warriors(0, 1, 2).Warriors(0, 8, 2).Hand(0, Suit.Bird, Suit.Fox);
            var t = TestTable.Start(board, Registry());
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            Assert.Equal(GustaveActionKey.BirdExtra, Bot(t).ChooseAction(View(t), new[] { "overwork:8", GustaveActionKey.BirdExtra, GustaveActionKey.Pass }));
            // No bird in hand: no extra action, and Overwork is skipped.
            board.Hand(0, Suit.Fox);
            Assert.Equal(GustaveActionKey.Pass, Bot(t).ChooseAction(View(t), new[] { "overwork:8", GustaveActionKey.BirdExtra, GustaveActionKey.Pass }));

            // Outside goal mode (low VP, expensive Route) Root's own AI decides.
            var calm = Table(bot: true).Warriors(0, 1, 1).Hand(0, Suit.Bird);
            var c = TestTable.Start(calm, Registry());
            Assert.False(Bot(c).GoalMode(View(c)));
            Assert.Null(Bot(c).ChooseAction(View(c), new[] { "overwork:1", GustaveActionKey.BirdExtra, GustaveActionKey.Pass }));
        }

        [Fact]
        public void GustaveBot_AnnounceTurnThickensThinnestChainClearing()
        {
            var board = Road(Table(bot: true)).Warriors(1, 12, 2);
            var t = TestTable.Start(board, Registry());
            t.State.GlobalAct = 2;
            t.State.GustaveArm = Tuning.GustaveArmMax;
            var list = Bot(t).Announce(View(t));
            Assert.Equal(C(12), list[0]);
            Assert.Equal(C(10), list[list.Count - 1]);
            Assert.Equal("recruit:12", Bot(t).ChooseAction(View(t), new[] { "recruit:8", "recruit:10", "recruit:12" }));

            // Not an announce turn: nothing to thicken.
            t.State.GustaveArm = Tuning.GustaveArmMax - 1;
            Assert.Empty(Bot(t).Announce(View(t)));
        }

        [Fact]
        public void GustaveBot_AnnounceTurnUsesFieldHospitalsInChain()
        {
            var board = Road(Table(bot: true));
            var t = TestTable.Start(board, Registry());
            t.State.GlobalAct = 2;
            t.State.GustaveArm = Tuning.GustaveArmMax;
            var legal = new[] { "field-hospital:5", "field-hospital:8", "recruit:12" };
            Assert.Equal("field-hospital:8", Bot(t).ChooseAction(View(t), legal));
            Assert.NotEqual("field-hospital:5", Bot(t).ChooseAction(View(t), new[] { "field-hospital:5" }));

            board.Vp(0, Tuning.CountdownVp - 1);
            Assert.NotEqual("field-hospital:8", Bot(t).ChooseAction(View(t), legal));
        }

        private static (FakeBoard board, TestTable t) PaintressBanner()
        {
            var board = Table(bot: true)
                .SupplyOf(1, PieceTypes.Roost, 5)
                .Building(1, 3, PieceTypes.Roost).Warriors(1, 3, 3)
                .Building(1, 7, PieceTypes.Roost).Warriors(1, 7, 2)
                .Building(1, 11, PieceTypes.Roost).Warriors(1, 11, 2)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 2)
                .Vp(1, 10)
                .Warriors(0, 7, 3).Warriors(0, 1, 2).Warriors(0, 8, 2);
            var reg = Registry().Register(CharacterIds.Paintress, () => new PaintressModule());
            var t = TestTable.Start(board, reg);
            t.State.GlobalAct = 2;
            t.State.GustaveRoute = Cs(1, 12, 8, 10);
            return (board, t);
        }

        [Fact]
        public void GustaveBot_SpoilerAnswersBannerBeforeOwnSteps()
        {
            var (_, t) = PaintressBanner();
            var legal = new[] { "build:8:sawmill", "battle:7:1" };
            Assert.Equal("build:8:sawmill", Bot(t).ChooseAction(View(t), legal));

            t.State.GlobalCountdowns.Add(S(1));
            var answer = Bot(t).Spoiler(View(t));
            Assert.Equal(S(1), answer.Threat);
            Assert.True(answer.Banner);
            Assert.True(answer.PreEmpts);
            Assert.Equal("battle:7:1", Bot(t).ChooseAction(View(t), legal));
        }

        [Fact]
        public void GustaveBot_SpoilerNeverOverchargesToBlock()
        {
            var (board, t) = PaintressBanner();
            t.State.GustaveArm = Tuning.GustaveArmMax;
            t.State.GlobalCountdowns.Add(S(1));
            Assert.Equal("battle:7:1", Bot(t).ChooseAction(View(t), new[] { "battle:7:1" }));
            Assert.False(Bot(t).Overcharge(View(t), C(7), S(1)));
            t.Game.OnBattleStarting(S(0), S(1), C(7));
            Assert.False(board.ExtraHits.ContainsKey(S(0)));
            Assert.False(t.State.GustaveOverchargeArmed);
        }
    }
}
