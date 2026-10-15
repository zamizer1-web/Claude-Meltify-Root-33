using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Monoco;
using Root33.Core.Characters.Paintress;
using Root33.Core.Characters.WhiteHairedMan;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Characters
{
    /// <summary>The White-Haired Man: win.white-haired-man, tweak.white-haired-man.*, state.white-haired-man.barrier-down,
    /// bot.white-haired-man and its rules. Autumn: the Monolith is 3; the Ring is Visages 9 (fox), Sirène 10 (rabbit) and
    /// The Reacher 11 (rabbit); 7 is next to 9, 4 next to 10 and 11, 2 next to 9.</summary>
    public class WhiteHairedManTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);

        private static ModuleRegistry Registry() =>
            new ModuleRegistry().Register(CharacterIds.WhiteHairedMan, () => new WhiteHairedManModule());

        private static ModuleRegistry RegistryWith(string characterId, System.Func<ICharacterModule> factory) =>
            Registry().Register(characterId, factory);

        /// <summary>The White-Haired Man's Corvids at seat 0, Gustave's Marquise at seat 1 (no module: plain Root rules).</summary>
        private static FakeBoard Table(bool bot = false) =>
            FakeBoard.Autumn().Seat(0, EngineId.Corvids, CharacterIds.WhiteHairedMan, bot: bot).Seat(1, EngineId.Marquise, CharacterIds.Gustave)
                .Supply(0, 10).Supply(1, 10);

        /// <summary>The Ring held: warriors in 9 and 10, a face-down plot alone in 11, the Monolith empty.</summary>
        private static FakeBoard RingHeld(FakeBoard b, int vp = 10) =>
            b.Warriors(0, 9, 2).Warriors(0, 10, 1).HiddenPlotAt(0, "11", CorvidPlots.Raid).Vp(0, vp);

        private static TestTable Start(FakeBoard b, ModuleRegistry registry = null, Difficulty d = Difficulty.Normal, int act = 1)
        {
            var t = TestTable.Start(b, registry ?? Registry(), d);
            if (act > 1) t.Game.OnActChanged(act);
            return t;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));

        private static readonly WhiteHairedManBot Bot = new WhiteHairedManBot();

        /// <summary>The Paintress (seat 2) one step from her win: she rules the Monolith with an easel and has Living Portraits in
        /// fox 9 and rabbit 11 at 10 VP; only the mouse Portrait is missing. Without the rabbit Portrait she is two steps away.</summary>
        private static FakeBoard PaintressNear(FakeBoard b, bool oneStep)
        {
            b.Building(2, "The Monolith", PieceTypes.Roost).Warriors(2, "The Monolith", 3)
             .Building(2, 9, PieceTypes.Roost).Warriors(2, 9, 2).Vp(2, 10);
            if (oneStep) b.Building(2, 11, PieceTypes.Roost).Warriors(2, 11, 2);
            return b;
        }

        // ================================================================= win.white-haired-man

        [Fact]
        public void WhiteHairedManWin_AnnouncesWithRingHeld()
        {
            var board = RingHeld(Table());
            var t = Start(board);
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            Assert.False(board.Banners.ContainsKey(S(0))); // no themed win in Act I
            t.PlayTurn(1).PlayTurn(0);
            Assert.Equal(Ids.WinConditions.WhiteHairedMan, board.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            Assert.Empty(board.Winners);

            t.PlayTurn(1).PlayTurn(0);
            Assert.Contains(new KeyValuePair<SeatId, string>(S(0), Ids.WinConditions.WhiteHairedMan), board.Winners);

            // One Ring clearing without any piece of his: no banner.
            var gap = Table().Warriors(0, 9, 2).Warriors(0, 10, 1).Vp(0, 10);
            var t2 = Start(gap);
            t2.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.False(gap.Banners.ContainsKey(S(0)));

            // Another faction ruling the Monolith: no banner.
            var taken = RingHeld(Table()).Warriors(1, "The Monolith", 1);
            var t3 = Start(taken);
            t3.PlayTurn(0).PlayTurn(1).PlayTurn(0);
            Assert.False(taken.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void WhiteHairedManWin_GatesBlockAnnounce()
        {
            // Below tune.countdown-vp in Act II: no banner.
            var low = RingHeld(Table(), vp: Tuning.CountdownVp - 1);
            var t = Start(low, act: 2);
            t.PlayTurn(0);
            Assert.False(low.Banners.ContainsKey(S(0)));

            // An active dominance card switches the win off (offWithDominance).
            var dom = RingHeld(Table()).Dominance(0, Suit.Fox);
            var t2 = Start(dom, act: 2);
            t2.PlayTurn(0);
            Assert.False(dom.Banners.ContainsKey(S(0)));
            dom.Dominance(0, null);
            t2.PlayTurn(1).PlayTurn(0);
            Assert.Equal(Ids.WinConditions.WhiteHairedMan, dom.Banners[S(0)]);

            // Hosted guards are not his: a rock guarded by two of his three warriors leaves one, two guards leave none.
            var guarded = Table().Warriors(0, 9, 2).Warriors(0, 10, 1).Warriors(0, 11, 2).Vp(0, 10);
            var t3 = Start(guarded, act: 2);
            var rock = t3.State.EsquieRocks[0];
            rock.Status = RockStatus.Guarded; rock.Clearing = C(11); rock.HostSeat = S(0); rock.GuardsLeft = 2;
            Assert.False(new WhiteHairedManWin().Announce(t3.Ctx, S(0)));
            guarded.Warriors(0, 11, 3);
            Assert.True(new WhiteHairedManWin().Announce(t3.Ctx, S(0)));
        }

        [Fact]
        public void WhiteHairedManWin_ConfirmFailsWhenVagabondEntersMonolith()
        {
            var board = RingHeld(Table()).Seat(2, EngineId.VagabondTinker).Pawn(2, "autumn.05");
            var t = Start(board);
            t.PlayRound();
            Assert.Equal(2, t.Ctx.Act);
            t.PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);

            t.PlayTurn(1).PlayTurn(2, during: x => x.Board.Pawn(2, "The Monolith")); // the one deliberate walk-in answer
            t.PlayTurn(0);
            Assert.Empty(board.Winners);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);
            Assert.Contains("ClearBanner seat0", board.Log);
        }

        [Fact]
        public void WhiteHairedManWin_FaceUpSnareHoldsOutstackedClearing()
        {
            // Gustave out-stacks him in Visages 9, but his face-up Snare (Barrier) holds it.
            var board = Table().FaceUpPlotAt(0, "9", CorvidPlots.Snare).Warriors(1, 9, 3)
                .Warriors(0, 10, 1).Warriors(0, 11, 1).Vp(0, 10);
            Assert.Equal(S(1), board.Ruler(C(9)));
            var t = Start(board, act: 2);
            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.WhiteHairedMan, board.Banners[S(0)]);

            // The same Snare face down does not hold an out-stacked clearing.
            var hidden = Table().HiddenPlotAt(0, "9", CorvidPlots.Snare).Warriors(1, 9, 3)
                .Warriors(0, 10, 1).Warriors(0, 11, 1).Vp(0, 10);
            var t2 = Start(hidden, act: 2);
            t2.PlayTurn(0);
            Assert.False(hidden.Banners.ContainsKey(S(0)));

            // Nor does another face-up plot.
            var bomb = Table().FaceUpPlotAt(0, "9", CorvidPlots.Bomb).Warriors(1, 9, 3)
                .Warriors(0, 10, 1).Warriors(0, 11, 1).Vp(0, 10);
            var t3 = Start(bomb, act: 2);
            t3.PlayTurn(0);
            Assert.False(bomb.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void WhiteHairedManWin_PaintressRuleCountsAsHeld()
        {
            // The Paintress (seat 2) rules Visages 9 and the Monolith: both still count as held for him.
            var board = Table().Seat(2, EngineId.Eyrie, CharacterIds.Paintress)
                .Warriors(0, 9, 1).Building(2, 9, PieceTypes.Roost).Warriors(2, 9, 2)
                .Warriors(0, 10, 1).Warriors(0, 11, 1)
                .Building(2, "The Monolith", PieceTypes.Roost).Warriors(2, "The Monolith", 3).Vp(0, 10);
            Assert.Equal(S(2), board.Ruler(C(9)));
            Assert.Equal(S(2), board.Ruler(C(3)));
            var t = Start(board, act: 2);
            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.WhiteHairedMan, board.Banners[S(0)]);

            // The same pieces as Gustave's: he does not hold them.
            var gustave = Table().Warriors(0, 9, 1).Building(1, 9, PieceTypes.Sawmill).Warriors(1, 9, 2)
                .Warriors(0, 10, 1).Warriors(0, 11, 1).Vp(0, 10);
            var t2 = Start(gustave, act: 2);
            t2.PlayTurn(0);
            Assert.False(gustave.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void WhiteHairedManModule_RegisteredByDefault()
        {
            Assert.IsType<WhiteHairedManModule>(ModuleRegistry.Default().Create(CharacterIds.WhiteHairedMan));
            Assert.Empty(ModuleRegistry.MissingBotRules(new WhiteHairedManBot()));
            var t = Start(RingHeld(Table()), act: 2);
            var win = (IGoalProgress)t.Game.ModuleOf(S(0)).Win;
            Assert.Equal(0, win.UnitsMissing(t.Ctx, S(0)));
            t.Board.Warriors(0, 10, 0);
            Assert.Equal(1, win.UnitsMissing(t.Ctx, S(0)));
            Assert.Contains(C(3), win.KeyClearings(t.Ctx, S(0)));
        }

        // ================================================================= tweak.white-haired-man.unbeatable-act1

        [Fact]
        public void UnbeatableActOne_RingWarriorsIgnoreFirstHit()
        {
            var board = Table().Warriors(0, 9, 2).Warriors(1, 9, 3);
            var t = Start(board);
            Assert.Equal(1, t.Ctx.Act);
            t.Game.OnBattleStarting(S(1), S(0), C(9)); // he defends
            Assert.Contains(S(0), board.IgnoresFirstHit);
            Assert.Equal(Tuning.WhiteHairedManUnbeatableHits, board.Log.Count(l => l == "IgnoreFirstHit seat0"));
            t.Game.OnBattleStarting(S(0), S(1), C(9)); // he attacks
            Assert.Equal(2 * Tuning.WhiteHairedManUnbeatableHits, board.Log.Count(l => l == "IgnoreFirstHit seat0"));
            Assert.DoesNotContain(S(1), board.IgnoresFirstHit);

            // Fallback without do.ignore-first-hit: one removed warrior of his comes back from supply after the battle.
            var fb = Table().Warriors(0, 10, 2).Warriors(1, 10, 3).Unsupported(Ids.BoardActions.IgnoreFirstHit);
            var t2 = Start(fb);
            t2.Game.OnBattleStarting(S(1), S(0), C(10));
            Assert.Empty(fb.IgnoresFirstHit);
            fb.Warriors(0, 10, 0);
            var result = new BattleResult { Clearing = C(10), Attacker = S(1), Defender = S(0) };
            result.Removed.Add(new RemovedPiece(S(0), PieceKind.Warrior, null));
            result.Removed.Add(new RemovedPiece(S(0), PieceKind.Warrior, null));
            t2.Game.OnBattleResolved(result);
            Assert.Equal(Tuning.WhiteHairedManUnbeatableHits, fb.Warriors(S(0), C(10)));
        }

        [Fact]
        public void UnbeatableActOne_OffFromActTwo()
        {
            var board = Table().Warriors(0, 9, 2).Warriors(1, 9, 3);
            var t = Start(board, act: 2);
            t.Game.OnBattleStarting(S(1), S(0), C(9));
            Assert.Empty(board.IgnoresFirstHit);

            // The fallback is off from Act II too.
            var fb = Table().Warriors(0, 9, 1).Warriors(1, 9, 3).Unsupported(Ids.BoardActions.IgnoreFirstHit);
            var t2 = Start(fb, act: 2);
            fb.Warriors(0, 9, 0);
            var result = new BattleResult { Clearing = C(9), Attacker = S(1), Defender = S(0) };
            result.Removed.Add(new RemovedPiece(S(0), PieceKind.Warrior, null));
            t2.Game.OnBattleResolved(result);
            Assert.Equal(0, fb.Warriors(S(0), C(9)));
        }

        [Fact]
        public void UnbeatableActOne_OnlyInRingClearings()
        {
            var board = Table().Warriors(0, 7, 2).Warriors(1, 7, 2).Warriors(0, 3, 2).Warriors(1, 3, 2)
                .Warriors(0, 11, 2).Warriors(1, 11, 2);
            var t = Start(board);
            t.Game.OnBattleStarting(S(1), S(0), C(7)); // not next to the Monolith
            t.Game.OnBattleStarting(S(1), S(0), C(3)); // the Monolith itself is not a Ring clearing
            Assert.Empty(board.IgnoresFirstHit);
            t.Game.OnBattleStarting(S(1), S(0), C(11));
            Assert.Contains(S(0), board.IgnoresFirstHit);

            // A battle in the Ring that he is not part of: nothing.
            var other = Table().Seat(2, EngineId.Alliance).Warriors(1, 11, 2).Warriors(2, 11, 2).Warriors(0, 11, 1);
            var t2 = Start(other);
            t2.Game.OnBattleStarting(S(1), S(2), C(11));
            Assert.Empty(other.IgnoresFirstHit);
        }

        // ================================================================= tweak.white-haired-man.monolith-barrier

        [Fact]
        public void MonolithBarrier_BlocksMonolithBattleUntilAxonsRuled()
        {
            var board = Table().Warriors(0, 3, 1).Warriors(1, 3, 1).Warriors(1, 9, 2);
            var t = Start(board);
            t.Ctx.Options.MonolithBarrier = true;
            Assert.False(t.Game.CanStartBattle(S(1), S(0), C(3), false));
            Assert.True(t.Game.CanStartBattle(S(1), S(0), C(9), false)); // only the Monolith
            Assert.True(t.Game.CanStartBattle(S(1), S(0), C(3), true));  // a Strike is not a battle

            t.PlayTurn(0); // only Visages is ruled
            Assert.False(t.State.WhiteHairedManBarrierDown);
            Assert.False(t.Game.CanStartBattle(S(1), S(0), C(3), false));

            t.PlayTurn(1, during: x => x.Board.Warriors(0, 10, 2)); // Sirène ruled by the White-Haired Man does not count
            Assert.False(t.State.WhiteHairedManBarrierDown);

            // Sirène taken at the end of a turn that lost Visages: not the same turn.
            t.PlayTurn(0, during: x => x.Board.Warriors(1, 9, 0).Warriors(0, 10, 0).Warriors(1, 10, 1));
            Assert.False(t.State.WhiteHairedManBarrierDown);

            t.PlayTurn(1, during: x => x.Board.Warriors(1, 9, 2)); // both Axons ruled by Gustave at the end of one turn
            Assert.True(t.State.WhiteHairedManBarrierDown);
            Assert.True(t.Game.CanStartBattle(S(1), S(0), C(3), false));

            // Axons ruled by the Paintress do not count either.
            var p = Table().Seat(2, EngineId.Eyrie, CharacterIds.Paintress).Warriors(1, 9, 2).Warriors(2, 10, 2);
            var t2 = Start(p);
            t2.Ctx.Options.MonolithBarrier = true;
            t2.PlayRound();
            Assert.False(t2.State.WhiteHairedManBarrierDown);
            Assert.False(t2.Game.CanStartBattle(S(1), S(2), C(3), false));
        }

        [Fact]
        public void MonolithBarrier_StaysDownOnceBroken()
        {
            var board = Table().Warriors(1, 9, 2).Warriors(1, 10, 2).Warriors(0, 3, 1).Warriors(1, 3, 1);
            var t = Start(board);
            t.Ctx.Options.MonolithBarrier = true;
            t.PlayTurn(0);
            Assert.True(t.State.WhiteHairedManBarrierDown);

            // He retakes both Axons: the Barrier does not go back up.
            t.PlayTurn(1, during: x => x.Board.Warriors(0, 9, 5).Warriors(0, 10, 5));
            Assert.True(t.State.WhiteHairedManBarrierDown);
            Assert.True(t.Game.CanStartBattle(S(1), S(0), C(3), false));

            // Restored from the save.
            var loaded = t.SaveAndLoad(Registry());
            loaded.Ctx.Options.MonolithBarrier = true;
            Assert.True(loaded.State.WhiteHairedManBarrierDown);
            Assert.True(loaded.Game.CanStartBattle(S(1), S(0), C(3), false));
        }

        [Fact]
        public void MonolithBarrier_OffUnlessOptionOn()
        {
            var board = Table().Warriors(1, 9, 2).Warriors(1, 10, 2).Warriors(0, 3, 1).Warriors(1, 3, 1);
            var t = Start(board);
            Assert.False(t.Ctx.Options.MonolithBarrier); // opt.monolith-barrier defaults to off
            Assert.True(t.Game.CanStartBattle(S(1), S(0), C(3), false));
            t.PlayTurn(0);
            Assert.False(t.State.WhiteHairedManBarrierDown);

            // On, it applies whether or not the White-Haired Man is seated.
            var noWhm = FakeBoard.Autumn().Seat(0, EngineId.Marquise, CharacterIds.Gustave).Seat(1, EngineId.Alliance)
                .Warriors(0, 3, 1).Warriors(1, 3, 1);
            var t2 = TestTable.Start(noWhm, new ModuleRegistry());
            t2.Ctx.Options.MonolithBarrier = true;
            Assert.False(t2.Game.CanStartBattle(S(0), S(1), C(3), false));
        }

        // ================================================================= bot.white-haired-man.01 ring status

        [Fact]
        public void WhiteHairedManBot_RingMarginMatchesFormula()
        {
            var board = Table(bot: true)
                .Warriors(0, 9, 4).Warriors(1, 9, 2).Building(1, 9, PieceTypes.Sawmill) // 4 - (2 + 1) = 1
                .Warriors(0, 10, 1).Warriors(1, 10, 3)                                   // 1 - 3 = -2
                .FaceUpPlotAt(0, "11", CorvidPlots.Snare);                               // 0 - 0, held by the Snare
            var t = Start(board);
            var spots = Bot.RingStatus(View(t));
            Assert.Equal(new[] { 9, 10, 11 }, spots.Select(s => s.Clearing.Value));
            Assert.Equal(new[] { 1, -2, 0 }, spots.Select(s => s.Margin));
            Assert.True(spots[2].Snare);
            Assert.True(spots[2].Held);
            Assert.False(spots[1].Held);
            Assert.True(spots[0].Held);

            // Guards he hosts are not his: 2 guards left in 9 take his count to 2.
            var rock = t.State.EsquieRocks[0];
            rock.Status = RockStatus.Guarded; rock.Clearing = C(9); rock.HostSeat = S(0); rock.GuardsLeft = 2;
            Assert.Equal(-1, Bot.RingStatus(View(t))[0].Margin);
        }

        [Fact]
        public void WhiteHairedManBot_RingMarginIgnoresPaintress()
        {
            var board = Table(bot: true).Seat(2, EngineId.Eyrie, CharacterIds.Paintress)
                .Warriors(0, 9, 2).Warriors(2, 9, 5).Warriors(1, 9, 1);
            var t = Start(board);
            Assert.Equal(1, Bot.Margin(View(t), C(9)));    // 2 - Gustave's 1; her 5 do not count
            board.Warriors(1, 9, 0);
            Assert.Equal(2, Bot.Margin(View(t), C(9)));
            Assert.True(Bot.RingStatus(View(t))[0].Held); // she rules it, which still counts as held
        }

        // ================================================================= 02 recruit

        [Fact]
        public void WhiteHairedManBot_RecruitsWithRingSuitCards()
        {
            // Fox Ring (9): margin 2. Rabbit Ring (10, 11): -1 + 0 = -1. No mouse Ring clearing.
            var board = Table(bot: true).Warriors(0, 9, 2).Warriors(1, 10, 1)
                .Hand(0, Suit.Mouse, Suit.Fox, Suit.Rabbit, Suit.Bird);
            var t = Start(board);
            var v = View(t);
            Assert.Equal(new[] { Suit.Rabbit, Suit.Fox }, Bot.Recruit(v).Select(r => r.Item));
            Assert.Equal("recruit:rabbit", Bot.ChooseAction(v, new[] { "recruit:mouse", "recruit:fox", "recruit:rabbit", "recruit:bird" }));
            Assert.Null(Bot.ChooseAction(v, new[] { "recruit:mouse", "recruit:bird" }));

            // A tie on total margin goes to the suit with more Ring clearings (rabbit).
            board.Warriors(0, 9, 0).Warriors(1, 9, 1);
            Assert.Equal(Suit.Rabbit, Bot.Recruit(View(t))[0].Item);

            // No warriors in supply: no recruit.
            board.Supply(0, 0);
            Assert.Empty(Bot.Recruit(View(t)));
        }

        // ================================================================= 03 plots

        [Fact]
        public void WhiteHairedManBot_SnaresLowestMarginRingClearing()
        {
            // Margins: 9 = 2, 10 = 1 - 3 = -2, 11 = 1.
            var board = Table(bot: true).Warriors(0, 9, 2).Warriors(0, 10, 1).Warriors(1, 10, 3).Warriors(0, 11, 1).Vp(0, 5);
            var t = Start(board);
            Assert.Equal(C(10), Bot.Plots(View(t)).Snare);
            Assert.Equal("plot:10:snare", Bot.ChooseAction(View(t), new[] { "plot:9:snare", "plot:10:snare", "plot:11:snare" }));

            // Placed face down; flipped face up only from goal mode.
            board.HiddenPlotAt(0, "10", CorvidPlots.Snare);
            Assert.Equal(C(11), Bot.Plots(View(t)).Snare); // 10 has a plot now: the next lowest margin
            Assert.Empty(Bot.Plots(View(t)).Flip);
            board.Vp(0, Tuning.WhiteHairedManGoalVp);
            Assert.Equal(new[] { C(10) }, Bot.Plots(View(t)).Flip);
            Assert.Equal("flip:10", Bot.ChooseAction(View(t), new[] { "plot:11:snare", "flip:10" }));
        }

        [Fact]
        public void WhiteHairedManBot_BombsWhereThreeEnemiesGather()
        {
            // Gestral Village 7 is next to Visages 9: three of Gustave's warriors gather there. Forgotten Battlefield 12 is not
            // in or next to the Ring.
            var board = Table(bot: true).Warriors(0, 7, 1).Warriors(1, 7, Tuning.WhiteHairedManBombWarriors)
                .Warriors(0, 12, 1).Warriors(1, 12, 4).Warriors(0, 9, 1).Warriors(0, 10, 1).Warriors(0, 11, 1);
            var t = Start(board);
            Assert.Equal(new[] { C(7) }, Bot.Plots(View(t)).Bomb.Select(r => r.Item));
            Assert.Equal("plot:7:bomb", Bot.ChooseAction(View(t), new[] { "plot:12:bomb", "plot:7:bomb" }));

            // One fewer: no Bomb is due, and a Raid refills a Ring clearing without a plot.
            board.Warriors(1, 7, Tuning.WhiteHairedManBombWarriors - 1);
            var plan = Bot.Plots(View(t), new[] { "plot:9:raid", "plot:10:raid" });
            Assert.Empty(plan.Bomb);
            Assert.Null(plan.Snare);
            Assert.Equal(C(9), plan.Raid);
            Assert.Equal("plot:9:raid", Bot.ChooseAction(View(t), new[] { "plot:12:bomb", "plot:10:raid", "plot:9:raid" }));
        }

        [Fact]
        public void WhiteHairedManBot_EasyLeavesOneRingClearingToFaceDownPlot()
        {
            // The Reacher 11 holds only a lone face-down Snare; 9 and 10 are fixed. He is in goal mode.
            FakeBoard Board() => Table(bot: true).Warriors(0, 9, 3).Warriors(0, 10, 2).Warriors(1, 10, 1)
                .HiddenPlotAt(0, "11", CorvidPlots.Snare).Warriors(0, 7, 2).Vp(0, Tuning.WhiteHairedManGoalVp);
            var legal = new[] { "flip:11", "move:7:9:1" };

            var easy = Start(Board(), d: Difficulty.Easy);
            var v = View(easy);
            Assert.Equal(C(11), Bot.EasyLeft(v));
            Assert.True(Bot.RingStatus(v)[2].Held);       // the face-down plot alone holds it, for now
            Assert.Empty(Bot.MoveBattle(v));               // never reinforced
            Assert.Empty(Bot.Plots(v).Flip);               // never flipped face up
            Assert.Null(Bot.ChooseAction(v, legal));

            var normal = Start(Board());
            var n = View(normal);
            Assert.Null(Bot.EasyLeft(n));
            Assert.Equal(new[] { C(11) }, Bot.MoveBattle(n).Select(r => r.Item));
            Assert.Equal("flip:11", Bot.ChooseAction(n, legal));

            // Without a lone plot, Easy leaves its lowest-margin Ring clearing.
            var plain = Start(Table(bot: true).Warriors(0, 9, 3).Warriors(1, 10, 2), d: Difficulty.Easy);
            Assert.Equal(C(10), Bot.EasyLeft(View(plain)));
        }

        // ================================================================= 04 move and battle

        [Fact]
        public void WhiteHairedManBot_FixesLowestMarginRingClearingFirst()
        {
            // Margins: 9 = 1 - 1 = 0, 10 = 0 - 2 = -2, 11 = 0 - 1 = -1. Spare warriors in 7 (next to 9) and 4 (next to 10, 11).
            var board = Table(bot: true).Warriors(0, 9, 1).Warriors(1, 9, 1).Warriors(1, 10, 2).Warriors(1, 11, 1)
                .Warriors(0, 7, 4).Warriors(0, 4, 2);
            var t = Start(board);
            var v = View(t);
            Assert.Equal(new[] { 10, 11, 9 }, Bot.MoveBattle(v).Select(r => r.Item.Value));
            Assert.Equal("move:4:10:2", Bot.ChooseAction(v, new[] { "move:7:9:1", "move:4:11:1", "move:4:10:1", "move:4:10:2", "battle:9:1" }));
            Assert.Equal("move:7:9:1", Bot.ChooseAction(v, new[] { "battle:9:1", "move:7:9:1" }));
            Assert.Equal("battle:9:1", Bot.ChooseAction(v, new[] { "battle:9:1", "move:7:5:1" }));
        }

        [Fact]
        public void WhiteHairedManBot_HardKeepsMarginTwoInRing()
        {
            // Margins: 9 = 2 - 1 = 1, 10 = 2, 11 = 3.
            FakeBoard Board() => Table(bot: true).Warriors(0, 9, 2).Warriors(1, 9, 1).Warriors(0, 10, 2).Warriors(0, 11, 3);
            var legal = new[] { "move:10:9:1", "move:11:9:1" };

            var normal = Start(Board());
            Assert.Equal(WhiteHairedManBot.NormalFixedMargin, Bot.FixedMargin(View(normal)));
            Assert.Empty(Bot.MoveBattle(View(normal)));
            Assert.Null(Bot.ChooseAction(View(normal), legal));

            var hard = Start(Board(), d: Difficulty.Hard);
            Assert.Equal(Tuning.WhiteHairedManHardMargin, Bot.FixedMargin(View(hard)));
            Assert.Equal(new[] { C(9) }, Bot.MoveBattle(View(hard)).Select(r => r.Item));
            // 10 has no warrior above margin 2; 11 has one.
            Assert.Equal("move:11:9:1", Bot.ChooseAction(View(hard), legal));
        }

        // ================================================================= 05 spare the Paintress

        [Fact]
        public void WhiteHairedManBot_NeverAttacksPaintressUnlessOneStepFromWin()
        {
            FakeBoard Board(bool oneStep) => PaintressNear(Table(bot: true).Seat(2, EngineId.Eyrie, CharacterIds.Paintress), oneStep)
                .Warriors(0, "The Monolith", 1).Warriors(0, 7, 1).Warriors(2, 7, Tuning.WhiteHairedManBombWarriors);
            var registry = RegistryWith(CharacterIds.Paintress, () => new PaintressModule());

            var far = Start(Board(false), registry, act: 2);
            var v = View(far);
            Assert.Equal(2, v.UnitsMissing(S(2)));
            Assert.True(Bot.SparePaintress(v));
            Assert.Null(Bot.ChooseAction(v, new[] { "battle:3:2" }));
            Assert.DoesNotContain(C(7), Bot.Plots(v).Bomb.Select(r => r.Item));
            Assert.Null(Bot.ChooseAction(v, new[] { "plot:7:bomb", "battle:7:2" }));

            var near = Start(Board(true), registry, act: 2);
            var n = View(near);
            Assert.Equal(1, n.UnitsMissing(S(2)));
            Assert.False(Bot.SparePaintress(n));
            Assert.Contains(C(7), Bot.Plots(n).Bomb.Select(r => r.Item));
            Assert.Equal("battle:3:2", Bot.ChooseAction(n, new[] { "battle:3:2" }));
        }

        // ================================================================= 06 goal mode

        [Fact]
        public void WhiteHairedManBot_GoalModeNeverLeavesRing()
        {
            var board = Table(bot: true).Warriors(0, 9, 3).Warriors(0, "The Monolith", 2).Warriors(0, 7, 2)
                .Vp(0, Tuning.WhiteHairedManGoalVp);
            var t = Start(board);
            var v = View(t);
            var orders = Bot.GoalMode(v);
            Assert.True(orders.Active);
            Assert.Equal(new[] { 3, 9, 10, 11 }, orders.Hold.Select(c => c.Value).OrderBy(x => x));
            Assert.False(Bot.MayMove(v, C(9), C(7)));
            Assert.False(Bot.MayMove(v, C(3), C(4)));
            Assert.True(Bot.MayMove(v, C(9), C(10)));
            Assert.True(Bot.MayMove(v, C(3), C(11)));
            Assert.True(Bot.MayMove(v, C(7), C(9)));
            Assert.Null(Bot.ChooseAction(v, new[] { "move:9:7:1", "move:9:2:2", "move:3:9:1" }));

            board.Vp(0, Tuning.WhiteHairedManGoalVp - 1);
            Assert.False(Bot.GoalMode(View(t)).Active);
            Assert.True(Bot.MayMove(View(t), C(9), C(7)));
        }

        [Fact]
        public void WhiteHairedManBot_GoalModeBattlesVagabondInMonolith()
        {
            // The Ring is held by face-up Snares, so step 4 has nothing to fix.
            var board = Table(bot: true).Seat(2, EngineId.VagabondRanger).Pawn(2, "The Monolith")
                .FaceUpPlotAt(0, "9", CorvidPlots.Snare).FaceUpPlotAt(0, "10", CorvidPlots.Snare).FaceUpPlotAt(0, "11", CorvidPlots.Snare)
                .Warriors(0, "The Monolith", 2).Vp(0, Tuning.WhiteHairedManGoalVp);
            var t = Start(board);
            var legal = new[] { "move:3:9:1", "battle:3:2" };
            Assert.Equal(new[] { S(2) }, Bot.GoalMode(View(t)).MonolithVagabonds);
            Assert.Equal("battle:3:2", Bot.ChooseAction(View(t), legal));

            // Not in goal mode: step 6 does not claim it.
            board.Vp(0, Tuning.WhiteHairedManGoalVp - 1);
            Assert.Empty(Bot.GoalMode(View(t)).MonolithVagabonds);
            Assert.Null(Bot.ChooseAction(View(t), legal));

            // No warriors of his in the Monolith: nothing to battle with.
            board.Vp(0, Tuning.WhiteHairedManGoalVp).Warriors(0, "The Monolith", 0);
            Assert.Empty(Bot.GoalMode(View(t)).MonolithVagabonds);
        }

        // ================================================================= 07 spoiler

        [Fact]
        public void WhiteHairedManBot_SpoilerStaysInRingInGoalMode()
        {
            // Monoco (seat 1) has his banner up; his Warlord stands in Gestral Village 7, next to Visages 9.
            FakeBoard Board(int vp) => FakeBoard.Autumn().Seat(0, EngineId.Corvids, CharacterIds.WhiteHairedMan, bot: true)
                .Seat(1, EngineId.Hundreds, CharacterIds.Monoco).Supply(0, 10)
                .Pawn(1, "7").Warriors(1, 7, 1).Warriors(0, 9, 3).Hand(0, Suit.Fox).Vp(0, vp);
            var registry = RegistryWith(CharacterIds.Monoco, () => new MonocoModule());
            var legal = new[] { "move:9:7:1", "recruit:fox" };

            var normal = Start(Board(Tuning.WhiteHairedManGoalVp - 1), registry, act: 2);
            normal.State.GlobalCountdowns.Add(S(1));
            var a = Bot.Spoiler(View(normal));
            Assert.Equal(WhmAnswerKind.MoveThenBattle, a.Kind);
            Assert.Equal(C(9), a.MoveFrom);
            Assert.True(a.PreEmpts);
            Assert.Equal("move:9:7:1", Bot.ChooseAction(View(normal), legal));

            var goal = Start(Board(Tuning.WhiteHairedManGoalVp), registry, act: 2);
            goal.State.GlobalCountdowns.Add(S(1));
            Assert.Null(Bot.Spoiler(View(goal)));
            Assert.NotEqual("move:9:7:1", Bot.ChooseAction(View(goal), legal));

            // Warriors outside the Ring may still answer in goal mode.
            goal.Board.Warriors(0, 2, 2);
            var b = Bot.Spoiler(View(goal));
            Assert.Equal(C(2), b.MoveFrom);
            Assert.Equal("move:2:7:2", Bot.ChooseAction(View(goal), new[] { "move:9:7:1", "move:2:7:2" }));
        }

        [Fact]
        public void WhiteHairedManBot_SpoilerSparesPaintressUnlessOneStep()
        {
            // Monoco's banner is up and his Warlord stands in Gestral Village 7 with two warriors, beside two of the
            // Paintress's warriors and one of his.
            FakeBoard Board(bool oneStep) => PaintressNear(FakeBoard.Autumn()
                    .Seat(0, EngineId.Corvids, CharacterIds.WhiteHairedMan, bot: true)
                    .Seat(1, EngineId.Hundreds, CharacterIds.Monoco).Seat(2, EngineId.Eyrie, CharacterIds.Paintress), oneStep)
                .Supply(0, 10).Pawn(1, "7").Warriors(1, 7, 2).Warriors(2, 7, 2).Warriors(0, 7, 1);
            var registry = RegistryWith(CharacterIds.Monoco, () => new MonocoModule())
                .Register(CharacterIds.Paintress, () => new PaintressModule());
            var legal = new[] { "plot:7:bomb", "battle:7:2", "battle:7:1" };

            var far = Start(Board(false), registry, act: 2);
            far.State.GlobalCountdowns.Add(S(1));
            var a = Bot.Spoiler(View(far));
            Assert.Equal(S(1), a.Threat);
            Assert.Equal(WhmAnswerKind.Battle, a.Kind); // no Bomb on her pieces
            Assert.Equal("battle:7:1", Bot.ChooseAction(View(far), legal));

            var near = Start(Board(true), registry, act: 2);
            near.State.GlobalCountdowns.Add(S(1));
            Assert.Equal(1, View(near).UnitsMissing(S(2)));
            var b = Bot.Spoiler(View(near));
            Assert.Equal(S(1), b.Threat);
            Assert.Equal(WhmAnswerKind.Bomb, b.Kind);
            Assert.Equal("plot:7:bomb", Bot.ChooseAction(View(near), legal));
        }
    }
}
