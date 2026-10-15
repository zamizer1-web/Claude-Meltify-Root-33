using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Acts
{
    /// <summary>sheets/acts.json: every row's tests (the act clock as hook.turn-end runs it).</summary>
    public class ActsTests
    {
        private static FakeBoard Two() =>
            FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true);

        private static ModuleRegistry Stubs(params StubModule[] modules)
        {
            var reg = new ModuleRegistry();
            foreach (var m in modules) reg.Register(m.CharacterId, () => m);
            return reg;
        }

        private static IEnumerable<string> Fired(TestTable t) => t.Ctx.StoryLog.Select(e => e.EventId);

        [Fact]
        public void Acts_Act1EndsWhenAnySeatReaches10Vp()
        {
            Assert.Equal(Tuning.Act1EndVp, Data.Acts["act1"].EndsAtVp);
            var t = TestTable.Start(Two());
            t.Board.Vp(1, 9);
            t.PlayTurn(0);
            Assert.Equal(1, t.Ctx.Act);
            t.Board.Vp(1, 10); // seat 1 reaches 10 during seat 0's turn: the act ends at that turn's end
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            Assert.Equal(2, t.State.GlobalAct);
        }

        [Fact]
        public void Acts_Act1EndsAfterRound4()
        {
            Assert.Equal(Tuning.Act1EndRound, Data.Acts["act1"].EndsAfterRound);
            var t = TestTable.Start(Two());
            t.PlayRounds(3);
            Assert.Equal(1, t.Ctx.Act);
            t.PlayTurn(0); // round 4 has not ended until the last seat has played
            Assert.Equal(4, t.State.GlobalRound);
            Assert.Equal(1, t.Ctx.Act);
            t.PlayTurn(1);
            Assert.Equal(2, t.Ctx.Act);
        }

        [Fact]
        public void Acts_NoThemedWinCanHappenInAct1()
        {
            Assert.All(Data.WinConditions.Values, row => Assert.False(WinRules.IsUnlocked(1, row)));

            // A countdown whose test passes at 10 VP and a quest that is complete from the start: nothing in Act I.
            var gustave = new StubModule("gustave");
            var esquie = new StubModule("esquie");
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie");
            var t = TestTable.Start(board, Stubs(gustave, esquie));
            t.PlayRounds(3);
            Assert.Equal(1, t.Ctx.Act);
            Assert.Empty(t.Board.Winners);
            Assert.Empty(t.Board.Banners);

            t.Board.Vp(0, 10);
            t.PlayTurn(0); // the win check of this turn still sees Act I; the act advances after it
            Assert.Empty(t.Board.Banners);
            Assert.Empty(t.Board.Winners);
            Assert.Equal(2, t.Ctx.Act);
        }

        [Fact]
        public void Acts_ActAdvancesAtEndOfTurnAfterWinChecks()
        {
            var log = new List<string>();
            var gustave = new StubModule("gustave", HookOrder.Gustave, log);
            var t = TestTable.Start(Two(), Stubs(gustave));
            log.Clear();
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            var turn = log.Where(e => !e.Contains("Birdsong")).ToList();
            Assert.Equal(new[] { "gustave:EveningEnd:0:act1", "gustave:TurnEnd:0:act1", "gustave:Story:story.act2.unlock-wins", "gustave:ActChanged:2" },
                turn.Where(e => !e.Contains("Story:") || e.Contains("unlock-wins")).ToArray());
            Assert.Empty(t.Board.Banners);
            Assert.True(t.Board.OverlayShown(Ids.Overlays.GlobalActBanner));

            t.PlayTurn(1).PlayTurn(0); // Gustave's next turn end runs in Act II: the banner goes up
            Assert.Contains("gustave:Announce:act2", log);
            Assert.Equal("win.gustave", t.Board.Banners[new SeatId(0)]);
        }

        [Fact]
        public void Acts_Act2EndsWhenAnySeatReaches20Vp()
        {
            Assert.Equal(Tuning.Act2EndVp, Data.Acts["act2"].EndsAtVp);
            var t = TestTable.Start(Two());
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            t.Board.Vp(1, 19);
            t.PlayTurn(1);
            Assert.Equal(2, t.Ctx.Act);
            t.Board.Vp(1, 20);
            t.PlayTurn(0);
            Assert.Equal(3, t.Ctx.Act);
        }

        [Fact]
        public void Acts_Act2EndsAfterRound8()
        {
            Assert.Equal(Tuning.Act2EndRound, Data.Acts["act2"].EndsAfterRound);
            var t = TestTable.Start(Two());
            t.PlayRounds(4);
            Assert.Equal(2, t.Ctx.Act);
            t.PlayRounds(3);
            t.PlayTurn(0);
            Assert.Equal(8, t.State.GlobalRound);
            Assert.Equal(2, t.Ctx.Act);
            t.PlayTurn(1);
            Assert.Equal(3, t.Ctx.Act);
        }

        [Fact]
        public void Acts_Act2UnlocksThemedWinsExceptTheCurator()
        {
            foreach (var row in Data.WinConditions.Values)
            {
                Assert.Equal(row.Id != "win.curator", WinRules.IsUnlocked(2, row));
                Assert.True(WinRules.IsUnlocked(3, row));
            }

            var gustave = new StubModule("gustave");
            var curator = new StubModule("curator");
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Duchy, "curator").Vp(0, 10).Vp(1, 10);
            var t = TestTable.Start(board, Stubs(gustave, curator));
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            Assert.Contains("story.act2.unlock-wins", Fired(t));
            t.PlayTurn(1).PlayTurn(0);
            Assert.True(t.Board.Banners.ContainsKey(new SeatId(0)));
            Assert.False(t.Board.Banners.ContainsKey(new SeatId(1)));
        }

        [Fact]
        public void Acts_ActEventsFireOnlyForSeatedOwners()
        {
            // Without Esquie or Lune their spawns and tags never fire.
            var t = TestTable.Start(Two());
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            Assert.Contains("story.act2.unlock-wins", Fired(t));
            Assert.DoesNotContain("story.act1.florrie", Fired(t));
            Assert.DoesNotContain("story.act2.dorrie", Fired(t));
            Assert.DoesNotContain("story.act2.soarrie", Fired(t));
            Assert.DoesNotContain("story.act2.parents-journal", Fired(t));

            // With Esquie seated his events fire, and go to his module; Lune's still do not.
            var esquie = new StubModule("esquie");
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie");
            var e = TestTable.Start(board, Stubs(esquie));
            Assert.Contains("story.act1.florrie", Fired(e));
            Assert.Contains("story.act1.francois-decoy", Fired(e));
            e.Board.Vp(0, 10);
            e.PlayTurn(0);
            Assert.Contains("story.act2.dorrie", Fired(e));
            Assert.Contains("story.act2.soarrie", Fired(e));
            Assert.DoesNotContain("story.act2.parents-journal", Fired(e));
            Assert.Contains(esquie.StoryEvents, x => x.Id == "story.act2.dorrie");
            Assert.All(e.Ctx.StoryLog.Where(r => r.Owner != null), r => Assert.True(e.Ctx.IsSeated(r.Owner)));
        }

        [Fact]
        public void Acts_TurnCrossingTwoLimitsReachesActThreeNextTurnEnd()
        {
            var t = TestTable.Start(Two());
            t.Board.Vp(1, 25);
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            t.PlayTurn(1);
            Assert.Equal(3, t.Ctx.Act);
            Assert.Contains("story.act3.fall", Fired(t));
        }

        [Fact]
        public void Acts_Act3RunsToTheEndOfTheGame()
        {
            Assert.Equal(0, Data.Acts["act3"].EndsAtVp);
            Assert.Equal(0, Data.Acts["act3"].EndsAfterRound);
            var t = TestTable.Start(Two());
            t.Board.Vp(0, 29);
            t.PlayRounds(12);
            Assert.Equal(3, t.Ctx.Act);
            Assert.Equal(3, t.Ctx.ActClock.LastAct);
            Assert.Equal(1, t.Ctx.StoryLog.Count(e => e.EventId == "story.act3.fall"));
        }

        [Fact]
        public void Acts_ActEventsNeverChangeVpChargeRankOrRelationships()
        {
            // Every character module that exists in Root33.Core takes part.
            var board = FakeBoard.Autumn()
                .Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie")
                .Seat(2, EngineId.VagabondRanger, "verso", bot: true).Seat(3, EngineId.Keepers, "lune", bot: true)
                .Vp(0, 3).Vp(1, 2).Vp(2, 4).Vp(3, 1)
                .Relationship(1, 0, Relationship.Friendly).Relationship(2, 3, Relationship.Hostile)
                .Pawn(1, "Esquie's Nest").Pawn(2, "The Monolith").Supply(3, 15)
                .Relic(1, "tablet", 2, 1).Relic(2, "figure", 3, 3).Relic(3, "jewelry", 1, 6);
            var t = TestTable.Start(board, ModuleRegistry.Default(), runSetup: false);
            var vp = board.Seats().ToDictionary(s => s, s => board.Vp(s));
            var rel = new[] { (1, 0), (1, 3), (2, 0), (2, 3) }.ToDictionary(p => p, p => board.RelationshipOf(new SeatId(p.Item1), new SeatId(p.Item2)));
            var s0 = t.State;
            var charge = (s0.GustaveArm, s0.ScielSun, s0.ScielMoon, s0.VersoRank, string.Join(",", s0.LuneStains.Values));

            t.Game.OnSetupComplete();   // Act I events
            t.Game.OnActChanged(2);     // Act II events
            t.Game.OnActChanged(3);     // Act III events

            Assert.Equal(3, t.Ctx.Act);
            Assert.Contains("story.act3.fall", Fired(t));
            Assert.Empty(board.ScoredVp);
            Assert.Empty(board.ExtraHits);
            Assert.All(vp, kv => Assert.Equal(kv.Value, board.Vp(kv.Key)));
            Assert.All(rel, kv => Assert.Equal(kv.Value, board.RelationshipOf(new SeatId(kv.Key.Item1), new SeatId(kv.Key.Item2))));
            var s = t.State;
            Assert.Equal(charge, (s.GustaveArm, s.ScielSun, s.ScielMoon, s.VersoRank, string.Join(",", s.LuneStains.Values)));
            Assert.Empty(s.GlobalPendingVp);
        }

        [Fact]
        public void Acts_CurrentActSurvivesSaveAndLoad()
        {
            var t = TestTable.Start(Two());
            t.Board.Vp(0, 10);
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);

            var loaded = t.SaveAndLoad();
            Assert.Equal(2, loaded.Ctx.Act);
            Assert.Equal(2, loaded.Ctx.ActClock.Act);
            Assert.Empty(loaded.Ctx.StoryLog); // Act II's events do not fire again
            loaded.PlayTurn(1);
            Assert.Equal(2, loaded.Ctx.Act);
            Assert.Empty(loaded.Ctx.StoryLog);

            // A save without the act recomputes it with the turn-end test (20 VP: Act III).
            t.Board.Vp(1, 20);
            var recomputed = t.SaveAndLoad(edit: text => string.Join("\n", text.Split('\n').Where(l => !l.StartsWith(Ids.State.GlobalAct + "="))));
            Assert.Equal(3, recomputed.Ctx.Act);
        }
    }
}
