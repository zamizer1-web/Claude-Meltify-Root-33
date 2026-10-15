using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Curator;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Characters
{
    /// <summary>The Curator: win.curator, tweak.curator.*, spawn.cell-crack, state.curator.cell-crack, bot.curator and its
    /// rules. Autumn: Lumière is 1 (neighbours 5, 8, 12), the Monolith is 3 (neighbours 9, 10, 11), corners 1-4.</summary>
    public class CuratorTests
    {
        private const int Lumiere = 1, Monolith = 3;

        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Curator, () => new CuratorModule());

        /// <summary>The Curator's Duchy at seat 0 (three tunnels in supply), the Paintress's Eyrie at seat 1 (no module).</summary>
        private static FakeBoard Table(bool bot = false) =>
            FakeBoard.Autumn().Seat(0, EngineId.Duchy, CharacterIds.Curator, bot).Seat(1, EngineId.Eyrie, CharacterIds.Paintress)
                .SupplyOf(0, PieceTypes.Tunnel, 3);

        /// <summary>Starts the game and opens each act up to <paramref name="act"/> (Act III fires story.act3.cell-crack).</summary>
        private static TestTable Start(FakeBoard b, int act = 1, Difficulty d = Difficulty.Normal, ModuleRegistry registry = null)
        {
            var t = TestTable.Start(b, registry ?? Registry(), d);
            for (var a = 2; a <= act; a++) t.Game.OnActChanged(a);
            return t;
        }

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));

        private static string Crack(TestTable t) => t.State.CuratorCellCrack;

        private static string CrackOverlay => FakeBoard.OverlayKey(Ids.Overlays.CuratorCellCrack, S(0), C(Monolith), null);

        private static bool Won(FakeBoard b) => b.Winners.Any(w => w.Key == S(0) && w.Value == Ids.WinConditions.Curator);

        // ================================================================= win.curator

        [Fact]
        public void CuratorWin_AnnouncesWithMonolithTunnelAndLumiereRuled()
        {
            var board = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            var t = Start(board, act: 3);
            Assert.Equal(1, board.TokensOfType(S(0), C(Monolith), PieceTypes.Tunnel)); // the crack opened into his tunnel
            var win = new CuratorWin();
            Assert.Equal(0, win.UnitsMissing(t.Ctx, S(0)));
            Assert.Equal(new[] { C(Lumiere), C(Monolith) }, win.KeyClearings(t.Ctx, S(0)));

            t.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Curator, board.Banners[S(0)]);
            Assert.Contains(S(0), t.State.GlobalCountdowns);
            Assert.Empty(board.Winners);

            t.PlayTurn(1).PlayTurn(0);
            Assert.True(Won(board));
            t.Game.OnGameOver(new[] { S(0) });
            Assert.True(board.OverlayShown(Ids.Overlays.CuratorEnding));

            // A Paintress roost in the Monolith blocks the announce.
            var roost = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors)
                .Building(1, "The Monolith", PieceTypes.Roost);
            var t2 = Start(roost, act: 3);
            t2.PlayTurn(0);
            Assert.False(roost.Banners.ContainsKey(S(0)));
            Assert.Equal(1, new CuratorWin().UnitsMissing(t2.Ctx, S(0)));

            // With the Paintress not seated that part always holds.
            var alone = FakeBoard.Autumn().Seat(0, EngineId.Duchy, CharacterIds.Curator).Seat(1, EngineId.Marquise)
                .SupplyOf(0, PieceTypes.Tunnel, 3).Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors)
                .Building(1, "The Monolith", PieceTypes.Roost);
            var t3 = Start(alone, act: 3);
            t3.PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Curator, alone.Banners[S(0)]);

            // Without his tunnel in the Monolith there is no announce.
            var noTunnel = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            var t4 = Start(noTunnel, act: 3);
            noTunnel.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
            t4.PlayTurn(0);
            Assert.False(noTunnel.Banners.ContainsKey(S(0)));
        }

        [Fact]
        public void CuratorWin_ConfirmFailsAfterLumiereRuleFlips()
        {
            var board = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            var t = Start(board, act: 3);
            t.PlayTurn(0);
            Assert.Contains(S(0), t.State.GlobalCountdowns);

            // The Paintress marches 4 warriors into Lumière: she rules it now.
            t.PlayTurn(1, during: x => x.Board.Warriors(1, "Lumière", 4));
            Assert.Equal(S(1), board.Ruler(C(Lumiere)));
            t.PlayTurn(0);
            Assert.Empty(board.Winners);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.DoesNotContain(S(0), t.State.GlobalCountdowns);
            Assert.Contains("ClearBanner seat0", board.Log);

            // Removing his Monolith tunnel before his Birdsong also stops it.
            var b2 = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            var t2 = Start(b2, act: 3);
            t2.PlayTurn(0);
            Assert.Contains(S(0), t2.State.GlobalCountdowns);
            t2.PlayTurn(1, during: x =>
            {
                x.Board.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
                x.Game.OnPieceRemoved(S(1), new RemovedPiece(S(0), PieceKind.Token, PieceTypes.Tunnel), C(Monolith));
            });
            t2.PlayTurn(0);
            Assert.Empty(b2.Winners);
            Assert.DoesNotContain(S(0), t2.State.GlobalCountdowns);
        }

        [Fact]
        public void CuratorWin_BlockedBeforeActThree()
        {
            // Act II: everything on the board holds (his tunnel dug into the Monolith by hand), but the win is locked.
            var board = Table().Vp(0, 12).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors).Token(0, "The Monolith", PieceTypes.Tunnel);
            var t = Start(board, act: 2);
            Assert.True(new CuratorWin().Announce(t.Ctx, S(0)));
            Assert.False(WinRules.IsUnlocked(2, Data.WinConditions[Ids.WinConditions.Curator]));
            t.PlayTurn(0);
            Assert.Equal(2, t.Ctx.Act);
            Assert.False(board.Banners.ContainsKey(S(0)));
            Assert.Empty(t.State.GlobalCountdowns);

            // Act III below tune.countdown-vp: blocked.
            var low = Table().Vp(0, Tuning.CountdownVp - 1).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            var t2 = Start(low, act: 3);
            t2.PlayTurn(0);
            Assert.False(low.Banners.ContainsKey(S(0)));

            // Act III with an active dominance card: off.
            var dom = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors).Dominance(0, Suit.Fox);
            var t3 = Start(dom, act: 3);
            t3.PlayTurn(0);
            Assert.False(dom.Banners.ContainsKey(S(0)));
            dom.Dominance(0, null);
            t3.PlayTurn(1).PlayTurn(0);
            Assert.Equal(Ids.WinConditions.Curator, dom.Banners[S(0)]);
        }

        [Fact]
        public void CuratorWin_NoKeepNeedsBuildingOrThreeWarriors()
        {
            var board = FakeBoard.Autumn().Seat(0, EngineId.Duchy, CharacterIds.Curator).Seat(1, EngineId.Eyrie, CharacterIds.Paintress)
                .Seat(2, EngineId.Marquise).SupplyOf(0, PieceTypes.Tunnel, 3).Vp(0, Tuning.CountdownVp)
                .Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors - 1);
            var t = Start(board, act: 3);
            var win = new CuratorWin();
            Assert.Equal(S(0), board.Ruler(C(Lumiere)));
            Assert.False(win.Announce(t.Ctx, S(0)));                       // rules with 2 warriors, no building, no keep
            Assert.Equal(1, win.UnitsMissing(t.Ctx, S(0)));

            board.Building(0, "Lumière", "citadel");
            Assert.True(win.Announce(t.Ctx, S(0)));                        // one of his buildings is enough
            board.RemoveBuilding(0, "Lumière", "citadel");
            board.Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors);
            Assert.True(win.Announce(t.Ctx, S(0)));                        // or 3 warriors

            // A keep in Lumière (any seat's): ruling it is enough.
            board.Warriors(0, "Lumière", 1).Token(2, "Lumière", PieceTypes.Keep);
            Assert.Equal(S(0), board.Ruler(C(Lumiere)));
            Assert.True(win.Announce(t.Ctx, S(0)));
            // ...but he must still rule it.
            board.Warriors(2, "Lumière", 1);
            Assert.Null(board.Ruler(C(Lumiere)));
            Assert.False(win.Announce(t.Ctx, S(0)));

            // Through the countdown: 2 warriors and no keep is no banner.
            var b2 = Table().Vp(0, Tuning.CountdownVp).Warriors(0, "Lumière", Tuning.CuratorLumiereWarriors - 1);
            var t2 = Start(b2, act: 3);
            t2.PlayTurn(0);
            Assert.False(b2.Banners.ContainsKey(S(0)));
        }

        // ================================================================= tweak.curator.quiet-helper

        private static FakeBoard QuietTable() =>
            Table().Seat(2, EngineId.Marquise).Building(1, "The Monolith", PieceTypes.Roost).Building(1, 9, PieceTypes.Roost);

        [Fact]
        public void QuietHelper_RoostRemoverDrawsCard()
        {
            var board = QuietTable();
            var t = Start(board);
            board.RemoveBuilding(1, "The Monolith", PieceTypes.Roost);
            t.Game.OnPieceRemoved(S(2), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(Monolith));
            Assert.Contains(new KeyValuePair<SeatId, int>(S(2), Tuning.CuratorQuietHelperCards), board.Draws);
            Assert.Contains(board.Bubbles, x => x.Key == S(0) && x.Value == "bark.curator.04");

            // Outside the remover's turn, with do.draw-cards unsupported, the card waits for its Evening.
            var b2 = QuietTable().Unsupported(Ids.BoardActions.DrawCards);
            var t2 = Start(b2);
            t2.Game.OnPieceRemoved(S(2), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(Monolith));
            Assert.Equal(Tuning.CuratorQuietHelperCards, t2.State.GlobalPendingDraws[S(2)]);
        }

        [Fact]
        public void QuietHelper_NotForRoostsOutsideMonolith()
        {
            var board = QuietTable();
            var t = Start(board);
            t.Game.OnPieceRemoved(S(2), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(9));
            Assert.Empty(board.Draws);
            // Nor for other pieces of hers in the Monolith.
            t.Game.OnPieceRemoved(S(2), new RemovedPiece(S(1), PieceKind.Warrior, null), C(Monolith));
            t.Game.OnPieceRemoved(S(2), new RemovedPiece(S(2), PieceKind.Building, PieceTypes.Sawmill), C(Monolith));
            Assert.Empty(board.Draws);
            Assert.DoesNotContain(board.Bubbles, x => x.Value == "bark.curator.04");
        }

        [Fact]
        public void QuietHelper_NotWhenCuratorRemovesIt()
        {
            var board = QuietTable();
            var t = Start(board);
            t.Game.OnPieceRemoved(S(0), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(Monolith));
            Assert.Empty(board.Draws);
            // Nor when the Paintress removes her own, nor with no remover (a card effect).
            t.Game.OnPieceRemoved(S(1), new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(Monolith));
            t.Game.OnPieceRemoved(null, new RemovedPiece(S(1), PieceKind.Building, PieceTypes.Roost), C(Monolith));
            Assert.Empty(board.Draws);
        }

        // ================================================================= tweak.curator.home-corner

        [Fact]
        public void CuratorSetup_HomeCornerExcludesLumiereAndMonolith()
        {
            var board = Table().Seat(2, EngineId.Marquise);
            TestTable.Start(board, Registry(), beforeSetup: g => { g.OnSetupChoice(S(1)); g.OnSetupChoice(S(0)); });
            Assert.Equal(new[] { C(2), C(4) }, board.HomeRestrictions[S(0)]);
            Assert.False(board.HomeRestrictions.ContainsKey(S(1)));

            // A corner holding another seat's piece is not free.
            var taken = Table().Seat(2, EngineId.Marquise).Warriors(2, 4, 1);
            TestTable.Start(taken, Registry(), beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.Equal(new[] { C(2) }, taken.HomeRestrictions[S(0)]);

            // Fallback: a bot is steered to an allowed corner, a human is warned.
            var bot = Table(bot: true).Unsupported(Ids.BoardActions.RestrictHome).Legal(0, "home:1", "home:2", "home:3", "home:4");
            TestTable.Start(bot, Registry(), beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.Equal(new[] { new KeyValuePair<SeatId, string>(S(0), "home:2") }, bot.SubmittedActions);
            var human = Table().Unsupported(Ids.BoardActions.RestrictHome);
            var th = TestTable.Start(human, Registry(), beforeSetup: g => g.OnSetupChoice(S(0)));
            Assert.Empty(human.HomeRestrictions);
            Assert.Contains(th.Ctx.Warnings, w => w.Contains("Curator"));
        }

        // ================================================================= spawn.cell-crack, state.curator.cell-crack

        [Fact]
        public void CellCrack_InertBeforeActThree()
        {
            var board = Table();
            var t = Start(board);
            Assert.Equal(CuratorTweaks.Inert, Crack(t));
            Assert.Contains(CrackOverlay, board.Overlays);
            Assert.Equal(0, board.TokensOfType(S(0), C(Monolith), PieceTypes.Tunnel));
            Assert.Equal(3, board.SupplyPieces(S(0), PieceTypes.Tunnel));
            Assert.False(CuratorParts.TunnelHeld(board, S(0), C(Monolith)));

            t.Game.OnActChanged(2);
            Assert.Equal(CuratorTweaks.Inert, Crack(t));
            Assert.Equal(0, board.TokensOfType(S(0), C(Monolith), PieceTypes.Tunnel));
            Assert.Equal(3, board.SupplyPieces(S(0), PieceTypes.Tunnel));
            Assert.Empty(board.FreeDigs);
            // It does not connect to the Burrow: his bot is not in goal mode and does not emerge there.
            var bot = new CuratorBot();
            Assert.False(bot.InGoalMode(View(t)));
            Assert.Null(bot.Reveal(View(t), new[] { "move:burrow:3:4", "dig:3" }));

            // Without the Curator seated the state stays 'none' and no crack is drawn.
            var none = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie, CharacterIds.Paintress);
            var tn = Start(none);
            Assert.Equal(CuratorTweaks.None, Crack(tn));
            Assert.False(none.OverlayShown(Ids.Overlays.CuratorCellCrack));
        }

        [Fact]
        public void CellCrack_BecomesTunnelAtActThree()
        {
            var board = Table();
            var t = Start(board, act: 3);
            Assert.Equal(CuratorTweaks.Tunnel, Crack(t));
            Assert.Equal(1, board.TokensOfType(S(0), C(Monolith), PieceTypes.Tunnel));
            Assert.Equal(2, board.SupplyPieces(S(0), PieceTypes.Tunnel));      // from his supply
            Assert.DoesNotContain(CrackOverlay, board.Overlays);
            Assert.Contains(t.Ctx.StoryLog, e => e.EventId == Ids.StoryEvents.Act3CellCrack);

            // Removed later: 'gone'; he Digs it again normally and it is a tunnel again.
            board.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
            t.Game.OnPieceRemoved(S(1), new RemovedPiece(S(0), PieceKind.Token, PieceTypes.Tunnel), C(Monolith));
            Assert.Equal(CuratorTweaks.Gone, Crack(t));
            board.Token(0, "The Monolith", PieceTypes.Tunnel);
            t.Game.OnActionResolved(S(0));
            Assert.Equal(CuratorTweaks.Tunnel, Crack(t));

            // Fallback: no tunnel placement, so his first Act III action is a free Dig into the Monolith.
            var noPlace = Table().Unsupported(Ids.BoardActions.PlaceTunnel);
            var t2 = Start(noPlace, act: 3);
            Assert.Equal(CuratorTweaks.DigOwed, Crack(t2));
            Assert.Equal(new[] { new KeyValuePair<SeatId, ClearingId>(S(0), C(Monolith)) }, noPlace.FreeDigs);
            Assert.Contains(CrackOverlay, noPlace.Overlays);                  // the crack stays until he digs there
            Assert.Equal("dig:3", new CuratorBot().Reveal(View(t2), new[] { "recruit", "dig:5", "dig:3" }));
            noPlace.Token(0, "The Monolith", PieceTypes.Tunnel);
            t2.Game.OnActionResolved(S(0));
            Assert.Equal(CuratorTweaks.Tunnel, Crack(t2));
            Assert.DoesNotContain(CrackOverlay, noPlace.Overlays);

            // No free Dig either: he Digs normally, paying the card (the table is told).
            var neither = Table().Unsupported(Ids.BoardActions.PlaceTunnel, Ids.BoardActions.GrantFreeDig);
            var t3 = Start(neither, act: 3);
            Assert.Equal(CuratorTweaks.DigOwed, Crack(t3));
            Assert.Contains(t3.Ctx.Warnings, w => w.Contains("Cell Crack"));
        }

        [Fact]
        public void CellCrack_FreeExtraTunnelWhenAllPlaced()
        {
            var board = Table().SupplyOf(0, PieceTypes.Tunnel, 0)
                .Token(0, 5, PieceTypes.Tunnel).Token(0, 7, PieceTypes.Tunnel).Token(0, 12, PieceTypes.Tunnel);
            var t = Start(board, act: 3);
            Assert.Equal(CuratorTweaks.Tunnel, Crack(t));
            Assert.Equal(1, board.TokensOfType(S(0), C(Monolith), PieceTypes.Tunnel));
            Assert.Contains(board.Log, l => l.StartsWith("PlaceTunnel seat0") && l.EndsWith("True"));
            Assert.Equal(1, board.TokensOfType(S(0), C(5), PieceTypes.Tunnel));  // none of his three moved
        }

        [Fact]
        public void CellCrack_LoadChecksStateAgainstBoard()
        {
            // 'tunnel' with no tunnel of his in the Monolith becomes 'gone'.
            var board = Table();
            var t = Start(board, act: 3);
            board.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(CuratorTweaks.Gone, loaded.State.CuratorCellCrack);

            // 'inert' in Act III (the opening never happened) becomes 'dig-owed'.
            var b2 = Table();
            var t2 = Start(b2, act: 3);
            b2.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
            var l2 = t2.SaveAndLoad(Registry(), text => text.Replace(Ids.State.CuratorCellCrack + "=tunnel", Ids.State.CuratorCellCrack + "=inert"));
            Assert.Equal(CuratorTweaks.DigOwed, l2.State.CuratorCellCrack);
            Assert.Contains(new KeyValuePair<SeatId, ClearingId>(S(0), C(Monolith)), b2.FreeDigs);

            // A save without the row, with him seated, reads as 'inert'; 'inert' before Act III stays.
            var b3 = Table();
            var t3 = Start(b3, act: 2);
            var l3 = t3.SaveAndLoad(Registry(), text => string.Join("\n", text.Split('\n').Where(x => !x.StartsWith(Ids.State.CuratorCellCrack + "="))));
            Assert.Equal(CuratorTweaks.Inert, l3.State.CuratorCellCrack);
            Assert.Contains(CrackOverlay, b3.Overlays);
        }

        // ================================================================= bot.curator

        private sealed class Progress { public int Missing = 2; }

        /// <summary>Another seat's win with a public Goal Tracker that reports what the test sets.</summary>
        private sealed class ThreatModule : CharacterModule
        {
            private readonly string _id;
            public ThreatModule(string id, Progress p, params ClearingId[] keys) { _id = id; Win = new ThreatWin(Data.Characters[id].WinCondition, p, keys); }
            public override string CharacterId => _id;
            public override IWinCondition Win { get; }
        }

        private sealed class ThreatWin : WinConditionBase, IGoalProgress
        {
            private readonly Progress _p;
            private readonly ClearingId[] _keys;
            public ThreatWin(string id, Progress p, ClearingId[] keys) : base(id) { _p = p; _keys = keys; }
            public int UnitsMissing(GameContext ctx, SeatId seat) => _p.Missing;
            public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat) => _keys;
        }

        [Fact]
        public void CuratorBot_StocksBurrowToSixInActsOneAndTwo()
        {
            var bot = new CuratorBot();
            Assert.Equal(Ids.BotPlans.Curator, bot.PlanId);
            Assert.Empty(ModuleRegistry.MissingBotRules(bot));
            Assert.True(ModuleRegistry.Default().Has(CharacterIds.Curator));

            var board = Table(bot: true).Burrow(0, Tuning.CuratorBurrowStock - 1);
            var t = Start(board);
            Assert.False(bot.InGoalMode(View(t)));
            Assert.Equal("recruit", bot.Helper(View(t), new[] { "recruit" }));
            Assert.Equal("recruit", bot.ChooseAction(View(t), new[] { "recruit", "dig:5" }));
            board.Burrow(0, Tuning.CuratorBurrowStock);
            Assert.Null(bot.Helper(View(t), new[] { "recruit" }));                 // stocked: no more recruiting
            board.Burrow(0, 2);
            t.Game.OnActChanged(2);
            Assert.Equal("recruit", bot.Helper(View(t), new[] { "recruit" }));

            // Order: a camp or stall in a ruled clearing with a free slot, then the highest-VP minister, then the Burrow.
            board.Warriors(0, 2, 1);
            Assert.Equal("build:2:citadel", bot.Helper(View(t), new[] { "recruit", "sway:captain:1", "build:2:citadel", "build:5:market" }));
            Assert.Equal("sway:banker:2", bot.Helper(View(t), new[] { "recruit", "sway:captain:1", "sway:banker:2" }));

            // Act III: the helper is done.
            t.Game.OnActChanged(3);
            Assert.Null(bot.Helper(View(t), new[] { "recruit" }));
        }

        [Fact]
        public void CuratorBot_AvoidsBigFightsBeforeActThree()
        {
            var board = Table(bot: true).Seat(2, EngineId.Marquise)
                .Warriors(0, 5, 3).Warriors(0, 6, 3).Warriors(2, 5, 1).Warriors(2, 6, 2).Burrow(0, Tuning.CuratorBurrowStock);
            var t = Start(board, act: 2);
            var bot = new CuratorBot();
            Assert.False(bot.CanBattle(View(t), C(6), S(2)));                        // 2 defenders: a big fight
            Assert.True(bot.CanBattle(View(t), C(5), S(2)));
            Assert.Equal(new[] { "battle:5:2", "recruit" }, bot.Allowed(View(t), new[] { "battle:6:2", "battle:5:2", "recruit" }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "battle:6:2" }));
            // Root's own AI may not start it for him either (hook.battle-legal, his bot seat only).
            Assert.False(t.Game.CanStartBattle(S(0), S(2), C(6), false));
            Assert.True(t.Game.CanStartBattle(S(0), S(2), C(5), false));
            Assert.True(t.Game.CanStartBattle(S(2), S(0), C(6), false));

            // A human Curator is never limited.
            var human = Table().Seat(2, EngineId.Marquise).Warriors(0, 6, 3).Warriors(2, 6, 2);
            var th = Start(human, act: 2);
            Assert.True(th.Game.CanStartBattle(S(0), S(2), C(6), false));

            // Act III: big fights are allowed.
            t.Game.OnActChanged(3);
            Assert.True(bot.CanBattle(View(t), C(6), S(2)));
            Assert.True(t.Game.CanStartBattle(S(0), S(2), C(6), false));
        }

        [Fact]
        public void CuratorBot_NeverAttacksPaintressUnlessOneStepFromWin()
        {
            var p = new Progress { Missing = 2 };
            var reg = Registry().Register(CharacterIds.Paintress, () => new ThreatModule(CharacterIds.Paintress, p, C(5)));
            var board = Table(bot: true).Vp(1, Tuning.CountdownVp).Warriors(0, 5, 2).Warriors(1, 5, 1).Burrow(0, Tuning.CuratorBurrowStock);
            var t = Start(board, act: 2, registry: reg);
            var bot = new CuratorBot();
            Assert.False(bot.CanBattle(View(t), C(5), S(1)));
            Assert.False(t.Game.CanStartBattle(S(0), S(1), C(5), false));
            Assert.Null(bot.ChooseAction(View(t), new[] { "battle:5:1" }));

            // One step from her win: now he may, and his spoiler layer does.
            p.Missing = 1;
            Assert.True(bot.CanBattle(View(t), C(5), S(1)));
            Assert.True(t.Game.CanStartBattle(S(0), S(1), C(5), false));
            Assert.Equal("battle:5:1", bot.ChooseAction(View(t), new[] { "battle:5:1" }));

            // Her banner up counts too.
            p.Missing = 2;
            t.State.GlobalCountdowns.Add(S(1));
            Assert.True(bot.CanBattle(View(t), C(5), S(1)));
        }

        /// <summary>Act III, the crack open; the Paintress's roost and <paramref name="defenders"/> warriors in the Monolith.</summary>
        private static (TestTable t, FakeBoard b) RevealTable(int defenders, int burrow, bool placeable = true)
        {
            var b = Table(bot: true).Building(1, "The Monolith", PieceTypes.Roost).Warriors(1, "The Monolith", defenders).Burrow(0, burrow);
            if (!placeable) b.Unsupported(Ids.BoardActions.PlaceTunnel);
            return (Start(b, act: 3), b);
        }

        [Fact]
        public void CuratorBot_EmergesInMonolithWithDefendersPlusTwo()
        {
            var bot = new CuratorBot();
            var (t, b) = RevealTable(defenders: 2, burrow: 2 + Tuning.CuratorAssaultMargin);
            Assert.True(bot.InGoalMode(View(t)));
            Assert.Equal(2 + Tuning.CuratorAssaultMargin, bot.RevealWarriorsNeeded(View(t)));
            var legal = new[] { "move:burrow:3:3", "move:burrow:3:4", "recruit" };
            Assert.Equal("move:burrow:3:4", bot.Reveal(View(t), legal));
            Assert.Equal("move:burrow:3:4", bot.ChooseAction(View(t), legal));

            // Out of the Burrow with defenders + 2: he takes the roost down.
            b.Burrow(0, 0).Warriors(0, "The Monolith", 4);
            Assert.Equal("battle:3:1", bot.Reveal(View(t), new[] { "recruit", "battle:3:1" }));

            // The roost gone, the step is done.
            b.RemoveBuilding(1, "The Monolith", PieceTypes.Roost);
            Assert.Null(bot.Reveal(View(t), new[] { "recruit", "battle:3:1" }));

            // Under the fallback the free Dig into the Monolith comes first.
            var (f, _) = RevealTable(defenders: 2, burrow: 4, placeable: false);
            Assert.Equal(CuratorTweaks.DigOwed, f.State.CuratorCellCrack);
            Assert.True(bot.InGoalMode(View(f)));
            Assert.Equal("dig:3", bot.ChooseAction(View(f), new[] { "move:burrow:3:4", "dig:3" }));
        }

        [Fact]
        public void CuratorBot_WaitsInBurrowUntilDefendersPlusTwo()
        {
            var bot = new CuratorBot();
            var (t, b) = RevealTable(defenders: 2, burrow: 3);
            Assert.Equal("recruit", bot.Reveal(View(t), new[] { "move:burrow:3:3", "recruit" }));
            Assert.Null(bot.Reveal(View(t), new[] { "move:burrow:3:3" }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:burrow:3:3" }));     // never emerges short
            b.Burrow(0, 4);
            Assert.Equal("move:burrow:3:4", bot.Reveal(View(t), new[] { "move:burrow:3:4", "recruit" }));
            // More defenders, more needed.
            b.Warriors(1, "The Monolith", 3);
            Assert.Equal("recruit", bot.Reveal(View(t), new[] { "move:burrow:3:4", "recruit" }));
        }

        /// <summary>Act III, the crack open, no roost in the Monolith; a Marquise at seat 2.</summary>
        private static (TestTable t, FakeBoard b) LumiereTable(Difficulty d = Difficulty.Normal, EngineId other = EngineId.Marquise)
        {
            var b = Table(bot: true).Seat(2, other);
            return (Start(b, act: 3, d: d), b);
        }

        [Fact]
        public void CuratorBot_DigsNextToLumiereWhileKeepStands()
        {
            var bot = new CuratorBot();
            var (t, b) = LumiereTable();
            b.Token(2, "Lumière", PieceTypes.Keep).Warriors(2, "Lumière", 2).Warriors(2, 5, 1).Warriors(2, 8, 2);
            Assert.Equal(C(12), bot.DigTarget(View(t)));                           // next to Lumière, fewest enemy warriors
            var legal = new[] { "dig:1", "dig:5", "dig:8", "dig:12" };
            Assert.Equal("dig:12", bot.Lumiere(View(t), legal));
            Assert.Equal("dig:12", bot.ChooseAction(View(t), legal));
            // Ties go to the lower clearing.
            b.Warriors(2, 5, 0);
            Assert.Equal(C(5), bot.DigTarget(View(t)));
            // His tunnel there: no second dig.
            b.Token(0, 5, PieceTypes.Tunnel);
            Assert.NotEqual("dig:5", bot.Lumiere(View(t), legal));

            // No keep: into Lumière itself.
            b.RemoveToken(2, "Lumière", PieceTypes.Keep);
            Assert.Equal(C(Lumiere), bot.DigTarget(View(t)));
            Assert.Equal("dig:1", bot.Lumiere(View(t), legal));

            // Not before Act III.
            var a2 = Start(Table(bot: true).Seat(2, EngineId.Marquise), act: 2);
            Assert.Null(bot.Lumiere(View(a2), legal));
        }

        [Fact]
        public void CuratorBot_EntersLumiereWithDefendersPlusTwo()
        {
            var bot = new CuratorBot();
            var (t, b) = LumiereTable();
            b.Warriors(2, "Lumière", 2).Token(0, "Lumière", PieceTypes.Tunnel).Burrow(0, 6).Warriors(0, 5, 5);
            Assert.Equal(S(2), b.Ruler(C(Lumiere)));
            Assert.Equal(2 + Tuning.CuratorAssaultMargin, bot.LumiereWarriorsNeeded(View(t)));
            Assert.Equal("move:burrow:1:4", bot.Lumiere(View(t), new[] { "move:burrow:1:3", "move:burrow:1:5", "move:burrow:1:4" }));
            Assert.Equal("move:5:1:4", bot.Lumiere(View(t), new[] { "move:5:1:2", "move:5:1:4", "move:5:1:5" }));
            Assert.Null(bot.Lumiere(View(t), new[] { "move:burrow:1:3" }));       // never in short

            // Battle and Move ministers are swayed first, then by VP.
            Assert.Equal("sway:marshal:1", bot.Lumiere(View(t), new[] { "sway:banker:3", "sway:marshal:1" }));
            Assert.Equal(new[] { "sway:captain:2", "sway:marshal:1", "sway:banker:3", "sway:foremole:1" },
                bot.MinisterRanking(new[] { "sway:foremole:1", "sway:banker:3", "sway:marshal:1", "sway:captain:2" }).Select(r => r.Item));

            // In with defenders + 2 he rules it; with no keep he builds there or holds 3 warriors.
            b.Warriors(0, "Lumière", 4);
            Assert.Equal(S(0), b.Ruler(C(Lumiere)));
            Assert.Equal("build:1:citadel", bot.Lumiere(View(t), new[] { "build:5:citadel", "build:1:citadel" }));
            b.Warriors(0, "Lumière", 2).Warriors(2, "Lumière", 0);
            Assert.Equal("move:5:1:1", bot.Lumiere(View(t), new[] { "move:5:1:1", "move:5:1:3" }));

            // A ruler he cannot out-rule (a Lizard garden): battle it with defenders + 2 in.
            var (z, zb) = LumiereTable(other: EngineId.Lizards);
            zb.Building(2, "Lumière", PieceTypes.Garden).Warriors(2, "Lumière", 1).Token(0, "Lumière", PieceTypes.Tunnel)
              .Warriors(0, "Lumière", 1 + Tuning.CuratorAssaultMargin);
            Assert.Equal(S(2), zb.Ruler(C(Lumiere)));
            Assert.Equal("battle:1:2", bot.Lumiere(View(z), new[] { "recruit", "battle:1:2" }));
        }

        [Fact]
        public void CuratorBot_EasyNeverRedigsAndNeedsDefendersPlusThree()
        {
            var bot = new CuratorBot();
            foreach (var d in new[] { Difficulty.Easy, Difficulty.Normal })
            {
                var (t, b) = LumiereTable(d);
                b.RemoveToken(0, "The Monolith", PieceTypes.Tunnel);
                t.Game.OnPieceRemoved(S(2), new RemovedPiece(S(0), PieceKind.Token, PieceTypes.Tunnel), C(Monolith));
                Assert.Equal(CuratorTweaks.Gone, t.State.CuratorCellCrack);
                b.Token(0, "Lumière", PieceTypes.Tunnel).Warriors(2, "Lumière", 2).Burrow(0, 6);
                var redig = bot.Lumiere(View(t), new[] { "dig:3", "move:burrow:1:4", "move:burrow:1:5" });
                if (d == Difficulty.Easy)
                {
                    Assert.NotEqual("dig:3", redig);
                    Assert.Equal(2 + Tuning.CuratorEasyAssaultMargin, bot.LumiereWarriorsNeeded(View(t)));
                    Assert.Equal("move:burrow:1:5", redig);
                    Assert.Null(bot.Lumiere(View(t), new[] { "move:burrow:1:4" }));
                }
                else
                {
                    Assert.Equal("dig:3", redig);
                    Assert.Equal(2 + Tuning.CuratorAssaultMargin, bot.LumiereWarriorsNeeded(View(t)));
                    Assert.Equal("move:burrow:1:4", bot.Lumiere(View(t), new[] { "move:burrow:1:4", "move:burrow:1:5" }));
                }
            }
        }

        [Fact]
        public void CuratorBot_AnnounceTurnPoursWarriorsIntoLumiere()
        {
            var bot = new CuratorBot();
            var (t, b) = LumiereTable();
            b.Vp(0, Tuning.CountdownVp).Warriors(2, "Lumière", 2).Token(0, 5, PieceTypes.Tunnel).Burrow(0, 3).Warriors(0, 8, 1);
            Assert.Equal(4, bot.Reachable(View(t), C(Lumiere)));
            Assert.True(bot.IsAnnounceTurn(View(t)));
            Assert.Null(bot.Lumiere(View(t), new[] { "dig:1", "move:8:1:1" }));        // step 3 leaves the turn to step 4
            Assert.Equal("recruit", bot.Announce(View(t), new[] { "move:8:1:1", "recruit", "move:burrow:5:3" }));
            Assert.Equal("move:8:1:1", bot.ChooseAction(View(t), new[] { "dig:1", "move:burrow:5:3", "move:8:1:1" }));
            Assert.Equal("move:burrow:5:3", bot.Announce(View(t), new[] { "move:burrow:5:1", "move:burrow:5:3" }));
            Assert.Equal("move:5:1:3", bot.Announce(View(t), new[] { "move:5:1:2", "move:5:1:3" }));

            // Not an announce turn below tune.countdown-vp, or with too few warriors in reach.
            b.Vp(0, Tuning.CountdownVp - 1);
            Assert.False(bot.IsAnnounceTurn(View(t)));
            Assert.Null(bot.Announce(View(t), new[] { "recruit" }));
            b.Vp(0, Tuning.CountdownVp).Burrow(0, 1);
            Assert.False(bot.IsAnnounceTurn(View(t)));
        }

        [Fact]
        public void CuratorBot_SpoilerKeepsHelperLimitsBeforeActThree()
        {
            var bot = new CuratorBot();
            var p = new Progress { Missing = 2 };
            var reg = Registry().Register(CharacterIds.Gustave, () => new ThreatModule(CharacterIds.Gustave, p, C(5), C(6)));
            (TestTable, FakeBoard) Make(int act, Difficulty d = Difficulty.Normal)
            {
                var b = FakeBoard.Autumn().Seat(0, EngineId.Duchy, CharacterIds.Curator, bot: true).Seat(1, EngineId.Marquise, CharacterIds.Gustave)
                    .SupplyOf(0, PieceTypes.Tunnel, 3).Vp(1, Tuning.CountdownVp).Warriors(1, 5, 2).Warriors(1, 6, 1).Warriors(0, 5, 2).Burrow(0, 3);
                var tt = Start(b, act, d, reg);
                tt.State.GlobalCountdowns.Add(S(1));
                b.Turn(0, TurnPhase.Daylight);
                return (tt, b);
            }
            var legal = new[] { "recruit", "battle:5:1", "move:burrow:6:2" };

            // Act II: the battle in 5 is a big fight, so he answers in 6 instead, and the banner pre-empts his helper.
            var (t2, _) = Make(2);
            var a2 = bot.Spoiler(View(t2), legal);
            Assert.Equal(S(1), a2.Threat);
            Assert.True(a2.Banner);
            Assert.Equal("move:burrow:6:2", a2.Key);
            Assert.True(a2.PreEmpts);
            Assert.Equal("move:burrow:6:2", bot.ChooseAction(View(t2), legal));
            Assert.Null(bot.Spoiler(View(t2), new[] { "recruit", "battle:5:1" }));

            // Act III: the cheapest answer, the battle in 5.
            var (t3, _) = Make(3);
            Assert.Equal("battle:5:1", bot.Spoiler(View(t3), legal).Key);

            // Easy answers only with the actions his steps leave free.
            var (e, _) = Make(2, Difficulty.Easy);
            Assert.False(bot.Spoiler(View(e), legal).PreEmpts);
            Assert.Equal("recruit", bot.ChooseAction(View(e), legal));
            Assert.Equal("move:burrow:6:2", bot.ChooseAction(View(e), new[] { "battle:5:1", "move:burrow:6:2" }));
        }

        [Fact]
        public void CuratorBot_SpoilerOrdersBannersByNextBirdsong()
        {
            var bot = new CuratorBot();
            var one = new Progress { Missing = 2 };
            var two = new Progress { Missing = 1 };
            var three = new Progress { Missing = 2 };
            var reg = Registry()
                .Register(CharacterIds.Gustave, () => new ThreatModule(CharacterIds.Gustave, one, C(5)))
                .Register(CharacterIds.Sciel, () => new ThreatModule(CharacterIds.Sciel, two, C(7)))
                .Register(CharacterIds.Paintress, () => new ThreatModule(CharacterIds.Paintress, three, C(9)));
            var b = FakeBoard.Autumn().Seat(0, EngineId.Duchy, CharacterIds.Curator, bot: true)
                .Seat(1, EngineId.Marquise, CharacterIds.Gustave).Seat(2, EngineId.Alliance, CharacterIds.Sciel)
                .Seat(3, EngineId.Eyrie, CharacterIds.Paintress).SupplyOf(0, PieceTypes.Tunnel, 3)
                .Vp(1, Tuning.CountdownVp).Vp(2, 15).Vp(3, Tuning.CountdownVp)
                .Warriors(1, 5, 1).Warriors(2, 7, 1).Warriors(3, 9, 1).Warriors(0, 5, 1).Warriors(0, 7, 1).Warriors(0, 9, 1);
            var t = Start(b, act: 3, registry: reg);
            t.State.GlobalCountdowns.Add(S(1));
            t.State.GlobalCountdowns.Add(S(3));
            var legal = new[] { "battle:5:1", "battle:7:2", "battle:9:3" };

            // His own turn: seat 1's Birdsong comes first; the one-step seat 2 ranks after both banners.
            b.Turn(0, TurnPhase.Daylight);
            Assert.Equal(new[] { S(1), S(3), S(2) }, global::Root33.Core.Engine.SpoilerLayer.Threats(View(t)).Select(x => x.Seat));
            var a = bot.Spoiler(View(t), legal);
            Assert.Equal(S(1), a.Threat);
            Assert.Equal("battle:5:1", a.Key);
            Assert.Equal("battle:5:1", bot.ChooseAction(View(t), legal));

            // During seat 2's turn (an Ambush or a defence), seat 3 confirms first.
            b.Turn(2, TurnPhase.Daylight);
            Assert.Equal(new[] { S(3), S(1), S(2) }, global::Root33.Core.Engine.SpoilerLayer.Threats(View(t)).Select(x => x.Seat));
            Assert.Equal("battle:9:3", bot.Spoiler(View(t), legal).Key);

            // With the banners down only the one-step seat is left: Normal answers with free actions, Hard pre-empts.
            t.State.GlobalCountdowns.Clear();
            b.Turn(0, TurnPhase.Daylight);
            var n = bot.Spoiler(View(t), legal);
            Assert.Equal(S(2), n.Threat);
            Assert.False(n.PreEmpts);
            Assert.False(n.Banner);
        }
    }
}
