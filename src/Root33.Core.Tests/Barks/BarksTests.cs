using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Barks
{
    /// <summary>sheets/barks.json and sheets/bark_triggers.json: tier, act, safe siblings, cooldowns and the shared triggers.</summary>
    public class BarksTests
    {
        private static SeatId S(int i) => new SeatId(i);

        private static ModuleRegistry Stubs(params StubModule[] modules)
        {
            var reg = new ModuleRegistry();
            foreach (var m in modules) reg.Register(m.CharacterId, () => m);
            return reg;
        }

        private static IEnumerable<string> Lines(string ch, string trigger, SpoilerMode mode, int act) =>
            BarkRules.Lines(ch, trigger, mode, act).Select(b => b.Id);

        private static List<string> Said(TestTable t, int seat) => t.Board.Bubbles.Where(b => b.Key == S(seat)).Select(b => b.Value).ToList();

        [Fact]
        public void Barks_EveryRowIsOriginalAndEveryFinishedLineHasASafeSiblingOnItsTrigger()
        {
            foreach (var b in Data.Barks.Values)
            {
                Assert.True(b.Original, b.Id);
                Assert.True(Data.BarkTriggers.ContainsKey(b.Trigger), b.Id);
                Assert.Contains(b.Tier, new[] { BarkRules.TierSafe, BarkRules.TierAct2, BarkRules.TierFinished });
                if (b.Tier != BarkRules.TierFinished) continue;
                Assert.False(string.IsNullOrEmpty(b.SafeSibling), b.Id);
                var sibling = Data.Barks[b.SafeSibling];
                Assert.Equal(BarkRules.TierSafe, sibling.Tier);
                Assert.Equal(b.Character, sibling.Character);
                Assert.Equal(b.Trigger, sibling.Trigger);
            }
        }

        [Fact]
        public void Barks_SafeModeUsesTheSafeSiblingAndFinishedModeSkipsIt()
        {
            Assert.Equal(new[] { "bark.gustave.15" }, Lines("gustave", Ids.BarkTriggers.CanvasTruth, SpoilerMode.Safe, 3));
            Assert.Equal(new[] { "bark.gustave.16" }, Lines("gustave", Ids.BarkTriggers.CanvasTruth, SpoilerMode.Finished, 3));
            Assert.Equal(new[] { "bark.maelle.08" }, Lines("maelle", Ids.BarkTriggers.Act3Start, SpoilerMode.Safe, 3));
            Assert.Equal(new[] { "bark.maelle.09" }, Lines("maelle", Ids.BarkTriggers.Act3Start, SpoilerMode.Finished, 3));
            // No finished line ever plays in safe mode.
            foreach (var ch in Data.Characters.Keys)
                foreach (var tr in Data.BarkTriggers.Keys)
                    for (var act = 1; act <= 3; act++)
                        Assert.All(BarkRules.Lines(ch, tr, SpoilerMode.Safe, act), b => Assert.NotEqual(BarkRules.TierFinished, b.Tier));
        }

        [Fact]
        public void Barks_MotherLinesOnlyInFinishedModeDuringActThree()
        {
            Assert.Contains("Act III only", Data.BarkTriggers[Ids.BarkTriggers.CanvasTruth].Description);
            var finished = Data.Barks.Values.Where(b => b.Tier == BarkRules.TierFinished && b.Trigger == Ids.BarkTriggers.CanvasTruth).ToList();
            Assert.NotEmpty(finished);
            foreach (var b in finished)
            {
                Assert.DoesNotContain(b.Id, Lines(b.Character, b.Trigger, SpoilerMode.Finished, 1));
                Assert.DoesNotContain(b.Id, Lines(b.Character, b.Trigger, SpoilerMode.Finished, 2));
                Assert.Contains(b.Id, Lines(b.Character, b.Trigger, SpoilerMode.Finished, 3));
                Assert.DoesNotContain(b.Id, Lines(b.Character, b.Trigger, SpoilerMode.Safe, 3));
            }
        }

        [Fact]
        public void Barks_Act2LinesPlayInEitherModeFromActTwo()
        {
            Assert.DoesNotContain("bark.lune.03", Lines("lune", Ids.BarkTriggers.TurnStart, SpoilerMode.Safe, 1));
            Assert.Contains("bark.lune.03", Lines("lune", Ids.BarkTriggers.TurnStart, SpoilerMode.Safe, 2));
            Assert.Contains("bark.lune.03", Lines("lune", Ids.BarkTriggers.TurnStart, SpoilerMode.Finished, 3));
            Assert.Contains("bark.lune.04", Lines("lune", Ids.BarkTriggers.TurnStart, SpoilerMode.Safe, 1));
        }

        [Fact]
        public void Barks_TriggerActWindowsFollowTheirDescriptions()
        {
            Assert.Contains("During Act I", Data.BarkTriggers[Ids.BarkTriggers.DecoyFound].Description);
            Assert.Contains("Act II or later", Data.BarkTriggers[Ids.BarkTriggers.SoarrieHandoff].Description);
            Assert.True(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.DecoyFound, 1));
            Assert.False(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.DecoyFound, 2));
            Assert.False(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.SoarrieHandoff, 1));
            Assert.True(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.SoarrieHandoff, 3));
            Assert.False(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.Act2Start, 3));
            Assert.True(BarkRules.TriggerAllowsAct(Ids.BarkTriggers.TurnStart, 1));
        }

        [Fact]
        public void Barks_CooldownCountsTheSpeakersOwnTurns()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            var barks = t.Ctx.Barks;
            Assert.Equal(2, BarkRules.CooldownTurns(Ids.BarkTriggers.Attacked));
            Assert.Equal("bark.gustave.11", barks.Say(S(0), Ids.BarkTriggers.Attacked));
            Assert.Null(barks.Say(S(0), Ids.BarkTriggers.Attacked));
            barks.TurnEnded(S(1));                                            // another seat's turns do not count
            barks.TurnEnded(S(1));
            barks.TurnEnded(S(0));
            Assert.Null(barks.Say(S(0), Ids.BarkTriggers.Attacked));
            Assert.Equal("bark.paintress.08", barks.Say(S(1), Ids.BarkTriggers.Attacked)); // cooldowns are per seat
            barks.TurnEnded(S(0));
            Assert.Equal("bark.gustave.11", barks.Say(S(0), Ids.BarkTriggers.Attacked));
            // 0 = no cooldown
            Assert.NotNull(barks.Say(S(0), Ids.BarkTriggers.Overcharge));
            Assert.NotNull(barks.Say(S(0), Ids.BarkTriggers.Overcharge));
        }

        [Fact]
        public void Barks_SeveralLinesTakeTheLeastRecentlyUsed()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            var barks = t.Ctx.Barks;
            Assert.Equal("bark.gustave.03", barks.Say(S(0), Ids.BarkTriggers.FlagPlanted));
            barks.TurnEnded(S(0));
            Assert.Equal("bark.gustave.04", barks.Say(S(0), Ids.BarkTriggers.FlagPlanted));
            barks.TurnEnded(S(0));
            Assert.Equal("bark.gustave.03", barks.Say(S(0), Ids.BarkTriggers.FlagPlanted));
        }

        [Fact]
        public void Barks_GoalMoveIsForBotSeatsOnly()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            Assert.Null(t.Ctx.Barks.Say(S(0), Ids.BarkTriggers.GoalMove));
            Assert.Equal("bark.paintress.05", t.Ctx.Barks.Say(S(1), Ids.BarkTriggers.GoalMove));
        }

        [Fact]
        public void Barks_GameStartLetsEachCharacterSpeakOnceHumanFirst()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Eyrie, "paintress", bot: true).Seat(1, EngineId.Marquise, "gustave"));
            Assert.Equal(new[] { "bark.gustave.01", "bark.paintress.03" }, t.Board.Bubbles.Select(b => b.Value).ToArray());
            Assert.Equal(S(1), t.Board.Bubbles[0].Key);
        }

        [Fact]
        public void Barks_TurnStartOnlyWhenNothingMoreSpecificIsDue()
        {
            var gustave = new StubModule("gustave");
            gustave.StubWin.AnnounceResult = true;
            gustave.StubWin.ConfirmResult = false;
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true);
            var t = TestTable.Start(board, Stubs(gustave, new StubModule("paintress")));
            t.PlayTurn(0);
            Assert.Contains("bark.gustave.05", Said(t, 0));                 // a plain Birdsong: the premise line
            board.Vp(0, 10);
            t.PlayTurn(1).PlayTurn(0);                                        // act II; the turn end opens it
            board.Bubbles.Clear();
            t.PlayTurn(1).PlayTurn(0);                                        // banner up at the end of seat 0's turn
            Assert.Contains("bark.gustave.09", Said(t, 0));
            board.Bubbles.Clear();
            t.PlayTurn(1).PlayTurn(0);                                        // confirm fails at Birdsong
            var said = Said(t, 0);
            Assert.Equal("bark.gustave.10", said[0]);
            Assert.DoesNotContain("bark.gustave.05", said);
        }

        [Fact]
        public void Barks_IdleNeedsFourSilentOwnTurnsAndOnlyOneSpeaker()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondRanger, "verso", bot: true);
            var t = TestTable.Start(board);
            var barks = t.Ctx.Barks;
            Assert.Null(barks.Pick(S(0), Ids.BarkTriggers.Idle));             // just spoke at game start
            for (var i = 0; i < 4; i++) { barks.TurnEnded(S(0)); barks.TurnEnded(S(1)); }
            Assert.Equal(4, barks.SilentTurns(S(0)));
            board.Bubbles.Clear();
            Assert.NotNull(barks.SayOne(new[] { S(0), S(1) }, Ids.BarkTriggers.Idle));
            Assert.Single(board.Bubbles);
            Assert.Equal(S(0), board.Bubbles[0].Key);                       // tie on silence: turn order
            Assert.Equal("bark.verso.01", barks.SayOne(new[] { S(0), S(1) }, Ids.BarkTriggers.Idle)); // seat 0 just spoke
        }

        [Fact]
        public void Barks_ActStartLinesAtTheActChange()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondRanger, "verso", bot: true);
            var t = TestTable.Start(board);
            board.Vp(0, 25);
            board.Bubbles.Clear();
            t.PlayTurn(0);                                                    // act II
            Assert.Contains("bark.gustave.12", Said(t, 0));
            t.PlayTurn(1);                                                    // act III
            Assert.Contains("bark.verso.10", Said(t, 1));                     // act2 tier, safe mode, Act III
        }

        [Fact]
        public void Barks_CanvasTruthAtRoundStartInActThreeOnly()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondRanger, "verso", bot: true);
            var t = TestTable.Start(board, spoilers: SpoilerMode.Finished);
            board.Bubbles.Clear();
            t.PlayRound();
            Assert.DoesNotContain(board.Bubbles, b => Data.Barks[b.Value].Trigger == Ids.BarkTriggers.CanvasTruth);
            board.Vp(0, 25);
            t.PlayRound();                                                    // act III after both turn ends
            board.Bubbles.Clear();
            t.Game.OnRoundStart(0);
            var truth = board.Bubbles.Where(b => Data.Barks[b.Value].Trigger == Ids.BarkTriggers.CanvasTruth).ToList();
            Assert.Single(truth);
            Assert.Equal(BarkRules.TierFinished, Data.Barks[truth[0].Value].Tier);
        }

        [Fact]
        public void Barks_RivalCountdownIsVoicedByBotsThatWouldBlock()
        {
            var esquie = new StubModule("esquie");
            esquie.StubWin.QuestResult = false;
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie", bot: true);
            var t = TestTable.Start(board, Stubs(new StubModule("gustave"), esquie));
            board.Vp(0, 10);
            t.PlayTurn(0).PlayTurn(1);                                        // act II
            board.Bubbles.Clear();
            t.PlayTurn(0);                                                    // Gustave's banner goes up
            Assert.Contains(new KeyValuePair<SeatId, string>(S(1), "bark.esquie.09"), board.Bubbles);

            // A coalition partner never blocks.
            var b2 = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondTinker, "esquie", bot: true).Coalition(1, 0);
            var esquie2 = new StubModule("esquie");
            esquie2.StubWin.QuestResult = false;
            var t2 = TestTable.Start(b2, Stubs(new StubModule("gustave"), esquie2));
            b2.Vp(0, 10);
            t2.PlayTurn(0).PlayTurn(1);
            b2.Bubbles.Clear();
            t2.PlayTurn(0);
            Assert.DoesNotContain(b2.Bubbles, b => b.Value == "bark.esquie.09");
        }

        [Fact]
        public void Barks_AttackedAndBattleWonFollowTheBattle()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Corvids, "white-haired-man", bot: true).Seat(1, EngineId.Marquise, "gustave");
            var t = TestTable.Start(board);
            board.Bubbles.Clear();
            var c = new ClearingId(5);
            t.Game.OnBattleStarting(S(0), S(1), c);
            Assert.Contains(new KeyValuePair<SeatId, string>(S(1), "bark.gustave.11"), board.Bubbles);
            var own = new BattleResult { Clearing = c, Attacker = S(0), Defender = S(1) };
            own.Removed.Add(new RemovedPiece(S(0), PieceKind.Warrior, null));
            t.Game.OnBattleResolved(own);
            Assert.DoesNotContain(board.Bubbles, b => b.Value == "bark.white-haired-man.03");
            var won = new BattleResult { Clearing = c, Attacker = S(0), Defender = S(1) };
            won.Removed.Add(new RemovedPiece(S(1), PieceKind.Warrior, null));
            t.Game.OnBattleResolved(won);
            Assert.Contains(new KeyValuePair<SeatId, string>(S(0), "bark.white-haired-man.03"), board.Bubbles);
        }

        [Fact]
        public void Barks_WinnersAndOneLoserSpeakAtGameOver()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondRanger, "verso", bot: true)
                .Seat(2, EngineId.VagabondThief, "maelle", bot: true);
            var t = TestTable.Start(board);
            board.Bubbles.Clear();
            t.Game.OnGameOver(new[] { S(1) });
            Assert.Contains(new KeyValuePair<SeatId, string>(S(1), "bark.verso.11"), board.Bubbles);
            Assert.Single(board.Bubbles.Where(b => Data.Barks[b.Value].Trigger == Ids.BarkTriggers.Loss));
            Assert.Contains(new KeyValuePair<SeatId, string>(S(0), "bark.gustave.14"), board.Bubbles); // Maelle has no loss line
        }

        [Fact]
        public void Barks_PlainRootGameNeverSpeaks()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie, bot: true);
            var game = new Root33Game(board, board, new ModuleRegistry());
            game.OnGameStart(board.Seats(), Difficulty.Normal, SpoilerMode.Safe);
            game.OnSetupComplete();
            game.OnRoundStart(1);
            game.OnBirdsongStart(S(0));
            Assert.Empty(board.Bubbles);
        }
    }
}
