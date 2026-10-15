using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Verso;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    public class VersoTests
    {
        private const string Monolith = "role.monolith";
        private static SeatId S(int i) => new SeatId(i);
        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Verso, () => new VersoModule());

        private static TestTable Start(FakeBoard b, int act = 2, Difficulty d = Difficulty.Normal)
        {
            var t = TestTable.Start(b, Registry(), d);
            if (act != 1)
            {
                t.Ctx.ActClock.Restore(act);
                t.State.GlobalAct = t.Ctx.ActClock.Act;
            }
            return t;
        }

        /// <summary>A Marquise (seat 0) and Verso (seat 1, Ranger) in the Monolith with 10 VP.</summary>
        private static FakeBoard Table(bool versoBot = false) => FakeBoard.Autumn()
            .Seat(0, EngineId.Marquise)
            .Seat(1, EngineId.VagabondRanger, CharacterIds.Verso, bot: versoBot)
            .Pawn(1, Monolith).Vp(1, Tuning.CountdownVp)
            .Item(1, ItemType.Sword).Item(1, ItemType.Sword).Item(1, ItemType.Boots);

        private static BattleResult Hit(ClearingId c, int damaged) => new BattleResult
        {
            Clearing = c, Attacker = S(0), Defender = S(1),
            DamagedItemsBefore = { [S(1)] = 0 }, DamagedItemsAfter = { [S(1)] = damaged },
        };

        // ---- win.verso ----

        [Fact]
        public void VersoWin_AnnouncesAtRankSAloneInMonolith()
        {
            var b = Table().Token(0, Monolith, PieceTypes.Wood); // tokens do not block him
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoRankS;
            t.PlayTurn(1);
            Assert.Equal(Ids.WinConditions.Verso, b.Banners[S(1)]);

            var crowded = Table().Warriors(0, Monolith, 1);
            var t2 = Start(crowded);
            t2.State.VersoRank = Tuning.VersoRankS;
            t2.PlayTurn(1);
            Assert.False(crowded.Banners.ContainsKey(S(1)));
        }

        [Fact]
        public void VersoWin_GatesBlockInActOneLowVpAndCoalition()
        {
            var actOne = Table();
            var t1 = Start(actOne, act: 1);
            t1.State.VersoRank = Tuning.VersoRankS;
            t1.PlayTurn(1);
            Assert.False(actOne.Banners.ContainsKey(S(1)));

            var lowVp = Table().Vp(1, Tuning.CountdownVp - 1);
            var t2 = Start(lowVp);
            t2.State.VersoRank = Tuning.VersoRankS;
            t2.PlayTurn(1);
            Assert.False(lowVp.Banners.ContainsKey(S(1)));

            var coalition = Table().Coalition(1, 0);
            var t3 = Start(coalition);
            t3.State.VersoRank = Tuning.VersoRankS;
            t3.PlayTurn(1);
            Assert.False(coalition.Banners.ContainsKey(S(1)));
        }

        [Fact]
        public void VersoWin_ConfirmFailsAfterHitDropsRank()
        {
            var b = Table();
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoRankS;
            t.PlayTurn(1);
            Assert.True(b.Banners.ContainsKey(S(1)));

            t.PlayTurn(0, during: x => x.Game.OnBattleResolved(Hit(b.C(Monolith), 1)));
            Assert.Equal(Tuning.VersoRankS - Tuning.VersoRankDownSmall, t.State.VersoRank);
            t.PlayTurn(1);
            Assert.Empty(b.Winners);
        }

        [Fact]
        public void VersoWin_EasyAssistOnlyForHuman()
        {
            var human = Table();
            var th = Start(human, d: Difficulty.Easy);
            th.State.VersoRank = Tuning.VersoEasyRank;
            th.PlayTurn(1);
            Assert.True(human.Banners.ContainsKey(S(1)));

            var bot = Table(versoBot: true);
            var tb = Start(bot, d: Difficulty.Easy);
            tb.State.VersoRank = Tuning.VersoEasyRank;
            tb.PlayTurn(1);
            Assert.False(bot.Banners.ContainsKey(S(1)));

            var normal = Table();
            var tn = Start(normal);
            tn.State.VersoRank = Tuning.VersoEasyRank;
            tn.PlayTurn(1);
            Assert.False(normal.Banners.ContainsKey(S(1)));
        }

        [Fact]
        public void VersoWin_ConfirmIgnoresWalkIns()
        {
            var b = Table();
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoRankS;
            t.PlayTurn(1);
            Assert.True(b.Banners.ContainsKey(S(1)));
            t.PlayTurn(0, during: x => b.Warriors(0, Monolith, 3));
            t.PlayTurn(1);
            Assert.Contains(b.Winners, w => w.Key == S(1) && w.Value == Ids.WinConditions.Verso);
        }

        [Fact]
        public void VersoWin_RankUpdatesAndClampsAfterBattles()
        {
            var b = Table();
            var t = Start(b);
            var m = b.C(Monolith);
            t.Game.OnBattleResolved(new BattleResult { Clearing = m, Attacker = S(1), Defender = S(0), Removed = { new RemovedPiece(S(0), PieceKind.Warrior, null) } });
            Assert.Equal(Tuning.VersoRankUp, t.State.VersoRank);
            t.Game.OnBattleResolved(new BattleResult { Clearing = m, Attacker = S(1), Defender = S(0) });
            Assert.Equal(Tuning.VersoRankUp, t.State.VersoRank); // no removal, no damage: unchanged
            t.State.VersoRank = Tuning.VersoRankS;
            t.Game.OnBattleResolved(Hit(m, Tuning.VersoHeavyDamage));
            Assert.Equal(Tuning.VersoRankS - Tuning.VersoRankDownLarge, t.State.VersoRank);
            t.State.VersoRank = 0;
            t.Game.OnBattleResolved(Hit(m, 1));
            Assert.Equal(0, t.State.VersoRank);
        }

        // ---- tweak.verso.perfection ----

        [Fact]
        public void Perfection_AddsHitAtRankAOrS()
        {
            foreach (var rank in new[] { Tuning.VersoPerfectionRank, Tuning.VersoRankS })
            {
                var b = Table();
                var t = Start(b);
                t.State.VersoRank = rank;
                t.Game.OnBattleStarting(S(1), S(0), b.C(Monolith));
                Assert.Equal(Tuning.VersoPerfectionHits, b.ExtraHits[S(1)]);
            }
        }

        [Fact]
        public void Perfection_NoHitBelowRankA()
        {
            var b = Table();
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoPerfectionRank - 1;
            t.Game.OnBattleStarting(S(1), S(0), b.C(Monolith));
            Assert.False(b.ExtraHits.ContainsKey(S(1)));
        }

        [Fact]
        public void Perfection_NoHitWhenDefending()
        {
            var b = Table();
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoRankS;
            t.Game.OnBattleStarting(S(0), S(1), b.C(Monolith));
            Assert.Empty(b.ExtraHits);
        }

        // ---- tweak.verso.soarrie-handoff ----

        private static FakeBoard Reunion() => FakeBoard.Autumn()
            .Seat(0, EngineId.Marquise)
            .Seat(1, EngineId.VagabondRanger, CharacterIds.Verso)
            .Seat(2, EngineId.VagabondTinker, CharacterIds.Esquie)
            .Pawn(1, "autumn.01").Pawn(2, "autumn.01");

        private static TestTable StartReunion(FakeBoard b)
        {
            var t = Start(b, act: 1);
            Assert.Equal(RockStatus.NotSpawned, t.State.EsquieRocks[ModState.Soarrie].Status);
            t.Ctx.ActClock.Restore(2);
            t.State.GlobalAct = t.Ctx.ActClock.Act;
            t.Game.OnActChanged(2);
            Assert.Equal(RockStatus.WithVerso, t.State.EsquieRocks[ModState.Soarrie].Status);
            Assert.Equal(S(1), t.State.EsquieRocks[ModState.Soarrie].HostSeat);
            return t;
        }

        [Fact]
        public void SoarrieHandoff_PassesWhenEsquieEndsTurnInVersoClearing()
        {
            var b = Reunion();
            var t = StartReunion(b);
            t.PlayTurn(2);
            Assert.Equal(RockStatus.Held, t.State.EsquieRocks[ModState.Soarrie].Status);
            Assert.Null(t.State.EsquieRocks[ModState.Soarrie].HostSeat);
        }

        [Fact]
        public void SoarrieHandoff_VersoScoresOneVp()
        {
            var b = Reunion();
            var t = StartReunion(b);
            t.PlayTurn(2);
            t.PlayTurn(2); // passes only once
            Assert.Equal(Tuning.VersoSoarrieVp, b.ScoredVp.Where(kv => kv.Key == S(1)).Sum(kv => kv.Value));
            Assert.DoesNotContain(b.ScoredVp, kv => kv.Key == S(2));
        }

        [Fact]
        public void SoarrieHandoff_NotWhenVersoInForest()
        {
            var b = Reunion();
            var t = StartReunion(b);
            b.PawnInForest(1, 1);
            t.PlayTurn(2);
            Assert.Equal(RockStatus.WithVerso, t.State.EsquieRocks[ModState.Soarrie].Status);
            Assert.Empty(b.ScoredVp);
        }
    }
}
