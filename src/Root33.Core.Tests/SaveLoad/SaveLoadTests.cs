using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.SaveLoad
{
    /// <summary>Save and load of the whole ModState through hook.save / hook.load (sys.saved-state).</summary>
    public class SaveLoadTests
    {
        private static SeatId S(int i) => new SeatId(i);

        private static ModuleRegistry Stubs(params StubModule[] modules)
        {
            var reg = new ModuleRegistry();
            foreach (var m in modules) reg.Register(m.CharacterId, () => m);
            return reg;
        }

        [Fact]
        public void SaveLoad_WholeModStateSurvivesHookSaveAndLoad()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondRanger, "verso", bot: true);
            var reg = Stubs(new StubModule("gustave"), new StubModule("verso"));
            var t = TestTable.Start(board, reg, Difficulty.Hard, SpoilerMode.Finished);
            board.Vp(0, 12);
            t.PlayTurn(0).PlayTurn(1).PlayTurn(0);                            // act II, Gustave's banner up
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            var s = t.State;
            s.GustaveArm = 2;
            s.GustaveFlags.Add(new ClearingId(5));
            s.GustaveOverchargeArmed = true;
            s.VersoRank = 3;
            s.MaelleStance = "defensive";
            s.LuneTaggedRelic = 7;
            s.LuneStainArmed = "fire";
            s.GlobalPendingVp[S(1)] = 2;
            s.GlobalPendingDraws[S(0)] = 1;
            var before = s.Save();
            Assert.Equal(Data.State.Count, before.Count);

            var loaded = t.SaveAndLoad(Stubs(new StubModule("gustave"), new StubModule("verso")));
            var after = loaded.State.Save();
            foreach (var kv in before) Assert.True(after[kv.Key] == kv.Value, kv.Key + ": " + kv.Value + " became " + after[kv.Key]);
            Assert.Equal(SpoilerMode.Finished, loaded.Ctx.Spoilers);
            Assert.Equal(Difficulty.Hard, loaded.Ctx.Difficulty);
            Assert.Equal(2, loaded.Ctx.Act);
            Assert.Equal(new[] { "gustave", "verso" }, loaded.Game.Modules.Select(m => m.CharacterId));
        }

        [Fact]
        public void SaveLoad_LoadedGameKeepsPlayingFromTheSavedAct()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true);
            var t = TestTable.Start(board);
            board.Vp(1, 20);
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            var loaded = t.SaveAndLoad();
            loaded.PlayTurn(1);
            Assert.Equal(3, loaded.Ctx.Act);                                 // the act clock goes on, one step per turn end
            Assert.Contains(Ids.StoryEvents.Act3Fall, loaded.Ctx.StoryLog.Select(e => e.EventId));
            Assert.DoesNotContain(Ids.StoryEvents.Act2UnlockWins, loaded.Ctx.StoryLog.Select(e => e.EventId)); // never re-fired
        }

        [Fact]
        public void SaveLoad_SaveTextHasAHeaderAndOneLinePerStateRow()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            var lines = t.Game.OnSave().Split('\n').Where(l => l.Length > 0).ToList();
            Assert.Equal("root33-state 1", lines[0].Trim());
            Assert.Equal(Data.State.Count, lines.Count - 1);
            foreach (var id in Data.State.Keys) Assert.Contains(lines, l => l.StartsWith(id + "="));
        }
    }
}
