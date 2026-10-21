using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Engine
{
    /// <summary>The framework: hook dispatch and ordering (game_hooks.json), the shared win timing (sys.win-conditions),
    /// save/load (sys.saved-state), bots' public view (sys.bots) and the FakeBoard itself.</summary>
    public class EngineTests
    {
        private static SeatId S(int i) => new SeatId(i);

        private static ModuleRegistry Stubs(params StubModule[] modules)
        {
            var reg = new ModuleRegistry();
            foreach (var m in modules) reg.Register(m.CharacterId, () => m);
            return reg;
        }

        /// <summary>Gustave (seat 0) and the Paintress (seat 1, bot) on Autumn, already in Act II.</summary>
        private static TestTable InActTwo(params StubModule[] modules)
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true), Stubs(modules));
            t.Board.Vp(1, 10);
            t.PlayTurn(0).PlayTurn(1);
            Assert.Equal(2, t.Ctx.Act);
            return t;
        }

        [Fact]
        public void Engine_ForgottenCharacterSelectStartsPlainRoot()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie);
            var gustave = new StubModule("gustave");
            var game = new Root33Game(board, board, Stubs(gustave));
            game.OnCharacterSelectShown(new[] { EngineId.Marquise, EngineId.Eyrie }, 4);   // the offline setup screen was opened...
            game.ForgetCharacterSelect();                                                    // ...but the game is online
            game.OnSeatAssigned(S(0), EngineId.Marquise, false);
            game.OnGameStart(board.Seats(), Difficulty.Normal, SpoilerMode.Safe);
            game.OnMapLoaded("autumn");
            game.OnSetupComplete();
            game.OnBirdsongStart(S(0));
            Assert.False(game.Context.E33Mode);
            Assert.Empty(game.Modules);
            Assert.Empty(gustave.Seen);
        }

        [Fact]
        public void Engine_PlainRootGameIgnoresEveryHook()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie);
            var gustave = new StubModule("gustave");
            var game = new Root33Game(board, board, Stubs(gustave));
            game.OnSeatAssigned(S(0), EngineId.Marquise, false); // no mod character select: plain Root
            game.OnGameStart(board.Seats(), Difficulty.Normal, SpoilerMode.Safe);
            game.OnMapLoaded("autumn");
            game.OnSetupComplete();
            game.OnBirdsongStart(S(0));
            game.OnEveningEnd(S(0));
            Assert.False(game.Context.E33Mode);
            Assert.False(game.Context.RolesAssigned);
            Assert.Empty(game.Modules);
            Assert.Empty(gustave.Seen);
            Assert.Equal("", game.OnSave());
            Assert.True(game.CanStartBattle(S(0), S(1), new ClearingId(1), false));
        }

        [Fact]
        public void Engine_SeatsGetTheCharacterOfTheirEngine()
        {
            Assert.Equal("esquie", ModuleRegistry.CharacterForEngine(EngineId.VagabondTinker));
            Assert.Equal("white-haired-man", ModuleRegistry.CharacterForEngine(EngineId.Corvids));
            Assert.Null(ModuleRegistry.CharacterForEngine(EngineId.Riverfolk));
            foreach (var c in Data.Characters.Values) Assert.NotNull(ModuleRegistry.EngineIdOf(c.Engine));

            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.VagabondThief).Seat(2, EngineId.Riverfolk);
            var t = TestTable.Start(board);
            Assert.Equal("gustave", t.Ctx.CharacterOf(S(0)));
            Assert.Equal("maelle", t.Ctx.CharacterOf(S(1)));
            Assert.Null(t.Ctx.CharacterOf(S(2)));
            Assert.Equal(S(1), t.Ctx.SeatOf("maelle"));
            Assert.True(board.ClockworkHidden);
        }

        [Fact]
        public void Engine_CountdownAnnouncesAtEveningEndAndWinsAtNextBirdsong()
        {
            var gustave = new StubModule("gustave");
            var t = InActTwo(gustave);
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            Assert.Equal("win.gustave", t.Board.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            Assert.True(t.Ctx.Countdown.IsAnnounced(S(0)));

            t.PlayTurn(1);
            t.Game.OnBirdsongStart(S(0));
            Assert.Equal(new[] { new KeyValuePair<SeatId, string>(S(0), "win.gustave") }, t.Board.Winners);
            Assert.Empty(t.Board.Banners);
            Assert.Empty(t.State.GlobalCountdowns);
            Assert.True(t.Ctx.GameOver);
        }

        [Fact]
        public void Engine_CountdownClearsWhenConfirmFails()
        {
            var gustave = new StubModule("gustave");
            var t = InActTwo(gustave);
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            gustave.StubWin.ConfirmResult = false;
            gustave.StubWin.AnnounceResult = false;
            t.PlayTurn(1).PlayTurn(0);
            Assert.Empty(t.Board.Winners);
            Assert.Empty(t.Board.Banners);
            Assert.Empty(t.State.GlobalCountdowns);
            Assert.Contains("ClearBanner seat0", t.Board.Log);
        }

        [Fact]
        public void Engine_GatesFollowTheWinRow()
        {
            // win.gustave: offWithDominance, vpMin 10. win.esquie: offInCoalition, quest.
            var gustave = new StubModule("gustave");
            var esquie = new StubModule("esquie");
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie").Vp(0, 10);
            var t = TestTable.Start(board, Stubs(gustave, esquie));
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);

            board.Coalition(1, 0);
            t.PlayTurn(1);
            Assert.Empty(board.Winners); // a Vagabond in a coalition has no themed win

            board.Dominance(0, Suit.Fox);
            t.PlayTurn(0);
            Assert.Empty(board.Banners); // an active dominance card switches the themed win off

            board.Dominance(0, null).Vp(0, 9);
            t.PlayTurn(1).PlayTurn(0);
            Assert.Empty(board.Banners); // below 10 VP

            board.Coalition(1, null);
            t.PlayTurn(1);
            Assert.Equal(new[] { new KeyValuePair<SeatId, string>(S(1), "win.esquie") }, board.Winners); // quest: at once
        }

        [Fact]
        public void Engine_EveningEndRunsModulesInOrderBeforeTheWinCheck()
        {
            var log = new List<string>();
            var esquie = new StubModule("esquie", HookOrder.Esquie, log);
            var gustave = new StubModule("gustave", HookOrder.Gustave, log);
            var board = FakeBoard.Autumn().Seat(0, EngineId.VagabondTinker, "esquie").Seat(1, EngineId.Marquise, "gustave").Vp(1, 10);
            var t = TestTable.Start(board, Stubs(esquie, gustave));
            t.PlayTurn(0); // Act II from the end of this turn
            log.Clear();
            t.PlayTurn(1);
            var end = log.Where(e => e.Contains("EveningEnd") || e.Contains("Announce") || e.Contains("TurnEnd")).ToList();
            Assert.Equal(new[] { "gustave:EveningEnd:1:act2", "esquie:EveningEnd:1:act2", "gustave:Announce:act2", "gustave:TurnEnd:1:act2", "esquie:TurnEnd:1:act2" }, end);
            Assert.Equal(new[] { "gustave", "esquie" }, t.Game.Modules.Select(m => m.CharacterId));
        }

        [Fact]
        public void Engine_BattleLegalNeedsEveryModuleToAllow()
        {
            var gustave = new StubModule("gustave");
            var paintress = new StubModule("paintress") { BattleVeto = (a, d, c) => c.Value != 3 };
            var t = InActTwo(gustave, paintress);
            Assert.True(t.Game.CanStartBattle(S(0), S(1), new ClearingId(5), false));
            Assert.False(t.Game.CanStartBattle(S(0), S(1), new ClearingId(3), false));
        }

        private sealed class FixedBot : IBotDirector
        {
            public string Pick;
            public int Plans;
            public string PlanId => "bot.paintress";
            public bool InGoalMode(BotView view) => false;
            public void PlanTurn(BotView view, ModState state) => Plans++;
            public string ChooseAction(BotView view, IReadOnlyList<string> legalActions) => Pick;
        }

        [Fact]
        public void Engine_BotDecisionOnlyReturnsLegalActions()
        {
            var bot = new FixedBot { Pick = "build.roost.c3" };
            var paintress = new StubModule("paintress") { StubBot = bot };
            var gustave = new StubModule("gustave") { StubBot = new FixedBot { Pick = "march" } };
            var t = InActTwo(paintress, gustave);
            Assert.True(bot.Plans >= 1); // planned at its Birdsong
            Assert.Equal("build.roost.c3", t.Game.OnBotDecision(S(1), new[] { "recruit", "build.roost.c3" }));
            Assert.Null(t.Game.OnBotDecision(S(1), new[] { "recruit" }));        // not legal: Root's own AI decides
            Assert.Null(t.Game.OnBotDecision(S(0), new[] { "march" }));          // a human seat is never driven
        }

        [Fact]
        public void Engine_PendingVpPaidAtNextBirdsongAndDrawsAtEvening()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress")
                .Unsupported(Ids.BoardActions.ScoreVp, Ids.BoardActions.DrawCards);
            var t = TestTable.Start(board);
            board.Turn(0);
            t.Ctx.ScoreVp(S(1), 1);
            t.Ctx.DrawCards(S(1), 2);
            Assert.Empty(board.ScoredVp);
            Assert.Equal(1, t.State.GlobalPendingVp[S(1)]);
            Assert.Equal(2, t.State.GlobalPendingDraws[S(1)]);

            t.PlayTurn(1);
            Assert.Equal(new[] { new KeyValuePair<SeatId, int>(S(1), 1) }, board.ScoredVp);
            Assert.Equal(new[] { new KeyValuePair<SeatId, int>(S(1), 2) }, board.Draws);
            Assert.Empty(t.State.GlobalPendingVp);
            Assert.Empty(t.State.GlobalPendingDraws);
        }

        [Fact]
        public void Engine_LoadKeepsOnlyCountdownsWhoseConfirmStillPasses()
        {
            var gustave = new StubModule("gustave");
            var t = InActTwo(gustave);
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            Assert.True(t.Board.Banners.ContainsKey(S(0)));

            var kept = new StubModule("gustave");
            var loaded = t.SaveAndLoad(Stubs(kept));
            Assert.Contains(S(0), loaded.State.GlobalCountdowns);
            Assert.True(loaded.Ctx.Countdown.IsAnnounced(S(0)));
            Assert.Contains("gustave:StateLoaded", kept.Seen);

            var broken = new StubModule("gustave");
            broken.StubWin.ConfirmResult = false;
            var dropped = t.SaveAndLoad(Stubs(broken));
            Assert.Empty(dropped.State.GlobalCountdowns);
            Assert.False(dropped.Ctx.Countdown.IsAnnounced(S(0)));
            Assert.False(t.Board.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void Engine_LoadDropsSeatWhoseEngineChanged()
        {
            var t = InActTwo();
            var text = t.Game.OnSave().Replace(Ids.State.GlobalSeatCharacters + "=0=gustave;1=paintress", Ids.State.GlobalSeatCharacters + "=0=gustave;1=sciel");
            var game = new Root33Game(t.Board, t.Board, new ModuleRegistry());
            game.OnLoad(text);
            Assert.Equal("gustave", game.Context.CharacterOf(S(0)));
            Assert.Null(game.Context.CharacterOf(S(1)));
            Assert.Single(game.Context.Warnings);
        }

        [Fact]
        public void Engine_LoadWithoutModStateIsPlainRoot()
        {
            var t = InActTwo();
            var game = new Root33Game(t.Board, t.Board, new ModuleRegistry());
            game.OnLoad("");
            Assert.False(game.Context.E33Mode);
            Assert.Equal("", game.OnSave());

            // A save that lost its roles recomputes them; one that has them keeps them.
            var noRoles = t.SaveAndLoad(edit: s => string.Join("\n", s.Split('\n').Where(l => !l.StartsWith(Ids.State.GlobalRoles + "="))));
            Assert.Equal(AutumnMap.FixedRoles().Count, noRoles.State.GlobalRoles.Count);
        }

        [Fact]
        public void Engine_RolesWaitForGustavesKeepAndSetupFallsBackToTheRules()
        {
            var board = FakeBoard.Autumn().MapNamed("winter").Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress");
            var log = new List<string>();
            var t = TestTable.Start(board, Stubs(new StubModule("paintress", log: log)), runSetup: false);
            Assert.False(t.Ctx.RolesAssigned);
            board.Token(0, 2, PieceTypes.Keep);
            t.Game.OnSetupComplete(); // no keep-placed hook arrived: the keep on the board still decides Lumière
            Assert.Equal(new ClearingId(2), t.Ctx.Role(Ids.MapRoles.Lumiere));
            Assert.Contains("paintress:RolesAssigned", log);
        }

        [Fact]
        public void Engine_BotViewRefusesOtherSeatsHiddenInformation()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Corvids, "white-haired-man", bot: true).Seat(1, EngineId.Alliance, "sciel", bot: true)
                .Hand(0, Suit.Fox, Suit.Bird).Hand(1, Suit.Mouse).HiddenPlotAt(0, "autumn.05", "bomb").SupportersAre(1, Suit.Rabbit)
                .Relic(1, "tablet", 3, 2, faceUp: false).Relic(2, "figure", 2, 2, faceUp: true);
            var t = TestTable.Start(board);
            var view = new BotView(t.Ctx, S(0));
            Assert.Equal(2, view.Board.HandSuits(S(0)).Count);
            Assert.Equal("bomb", view.Board.HiddenPlotType(S(0), board.C("autumn.05")));
            Assert.Equal(1, view.Board.HandSize(S(1)));
            Assert.Throws<InvalidOperationException>(() => view.Board.HandSuits(S(1)));
            Assert.Throws<InvalidOperationException>(() => view.Board.Supporters(S(1)));
            Assert.Throws<InvalidOperationException>(() => new BotView(t.Ctx, S(1)).Board.HiddenPlotType(S(0), board.C("autumn.05")));
            Assert.Null(view.Board.RelicValue(1));
            Assert.Equal(2, view.Board.RelicValue(2));
            Assert.Equal("tablet", view.Board.RelicType(1));
        }

        private sealed class PartialDirector : IBotDirector
        {
            public string PlanId => "bot.gustave";
            public bool InGoalMode(BotView view) => false;
            public void PlanTurn(BotView view, ModState state) { }
            public string ChooseAction(BotView view, IReadOnlyList<string> legalActions) => null;
            [BotRule("bot.gustave.01-route")] public List<Ranked<ClearingId>> Route(BotView view) => new List<Ranked<ClearingId>>();
        }

        [Fact]
        public void Engine_MissingBotRulesListsRowsWithoutAMethod()
        {
            var missing = ModuleRegistry.MissingBotRules(new PartialDirector());
            var rows = Data.BotPlans["bot.gustave"].Rules;
            Assert.DoesNotContain("bot.gustave.01-route", missing);
            Assert.Equal(rows.Count - 1, missing.Count);
            Assert.All(missing, id => Assert.Contains(id, rows));
        }

        [Fact]
        public void FakeBoard_AutumnMatchesTheSheet()
        {
            var b = FakeBoard.Autumn();
            Assert.Equal("autumn", b.MapName());
            Assert.Equal(Enumerable.Range(1, 12), b.Clearings().Select(c => c.Value));
            foreach (var row in Data.MapAutumn.Values)
            {
                var c = b.C(row.Id);
                Assert.Equal(c, b.C(row.ContinentName));
                Assert.Equal(AutumnMap.SuitOf(row), b.SuitsOf(c).Single());
                Assert.Equal(row.Slots, b.TotalSlots(c));
                Assert.Equal(row.Corner, b.Corners().Contains(c));
                Assert.Equal(row.Ruin, b.HasRuin(c));
                Assert.Equal(row.River, b.OnRiver(c));
                Assert.Equal(row.Paths.Select(AutumnMap.ClearingOf).OrderBy(x => x.Value), b.PathNeighbours(c).OrderBy(x => x.Value));
                if (!string.IsNullOrEmpty(row.Role)) Assert.Equal(c, b.C(row.Role));
            }
            Assert.Equal(new[] { 4, 4, 4 }, new[] { Suit.Fox, Suit.Rabbit, Suit.Mouse }.Select(s => b.Clearings().Count(c => b.SuitsOf(c)[0] == s)));
            Assert.Equal(22, b.Clearings().Sum(b.TotalSlots));
            Assert.Equal(18, b.Clearings().Sum(c => b.PathNeighbours(c).Count) / 2);
            Assert.Equal(b.C("The Monolith"), b.OppositeCorner(b.C("Lumière")));
            Assert.Equal(7, Enumerable.Range(1, 7).Count(f => b.ForestClearings(f).Count >= 3));
            Assert.All(b.Clearings().Where(b.OnRiver), c => Assert.NotEmpty(b.RiverNeighbours(c)));
        }

        [Fact]
        public void FakeBoard_RuleIsMostWarriorsPlusBuildingsAndTiesRuleNobody()
        {
            var b = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Alliance).Seat(2, EngineId.Eyrie).Seat(3, EngineId.Lizards);
            Assert.Null(b.Ruler(b.C("autumn.05")));
            b.Warriors(0, "autumn.05", 2).Building(1, "autumn.05", "base.fox").Warriors(1, "autumn.05", 1);
            Assert.Null(b.Ruler(b.C("autumn.05")));                       // 2 vs 2: nobody
            b.Building(0, "autumn.05", PieceTypes.Sawmill);
            Assert.Equal(S(0), b.Ruler(b.C("autumn.05")));                 // 3 vs 2
            b.Token(1, "autumn.05", PieceTypes.Sympathy).Token(1, "autumn.05", PieceTypes.Sympathy);
            Assert.Equal(S(0), b.Ruler(b.C("autumn.05")));                 // tokens never count
            b.Warriors(2, "autumn.05", 3);
            Assert.Equal(S(2), b.Ruler(b.C("autumn.05")));                 // the Eyrie wins a tie it is part of
            b.Building(3, "autumn.05", PieceTypes.Garden);
            Assert.Equal(S(3), b.Ruler(b.C("autumn.05")));                 // a Lizard garden always rules
            Assert.Equal(1, b.BuildingsOfType(S(1), b.C("autumn.05"), PieceTypes.Base));
            Assert.Equal(0, b.FreeSlots(b.C("autumn.05")));
        }
    }
}
