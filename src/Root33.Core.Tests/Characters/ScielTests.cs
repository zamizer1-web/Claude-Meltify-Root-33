using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Sciel;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Every test the Sciel rows name: win.sciel, tweak.sciel.twilight and bot.sciel's rules.</summary>
    public class ScielTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Sciel, () => new ScielModule());

        private static void SetAct(TestTable t, int act)
        {
            t.Ctx.ActClock.Restore(act);
            t.State.GlobalAct = t.Ctx.ActClock.Act;
        }

        /// <summary>Sciel (seat 0, Alliance) with five Sanctuaries spanning fox (5, 9), rabbit (11) and mouse (6, 7), an
        /// Aquafarm at 5 and 10 VP; a plain Marquise at seat 1 with warriors in Lumière (1).</summary>
        private static FakeBoard WinBoard(bool bot = false)
        {
            var b = FakeBoard.Autumn()
                .Seat(0, EngineId.Alliance, CharacterIds.Sciel, bot)
                .Seat(1, EngineId.Marquise)
                .Warriors(1, 1, 3).Building(1, 1, PieceTypes.Sawmill)
                .Building(0, 5, "base.fox").Vp(0, 10);
            foreach (var c in new[] { 5, 9, 11, 6, 7 }) b.Token(0, c, PieceTypes.Sympathy);
            return b;
        }

        private static TestTable StartWin(FakeBoard b, int act = 2)
        {
            var t = TestTable.Start(b, Registry());
            SetAct(t, act);
            return t;
        }

        // ================================================================= win.sciel

        [Fact]
        public void ScielWin_AnnouncesWithFiveSanctuariesAndAquafarm()
        {
            var b = WinBoard();
            var t = StartWin(b);
            Assert.True(new ScielWin().Announce(t.Ctx, S(0)));
            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Sciel, b.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);

            // Without an Aquafarm, or with only four Sanctuaries, or missing a suit, there is no announce.
            var noFarm = WinBoard().RemoveBuilding(0, "5", "base");
            Assert.False(new ScielWin().Announce(StartWin(noFarm).Ctx, S(0)));
            var four = WinBoard().RemoveToken(0, "9", PieceTypes.Sympathy);
            Assert.False(new ScielWin().Announce(StartWin(four).Ctx, S(0)));
            var noRabbit = WinBoard().RemoveToken(0, "11", PieceTypes.Sympathy).Token(0, 12, PieceTypes.Sympathy);
            Assert.False(new ScielWin().Announce(StartWin(noRabbit).Ctx, S(0)));
            // An enemy warrior standing on a Lantern breaks that Sanctuary before the announce.
            var walkedIn = WinBoard().Warriors(1, 9, 1);
            Assert.False(new ScielWin().Announce(StartWin(walkedIn).Ctx, S(0)));
        }

        [Fact]
        public void ScielWin_ConfirmIgnoresWalkInWarriors()
        {
            var b = WinBoard();
            var t = StartWin(b).PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            t.PlayTurn(1, during: x => x.Board.Warriors(1, 9, 2).Warriors(1, 6, 1));
            t.PlayTurn(0);
            Assert.Contains(b.Winners, w => w.Key == S(0) && w.Value == Ids.WinConditions.Sciel);
        }

        [Fact]
        public void ScielWin_ConfirmFailsAfterLanternRemoved()
        {
            var b = WinBoard();
            var t = StartWin(b).PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            t.PlayTurn(1, during: x => x.Board.RemoveToken(0, "7", PieceTypes.Sympathy));
            t.PlayTurn(0);
            Assert.Empty(b.Winners);
            Assert.False(b.Banners.ContainsKey(S(0)));
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);

            // A building or token placed in a Lantern clearing is also a real answer.
            var b2 = WinBoard();
            var t2 = StartWin(b2).PlayTurn(0);
            t2.PlayTurn(1, during: x => x.Board.Token(1, 6, PieceTypes.Wood));
            t2.PlayTurn(0);
            Assert.Empty(b2.Winners);

            // So is razing her only Aquafarm.
            var b3 = WinBoard();
            var t3 = StartWin(b3).PlayTurn(0);
            t3.PlayTurn(1, during: x => x.Board.RemoveBuilding(0, "5", "base"));
            t3.PlayTurn(0);
            Assert.Empty(b3.Winners);
        }

        [Fact]
        public void ScielWin_BlockedInActOne()
        {
            var b = WinBoard();
            var t = StartWin(b, act: 1);
            Assert.True(new ScielWin().Announce(t.Ctx, S(0))); // the themed part holds ...
            t.PlayTurn(0);
            Assert.False(b.Banners.ContainsKey(S(0)));         // ... but Act I blocks the win
            Assert.Empty(t.State.GlobalCountdowns);

            // The other gates: 9 VP, or an active dominance card.
            var low = WinBoard().Vp(0, Tuning.CountdownVp - 1);
            var tl = StartWin(low).PlayTurn(0);
            Assert.False(low.Banners.ContainsKey(S(0)));
            var dom = WinBoard().Dominance(0, Suit.Fox);
            StartWin(dom).PlayTurn(0);
            Assert.False(dom.Banners.ContainsKey(S(0)));
        }

        // ================================================================= tweak.sciel.twilight

        /// <summary>Her own turn with an Evening in which <paramref name="evening"/> runs (battles happen there).</summary>
        private static void EveningTurn(TestTable t, int seat, Action<TestTable> daylight = null, Action<TestTable> evening = null)
        {
            var s = S(seat);
            if (t.Ctx.TurnOrder[0] == s) t.Game.OnRoundStart(0);
            t.Board.Turn(seat, TurnPhase.Birdsong);
            t.Game.OnBirdsongStart(s);
            t.Board.Turn(seat, TurnPhase.Daylight);
            daylight?.Invoke(t);
            t.Board.Turn(seat, TurnPhase.Evening);
            t.Game.OnEveningStart(s);
            evening?.Invoke(t);
            t.Game.OnEveningEnd(s);
            t.Game.OnTurnEnd(s);
        }

        private static BattleResult Battle(int attacker, int defender, int clearing, params RemovedPiece[] removed)
        {
            var r = new BattleResult { Clearing = new ClearingId(clearing), Attacker = S(attacker), Defender = S(defender) };
            r.Removed.AddRange(removed);
            return r;
        }

        private static RemovedPiece Warrior(int owner) => new RemovedPiece(S(owner), PieceKind.Warrior, null);

        private static TestTable TwilightTable(out FakeBoard b, bool updateRules = true)
        {
            b = FakeBoard.Autumn().Seat(0, EngineId.Alliance, CharacterIds.Sciel).Seat(1, EngineId.Marquise)
                .Warriors(0, 5, 3).Warriors(1, 5, 2).Warriors(0, 6, 3).Warriors(1, 6, 2);
            return TestTable.Start(b, Registry(), updateRules: updateRules); // v1.1 row: on unless a test says otherwise
        }

        [Fact]
        public void Twilight_SunAndMoonAddHitToFirstEveningBattle()
        {
            var t = TwilightTable(out var b);
            // Turn 1: a Lantern (Sun) and a battle she starts that removes an enemy warrior (Moon).
            EveningTurn(t, 0,
                daylight: x => x.Game.OnPiecePlaced(S(0), PieceKind.Token, PieceTypes.Sympathy, new ClearingId(5)),
                evening: x =>
                {
                    x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5));
                    x.Game.OnBattleResolved(Battle(0, 1, 5, Warrior(1)));
                });
            Assert.True(t.State.ScielSun);
            Assert.True(t.State.ScielMoon);
            Assert.False(b.ExtraHits.ContainsKey(S(0))); // not armed in that Evening: she lacked both at its start
            t.PlayTurn(1);

            // Turn 2: armed at the start of the Evening; her first battle gets +tune.sciel-twilight-hits, the second none.
            EveningTurn(t, 0, evening: x =>
            {
                Assert.True(x.State.ScielTwilightArmed);
                x.Game.OnBattleStarting(S(0), S(1), new ClearingId(6));
                Assert.Equal(Tuning.ScielTwilightHits, b.ExtraHits[S(0)]);
                x.Game.OnBattleResolved(Battle(0, 1, 6, Warrior(1)));
                x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5));
                x.Game.OnBattleResolved(Battle(0, 1, 5));
            });
            Assert.Equal(Tuning.ScielTwilightHits, b.ExtraHits[S(0)]);
            // Another seat's battle never gets her hit.
            t.Game.OnBattleStarting(S(1), S(0), new ClearingId(5));
            Assert.False(b.ExtraHits.ContainsKey(S(1)));
        }

        [Fact]
        public void Twilight_ChargesResetAfterBoostedBattle()
        {
            var t = TwilightTable(out var b);
            t.State.ScielSun = true;
            t.State.ScielMoon = true;
            // Holding both without a battle keeps them for a later Evening; the arming lapses at turn end.
            EveningTurn(t, 0);
            Assert.True(t.State.ScielSun && t.State.ScielMoon);
            Assert.False(t.State.ScielTwilightArmed);
            t.PlayTurn(1);

            EveningTurn(t, 0, evening: x =>
            {
                x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5));
                x.Game.OnBattleResolved(Battle(0, 1, 5, Warrior(1)));
                Assert.False(x.State.ScielSun);
                Assert.False(x.State.ScielMoon);
                Assert.False(x.State.ScielTwilightArmed);
            });
            Assert.Equal(Tuning.ScielTwilightHits, b.ExtraHits[S(0)]);

            // The charges survive a save and load.
            t.State.ScielSun = true;
            var loaded = t.SaveAndLoad(Registry());
            Assert.True(loaded.State.ScielSun);
            Assert.False(loaded.State.ScielMoon);
        }

        [Fact]
        public void Twilight_NoHitWithOnlyOneCharge()
        {
            var t = TwilightTable(out var b);
            t.State.ScielSun = true;
            EveningTurn(t, 0, evening: x =>
            {
                Assert.False(x.State.ScielTwilightArmed);
                x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5));
                x.Game.OnBattleResolved(Battle(0, 1, 5));
            });
            Assert.False(b.ExtraHits.ContainsKey(S(0)));
            Assert.True(t.State.ScielSun);   // kept
            Assert.False(t.State.ScielMoon); // nothing removed: no Moon

            var t2 = TwilightTable(out var b2);
            t2.State.ScielMoon = true;
            EveningTurn(t2, 0, evening: x => x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5)));
            Assert.False(b2.ExtraHits.ContainsKey(S(0)));
            // A battle someone else starts gives her no Moon.
            var t3 = TwilightTable(out _);
            t3.Game.OnBattleResolved(Battle(1, 0, 5, Warrior(0), Warrior(1)));
            Assert.False(t3.State.ScielMoon);
        }

        // ================================================================= bot.sciel

        /// <summary>A bot table: Sciel at seat 0, a plain Marquise at 1, optionally the Paintress (Eyrie) at 2 and a plain
        /// Tinker at 3.</summary>
        private static FakeBoard BotBoard(bool paintress = false, bool vagabond = false)
        {
            var b = FakeBoard.Autumn().Seat(0, EngineId.Alliance, CharacterIds.Sciel, bot: true).Seat(1, EngineId.Marquise);
            if (paintress) b.Seat(2, EngineId.Eyrie, CharacterIds.Paintress, bot: true);
            if (vagabond) b.Seat(3, EngineId.VagabondTinker);
            return b;
        }

        private static (TestTable t, BotView v) View(FakeBoard b, Difficulty d = Difficulty.Normal, int act = 1, TurnPhase phase = TurnPhase.Birdsong)
        {
            var t = TestTable.Start(b, Registry(), d);
            SetAct(t, act);
            b.Turn(0, phase);
            return (t, new BotView(t.Ctx, S(0)));
        }

        private static List<int> Items(List<Ranked<ClearingId>> r) => r.Select(x => x.Item.Value).ToList();

        [Fact]
        public void ScielBot_CoversEveryPlanRule()
        {
            var bot = new ScielBot();
            Assert.Equal(Ids.BotPlans.Sciel, bot.PlanId);
            Assert.Empty(ModuleRegistry.MissingBotRules(bot));
            Assert.True(ModuleRegistry.Default().Has(CharacterIds.Sciel));
        }

        [Fact]
        public void ScielBot_SpreadScoreMatchesFormula()
        {
            var b = BotBoard().Token(0, 5, PieceTypes.Sympathy)          // a fox Sanctuary
                .Warriors(1, 6, 1).Warriors(1, 8, 2).Warriors(1, 7, 1);   // 6: one enemy piece; enemies next to 6 and 9
            b.Legal(0, "spread:6", "spread:9", "spread:11");
            var (_, v) = View(b);
            var r = new ScielBot().Spread(v);
            var s6 = Tuning.ScielSpreadBase - 1 - 2 + Tuning.ScielSpreadNewSuit;      // mouse has no Sanctuary
            var s9 = Tuning.ScielSpreadBase - 0 - 1;                                  // fox has one; 7 holds 1 enemy warrior
            var s11 = Tuning.ScielSpreadBase - 0 - 0 + Tuning.ScielSpreadNewSuit;     // rabbit, quiet
            Assert.Equal(s6, ScielBot.SpreadScore(v.Board, S(0), new ClearingId(6)));
            Assert.Equal(new[] { 11, 6, 9 }.OrderByDescending(c => c == 11 ? s11 : c == 6 ? s6 : s9).ThenBy(c => c).ToList(), Items(r));
            Assert.Equal(new double[] { s11, s6, s9 }.OrderByDescending(x => x), r.Select(x => x.Score));
            Assert.Equal("spread:11", new ScielBot().ChooseAction(v, b.LegalActions(S(0))));
        }

        [Fact]
        public void ScielBot_NeverSpreadsIntoTwoEnemyPiecesOrMartialLaw()
        {
            var b = BotBoard().Warriors(1, 8, 1).Building(1, 8, PieceTypes.Sawmill)   // two enemy pieces
                .Warriors(1, 12, ScielBot.MartialLawWarriors)                            // Martial Law
                .Warriors(1, 5, 1);                                                      // one piece: allowed
            b.Legal(0, "spread:8", "spread:12");
            var (_, v) = View(b);
            var bot = new ScielBot();
            Assert.Empty(bot.Spread(v));
            Assert.Null(bot.ChooseAction(v, b.LegalActions(S(0))));
            Assert.True(ScielBot.MartialLaw(v.Board, S(0), new ClearingId(12)));
            b.Legal(0, "spread:8", "spread:12", "spread:5");
            Assert.Equal(new[] { 5 }, Items(bot.Spread(v)));
        }

        [Fact]
        public void ScielBot_RevoltsWhereItMakesASanctuary()
        {
            var b = BotBoard(vagabond: true)
                .Token(0, 9, PieceTypes.Sympathy).Building(1, 9, PieceTypes.Sawmill).Building(1, 9, PieceTypes.Workshop).Pawn(3, "9")
                .Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood)
                .Token(0, 12, PieceTypes.Sympathy).Token(1, 12, PieceTypes.Wood).Building(0, 6, "base.mouse"); // mouse has an Aquafarm
            b.Legal(0, "revolt:9", "revolt:10", "revolt:12");
            var (_, v) = View(b);
            var bot = new ScielBot();
            Assert.Equal(new[] { 10, 9 }, Items(bot.Revolt(v)));   // 10 becomes a Sanctuary at once; 9 keeps the pawn; 12 is excluded
            Assert.Equal("revolt:10", bot.ChooseAction(v, b.LegalActions(S(0))));
        }

        [Fact]
        public void ScielBot_KeepsTwoMatchingSupportersReady()
        {
            // The next Revolt target is rabbit (10): she keeps two rabbit supporters for it.
            var b = BotBoard().Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood).Warriors(1, 6, 1)
                .SupportersAre(0, Suit.Rabbit, Suit.Rabbit, Suit.Fox);
            b.Legal(0, "spread:11", "spread:5");
            var (_, v) = View(b);
            var bot = new ScielBot();
            Assert.Equal(Suit.Rabbit, bot.RevoltReserve(v));
            Assert.Equal(11, bot.Spread(v)[0].Item.Value);                       // 11 scores best ...
            Assert.False(bot.SpreadKeepsReserve(v, new ClearingId(11)));
            Assert.Equal("spread:5", bot.ChooseAction(v, b.LegalActions(S(0)))); // ... but would spend a reserved rabbit
            b.SupportersAre(0, Suit.Rabbit, Suit.Rabbit, Suit.Rabbit, Suit.Fox);
            Assert.Equal("spread:11", bot.ChooseAction(v, b.LegalActions(S(0))));
            // Birds are wild: spending a bird on a fox Spread would also dip into the rabbit reserve.
            b.SupportersAre(0, Suit.Rabbit, Suit.Bird);
            Assert.False(bot.SpreadKeepsReserve(v, new ClearingId(5)));
        }

        [Fact]
        public void ScielBot_EasySometimesTakesFirstLegalRevolt()
        {
            var b = BotBoard(vagabond: true)
                .Token(0, 9, PieceTypes.Sympathy).Building(1, 9, PieceTypes.Sawmill).Pawn(3, "9")
                .Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood);
            b.Legal(0, "revolt:9", "revolt:10");
            var (_, easy) = View(b, Difficulty.Easy);
            var heads = new ScielBot { EasyCoin = _ => true };
            var tails = new ScielBot { EasyCoin = _ => false };
            Assert.Equal(9, heads.RevoltChoice(easy)!.Value.Value);
            Assert.Equal(10, tails.RevoltChoice(easy)!.Value.Value);
            // The seeded coin gives both over a few decisions.
            var seeded = new ScielBot();
            var picks = Enumerable.Range(0, 20).Select(_ => seeded.RevoltChoice(easy)!.Value.Value).ToList();
            Assert.Contains(9, picks);
            Assert.Contains(10, picks);
            // Normal always takes the best-scored one.
            var b2 = BotBoard(vagabond: true)
                .Token(0, 9, PieceTypes.Sympathy).Building(1, 9, PieceTypes.Sawmill).Pawn(3, "9")
                .Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood);
            b2.Legal(0, "revolt:9", "revolt:10");
            var (_, normal) = View(b2);
            Assert.Equal(10, heads.RevoltChoice(normal)!.Value.Value);
        }

        [Fact]
        public void ScielBot_EvictsLoneEnemyFromLantern()
        {
            var b = BotBoard()
                .Token(0, 5, PieceTypes.Sympathy).Warriors(0, 5, 2).Warriors(1, 5, 1)    // lone warrior
                .Token(0, 6, PieceTypes.Sympathy).Warriors(0, 6, 3).Warriors(1, 6, 2)    // two: not a target
                .Token(0, 7, PieceTypes.Sympathy).Warriors(0, 7, 1).Token(1, 7, PieceTypes.Wood).Building(1, 7, PieceTypes.Sawmill); // token, but a building stays
            b.Legal(0, "battle:5", "battle:6", "battle:7");
            var (_, v) = View(b, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            Assert.Equal(new[] { 5, 7 }, Items(bot.Evict(v)));
            Assert.Equal("battle:5", bot.ChooseAction(v, b.LegalActions(S(0))));
            b.Turn(0, TurnPhase.Daylight);
            Assert.Empty(bot.Evict(v)); // Evening only
        }

        [Fact]
        public void ScielBot_EasyEvictsOnlyWithTwoToOne()
        {
            var b = BotBoard().Token(0, 5, PieceTypes.Sympathy).Warriors(0, 5, 1).Warriors(1, 5, 1);
            b.Legal(0, "battle:5");
            var (_, easy) = View(b, Difficulty.Easy, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            Assert.Empty(bot.Evict(easy));
            b.Warriors(0, 5, Tuning.ScielEasyEvictRatio);
            Assert.Equal(new[] { 5 }, Items(bot.Evict(easy)));
            var b2 = BotBoard().Token(0, 5, PieceTypes.Sympathy).Warriors(0, 5, 1).Warriors(1, 5, 1);
            b2.Legal(0, "battle:5");
            var (_, normal) = View(b2, phase: TurnPhase.Evening);
            Assert.Equal(new[] { 5 }, Items(bot.Evict(normal)));
        }

        [Fact]
        public void ScielBot_PrefersTwilightEveningToEvict()
        {
            var b = BotBoard().Token(0, 5, PieceTypes.Sympathy).Warriors(0, 5, 2).Warriors(1, 5, 1);
            b.Legal(0, "battle:5");
            var (t, v) = View(b, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            t.State.ScielSun = true;
            t.State.ScielMoon = true;
            t.State.ScielTwilightArmed = false;          // her next Evening is a Twilight Evening: she waits
            Assert.Empty(bot.Evict(v));
            Assert.Null(bot.ChooseAction(v, b.LegalActions(S(0))));
            t.State.ScielTwilightArmed = true;           // this one is: she evicts with the boosted battle
            Assert.Equal("battle:5", bot.ChooseAction(v, b.LegalActions(S(0))));
            t.State.ScielSun = t.State.ScielMoon = t.State.ScielTwilightArmed = false;
            Assert.Equal("battle:5", bot.ChooseAction(v, b.LegalActions(S(0)))); // no Twilight coming: evict now
        }

        [Fact]
        public void ScielBot_OrganizesIdleWarriorWhenSunShort()
        {
            var b = BotBoard().Warriors(0, 7, 1).Warriors(0, 12, 1).Warriors(1, 9, 1).SupplyOf(0, PieceTypes.Sympathy, 5);
            b.Legal(0, "organize:7", "organize:12");
            var (_, v) = View(b, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            Assert.Equal(new[] { 12, 7 }, Items(bot.Organize(v)));      // 7 sits next to an enemy warrior
            Assert.Equal("organize:12", bot.ChooseAction(v, b.LegalActions(S(0))));

            // A warrior that an eviction needs is not idle.
            b.Token(0, 12, PieceTypes.Sympathy).Warriors(1, 12, 1).Warriors(0, 12, 2);
            b.Legal(0, "organize:7");
            Assert.Equal(new[] { 7 }, Items(bot.Organize(v)));
            // With five Sanctuaries the Sun gauge is full: no Organize.
            foreach (var c in new[] { 1, 2, 3, 4, 5 }) b.Token(0, c, PieceTypes.Sympathy);
            Assert.Empty(bot.Organize(v));
        }

        [Fact]
        public void ScielBot_RecruitsAtThreatenedAquafarm()
        {
            var b = BotBoard().Building(0, 5, "base.fox").Building(0, 6, "base.mouse").Building(0, 11, "base.rabbit")
                .Warriors(1, 1, 1).Warriors(1, 8, 2);
            b.Legal(0, "recruit:5", "recruit:6", "recruit:11");
            var (_, v) = View(b, phase: TurnPhase.Daylight);
            var bot = new ScielBot();
            Assert.Equal(new[] { 6, 5 }, Items(bot.DefendAquafarms(v)));   // 11 is not threatened
            Assert.Equal("recruit:6", bot.ChooseAction(v, b.LegalActions(S(0))));
        }

        [Fact]
        public void ScielBot_KeepsTeacherGroupBesideBiggestLanternCluster()
        {
            var b = BotBoard().Token(0, 3, PieceTypes.Sympathy).Token(0, 10, PieceTypes.Sympathy).Token(0, 11, PieceTypes.Sympathy)
                .Token(0, 6, PieceTypes.Sympathy).Warriors(0, 2, 4).Warriors(0, 6, 1).OfficersAre(0, 1);
            b.Legal(0, "move:2:7", "move:2:9");
            var (_, v) = View(b, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            Assert.Equal(new[] { 3, 10, 11 }, ScielBot.BiggestCluster(v).Select(c => c.Value).OrderBy(x => x));
            var m = bot.TeacherGroup(v);
            Assert.Equal(2, m.From.Value);
            Assert.Equal(9, m.To.Value);                                          // 9 is beside the Monolith (3)
            Assert.Equal("move:2:9", bot.ChooseAction(v, b.LegalActions(S(0))));
            b.Warriors(0, 2, 0).Warriors(0, 9, 4);
            Assert.Null(bot.TeacherGroup(v));                                     // already beside the cluster
        }

        /// <summary>Four quiet Lanterns (1, 2, 3, 4): finish mode.</summary>
        private static FakeBoard FinishBoard(bool finish)
        {
            var b = BotBoard(vagabond: true).Token(0, 1, PieceTypes.Sympathy).Token(0, 2, PieceTypes.Sympathy).Token(0, 3, PieceTypes.Sympathy);
            if (finish) b.Token(0, 4, PieceTypes.Sympathy);
            return b;
        }

        [Fact]
        public void ScielBot_FinishModeHoardsSupporters()
        {
            var b = FinishBoard(true).Warriors(1, 5, 1)
                .Token(0, 9, PieceTypes.Sympathy).Building(1, 9, PieceTypes.Sawmill).Pawn(3, "9");    // a Revolt that leaves the pawn
            b.Legal(0, "spread:5", "revolt:9");
            var (_, v) = View(b);
            var bot = new ScielBot();
            Assert.True(bot.Finish(v));
            Assert.True(bot.InGoalMode(v));
            Assert.Null(bot.ChooseAction(v, b.LegalActions(S(0))));               // neither makes her next Sanctuary
            b.Legal(0, "spread:5", "revolt:9", "spread:7");
            Assert.Equal("spread:7", bot.ChooseAction(v, b.LegalActions(S(0))));  // a quiet clearing: a Sanctuary at once

            var nb = FinishBoard(false).Warriors(1, 5, 1);
            nb.Legal(0, "spread:5");
            var (_, nv) = View(nb);
            Assert.False(bot.Finish(nv));
            Assert.Equal("spread:5", bot.ChooseAction(nv, nb.LegalActions(S(0))));
        }

        [Fact]
        public void ScielBot_FinishModeNeverPullsWarriorsFromCluster()
        {
            // Cluster 1-5-6 plus a lone Lantern at 9: four Sanctuaries. The Paintress's banner is up with a Portrait at 12.
            FakeBoard Board(bool finish)
            {
                var b = BotBoard(paintress: true).Token(0, 1, PieceTypes.Sympathy).Token(0, 5, PieceTypes.Sympathy).Token(0, 6, PieceTypes.Sympathy)
                    .Warriors(0, 1, 3).Building(2, 12, PieceTypes.Roost).Warriors(2, 12, Tuning.PaintressPortraitWarriors);
                if (finish) b.Token(0, 9, PieceTypes.Sympathy);
                b.Legal(0, "move:1:12");
                return b;
            }
            var fb = Board(true);
            var (ft, fv) = View(fb, act: 2, phase: TurnPhase.Evening);
            ft.State.GlobalCountdowns.Add(S(2));
            var bot = new ScielBot();
            Assert.True(bot.Finish(fv));
            Assert.Null(bot.Spoiler(fv));
            Assert.Null(bot.ChooseAction(fv, fb.LegalActions(S(0))));

            var nb = Board(false);
            var (nt, nv) = View(nb, act: 2, phase: TurnPhase.Evening);
            nt.State.GlobalCountdowns.Add(S(2));
            Assert.Equal("move:1:12", bot.ChooseAction(nv, nb.LegalActions(S(0))));
        }

        /// <summary>The announce board, in Evening, with enemy warriors next to her Sanctuaries 6 and 7 (and 9).</summary>
        private static FakeBoard AnnounceBoard(int vp = 10)
        {
            var b = BotBoard().Building(0, 5, "base.fox").Vp(0, vp).Warriors(0, 5, 3).Warriors(1, 8, 2).Warriors(1, 2, 1);
            foreach (var c in new[] { 5, 9, 11, 6, 7 }) b.Token(0, c, PieceTypes.Sympathy);
            return b;
        }

        [Fact]
        public void ScielBot_AnnounceTurnGarrisonsMostExposedSanctuaries()
        {
            var b = AnnounceBoard();
            b.Legal(0, "move:5:1", "move:5:6");
            var (_, v) = View(b, act: 2, phase: TurnPhase.Evening);
            var bot = new ScielBot();
            Assert.True(ScielBot.IsAnnounceTurn(v));
            Assert.Equal(new[] { 6, 7, 9, 11, 5 }, Items(bot.Announce(v)));    // exposure 3, 3, 1, 0; 5 already garrisoned
            Assert.Equal("move:5:6", bot.ChooseAction(v, b.LegalActions(S(0))));
            b.Warriors(0, 6, 1);
            Assert.Equal(7, bot.Announce(v)[0].Item.Value);                    // one each before a second anywhere
            b.Vp(0, 9);
            Assert.Empty(bot.Announce(v));                                    // not the announce turn
        }

        [Fact]
        public void ScielBot_AnnounceTurnKeepsSupporters()
        {
            var b = AnnounceBoard().Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood).SupportersAre(0, Suit.Mouse, Suit.Rabbit, Suit.Rabbit);
            b.Legal(0, "spread:12", "revolt:10");
            var (_, v) = View(b, act: 2, phase: TurnPhase.Birdsong);
            var bot = new ScielBot();
            Assert.True(ScielBot.IsAnnounceTurn(v));
            Assert.Null(bot.ChooseAction(v, b.LegalActions(S(0))));            // no Spread or Revolt: she keeps her supporters
            b.Vp(0, 9);
            Assert.Equal("spread:12", bot.ChooseAction(v, b.LegalActions(S(0))));
        }

        /// <summary>The Paintress (seat 2) has a Portrait at 12, where Sciel has a Lantern; 10 is a better plain Revolt.</summary>
        private static FakeBoard SpoilerBoard()
        {
            var b = BotBoard(paintress: true)
                .Token(0, 12, PieceTypes.Sympathy).Building(2, 12, PieceTypes.Roost).Warriors(2, 12, Tuning.PaintressPortraitWarriors)
                .Token(0, 10, PieceTypes.Sympathy).Token(1, 10, PieceTypes.Wood).Building(1, 10, PieceTypes.Sawmill)
                .Warriors(0, 8, 2).Vp(2, 12);
            return b;
        }

        [Fact]
        public void ScielBot_SpoilerRevoltsInKeyLanternClearing()
        {
            var b = SpoilerBoard();
            b.Legal(0, "spread:7", "revolt:10", "revolt:12");
            var (t, v) = View(b, act: 2);
            var bot = new ScielBot();
            Assert.Equal("spread:7", bot.ChooseAction(v, b.LegalActions(S(0))));   // no banner: the plan's steps
            t.State.GlobalCountdowns.Add(S(2));
            var answer = bot.Spoiler(v);
            Assert.Equal("revolt:12", answer.ActionKey);
            Assert.True(answer.Preempts);
            Assert.Equal("revolt:12", bot.ChooseAction(v, b.LegalActions(S(0))));  // the banner pre-empts Spread and the better Revolt
            // Through the game's bot decision hook too.
            Assert.Equal("revolt:12", t.Game.OnBotDecision(S(0), b.LegalActions(S(0))));
        }

        [Fact]
        public void ScielBot_SpoilerEasyAnswersBannersOnlyWithFreeActions()
        {
            var b = SpoilerBoard();
            b.Legal(0, "spread:7", "move:8:12");
            var (t, v) = View(b, Difficulty.Easy, act: 2, phase: TurnPhase.Evening);
            t.State.GlobalCountdowns.Add(S(2));
            var bot = new ScielBot { UnitsMissingOf = _ => 1 };
            Assert.False(bot.Spoiler(v).Preempts);
            Assert.Equal("spread:7", bot.ChooseAction(v, b.LegalActions(S(0))));   // the plan's step keeps the decision
            b.Legal(0, "move:8:12");
            Assert.Equal("move:8:12", bot.ChooseAction(v, b.LegalActions(S(0))));  // a free decision answers the banner

            // Easy ignores a one-step threat; Normal answers it with free actions only, Hard pre-empts.
            t.State.GlobalCountdowns.Clear();
            Assert.Null(bot.Spoiler(v));
            var nb = SpoilerBoard();
            nb.Legal(0, "spread:7", "move:8:12");
            var (_, nv) = View(nb, Difficulty.Normal, act: 2, phase: TurnPhase.Evening);
            var oneStep = new ScielBot { UnitsMissingOf = s => s == S(2) ? 1 : (int?)null };
            Assert.False(oneStep.Spoiler(nv).Threat.Banner);
            Assert.Equal("spread:7", oneStep.ChooseAction(nv, nb.LegalActions(S(0))));
            var hb = SpoilerBoard();
            hb.Legal(0, "spread:7", "move:8:12");
            var (_, hv) = View(hb, Difficulty.Hard, act: 2, phase: TurnPhase.Evening);
            Assert.Equal("move:8:12", oneStep.ChooseAction(hv, hb.LegalActions(S(0))));
        }

        [Fact]
        public void Twilight_OffWithoutUpdateRules()
        {
            var t = TwilightTable(out var b, updateRules: false); // builder-questions C1 (a): off in the first release
            EveningTurn(t, 0,
                daylight: x => x.Game.OnPiecePlaced(S(0), PieceKind.Token, PieceTypes.Sympathy, new ClearingId(5)),
                evening: x =>
                {
                    x.Game.OnBattleStarting(S(0), S(1), new ClearingId(5));
                    x.Game.OnBattleResolved(Battle(0, 1, 5, Warrior(1)));
                });
            Assert.False(t.State.ScielSun);   // no Sun for a Lantern
            Assert.False(t.State.ScielMoon);  // no Moon for a removal
            t.PlayTurn(1);

            // Even holding both charges, nothing is armed and no hit is added.
            t.State.ScielSun = true;
            t.State.ScielMoon = true;
            EveningTurn(t, 0, evening: x =>
            {
                Assert.False(x.State.ScielTwilightArmed);
                x.Game.OnBattleStarting(S(0), S(1), new ClearingId(6));
                x.Game.OnBattleResolved(Battle(0, 1, 6, Warrior(1)));
            });
            Assert.False(b.ExtraHits.ContainsKey(S(0)));
            Assert.True(t.State.ScielSun && t.State.ScielMoon);
        }
    }
}
