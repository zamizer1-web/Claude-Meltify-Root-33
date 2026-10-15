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

        private static BattleResult Battle(int defender, SeatId? leader, bool attackerMoved, params (int seat, PieceKind kind, string type)[] removed)
        {
            var r = new BattleResult { Clearing = new ClearingId(1), Attacker = Me, Defender = new SeatId(defender),
                AttackerMovedInThisTurn = attackerMoved, VpLeaderAtStart = leader };
            foreach (var (seat, kind, type) in removed) r.Removed.Add(new RemovedPiece(new SeatId(seat), kind, type));
            return r;
        }

        /// <summary>Review fix (win.monoco notes): with a tie at the top at battle start (VpLeaderAtStart null) that includes
        /// Monoco, the other tied seats are the leaders even after the VP he scores in the battle lifts him alone to the top.</summary>
        [Fact]
        public void MonocoWin_AlmightyTieLeaderCountsAfterHisOwnBattleScore()
        {
            // Seats 0, 1 and Monoco were tied on 8 at battle start; razing seat 0's building put Monoco on 9.
            var t = Start(Board().Vp(0, 8).Vp(1, 8).Vp(2, 3).Vp(3, 9), act: Tuning.MonocoAlmightyAct);
            t.Game.OnBattleResolved(Battle(0, null, false, (0, PieceKind.Building, PieceTypes.Sawmill)));
            Assert.True(t.State.MonocoFeet[Foot.Almighty].Earned);
            Assert.Equal(new SeatId(0), t.State.MonocoFeet[Foot.Almighty].Faction);

            // A seat that was not tied for the top never counts.
            var low = Start(Board().Vp(0, 8).Vp(1, 8).Vp(2, 3).Vp(3, 9), act: Tuning.MonocoAlmightyAct);
            low.Game.OnBattleResolved(Battle(2, null, false, (2, PieceKind.Building, PieceTypes.Base)));
            Assert.False(low.State.MonocoFeet[Foot.Almighty].Earned);
        }

        private static void SetAct(TestTable t, int act)
        {
            t.Ctx.ActClock.Restore(act);
            t.State.GlobalAct = t.Ctx.ActClock.Act;
        }

        [Fact]
        public void MonocoWin_AnnouncesWithFiveFeetFromThreeFactions()
        {
            var b = Board().Vp(3, Tuning.CountdownVp).Vp(0, Tuning.CountdownVp + 2);
            var t = Start(b, act: Tuning.MonocoAlmightyAct);
            var leader = new SeatId(0);
            // Feet earned in Warlord battles (the Warlord stands in autumn.01): Agile, Caster, Heavy + Almighty, then Balanced.
            t.Game.OnBattleResolved(Battle(1, leader, true, (1, PieceKind.Warrior, "warrior")));
            t.Game.OnBattleResolved(Battle(2, leader, false, (2, PieceKind.Token, PieceTypes.Sympathy)));
            t.Game.OnBattleResolved(Battle(0, leader, false, (0, PieceKind.Building, PieceTypes.Sawmill)));
            var f = t.State.MonocoFeet;
            Assert.Equal(new SeatId(1), f[Foot.Agile].Faction);
            Assert.Equal(new SeatId(2), f[Foot.Caster].Faction);
            Assert.Equal(new SeatId(0), f[Foot.Heavy].Faction);
            Assert.Equal(new SeatId(0), f[Foot.Almighty].Faction);
            Assert.False(f[Foot.Balanced].Earned);
            t.PlayTurn(3);
            Assert.False(b.Banners.ContainsKey(Me));
            t.Game.OnBattleResolved(Battle(1, leader, false, (1, PieceKind.Warrior, "warrior"), (3, PieceKind.Warrior, "warrior")));
            Assert.Equal(new SeatId(1), f[Foot.Balanced].Faction);
            Assert.Equal(5, f[Foot.Balanced].Order);

            // Gates: below tune.countdown-vp, an active dominance, Act I.
            b.Vp(3, Tuning.CountdownVp - 1);
            t.PlayTurn(3);
            Assert.False(b.Banners.ContainsKey(Me));
            b.Vp(3, Tuning.CountdownVp).Dominance(3, Suit.Fox);
            t.PlayTurn(3);
            Assert.False(b.Banners.ContainsKey(Me));
            b.Dominance(3, null);
            SetAct(t, 1);
            Assert.False(WinRules.AnnouncePasses(t.Ctx, Me, t.Ctx.WinOf(Me)));
            SetAct(t, Tuning.MonocoAlmightyAct);

            t.PlayTurn(3);
            Assert.Equal(Ids.WinConditions.Monoco, b.Banners[Me]);
            Assert.Contains(Me, t.State.GlobalCountdowns);
            Assert.False(b.OverlayShown(Ids.Overlays.MonocoEnding));
            t.PlayTurn(3);
            Assert.True(t.Ctx.GameOver);
            Assert.Contains(new KeyValuePair<SeatId, string>(Me, Ids.WinConditions.Monoco), b.Winners);
            t.Game.OnGameOver(new[] { Me });
            Assert.True(b.OverlayShown(Ids.Overlays.MonocoEnding));
        }

        [Fact]
        public void MonocoWin_ConfirmFailsAfterWarlordRemoved()
        {
            var b = Board().Vp(3, Tuning.CountdownVp);
            var t = Start(b, act: Tuning.MonocoAlmightyAct);
            AllFeet(t);
            t.PlayTurn(3);
            Assert.Equal(Ids.WinConditions.Monoco, b.Banners[Me]);
            // Counterplay: the Warlord is battled off the map during the countdown.
            t.Game.OnWarlordRemoved(Me, new ClearingId(1));
            b.PawnInForest(3, 1);
            t.PlayTurn(3);
            Assert.False(t.Ctx.GameOver);
            Assert.Empty(b.Winners);
            Assert.False(b.Banners.ContainsKey(Me));
            Assert.DoesNotContain(Me, t.State.GlobalCountdowns);
            Assert.False(t.State.MonocoFeet[Foot.Almighty].Earned);
            Assert.False(b.OverlayShown(Ids.Overlays.MonocoEnding));

            // The confirm alone also needs the Warlord on the map: all five feet but no Warlord fails it.
            var b2 = Board().Vp(3, Tuning.CountdownVp);
            var t2 = Start(b2, act: Tuning.MonocoAlmightyAct);
            AllFeet(t2);
            var win = t2.Ctx.WinOf(Me);
            Assert.True(WinRules.ConfirmPasses(t2.Ctx, Me, win));
            b2.PawnInForest(3, 1);
            Assert.True(WinRules.AnnouncePasses(t2.Ctx, Me, win));
            Assert.False(WinRules.ConfirmPasses(t2.Ctx, Me, win));
        }

        [Fact]
        public void MonocoWin_AlmightyOnlyInActThree()
        {
            var b = Board().Pawn(3, "autumn.01").Vp(0, 8);
            var r = Battle(0, new SeatId(0), false, (0, PieceKind.Building, PieceTypes.Sawmill));
            var t = Start(b, act: 2);
            t.Game.OnBattleResolved(r);
            Assert.True(t.State.MonocoFeet[Foot.Heavy].Earned);
            Assert.False(t.State.MonocoFeet[Foot.Almighty].Earned);
            var t3 = Start(Board().Vp(0, 8), act: Tuning.MonocoAlmightyAct);
            t3.Game.OnBattleResolved(r);
            Assert.True(t3.State.MonocoFeet[Foot.Almighty].Earned);
            Assert.Equal(new SeatId(0), t3.State.MonocoFeet[Foot.Almighty].Faction);

            // Not from his own lead, and only a building or tune.monoco-almighty-pieces+ of the leader's pieces.
            var own = Start(Board().Vp(3, 8), act: Tuning.MonocoAlmightyAct);
            own.Game.OnBattleResolved(Battle(0, Me, false, (0, PieceKind.Building, PieceTypes.Sawmill)));
            Assert.False(own.State.MonocoFeet[Foot.Almighty].Earned);
            var few = Start(Board().Vp(0, 8), act: Tuning.MonocoAlmightyAct);
            few.Game.OnBattleResolved(Battle(0, new SeatId(0), false, (0, PieceKind.Warrior, "warrior"), (0, PieceKind.Warrior, "warrior")));
            Assert.False(few.State.MonocoFeet[Foot.Almighty].Earned);
            few.Game.OnBattleResolved(Battle(0, new SeatId(0), false, (0, PieceKind.Warrior, "warrior"), (0, PieceKind.Warrior, "warrior"),
                (0, PieceKind.Token, PieceTypes.Wood)));
            Assert.True(few.State.MonocoFeet[Foot.Almighty].Earned);

            // A tie at the top (VpLeaderAtStart null): any tied seat other than Monoco counts as the leader.
            var tie = Start(Board().Vp(0, 8).Vp(1, 8), act: Tuning.MonocoAlmightyAct);
            tie.Game.OnBattleResolved(Battle(1, null, false, (1, PieceKind.Building, PieceTypes.Roost)));
            Assert.Equal(new SeatId(1), tie.State.MonocoFeet[Foot.Almighty].Faction);

            // A battle without the Warlord earns nothing.
            var away = Start(Board().Vp(0, 8), act: Tuning.MonocoAlmightyAct);
            away.Board.PawnInForest(3, 1);
            away.Game.OnBattleResolved(r);
            Assert.All(away.State.MonocoFeet, x => Assert.False(x.Earned));

            // state.monoco.feet onLoad: Almighty is cleared when the loaded act is below tune.monoco-almighty-act.
            var load = Start(Board(), act: 2);
            AllFeet(load);
            var loaded = load.SaveAndLoad(Registry());
            Assert.Equal(2, loaded.Ctx.Act);
            Assert.False(loaded.State.MonocoFeet[Foot.Almighty].Earned);
            Assert.True(loaded.State.MonocoFeet[Foot.Heavy].Earned);
        }

        [Fact]
        public void MonocoWin_FewerOpponentsNeedEveryOpponent()
        {
            var b = Board(opponents: 2).Pawn(2, "autumn.01").Vp(2, Tuning.CountdownVp);
            var t = Start(b, act: Tuning.MonocoAlmightyAct);
            var seat = new SeatId(2);
            var win = t.Ctx.WinOf(seat);
            Assert.Equal(2, MonocoWin.FactionsNeeded(b, seat));
            foreach (var f in t.State.MonocoFeet) { f.Earned = true; f.Faction = new SeatId(0); f.Order = 1; }
            Assert.False(WinRules.AnnouncePasses(t.Ctx, seat, win));
            // A foot already earned is re-earned from a new faction because that raises the count of factions...
            var r = new BattleResult { Clearing = new ClearingId(1), Attacker = seat, Defender = new SeatId(1), VpLeaderAtStart = new SeatId(1) };
            r.Removed.Add(new RemovedPiece(new SeatId(1), PieceKind.Building, PieceTypes.Roost));
            t.Game.OnBattleResolved(r);
            Assert.Equal(new SeatId(1), t.State.MonocoFeet[Foot.Heavy].Faction);
            Assert.True(WinRules.AnnouncePasses(t.Ctx, seat, win));
            // ...but not back to a faction he already has.
            var back = new BattleResult { Clearing = new ClearingId(1), Attacker = seat, Defender = new SeatId(0) };
            back.Removed.Add(new RemovedPiece(new SeatId(0), PieceKind.Building, PieceTypes.Sawmill));
            t.Game.OnBattleResolved(back);
            Assert.Equal(new SeatId(1), t.State.MonocoFeet[Foot.Heavy].Faction);

            var solo = Board(opponents: 1).Vp(1, Tuning.CountdownVp);
            var t1 = Start(solo, act: Tuning.MonocoAlmightyAct);
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
            // Agile, Balanced and Heavy already earned from seat 0 (Caster needs a token, Almighty Act III): only the win chance.
            Feet(t, (Foot.Agile, 0), (Foot.Balanced, 0), (Foot.Heavy, 0));
            var again = bot.BattleScore(new BotView(t.Ctx, Me)).Single(z => z.Item.Clearing == x && z.Item.Defender == new SeatId(0));
            Assert.Equal(15.0 / 16, again.Score, 6);
            Assert.Equal(1.0, MonocoBot.WinChance(1, 0));
            Assert.Equal(12.0 / 16, MonocoBot.WinChance(3, 2), 6);
            Assert.Equal(0.0, MonocoBot.WinChance(1, 2));
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
            var legal = new[] { "battle:" + x.Value + ":0" };
            Assert.Empty(new MonocoBot().Targets(new BotView(t.Ctx, Me)));
            Assert.Null(new MonocoBot().ChooseAction(new BotView(t.Ctx, Me), legal));
            b.Warriors(3, "autumn.01", 1);
            Assert.NotEmpty(new MonocoBot().Targets(new BotView(t.Ctx, Me)));
            Assert.Equal(legal[0], new MonocoBot().ChooseAction(new BotView(t.Ctx, Me), legal));
            // Normal fights the same battle without the margin.
            b.Warriors(3, "autumn.01", 0);
            var normal = Start(b);
            Assert.Equal(legal[0], new MonocoBot().ChooseAction(new BotView(normal.Ctx, Me), legal));
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

            // In play: the planned battle is an unguarded building next to the Warlord; a rat next to it is Command-moved in
            // before the Warlord advances, ahead of a rat farther away.
            var b2 = Board();
            var w = new ClearingId(1);
            var tgt = Near(b2)[0];
            var dt = MonocoBot.Distances(b2, tgt);
            var src = dt.Where(kv => kv.Value == 1 && kv.Key != w).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            var src2 = dt.Where(kv => kv.Value == 2 && kv.Key != w).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            var step2 = b2.PathNeighbours(src2).First(n => dt[n] == 1);
            b2.Building(0, tgt, PieceTypes.Sawmill).Warriors(3, src, 1).Warriors(3, src2, 2);
            var t2 = Start(b2);
            var view = new BotView(t2.Ctx, Me);
            var bot = new MonocoBot();
            Assert.Equal(tgt, bot.Planned(view).Clearing);
            var into = "command:" + src.Value + ":" + tgt.Value;
            var closer = "command:" + src2.Value + ":" + step2.Value;
            Assert.Equal(into, bot.ChooseAction(view, new[] { closer, into }));
            Assert.Equal(closer, bot.ChooseAction(view, new[] { closer }));
        }

        [Fact]
        public void MonocoBot_EasyWarlordAdvancesAlone()
        {
            var b = Board();
            b.Warriors(3, Near(b)[0], 2);
            var t = Start(b, d: Difficulty.Easy);
            Assert.Empty(new MonocoBot().BringRats(new BotView(t.Ctx, Me), new ClearingId(1)));

            // In play: Easy never Command-moves rats toward the planned battle; the Warlord advances alone.
            var b2 = Board();
            var n = Near(b2);
            var tgt = n[0];
            b2.Warriors(3, "autumn.01", 1).Building(0, tgt, PieceTypes.Sawmill).Warriors(3, n[1], 2);
            var easy = Start(b2, d: Difficulty.Easy);
            var view = new BotView(easy.Ctx, Me);
            Assert.Equal(tgt, new MonocoBot().Planned(view).Clearing);
            var rat = "command:" + n[1].Value + ":" + tgt.Value;
            var advance = "advance:1:" + tgt.Value;
            Assert.Null(new MonocoBot().ChooseAction(view, new[] { rat }));
            Assert.Equal(advance, new MonocoBot().ChooseAction(view, new[] { rat, advance }));
            var normal = Start(b2);
            Assert.Equal(advance, new MonocoBot().ChooseAction(new BotView(normal.Ctx, Me), new[] { rat, advance }));
            Assert.Equal(rat, new MonocoBot().ChooseAction(new BotView(normal.Ctx, Me), new[] { rat }));
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

            // In play: with a planned battle (an unguarded building next door) the mood key whose effect adds most is chosen;
            // ties keep the current mood; with no known mood effects the choice is left to Root's AI.
            b.Building(0, Near(b)[0], PieceTypes.Sawmill);
            var legal = new[] { "mood:mood.a", "mood:mood.b" };
            Assert.Null(bot.ChooseAction(view, legal));
            bot.MoodEffect = (v, planned, m) => planned.Clearing == Near(b)[0] && m == "mood.b" ? 1 : 0;
            Assert.Equal("mood:mood.b", bot.ChooseAction(view, legal));
            bot.MoodEffect = (v, planned, m) => 0;
            Assert.Equal("mood:mood.a", bot.ChooseAction(view, legal));
        }

        [Fact]
        public void MonocoBot_BuildsArenasAlongRoute()
        {
            var b = Board().Hoard(3, "prowess", 2);
            var d = MonocoBot.Distances(b, new ClearingId(1));
            var target = d.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            var path = MonocoBot.PathTo(b, new ClearingId(1), target);
            var mid = path[1];
            var off = d.Where(kv => kv.Value == 1 && kv.Key != mid).Select(kv => kv.Key).OrderBy(c => c.Value).First();
            b.Warriors(3, mid, 2).Warriors(3, off, 2).Building(0, target, PieceTypes.Sawmill).Warriors(0, target, 3);
            Assert.True(b.FreeSlots(mid) > 0);
            Assert.True(b.FreeSlots(off) > 0);
            var t = Start(b);
            var view = new BotView(t.Ctx, Me);
            var bot = new MonocoBot();
            var arenas = bot.Arenas(view, target);
            Assert.Equal(mid, arenas[0].Item);
            Assert.DoesNotContain(arenas, a => !path.Contains(a.Item));
            Assert.DoesNotContain(arenas, a => a.Item == off);
            // In play: the planned battle is the building two steps away; the Arena goes on the route, not beside it.
            Assert.Equal(target, bot.Planned(view).Clearing);
            var onRoute = "build:" + mid.Value + ":" + PieceTypes.Stronghold;
            var offRoute = "build:" + off.Value + ":" + PieceTypes.Stronghold;
            Assert.Equal(onRoute, bot.ChooseAction(view, new[] { offRoute, onRoute }));
            Assert.Null(bot.ChooseAction(view, new[] { offRoute }));
        }

        [Fact]
        public void MonocoBot_EscortsWarlordWithTwoAfterThreeFeet()
        {
            var b = Board();
            var n = Near(b);
            b.Warriors(3, n[0], 3);
            var t = Start(b);
            var bot = new MonocoBot();
            var legal = new[] { "command:" + n[0].Value + ":1" };
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1));
            Assert.Equal(0, bot.Escort(new BotView(t.Ctx, Me)));
            Assert.Null(bot.ChooseAction(new BotView(t.Ctx, Me), legal));
            Feet(t, (Foot.Agile, 0), (Foot.Caster, 1), (Foot.Heavy, 2));
            Assert.Equal(Tuning.MonocoBotEscort, bot.Escort(new BotView(t.Ctx, Me)));
            Assert.Equal(legal[0], bot.ChooseAction(new BotView(t.Ctx, Me), legal));
            b.Warriors(3, "autumn.01", Tuning.MonocoBotEscort - 1);
            Assert.Equal(1, bot.Escort(new BotView(t.Ctx, Me)));
            b.Warriors(3, "autumn.01", Tuning.MonocoBotEscort);
            Assert.Equal(0, bot.Escort(new BotView(t.Ctx, Me)));
            Assert.Null(bot.ChooseAction(new BotView(t.Ctx, Me), legal));
        }

        [Fact]
        public void MonocoBot_AnnounceTurnEndsInStrongestEnemyFreeClearing()
        {
            var b = Board().Vp(3, Tuning.CountdownVp);
            var n = Near(b);
            b.Warriors(3, n[0], 3).Warriors(3, n[1], 4).Warriors(0, n[1], 1);
            var t = Start(b, act: Tuning.MonocoAlmightyAct);
            var bot = new MonocoBot();
            var legal = new[] { "battle:" + n[1].Value + ":0", "advance:1:" + n[1].Value, "advance:1:" + n[0].Value };
            Assert.Empty(bot.Announce(new BotView(t.Ctx, Me)));
            // Not the announce turn: the battle that fills a foot comes first.
            Assert.Equal(legal[0], bot.ChooseAction(new BotView(t.Ctx, Me), legal));
            AllFeet(t);
            var ranked = bot.Announce(new BotView(t.Ctx, Me));
            Assert.Equal(n[0], ranked[0].Item);
            Assert.DoesNotContain(ranked, r => r.Item == n[1]);
            // The announce turn: no more battles; the Warlord ends in the strongest enemy-free clearing.
            Assert.Equal(legal[2], bot.ChooseAction(new BotView(t.Ctx, Me), legal));
            b.Vp(3, Tuning.CountdownVp - 1);
            Assert.Empty(bot.Announce(new BotView(t.Ctx, Me)));
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
