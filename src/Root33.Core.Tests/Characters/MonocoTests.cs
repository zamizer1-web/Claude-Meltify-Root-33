using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Monoco;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    public class MonocoTests
    {
        private static readonly SeatId Me = new SeatId(3);

        /// <summary>A test opponent whose win reports a configurable Goal Tracker (for the spoiler layer).</summary>
        private sealed class ProgressWin : WinConditionBase, IGoalProgress
        {
            public ProgressWin() : base(Ids.WinConditions.Gustave) { }
            public int Missing { get; set; }
            public List<ClearingId> Keys { get; } = new List<ClearingId>();
            public int UnitsMissing(GameContext ctx, SeatId seat) => Missing;
            public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat) => Keys;
        }

        private sealed class ProgressModule : CharacterModule
        {
            public static readonly ProgressWin Shared = new ProgressWin();
            public override string CharacterId => CharacterIds.Gustave;
            public override IWinCondition Win => Shared;
        }

        private static FakeBoard Board(int opponents = 3)
        {
            var b = FakeBoard.Autumn();
            var engines = new[] { EngineId.Marquise, EngineId.Eyrie, EngineId.Alliance };
            for (var i = 0; i < opponents; i++) b.Seat(i, engines[i]);
            return b.Seat(opponents, EngineId.Hundreds, "monoco", bot: true).Pawn(opponents, "autumn.01");
        }

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Monoco, () => new MonocoModule());

        private static TestTable Start(FakeBoard b, int act = 2, Difficulty d = Difficulty.Normal, ModuleRegistry reg = null)
        {
            var t = TestTable.Start(b, reg ?? Registry(), d);
            t.Ctx.ActClock.Restore(act);
            t.State.GlobalAct = t.Ctx.ActClock.Act;
            return t;
        }

        private static void Feet(TestTable t, params (int foot, int seat)[] feet)
        {
            var order = 0;
            foreach (var (foot, seat) in feet)
            {
                var f = t.State.MonocoFeet[foot];
                f.Earned = true;
                f.Faction = new SeatId(seat);
                f.Order = ++order;
            }
        }

        private static void AllFeet(TestTable t) =>
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Balanced, 2), (Foot.Heavy, 0), (Foot.Almighty, 1));

        private static List<ClearingId> Near(FakeBoard b, int c = 1) => b.PathNeighbours(new ClearingId(c)).OrderBy(x => x.Value).ToList();

        // ================================================================= win.monoco

        [Fact]
        public void MonocoWin_AnnouncesWithFiveFeetFromThreeFactions()
        {
            var b = Board().Vp(3, Tuning.CountdownVp);
            var t = Start(b);
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 0), (Foot.Balanced, 1), (Foot.Heavy, 1), (Foot.Almighty, 1));
            t.PlayTurn(3);
            Assert.False(b.Banners.ContainsKey(Me));
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Balanced, 2), (Foot.Heavy, 0), (Foot.Almighty, 1));
            t.PlayTurn(3);
            Assert.Equal(Ids.WinConditions.Monoco, b.Banners[Me]);
            t.PlayTurn(3);
            Assert.True(t.Ctx.GameOver);
            Assert.Contains(new KeyValuePair<SeatId, string>(Me, Ids.WinConditions.Monoco), b.Winners);
        }

        [Fact]
        public void MonocoWin_ConfirmFailsAfterWarlordRemoved()
        {
            var b = Board().Vp(3, Tuning.CountdownVp);
            var t = Start(b);
            AllFeet(t);
            t.PlayTurn(3);
            Assert.Equal(Ids.WinConditions.Monoco, b.Banners[Me]);
            t.Game.OnWarlordRemoved(Me, new ClearingId(1));
            b.PawnInForest(3, 1);
            t.PlayTurn(3);
            Assert.False(t.Ctx.GameOver);
            Assert.False(b.Banners.ContainsKey(Me));
            Assert.False(t.State.MonocoFeet[Foot.Almighty].Earned);
        }

        [Fact]
        public void MonocoWin_AlmightyOnlyInActThree()
        {
            var b = Board().Pawn(3, "autumn.01").Vp(0, 8);
            var r = new BattleResult { Clearing = new ClearingId(1), Attacker = Me, Defender = new SeatId(0), VpLeaderAtStart = new SeatId(0) };
            r.Removed.Add(new RemovedPiece(new SeatId(0), PieceKind.Building, PieceTypes.Sawmill));
            var t = Start(b, act: 2);
            t.Game.OnBattleResolved(r);
            Assert.True(t.State.MonocoFeet[Foot.Heavy].Earned);
            Assert.False(t.State.MonocoFeet[Foot.Almighty].Earned);
            var t3 = Start(Board().Vp(0, 8), act: Tuning.MonocoAlmightyAct);
            t3.Game.OnBattleResolved(r);
            Assert.True(t3.State.MonocoFeet[Foot.Almighty].Earned);
            Assert.Equal(new SeatId(0), t3.State.MonocoFeet[Foot.Almighty].Faction);
        }

        [Fact]
        public void MonocoWin_FewerOpponentsNeedEveryOpponent()
        {
            var b = Board(opponents: 2).Vp(2, Tuning.CountdownVp);
            var t = Start(b, reg: Registry());
            var seat = new SeatId(2);
            var win = new MonocoWin();
            foreach (var f in t.State.MonocoFeet) { f.Earned = true; f.Faction = new SeatId(0); f.Order = 1; }
            Assert.False(WinRules.AnnouncePasses(t.Ctx, seat, win));
            t.State.MonocoFeet[Foot.Heavy].Faction = new SeatId(1);
            Assert.True(WinRules.AnnouncePasses(t.Ctx, seat, win));
            var solo = Board(opponents: 1).Vp(1, Tuning.CountdownVp);
            var t1 = Start(solo);
            foreach (var f in t1.State.MonocoFeet) { f.Earned = true; f.Faction = new SeatId(0); f.Order = 1; }
            Assert.True(WinRules.AnnouncePasses(t1.Ctx, new SeatId(1), win));
        }

        // ================================================================= tweak.monoco.sacred-river-rebirth

        [Fact]
        public void SacredRiverRebirth_LosesAlmightyAndLatestFoot()
        {
            var b = Board();
            var t = Start(b, act: 3);
            Feet(t, (Foot.Agile, 0), (Foot.Heavy, 1), (Foot.Caster, 2), (Foot.Almighty, 0));
            t.Game.OnWarlordRemoved(Me, new ClearingId(1));
            var f = t.State.MonocoFeet;
            Assert.False(f[Foot.Almighty].Earned);
            Assert.False(f[Foot.Caster].Earned);
            Assert.True(f[Foot.Agile].Earned);
            Assert.True(f[Foot.Heavy].Earned);
            Assert.True(b.OverlayShown(Ids.Overlays.MonocoWheel));
        }

        [Fact]
        public void SacredRiverRebirth_NoFeetNoChange()
        {
            var b = Board();
            var t = Start(b, act: 3);
            t.Game.OnWarlordRemoved(Me, new ClearingId(1));
            Assert.All(t.State.MonocoFeet, f => Assert.False(f.Earned));
            Assert.False(b.OverlayShown(Ids.Overlays.MonocoWheel));
        }

        // ================================================================= bot.monoco

        [Fact]
        public void MonocoBot_BattleScoreMatchesFormula()
        {
            var b = Board();
            var x = Near(b)[0];
            b.Warriors(3, "autumn.01", 2).Warriors(0, x, 1).Building(0, x, PieceTypes.Sawmill);
            var t = Start(b);
            var bot = new MonocoBot();
            Assert.Empty(ModuleRegistry.MissingBotRules(bot));
            var r = bot.BattleScore(new BotView(t.Ctx, Me)).Single(z => z.Item.Clearing == x && z.Item.Defender == new SeatId(0));
            Assert.Equal(Tuning.MonocoBotMissingFoot + Tuning.MonocoBotNewFaction + 15.0 / 16, r.Score, 6);
        }

        [Fact]
        public void MonocoBot_HeavyPenaltyForBattleThatCouldWipeHisStack()
        {
            var b = Board();
            var n = Near(b);
            b.Warriors(3, "autumn.01", 2).Warriors(0, n[0], 3).Building(0, n[0], PieceTypes.Sawmill).Token(1, n[1], "token");
            var t = Start(b);
            Feet(t, (Foot.Caster, 1));
            var ranked = new MonocoBot().BattleScore(new BotView(t.Ctx, Me));
            var risky = ranked.FindIndex(z => z.Item.Clearing == n[0]);
            var safe = ranked.FindIndex(z => z.Item.Clearing == n[1]);
            Assert.True(ranked[risky].Score > ranked[safe].Score);
            Assert.True(safe < risky);
        }

        [Fact]
        public void MonocoBot_TargetsMatchMissingFoot()
        {
            var b = Board();
            var n = Near(b);
            b.Warriors(3, "autumn.01", 2).Building(0, n[0], PieceTypes.Sawmill).Warriors(0, n[0], 1).Token(1, n[1], "token");
            var t = Start(b);
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Balanced, 2), (Foot.Almighty, 1));
            var targets = new MonocoBot().Targets(new BotView(t.Ctx, Me));
            Assert.NotEmpty(targets);
            Assert.All(targets, z => Assert.Equal(Foot.Heavy, z.Item.Foot));
            Assert.Equal(n[0], targets[0].Item.Battle.Clearing);
        }

        [Fact]
        public void MonocoBot_AlmightyTargetsLeaderLeastGuardedBuilding()
        {
            var b = Board().Vp(0, 15);
            var n = Near(b);
            b.Warriors(3, "autumn.01", 2).Building(0, n[0], PieceTypes.Sawmill).Warriors(0, n[0], 1)
                .Building(0, n[1], PieceTypes.Sawmill);
            var t = Start(b, act: Tuning.MonocoAlmightyAct);
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Balanced, 2), (Foot.Heavy, 1));
            var top = new MonocoBot().Targets(new BotView(t.Ctx, Me))[0].Item;
            Assert.Equal(Foot.Almighty, top.Foot);
            Assert.Equal(n[1], top.Battle.Clearing);
        }

        [Fact]
        public void MonocoBot_EasyAdvancesOnlyOutnumberingByTwo()
        {
            var b = Board();
            var x = Near(b)[0];
            b.Building(0, x, PieceTypes.Sawmill);
            var t = Start(b, d: Difficulty.Easy);
            Assert.Empty(new MonocoBot().Targets(new BotView(t.Ctx, Me)));
            b.Warriors(3, "autumn.01", 1);
            Assert.NotEmpty(new MonocoBot().Targets(new BotView(t.Ctx, Me)));
        }

        [Fact]
        public void MonocoBot_CommandsRatsToTargetTurnBefore()
        {
            var b = Board();
            var target = new ClearingId(1);
            var near = Near(b)[0];
            var d = MonocoBot.Distances(b, target);
            var far = d.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            b.Pawn(3, "autumn." + far.Value.ToString("00")).Warriors(3, near, 1).Warriors(3, d.First(kv => kv.Value == 3).Key, 2);
            var t = Start(b);
            var rats = new MonocoBot().BringRats(new BotView(t.Ctx, Me), target);
            Assert.Equal(near, rats[0].Item);
        }

        [Fact]
        public void MonocoBot_EasyWarlordAdvancesAlone()
        {
            var b = Board();
            b.Warriors(3, Near(b)[0], 2);
            var t = Start(b, d: Difficulty.Easy);
            Assert.Empty(new MonocoBot().BringRats(new BotView(t.Ctx, Me), new ClearingId(1)));
        }

        [Fact]
        public void MonocoBot_PicksMoodThatBoostsPlannedBattle()
        {
            var b = Board().MoodIs(3, "mood.a");
            var t = Start(b);
            var view = new BotView(t.Ctx, Me);
            var bot = new MonocoBot();
            var moods = new[] { "mood.a", "mood.b" };
            Assert.Equal("mood.b", bot.Mood(view, moods, m => m == "mood.b" ? 2 : 1));
            Assert.Equal("mood.a", bot.Mood(view, moods, m => 1));
        }

        [Fact]
        public void MonocoBot_BuildsArenasAlongRoute()
        {
            var b = Board();
            var d = MonocoBot.Distances(b, new ClearingId(1));
            var target = d.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            var path = MonocoBot.PathTo(b, new ClearingId(1), target);
            var mid = path[1];
            b.Warriors(3, mid, 2);
            var t = Start(b);
            var arenas = new MonocoBot().Arenas(new BotView(t.Ctx, Me), target);
            if (b.FreeSlots(mid) > 0) Assert.Equal(mid, arenas[0].Item);
            Assert.DoesNotContain(arenas, a => !path.Contains(a.Item));
        }

        [Fact]
        public void MonocoBot_EscortsWarlordWithTwoAfterThreeFeet()
        {
            var b = Board();
            var t = Start(b);
            var bot = new MonocoBot();
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1));
            Assert.Equal(0, bot.Escort(new BotView(t.Ctx, Me)));
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Heavy, 2));
            Assert.Equal(Tuning.MonocoBotEscort, bot.Escort(new BotView(t.Ctx, Me)));
            b.Warriors(3, "autumn.01", Tuning.MonocoBotEscort);
            Assert.Equal(0, bot.Escort(new BotView(t.Ctx, Me)));
        }

        [Fact]
        public void MonocoBot_AnnounceTurnEndsInStrongestEnemyFreeClearing()
        {
            var b = Board().Vp(3, Tuning.CountdownVp);
            var n = Near(b);
            b.Warriors(3, n[0], 3).Warriors(3, n[1], 4).Warriors(0, n[1], 1);
            var t = Start(b);
            var bot = new MonocoBot();
            Assert.Empty(bot.Announce(new BotView(t.Ctx, Me)));
            AllFeet(t);
            Assert.Equal(n[0], bot.Announce(new BotView(t.Ctx, Me))[0].Item);
        }

        private static (FakeBoard b, ClearingId key, ClearingId goal) SpoilerBoard()
        {
            var b = FakeBoard.Autumn().Seat(0, EngineId.Marquise, CharacterIds.Gustave).Seat(1, EngineId.Eyrie).Seat(2, EngineId.Alliance)
                .Seat(3, EngineId.Hundreds, "monoco", bot: true).Pawn(3, "autumn.01").Vp(0, 9);
            var n = b.PathNeighbours(new ClearingId(1)).OrderBy(x => x.Value).ToList();
            b.Warriors(3, "autumn.01", 2).Warriors(0, n[0], 1).Token(1, n[1], "token");
            return (b, n[0], n[1]);
        }

        private static ModuleRegistry SpoilerRegistry() =>
            Registry().Register(CharacterIds.Gustave, () => new ProgressModule());

        [Fact]
        public void MonocoBot_SpoilerHardAnswersOneStepAheadOfGoalSteps()
        {
            ProgressModule.Shared.Missing = 0;
            var (b, key, goal) = SpoilerBoard();
            ProgressModule.Shared.Keys.Clear();
            ProgressModule.Shared.Keys.Add(key);
            var legal = new[] { "battle:" + key.Value + ":0", "battle:" + goal.Value + ":1" };
            var hard = Start(b, d: Difficulty.Hard, reg: SpoilerRegistry());
            Assert.Equal(legal[0], new MonocoBot().ChooseAction(new BotView(hard.Ctx, Me), legal));
            var normal = Start(b, d: Difficulty.Normal, reg: SpoilerRegistry());
            Assert.Equal(legal[1], new MonocoBot().ChooseAction(new BotView(normal.Ctx, Me), legal));
        }

        [Fact]
        public void MonocoBot_SpoilerBreaksMetPartBeforeBlockingMissingOne()
        {
            ProgressModule.Shared.Missing = 0;
            var (b, key, goal) = SpoilerBoard();
            ProgressModule.Shared.Keys.Clear();
            ProgressModule.Shared.Keys.Add(goal);
            ProgressModule.Shared.Keys.Add(key);
            var t = Start(b, d: Difficulty.Hard, reg: SpoilerRegistry());
            var a = new MonocoBot().Spoiler(new BotView(t.Ctx, Me));
            Assert.NotNull(a);
            Assert.True(a.BreaksMetPart);
            Assert.Equal(key, a.Clearing);
        }
    }
}
