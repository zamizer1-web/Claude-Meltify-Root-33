using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.SpoilerLayer
{
    /// <summary>The shared spoiler layer of every bot plan (the bot_rules *-spoiler rows' common TRIGGER and PRIORITY).</summary>
    public class SpoilerLayerTests
    {
        private static SeatId S(int i) => new SeatId(i);

        private sealed class ProgressWin : WinConditionBase, IGoalProgress
        {
            public ProgressWin(string winId) : base(winId) { }
            public int Missing = 3;
            public int UnitsMissing(GameContext ctx, SeatId seat) => Missing;
            public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat) => new[] { new ClearingId(6) };
        }

        private sealed class ProgressModule : CharacterModule
        {
            private readonly string _id;
            public ProgressModule(string id) { _id = id; Progress = new ProgressWin(Data.Characters[id].WinCondition); }
            public ProgressWin Progress { get; }
            public override string CharacterId => _id;
            public override IWinCondition Win => Progress;
        }

        private static (TestTable t, Dictionary<string, ProgressModule> m) Table(params (int seat, EngineId e, string ch, bool bot)[] seats)
        {
            var board = FakeBoard.Autumn();
            var reg = new ModuleRegistry();
            var mods = new Dictionary<string, ProgressModule>();
            foreach (var s in seats)
            {
                board.Seat(s.seat, s.e, s.ch, s.bot);
                var m = new ProgressModule(s.ch);
                mods[s.ch] = m;
                reg.Register(s.ch, () => m);
            }
            var t = TestTable.Start(board, reg);
            t.State.GlobalAct = 2;
            return (t, mods);
        }

        private static (TestTable t, Dictionary<string, ProgressModule> m) Three() =>
            Table((0, EngineId.Marquise, "gustave", false), (1, EngineId.Alliance, "sciel", false), (2, EngineId.Eyrie, "paintress", true));

        private static BotView Bot(TestTable t, int seat = 2) => new BotView(t.Ctx, S(seat));

        [Fact]
        public void SpoilerLayer_TriggersOnABannerOrOneStepFromAnUnlockedWin()
        {
            var (t, m) = Three();
            t.Board.Vp(0, 10);
            m["gustave"].Progress.Missing = 2;
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)));
            m["gustave"].Progress.Missing = 1;
            var threat = global::Root33.Core.Engine.SpoilerLayer.Top(Bot(t));
            Assert.Equal(S(0), threat.Seat);
            Assert.False(threat.Banner);
            Assert.Equal(new[] { new ClearingId(6) }, threat.KeyClearings);

            t.Board.Vp(0, 9);                                                // the tune.countdown-vp gate is one more unit
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)));
            m["gustave"].Progress.Missing = 0;
            Assert.Equal(S(0), global::Root33.Core.Engine.SpoilerLayer.Top(Bot(t)).Seat);

            t.State.GlobalAct = 1;                                            // no themed win is unlocked in Act I
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)));
            t.State.GlobalCountdowns.Add(S(0));
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)));
            t.State.GlobalAct = 2;
            Assert.True(global::Root33.Core.Engine.SpoilerLayer.Top(Bot(t)).Banner);
        }

        [Fact]
        public void SpoilerLayer_CuratorIsAThreatOnlyFromActThree()
        {
            var (t, m) = Table((0, EngineId.Duchy, "curator", false), (1, EngineId.Eyrie, "paintress", true));
            t.Board.Vp(0, 12);
            m["curator"].Progress.Missing = 1;
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t, 1)));
            t.State.GlobalAct = 3;
            Assert.Equal(S(0), global::Root33.Core.Engine.SpoilerLayer.Top(Bot(t, 1)).Seat);
        }

        [Fact]
        public void SpoilerLayer_IgnoresCoalitionPartnersAndSwitchedOffWins()
        {
            var (t, m) = Table((0, EngineId.Marquise, "gustave", false), (1, EngineId.VagabondRanger, "verso", true), (2, EngineId.Eyrie, "paintress", true));
            t.Board.Vp(0, 12).Vp(1, 12);
            m["gustave"].Progress.Missing = 1;
            m["verso"].Progress.Missing = 1;
            t.Board.Coalition(1, 0);
            Assert.DoesNotContain(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t, 1)), x => x.Seat == S(0)); // partner
            var fromPaintress = global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)).Select(x => x.Seat).ToList();
            Assert.Contains(S(0), fromPaintress);                           // the partner keeps its themed win
            Assert.DoesNotContain(S(1), fromPaintress);                     // win.verso is off in a coalition
            t.Board.Dominance(0, Suit.Fox);                                  // win.gustave is off with dominance
            Assert.True(Data.WinConditions["win.gustave"].OffWithDominance);
            Assert.DoesNotContain(S(0), global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)).Select(x => x.Seat));
        }

        [Fact]
        public void SpoilerLayer_OrdersBannersThenNextBirdsongThenVp()
        {
            var (t, m) = Three();
            t.Board.Vp(0, 15).Vp(1, 11);
            m["gustave"].Progress.Missing = 1;
            m["sciel"].Progress.Missing = 1;
            t.Board.Turn(2, TurnPhase.Daylight);                             // the bot's own turn: seat 0 plays next
            Assert.Equal(new[] { S(0), S(1) }, global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)).Select(x => x.Seat));
            t.State.GlobalCountdowns.Add(S(1));
            Assert.Equal(new[] { S(1), S(0) }, global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)).Select(x => x.Seat));
        }

        [Fact]
        public void SpoilerLayer_EasyAnswersBannersOnlyAndNeverPreEmpts()
        {
            var (t, m) = Three();
            t.State.GlobalDifficulty = Difficulty.Easy;
            t.Board.Vp(0, 12);
            m["gustave"].Progress.Missing = 1;
            Assert.Empty(global::Root33.Core.Engine.SpoilerLayer.Threats(Bot(t)));
            t.State.GlobalCountdowns.Add(S(0));
            var banner = global::Root33.Core.Engine.SpoilerLayer.Top(Bot(t));
            Assert.NotNull(banner);
            Assert.False(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), banner));
        }

        [Fact]
        public void SpoilerLayer_PreEmptsForBannersAndOnHardForOneStepThreats()
        {
            var (t, _) = Three();
            var banner = new SpoilerThreat(S(0), true, new ClearingId[0]);
            var oneStep = new SpoilerThreat(S(0), false, new ClearingId[0]);
            t.State.GlobalDifficulty = Difficulty.Normal;
            Assert.True(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), banner));
            Assert.False(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), banner, canFinishQuestThisTurn: true));
            Assert.False(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), oneStep));
            t.State.GlobalDifficulty = Difficulty.Hard;
            Assert.True(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), oneStep));
            Assert.False(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), oneStep, ownAnnounceTurn: true));
            Assert.False(global::Root33.Core.Engine.SpoilerLayer.PreEmpts(Bot(t), null));
        }

        [Fact]
        public void SpoilerLayer_EveryPlanHasASpoilerRuleWithTheSharedText()
        {
            foreach (var plan in Data.BotPlans.Values)
            {
                var rows = Data.BotRules.Values.Where(r => r.Plan == plan.Id && r.Id.EndsWith("-spoiler")).ToList();
                Assert.Single(rows);
                Assert.StartsWith("Shared spoiler layer", rows[0].Rule);
            }
        }
    }
}
