using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Clea;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Clea: win.clea, tweak.clea.*, state.clea.covered, bot.clea and its rules. Autumn: the Monolith is 3 (mouse;
    /// neighbours 9 fox, 10 and 11 rabbit), the Flying Manor is 4 (fox; neighbours 10, 11, 12), Stone Wave Cliffs is 6 and
    /// Esquie's Nest is 2. Suits: fox 4 5 8 9, rabbit 1 2 10 11, mouse 3 6 7 12.</summary>
    public class CleaTests
    {
        private const int Nest = 2, Monolith = 3, Manor = 4, Cliffs = 6;

        /// <summary>Eight covered clearings, the Monolith among them (the Flying Manor is not one of them).</summary>
        private static readonly int[] Eight = { 1, 2, 3, 5, 7, 8, 9, 12 };

        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);
        private static string A(int c) => "autumn." + c.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Clea, () => new CleaModule());

        /// <summary>Clea's Lizards at seat 0 (20 in supply), a Marquise at seat 1 (20 in supply).</summary>
        private static FakeBoard Table(bool bot = false) =>
            FakeBoard.Autumn().Seat(0, EngineId.Lizards, CharacterIds.Clea, bot).Seat(1, EngineId.Marquise).Supply(0, 20).Supply(1, 20);

        private static FakeBoard Spread(FakeBoard b, params int[] clearings)
        {
            foreach (var c in clearings) b.Warriors(0, c, 1);
            return b;
        }

        /// <summary>Eight covered clearings with the Monolith, the atelier in the Flying Manor and 10 VP.</summary>
        private static FakeBoard Ready(FakeBoard b) => Spread(b.Vp(0, Tuning.CountdownVp), Eight).Building(0, Manor, PieceTypes.Garden);

        private static TestTable Start(FakeBoard b, int act = 1, Difficulty d = Difficulty.Normal, ModuleRegistry registry = null)
        {
            var t = TestTable.Start(b, registry ?? Registry(), d, SpoilerMode.Finished);
            for (var a = 2; a <= act; a++) t.Game.OnActChanged(a);
            return t;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));

        private static bool Won(FakeBoard b) => b.Winners.Any(w => w.Key == S(0) && w.Value == Ids.WinConditions.Clea);

        // ================================================================= win.clea

        [Fact]
        public void CleaWin_AnnouncesWithEightCoveredIncludingMonolith()
        {
            var board = Ready(Table());
            var t = Start(board, act: 2);
            var win = new CleaWin();
            Assert.Equal(0, win.UnitsMissing(t.Ctx, S(0)));
            Assert.Equal(new[] { C(Monolith), C(Manor) }, win.KeyClearings(t.Ctx, S(0)).Take(2));

            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Clea, board.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            Assert.Empty(board.Winners);

            t.PlayTurn(1).PlayTurn(0);
            Assert.True(Won(board));
            t.Game.OnGameOver(new[] { S(0) });
            Assert.True(board.OverlayShown(Ids.Overlays.CleaEnding));

            // Seven covered clearings: one unit short, no banner.
            var seven = Spread(Table().Vp(0, Tuning.CountdownVp), 1, 2, 3, 5, 7, 8, 9).Building(0, Manor, PieceTypes.Garden);
            var t7 = Start(seven, act: 2);
            Assert.Equal(1, new CleaWin().UnitsMissing(t7.Ctx, S(0)));
            t7.PlayTurn(0);
            Assert.False(seven.Banners.ContainsKey(S(0)));

            // Eight covered without the Monolith: the Monolith is one unit (a warrior there also covers a clearing).
            var noMonolith = Spread(Table().Vp(0, Tuning.CountdownVp), 1, 2, 5, 6, 7, 8, 9, 12).Building(0, Manor, PieceTypes.Garden);
            var tm = Start(noMonolith, act: 2);
            Assert.Equal(1, new CleaWin().UnitsMissing(tm.Ctx, S(0)));
            tm.PlayTurn(0);
            Assert.False(noMonolith.Banners.ContainsKey(S(0)));

            // No atelier in the Flying Manor: no banner.
            var noAtelier = Spread(Table().Vp(0, Tuning.CountdownVp), Eight);
            var ta = Start(noAtelier, act: 2);
            Assert.Equal(1, new CleaWin().UnitsMissing(ta.Ctx, S(0)));
            ta.PlayTurn(0);
            Assert.False(noAtelier.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void CleaWin_ConfirmFailsAfterManorAtelierRazed()
        {
            var board = Ready(Table());
            var t = Start(board, act: 2);
            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Clea, board.Banners[S(0)]);

            t.PlayTurn(1, during: x => board.RemoveBuilding(0, A(Manor), PieceTypes.Garden));
            t.Game.OnRoundStart(t.State.GlobalRound + 1);
            board.Turn(0);
            t.Game.OnBirdsongStart(S(0));
            Assert.Empty(board.Winners);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);

            // The other counterplay: her warriors removed from the Monolith before her Birdsong.
            var other = Ready(Table());
            var t2 = Start(other, act: 2);
            t2.PlayTurn(0);
            t2.PlayTurn(1, during: x => other.Warriors(0, Monolith, 0));
            t2.PlayTurn(0);
            Assert.False(Won(other));
        }

        [Fact]
        public void CleaWin_BlockedBelowTenVp()
        {
            var low = Ready(Table()).Vp(0, Tuning.CountdownVp - 1);
            var t = Start(low, act: 2);
            Assert.Equal(0, new CleaWin().UnitsMissing(t.Ctx, S(0)));
            Assert.Equal(1, View(t).UnitsMissing(S(0)));   // the VP gate is the one unit the Goal Tracker shows
            t.PlayTurn(0);
            Assert.False(low.Banners.ContainsKey(S(0)));

            // Act I: no themed win.
            var act1 = Ready(Table());
            var t1 = Start(act1);
            t1.PlayTurn(0);
            Assert.False(act1.Banners.ContainsKey(S(0)));

            // Active dominance switches it off.
            var dom = Ready(Table()).Dominance(0, Suit.Fox);
            var td = Start(dom, act: 2);
            td.PlayTurn(0);
            Assert.False(dom.Banners.ContainsKey(S(0)));

            // At 10 VP it passes.
            var ok = Ready(Table());
            Start(ok, act: 2).PlayTurn(0);
            Assert.True(ok.Banners.ContainsKey(S(0)));
        }

        // ================================================================= tweaks

        [Fact]
        public void CleaSetup_HomeIsFlyingManor()
        {
            var board = Table();
            TestTable.Start(board, Registry(), spoilers: SpoilerMode.Finished, beforeSetup: g => { g.OnSetupChoice(S(1)); g.OnSetupChoice(S(0)); });
            Assert.Equal(new[] { C(Manor) }, board.HomeRestrictions[S(0)]);
            Assert.False(board.HomeRestrictions.ContainsKey(S(1)));

            // Fallback: a bot is steered to the Flying Manor, a human is warned.
            var bot = Table(bot: true).Unsupported(Ids.BoardActions.RestrictHome).Legal(0, "home:1", "home:2", "home:3", "home:4");
            TestTable.Start(bot, Registry(), spoilers: SpoilerMode.Finished, beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.Equal(new[] { new KeyValuePair<SeatId, string>(S(0), "home:4") }, bot.SubmittedActions);
            var human = Table().Unsupported(Ids.BoardActions.RestrictHome);
            var th = TestTable.Start(human, Registry(), spoilers: SpoilerMode.Finished, beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.Empty(human.HomeRestrictions);
            Assert.Contains(th.Ctx.Warnings, w => w.Contains("Flying Manor"));
        }

        // ================================================================= state.clea.covered

        [Fact]
        public void CleaState_CoveredWrittenAtEveningEndAndRecheckedOnLoad()
        {
            var board = Spread(Table(), 1, 3, 5);
            var t = Start(board);
            Assert.Empty(t.State.CleaCovered);
            t.PlayTurn(0);
            Assert.Equal(new[] { 1, 3, 5 }, t.State.CleaCovered.Select(c => c.Value).OrderBy(v => v));
            Assert.Contains(t.Ctx.Barks.Said, r => r.Seat == S(0) && r.TriggerId == Ids.BarkTriggers.CoverageCount);
            Assert.True(board.OverlayShown(Ids.Overlays.CleaCoverage));

            // Loaded: restored from the save even though the board changed since.
            board.Warriors(0, 5, 0);
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(new[] { 1, 3, 5 }, loaded.State.CleaCovered.Select(c => c.Value).OrderBy(v => v));

            // A save without the row: recomputed from the board.
            var none = t.SaveAndLoad(Registry(), text => string.Join("\n", text.Split('\n').Where(l => !l.StartsWith(Ids.State.CleaCovered + "=", StringComparison.Ordinal))));
            Assert.Equal(new[] { 1, 3 }, none.State.CleaCovered.Select(c => c.Value).OrderBy(v => v));
        }

        // ================================================================= bot.clea

        [Fact]
        public void CleaBot_RecruitPlacesInUncoveredClearings()
        {
            var board = Spread(Table(bot: true), Monolith, Manor).Building(0, Manor, PieceTypes.Garden)
                .Warriors(1, 1, 2).Warriors(1, 10, 1).Hand(0, Suit.Rabbit, Suit.Mouse, Suit.Fox, Suit.Bird);
            var t = Start(board);
            var bot = new CleaBot();
            Assert.Empty(ModuleRegistry.MissingBotRules(bot));
            var v = View(t);

            // Rabbit has the most uncovered clearings (1, 2, 10, 11); within it the fewest enemy warriors in and next to it.
            var ranking = bot.Recruit(v).Select(r => r.Item.Value).ToList();
            Assert.Equal(new[] { 2, 11, 10, 1 }, ranking.Take(4));
            Assert.DoesNotContain(Monolith, ranking);
            Assert.DoesNotContain(Manor, ranking);

            var legal = new[] { "recruit:3:mouse", "recruit:5:fox", "recruit:2:bird", "recruit:2:rabbit" };
            Assert.Equal("recruit:2:rabbit", bot.ChooseAction(v, legal));
            Assert.Equal("recruit:5:fox", bot.ChooseAction(v, new[] { "recruit:3:mouse", "recruit:5:fox" }));
        }

        [Fact]
        public void CleaBot_EasyHalfTheTimeStacksACoveredClearing()
        {
            var board = Spread(Table(bot: true), Monolith, Manor).Building(0, Manor, PieceTypes.Garden).Hand(0, Suit.Rabbit, Suit.Mouse);
            var legal = new[] { "recruit:3:mouse", "recruit:2:rabbit" };

            var easy = Start(board, d: Difficulty.Easy);
            var bot = new CleaBot { EasyCoin = _ => true };
            Assert.Equal("recruit:3:mouse", bot.ChooseAction(View(easy), legal));
            bot.EasyCoin = _ => false;
            Assert.Equal("recruit:2:rabbit", bot.ChooseAction(View(easy), legal));

            // The seeded random source stacks about half the time.
            var seeded = new CleaBot();
            var stacked = Enumerable.Range(0, 200).Count(_ => seeded.ChooseAction(View(easy), legal) == "recruit:3:mouse");
            Assert.InRange(stacked, 60, 140);

            // Normal never stacks.
            var normal = Start(Spread(Table(bot: true), Monolith, Manor).Building(0, Manor, PieceTypes.Garden).Hand(0, Suit.Rabbit, Suit.Mouse));
            var always = new CleaBot { EasyCoin = _ => true };
            Assert.Equal("recruit:2:rabbit", always.ChooseAction(View(normal), legal));
        }

        [Fact]
        public void CleaBot_ConvertsInOutcastClearingsWithoutHerWarrior()
        {
            // Fox is the Outcast: fox clearings 4, 5, 8, 9. Enemy warriors in 5, 8, 9 and 12 (mouse); she already has one in 8.
            var board = Table(bot: true).Outcast(0, Suit.Fox).AcolytesAre(0, 2).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1)
                .Warriors(1, 5, 1).Warriors(1, 8, 2).Warriors(1, 9, 1).Warriors(1, 12, 1).Warriors(0, 8, 1);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            Assert.Equal(new[] { 5, 9 }, bot.ConvertTargets(v).Select(r => r.Item.Value));
            var legal = new[] { "convert:8:1", "convert:9:1", "convert:5:1" };
            Assert.Equal("convert:5:1", bot.Convert(v, legal));
            Assert.Equal("convert:5:1", bot.ChooseAction(v, legal));

            // No Outcast yet: no Convert target.
            var noOutcast = Start(Table(bot: true).Warriors(1, 5, 1));
            Assert.Empty(bot.ConvertTargets(View(noOutcast)));
        }

        [Fact]
        public void CleaBot_SanctifiesVpLeaderBuildings()
        {
            var board = Table(bot: true).Seat(2, EngineId.Eyrie).Outcast(0, Suit.Fox).AcolytesAre(0, 3)
                .Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1)
                .Vp(1, 8).Vp(2, 12).Building(1, 5, PieceTypes.Sawmill).Building(2, 9, PieceTypes.Roost).Building(2, 8, PieceTypes.Roost);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            Assert.Equal(S(2), CleaBot.VpLeader(v));
            var legal = new[] { "sanctify:5:1:sawmill", "sanctify:9:2:roost", "sanctify:8:2:roost" };
            Assert.Equal("sanctify:8:2:roost", bot.Convert(v, legal));

            // Converts come first; Sanctify takes the acolytes left after them.
            board.Warriors(1, 5, 1);
            Assert.Equal("convert:5:1", bot.Convert(v, legal.Concat(new[] { "convert:5:1" }).ToList()));
        }

        [Fact]
        public void CleaBot_EasyNeverSanctifies()
        {
            var board = Table(bot: true).Outcast(0, Suit.Fox).AcolytesAre(0, 3).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1)
                .Vp(1, 12).Building(1, 5, PieceTypes.Sawmill);
            var legal = new[] { "sanctify:5:1:sawmill" };
            var easy = Start(board, d: Difficulty.Easy);
            var bot = new CleaBot();
            Assert.Null(bot.Convert(View(easy), legal));
            Assert.Empty(bot.Allowed(View(easy), legal));
            Assert.Null(bot.ChooseAction(View(easy), legal));

            var normal = Start(Table(bot: true).Outcast(0, Suit.Fox).AcolytesAre(0, 3).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1)
                .Vp(1, 12).Building(1, 5, PieceTypes.Sawmill));
            Assert.Equal("sanctify:5:1:sawmill", bot.ChooseAction(View(normal), legal));
        }

        [Fact]
        public void CleaBot_KeepsBirdCardForMonolith()
        {
            var board = Table(bot: true).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1).Hand(0, Suit.Bird);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            // Her only bird goes to the Monolith, never elsewhere.
            Assert.Equal("recruit:3:bird", bot.ChooseAction(v, new[] { "recruit:5:bird", "recruit:3:bird" }));
            Assert.Null(bot.ChooseAction(v, new[] { "recruit:5:bird" }));
            Assert.Empty(bot.Allowed(v, new[] { "recruit:5:bird" }));
            Assert.Equal(3, bot.CardCost(v, Suit.Bird));

            // A matching card is spent before the kept bird, even for the Monolith.
            board.Hand(0, Suit.Mouse, Suit.Bird);
            Assert.Equal("recruit:3:mouse", bot.Monolith(v, new[] { "recruit:3:bird", "recruit:3:mouse" }));

            // With two birds, one may go elsewhere.
            board.Hand(0, Suit.Bird, Suit.Bird);
            Assert.Equal("recruit:5:bird", bot.ChooseAction(v, new[] { "recruit:5:bird" }));
        }

        [Fact]
        public void CleaBot_CrusadesOnlyWhenMonolithAreaIsOutcastSuit()
        {
            // The Monolith (3) and its neighbours (9, 10, 11) are all mouse on this board.
            FakeBoard Area(bool bot = true) => Table(bot).Suits(9, Suit.Mouse).Suits(10, Suit.Mouse).Suits(11, Suit.Mouse)
                .Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1).Warriors(0, 5, 2).Warriors(1, 5, 1).AcolytesAre(0, 2);
            var legal = new[] { "crusade:5:1", "move:5:8:1" };

            var fox = Area().Outcast(0, Suit.Fox);
            var tf = Start(fox);
            var bot = new CleaBot();
            Assert.False(bot.CrusadeAllowed(View(tf)));
            Assert.Empty(bot.Allowed(View(tf), legal));
            Assert.Null(bot.ChooseAction(View(tf), legal));
            Assert.False(tf.Game.CanStartBattle(S(0), S(1), C(5), false));   // binds Root's own AI for her bot too

            var mouse = Area().Outcast(0, Suit.Mouse);
            var tm = Start(mouse);
            Assert.True(bot.CrusadeAllowed(View(tm)));
            Assert.Equal(legal, bot.Allowed(View(tm), legal));
            Assert.True(tm.Game.CanStartBattle(S(0), S(1), C(5), false));

            // No Outcast set: no Crusade. A human Clea is never limited.
            Assert.False(bot.CrusadeAllowed(View(Start(Area()))));
            var human = Start(Area(bot: false).Outcast(0, Suit.Fox));
            Assert.True(human.Game.CanStartBattle(S(0), S(1), C(5), false));
        }

        [Fact]
        public void CleaBot_RebuildsFlyingManorAtelierFirst()
        {
            var board = Table(bot: true).Warriors(0, Manor, 1).Hand(0, Suit.Fox, Suit.Rabbit, Suit.Mouse);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            var legal = new[] { "recruit:2:rabbit", "recruit:3:mouse", "build:4:fox" };
            Assert.True(bot.AtelierMissing(v));
            Assert.Equal("build:4:fox", bot.ChooseAction(v, legal));

            // Her last bird may rebuild it.
            board.Hand(0, Suit.Bird);
            Assert.Equal("build:4:bird", bot.ChooseAction(v, new[] { "recruit:2:bird", "build:4:bird" }));

            // With the atelier standing, step 1 recruits.
            board.Building(0, Manor, PieceTypes.Garden).Hand(0, Suit.Fox, Suit.Rabbit, Suit.Mouse);
            Assert.False(bot.AtelierMissing(v));
            Assert.StartsWith("recruit:", bot.ChooseAction(v, legal));
        }

        [Fact]
        public void CleaBot_KeepsAWarriorInFlyingManor()
        {
            var board = Table(bot: true).Building(0, Manor, PieceTypes.Garden).Warriors(1, Manor, 1).Hand(0, Suit.Fox).Outcast(0, Suit.Mouse);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            Assert.Equal("recruit:4:fox", bot.Home(v, new[] { "recruit:4:fox" }));
            Assert.Equal("recruit:4:fox", bot.ChooseAction(v, new[] { "recruit:4:fox" }));

            // A Crusade move never takes her last warrior out of the Flying Manor.
            board.Warriors(0, Manor, 1);
            Assert.Null(bot.Home(v, new[] { "recruit:4:fox" }));
            Assert.Empty(bot.Allowed(v, new[] { "move:4:12:1" }));
            board.Warriors(0, Manor, 2);
            Assert.Equal(new[] { "move:4:12:1" }, bot.Allowed(v, new[] { "move:4:12:1" }));
        }

        [Fact]
        public void CleaBot_KeepsOutcastCardsForConspiracies()
        {
            var board = Table(bot: true).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1).Outcast(0, Suit.Fox)
                .Hand(0, Suit.Fox, Suit.Rabbit, Suit.Bird, Suit.Bird);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            Assert.Equal(0, bot.CardCost(v, Suit.Rabbit));
            Assert.Equal(1, bot.CardCost(v, Suit.Bird));
            Assert.Equal(2, bot.CardCost(v, Suit.Fox));
            // A surplus bird is revealed before the Outcast-suit fox.
            Assert.Equal("recruit:5:bird", bot.ChooseAction(v, new[] { "recruit:5:fox", "recruit:5:bird" }));
            // A fox card is spent only when nothing else can do it.
            board.Hand(0, Suit.Fox, Suit.Bird);
            Assert.Equal("recruit:5:fox", bot.ChooseAction(v, new[] { "recruit:5:fox", "recruit:5:bird" }));
        }

        [Fact]
        public void CleaBot_SacrificesBirdsOnlyWithTwoOrMore()
        {
            var board = Table(bot: true).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1).Hand(0, Suit.Bird, Suit.Fox);
            var t = Start(board);
            var bot = new CleaBot();
            var v = View(t);
            var legal = new[] { CleaAction.SacrificeKey };
            Assert.Null(bot.Hand(v, legal));
            Assert.Empty(bot.Allowed(v, legal));
            Assert.Null(bot.ChooseAction(v, legal));

            board.Hand(0, Suit.Bird, Suit.Bird);
            Assert.Equal(CleaAction.SacrificeKey, bot.Hand(v, legal));
            Assert.Equal(CleaAction.SacrificeKey, bot.ChooseAction(v, legal));
        }

        [Fact]
        public void CleaBot_GoalModeRecoversLostClearingFirst()
        {
            var board = Spread(Table(bot: true), Eight).Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1)
                .Hand(0, Suit.Mouse, Suit.Fox, Suit.Rabbit);
            var t = Start(board);
            t.PlayTurn(0);
            Assert.Equal(Eight.Length + 1, t.State.CleaCovered.Count);

            // Before her next turn she loses the Monolith (2 enemy warriors there now) and Spring Meadows (5).
            board.Warriors(0, Monolith, 0).Warriors(1, Monolith, 2).Warriors(0, 5, 0);
            var bot = new CleaBot();
            var v = View(t);
            Assert.True(bot.InGoalMode(v));
            Assert.Equal(new[] { Monolith, 5 }, bot.GoalMode(v).Select(r => r.Item.Value));
            var legal = new[] { "recruit:10:rabbit", "recruit:5:fox", "recruit:3:mouse" };
            Assert.Equal("recruit:3:mouse", bot.ChooseAction(v, legal));
            Assert.Equal("recruit:5:fox", bot.ChooseAction(v, new[] { "recruit:10:rabbit", "recruit:5:fox" }));

            // Below tune.clea-goal-coverage covered clearings there is no goal mode and step 1 decides.
            board.Warriors(0, 1, 0).Warriors(0, 2, 0).Warriors(0, 7, 0);
            Assert.False(bot.InGoalMode(v));
            Assert.Empty(bot.GoalMode(v));
        }

        // ================================================================= spoiler layer

        /// <summary>Clea's bot at seat 0 (Lizards), the Paintress at seat 1 (Eyrie).</summary>
        private static FakeBoard SpoilerBoard() =>
            FakeBoard.Autumn().Seat(0, EngineId.Lizards, CharacterIds.Clea, true).Seat(1, EngineId.Eyrie, CharacterIds.Paintress).Supply(0, 20);

        /// <summary>Starts in Act II with the Paintress (a stub module) at seat 1 and her banner up.</summary>
        private static TestTable SpoilerTable(FakeBoard board)
        {
            var registry = Registry().Register(CharacterIds.Paintress, () => new StubModule(CharacterIds.Paintress));
            var t = Start(board, act: 2, registry: registry);
            t.State.GlobalCountdowns.Add(S(1));
            board.Turn(0);
            return t;
        }

        [Fact]
        public void CleaBot_SpoilerUsesOnlyPublicInformation()
        {
            var board = SpoilerBoard().Seat(2, EngineId.Alliance).Seat(3, EngineId.Corvids)
                .Building(0, Manor, PieceTypes.Garden).Warriors(0, Manor, 1).Outcast(0, Suit.Fox).Hand(0, Suit.Fox, Suit.Rabbit)
                .Building(1, 9, PieceTypes.Roost).Warriors(1, 9, 1).Hand(1, Suit.Fox, Suit.Fox, Suit.Bird)
                .SupportersAre(2, Suit.Fox, Suit.Mouse).HiddenPlotAt(3, A(7), "bomb").Hand(3, Suit.Mouse);
            var t = SpoilerTable(board);
            var bot = new CleaBot();
            var v = View(t);
            Assert.Throws<InvalidOperationException>(() => v.Board.HandSuits(S(1)));
            Assert.Throws<InvalidOperationException>(() => v.Board.Supporters(S(2)));
            Assert.Throws<InvalidOperationException>(() => v.Board.HiddenPlotType(S(3), C(7)));

            var legal = new[] { "recruit:2:rabbit", "convert:9:1", "recruit:9:fox" };
            var answer = bot.Spoiler(v, legal);
            Assert.Equal(S(1), answer.Threat);
            Assert.Equal(C(9), answer.Clearing);
            var choice = bot.ChooseAction(v, legal);
            Assert.Equal(answer.Key, choice);

            // Other seats' hidden information changes nothing.
            board.Hand(1, Suit.Mouse).SupportersAre(2, Suit.Rabbit).HiddenPlotAt(3, A(7), "snare").Hand(3, Suit.Bird, Suit.Bird);
            Assert.Equal(choice, bot.ChooseAction(View(t), legal));
        }
    }
}
