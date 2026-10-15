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
        /// <summary>Verso and Esquie: tweak.verso.soarrie-handoff is carried out by Esquie's module (spawn.soarrie), so the hand-off
        /// tests need both; Esquie's module is bound only on tables where he is seated.</summary>
        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Verso, () => new VersoModule())
            .Register(CharacterIds.Esquie, () => new Root33.Core.Characters.Esquie.EsquieModule());

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
            // ---- bot.verso ----

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(1));

        /// <summary>A Marquise (0), Verso (1, Ranger bot) with two swords and boots, and an Eyrie (2).</summary>
        private static FakeBoard BotBoard(string at = "autumn.05") => FakeBoard.Autumn()
            .Seat(0, EngineId.Marquise)
            .Seat(1, EngineId.VagabondRanger, CharacterIds.Verso, bot: true)
            .Seat(2, EngineId.Eyrie)
            .Pawn(1, at).Item(1, ItemType.Sword).Item(1, ItemType.Sword).Item(1, ItemType.Boots);

        private static FakeBoard OneSword(FakeBoard b) => b.ClearItems(1).Item(1, ItemType.Sword).Item(1, ItemType.Boots);

        [Fact]
        public void VersoBot_ClimbsOnlyWithSafeBattlesBelowRankA()
        {
            Assert.Empty(ModuleRegistry.MissingBotRules(new VersoBot()));
            Assert.IsType<VersoBot>(new VersoModule().Bot);
            var bot = new VersoBot();
            var b = BotBoard().Warriors(0, 5, 2).Warriors(2, 5, 1).Vp(2, 5);
            var t = Start(b, act: 1);
            var legal = new[] { "battle:5:0", "battle:5:2", "explore:5" };
            Assert.Equal(new[] { S(2) }, bot.Climb(View(t)).Select(r => r.Item.Defender).ToArray());
            Assert.Equal("battle:5:2", bot.ChooseAction(View(t), legal));
            // Two defending warriors (0.81 expected hits) are never a climbing fight.
            Assert.False(bot.MayBattle(View(t), b.C(5), S(0)));
            Assert.Null(bot.ChooseAction(View(t), new[] { "battle:5:0", "explore:5" }));

            // Undefended buildings qualify; the VP leader comes first, a Hostile faction before it.
            b.Warriors(0, 5, 0).Building(0, 5, PieceTypes.Sawmill);
            Assert.Equal(0, bot.Target(View(t), b.C(5), S(0)).ExpectedHits);
            Assert.Equal("battle:5:2", bot.ChooseAction(View(t), legal));
            b.Relationship(1, 0, Relationship.Hostile);
            Assert.Equal("battle:5:0", bot.ChooseAction(View(t), legal));

            // From rank A he no longer climbs.
            t.State.VersoRank = Tuning.VersoPerfectionRank;
            Assert.Empty(bot.Climb(View(t)));
        }

        [Fact]
        public void VersoBot_TreatsThreeCardDefenderAsAmbush()
        {
            var bot = new VersoBot();
            var b = OneSword(BotBoard()).Warriors(0, 5, 1).Hand(0, Suit.Fox, Suit.Rabbit);
            var t = Start(b, act: 1);
            var legal = new[] { "battle:5:0" };
            Assert.Equal("battle:5:0", bot.ChooseAction(View(t), legal));

            b.Hand(0, Suit.Fox, Suit.Rabbit, Suit.Bird);
            Assert.Equal(Tuning.VersoBotAmbushHand, b.HandSize(S(0)));
            Assert.Null(bot.Target(View(t), b.C(5), S(0)));
            Assert.Null(bot.ChooseAction(View(t), legal));

            // A second undamaged sword (tune.verso-bot-ambush-swords) makes the possible Ambush acceptable.
            b.Item(1, ItemType.Sword);
            Assert.Equal(Tuning.VersoBotAmbushSwords, VersoRules.UndamagedSwords(b, S(1)));
            var target = bot.Target(View(t), b.C(5), S(0));
            Assert.True(target.PossibleAmbush);
            Assert.Equal("battle:5:0", bot.ChooseAction(View(t), legal));
        }

        [Fact]
        public void VersoBot_EasyIgnoresAmbushRisk()
        {
            var bot = new VersoBot();
            FakeBoard Board() => OneSword(BotBoard()).Warriors(0, 5, 1).Hand(0, Suit.Fox, Suit.Rabbit, Suit.Bird);
            var legal = new[] { "battle:5:0" };
            var normal = Start(Board(), act: 1);
            Assert.Null(bot.ChooseAction(View(normal), legal));
            var hard = Start(Board(), act: 1, d: Difficulty.Hard);
            Assert.Null(bot.ChooseAction(View(hard), legal));
            var easy = Start(Board(), act: 1, d: Difficulty.Easy);
            Assert.Equal("battle:5:0", bot.ChooseAction(View(easy), legal));
            // Easy still keeps the expected-hits filter.
            var crowded = Board().Warriors(0, 5, 2);
            var easy2 = Start(crowded, act: 1, d: Difficulty.Easy);
            Assert.Null(bot.ChooseAction(View(easy2), legal));
        }

        [Fact]
        public void VersoBot_RepairsBeforeNextFightAfterDamage()
        {
            var bot = new VersoBot();
            var b = BotBoard().ClearItems(1).Item(1, ItemType.Sword).Item(1, ItemType.Sword, damaged: true)
                .Item(1, ItemType.Torch).Item(1, ItemType.Boots).Warriors(0, 5, 1);
            var t = Start(b, act: 1);
            Assert.False(bot.MayBattle(View(t), b.C(5), S(0)));
            Assert.Equal("hideout", bot.ChooseAction(View(t), new[] { "battle:5:0", "move:5:1", "move:5:f5", "hideout" }));
            Assert.Equal((int?)5, bot.Repair(View(t)).ToForest);
            Assert.Equal("move:5:f5", bot.ChooseAction(View(t), new[] { "battle:5:0", "move:5:1", "move:5:f5" }));

            // In the forest he ends the turn there (the Evening rest repairs).
            b.PawnInForest(1, 5);
            Assert.True(bot.Repair(View(t)).Stay);
            Assert.Equal("end", bot.ChooseAction(View(t), new[] { "move:f5:5", "end" }));

            // Repaired: the next fight is on again.
            b.ClearItems(1).Item(1, ItemType.Sword).Item(1, ItemType.Sword).Item(1, ItemType.Boots).Pawn(1, "autumn.05");
            Assert.Null(bot.Repair(View(t)));
            Assert.Equal("battle:5:0", bot.ChooseAction(View(t), new[] { "move:5:f5", "battle:5:0" }));
        }

        [Fact]
        public void VersoBot_ApproachesMonolithAtRankAInActTwo()
        {
            var bot = new VersoBot();
            var b = BotBoard("autumn.08").Warriors(0, Monolith, 3);
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoPerfectionRank;
            Assert.True(bot.InGoalMode(View(t)));
            Assert.False(bot.Assault(View(t)).Go);
            var legal = new[] { "move:8:1", "move:8:12", "move:8:10" };
            Assert.Equal("move:8:10", bot.ChooseAction(View(t), legal));
            Assert.DoesNotContain(bot.Approach(View(t)), r => r.Item == b.C(Monolith));

            // Next to the Monolith he farms a lone warrior and does not walk in while the assault is off.
            b.Pawn(1, "autumn.10").Warriors(2, 10, 1);
            Assert.Equal(b.C(10), bot.Approach(View(t))[0].Item);
            Assert.Equal("battle:10:2", bot.ChooseAction(View(t), new[] { "move:10:3", "move:10:4", "battle:10:2" }));
            Assert.False(bot.Allowed(View(t), VersoAction.Parse("move:10:3")));
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:10:3", "move:10:4", "move:10:8" }));

            // Below rank A, or in Act I, there is no approach.
            b.Pawn(1, "autumn.08");
            t.State.VersoRank = Tuning.VersoPerfectionRank - 1;
            Assert.Empty(bot.Approach(View(t)));
            Assert.Null(bot.ChooseAction(View(t), legal));
            var t1 = Start(BotBoard("autumn.08").Warriors(0, Monolith, 3), act: 1);
            t1.State.VersoRank = Tuning.VersoPerfectionRank;
            Assert.False(bot.InGoalMode(View(t1)));
            Assert.Null(bot.ChooseAction(View(t1), legal));
        }

        [Fact]
        public void VersoBot_AssaultsOnlyWhenMonolithClearableThisTurn()
        {
            var bot = new VersoBot();
            var legal = new[] { "move:10:4", "move:10:8", "move:10:3" };
            var b = BotBoard("autumn.10").Warriors(0, Monolith, 1).Building(0, Monolith, PieceTypes.Sawmill);
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoRankS;
            var plan = bot.Assault(View(t));
            Assert.Equal(2, plan.PiecesToClear);
            Assert.True(plan.ExpectedRemovals >= plan.PiecesToClear);
            Assert.True(plan.ExpectedDamage < Tuning.VersoBotAssaultMaxDamage);
            Assert.True(plan.Go);
            Assert.Equal("move:10:3", bot.ChooseAction(View(t), legal));

            // Inside: warriors first, then the buildings; never end the turn with pieces left.
            b.Pawn(1, Monolith).Building(2, Monolith, PieceTypes.Roost);
            Assert.True(bot.Assault(View(t)).Go);
            Assert.Equal("battle:3:0", bot.ChooseAction(View(t), new[] { "end", "battle:3:2", "battle:3:0", "move:3:10" }));
            Assert.False(bot.Allowed(View(t), VersoAction.Parse("end")));

            // Three warriors: the second battle pushes expected damage to 1 or more.
            var t2 = Start(BotBoard("autumn.10").Warriors(0, Monolith, 3).Building(0, Monolith, PieceTypes.Sawmill));
            t2.State.VersoRank = Tuning.VersoRankS;
            Assert.True(bot.Assault(View(t2)).ExpectedDamage >= Tuning.VersoBotAssaultMaxDamage);
            Assert.False(bot.Assault(View(t2)).Go);
            Assert.NotEqual("move:10:3", bot.ChooseAction(View(t2), legal));

            // A possible Ambush in the Monolith stops it.
            var t3 = Start(BotBoard("autumn.10").Warriors(0, Monolith, 1).Hand(0, Suit.Fox, Suit.Fox, Suit.Fox));
            t3.State.VersoRank = Tuning.VersoRankS;
            Assert.False(bot.Assault(View(t3)).Go);

            // One ready sword cannot remove three pieces this turn.
            var t4 = Start(OneSword(BotBoard("autumn.10")).Warriors(0, Monolith, 2).Building(0, Monolith, PieceTypes.Sawmill));
            t4.State.VersoRank = Tuning.VersoRankS;
            Assert.True(bot.Assault(View(t4)).ExpectedRemovals < 3);
            Assert.False(bot.Assault(View(t4)).Go);
            Assert.NotEqual("move:10:3", bot.ChooseAction(View(t4), legal));
        }

        [Fact]
        public void VersoBot_EndsTurnInMonolithOnlyAtRankSWhenEmpty()
        {
            var bot = new VersoBot();
            var b = BotBoard(Monolith);
            var t = Start(b);
            var legal = new[] { "move:3:10", "end" };
            t.State.VersoRank = Tuning.VersoRankS;
            Assert.True(bot.Assault(View(t)).EndTurnThere);
            Assert.Equal("end", bot.ChooseAction(View(t), legal));
            Assert.False(bot.Allowed(View(t), VersoAction.Parse("move:3:10")));

            // At rank A he may not end the turn there: he walks back out.
            t.State.VersoRank = Tuning.VersoPerfectionRank;
            Assert.False(bot.Assault(View(t)).EndTurnThere);
            Assert.False(bot.Allowed(View(t), VersoAction.Parse("end")));
            Assert.Equal("move:3:10", bot.ChooseAction(View(t), legal));

            // At rank S a token left there keeps him fighting rather than ending.
            t.State.VersoRank = Tuning.VersoRankS;
            b.Token(0, Monolith, PieceTypes.Wood);
            Assert.False(bot.Assault(View(t)).EndTurnThere);
            Assert.Equal("battle:3:0", bot.ChooseAction(View(t), new[] { "move:3:10", "end", "battle:3:0" }));

            // Another pawn there also keeps him from ending.
            var t2 = Start(BotBoard(Monolith).Seat(3, EngineId.VagabondThief).Pawn(3, Monolith));
            t2.State.VersoRank = Tuning.VersoRankS;
            Assert.False(bot.Assault(View(t2)).EndTurnThere);
            Assert.NotEqual("end", bot.ChooseAction(View(t2), legal));
        }

        [Fact]
        public void VersoBot_EasyWaitsForFourUndamagedSwords()
        {
            var bot = new VersoBot();
            var legal = new[] { "move:10:4", "move:10:3" };
            FakeBoard Board(int swords)
            {
                var b = BotBoard("autumn.10").ClearItems(1).Item(1, ItemType.Boots);
                for (var i = 0; i < swords; i++) b.Item(1, ItemType.Sword);
                return b;
            }
            TestTable At(FakeBoard b, Difficulty d)
            {
                var t = Start(b, d: d);
                t.State.VersoRank = Tuning.VersoRankS;
                return t;
            }
            var easy = At(Board(Tuning.VersoEasyAssaultSwords - 1), Difficulty.Easy);
            Assert.False(bot.Assault(View(easy)).Go);
            Assert.NotEqual("move:10:3", bot.ChooseAction(View(easy), legal));

            var easyDamaged = At(Board(Tuning.VersoEasyAssaultSwords - 1).Item(1, ItemType.Sword, damaged: true), Difficulty.Easy);
            Assert.False(bot.Assault(View(easyDamaged)).Go);

            var easyReady = At(Board(Tuning.VersoEasyAssaultSwords), Difficulty.Easy);
            Assert.True(bot.Assault(View(easyReady)).Go);
            Assert.Equal("move:10:3", bot.ChooseAction(View(easyReady), legal));

            var normal = At(Board(2), Difficulty.Normal);
            Assert.Equal("move:10:3", bot.ChooseAction(View(normal), legal));
        }

        [Fact]
        public void VersoBot_BailsToForestWhenRankDropsBelowA()
        {
            var bot = new VersoBot();
            var b = BotBoard(Monolith).Warriors(0, Monolith, 2);
            var t = Start(b);
            t.State.VersoRank = Tuning.VersoPerfectionRank - 1;
            Assert.Equal((int?)1, bot.Bail(View(t)));
            Assert.Equal("move:3:f1", bot.ChooseAction(View(t), new[] { "battle:3:0", "move:3:9", "move:3:f3", "move:3:f1", "end" }));

            // At rank A it is not a bail; outside the Monolith neither.
            t.State.VersoRank = Tuning.VersoPerfectionRank;
            Assert.Null(bot.Bail(View(t)));
            t.State.VersoRank = 0;
            b.Pawn(1, "autumn.10");
            Assert.Null(bot.Bail(View(t)));
        }

        [Fact]
        public void VersoBot_WalksToEsquieWhenReachableInActTwo()
        {
            var bot = new VersoBot();
            var b = Reunion().Pawn(1, "autumn.05").Pawn(2, "autumn.06").Item(1, ItemType.Sword).Item(1, ItemType.Boots);
            var t = StartReunion(b);
            var legal = new[] { "move:5:1", "move:5:6" };
            Assert.Equal((ClearingId?)b.C(6), bot.Reunion(View(t)));
            Assert.Equal("move:5:6", bot.ChooseAction(View(t), legal));

            // Two moves away with one ready boot: not this turn; a second boot reaches him.
            b.Pawn(2, "autumn.02");
            Assert.Null(bot.Reunion(View(t)));
            Assert.Null(bot.ChooseAction(View(t), legal));
            b.Item(1, ItemType.Boots, exhausted: true);
            Assert.Null(bot.Reunion(View(t)));
            b.Item(1, ItemType.Boots);
            Assert.Equal((ClearingId?)b.C(2), bot.Reunion(View(t)));
            Assert.Equal("move:5:6", bot.ChooseAction(View(t), legal));

            // Not in Act I.
            var t1 = Start(Reunion().Pawn(1, "autumn.05").Pawn(2, "autumn.06").Item(1, ItemType.Boots), act: 1);
            t1.State.EsquieRocks[ModState.Soarrie].Status = RockStatus.WithVerso;
            t1.State.EsquieRocks[ModState.Soarrie].HostSeat = S(1);
            Assert.Null(bot.Reunion(View(t1)));
        }

        [Fact]
        public void VersoBot_NoReunionWalkOnceSoarriePassed()
        {
            var bot = new VersoBot();
            var b = Reunion();
            var t = StartReunion(b);
            t.PlayTurn(2);
            Assert.Equal(RockStatus.Held, t.State.EsquieRocks[ModState.Soarrie].Status);
            b.Pawn(1, "autumn.05").Pawn(2, "autumn.06").Item(1, ItemType.Boots);
            Assert.Null(bot.Reunion(View(t)));
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:5:1", "move:5:6" }));
        }

        [Fact]
        public void VersoBot_SpoilerKeepsClimbFilterBelowRankA()
        {
            var bot = new VersoBot();
            var b = FakeBoard.Autumn().Seat(0, EngineId.Marquise)
                .Seat(1, EngineId.VagabondRanger, CharacterIds.Verso, bot: true)
                .Seat(2, EngineId.Eyrie, CharacterIds.Paintress)
                .Pawn(1, "autumn.05").Item(1, ItemType.Sword).Item(1, ItemType.Sword).Item(1, ItemType.Boots)
                .Building(2, 5, PieceTypes.Roost).Warriors(2, 5, 2).Vp(2, Tuning.CountdownVp);
            var t = Start(b);
            t.State.GlobalCountdowns.Add(S(2));
            var legal = new[] { "battle:5:2", "move:5:1", "move:5:6" };
            Assert.Single(bot.Threats(View(t)));
            Assert.Null(bot.Spoiler(View(t), legal));
            Assert.Null(bot.ChooseAction(View(t), legal));

            // A lone warrior passes step 1's filter: the banner is answered ahead of the steps.
            b.Warriors(2, 5, 1);
            var answer = bot.Spoiler(View(t), legal);
            Assert.Equal("battle:5:2", answer.ActionKey);
            Assert.True(answer.Preempts);
            Assert.Equal("battle:5:2", bot.ChooseAction(View(t), legal));
        }

        [Fact]
        public void VersoBot_SpoilerNeverBlocksCoalitionPartner()
        {
            var bot = new VersoBot();
            FakeBoard Board() => FakeBoard.Autumn().Seat(0, EngineId.Corvids, CharacterIds.WhiteHairedMan)
                .Seat(1, EngineId.VagabondRanger, CharacterIds.Verso, bot: true).Seat(2, EngineId.Marquise)
                .Pawn(1, "autumn.10").Item(1, ItemType.Sword).Item(1, ItemType.Boots).Vp(0, Tuning.CountdownVp);
            var legal = new[] { "move:10:3", "move:10:8" };

            var free = Start(Board());
            free.State.GlobalCountdowns.Add(S(0));
            Assert.Equal("move:10:3", bot.Spoiler(View(free), legal).ActionKey);
            Assert.Equal("move:10:3", bot.ChooseAction(View(free), legal));

            var partner = Start(Board().Coalition(1, 0));
            partner.State.GlobalCountdowns.Add(S(0));
            Assert.Empty(bot.Threats(View(partner)));
            Assert.Null(bot.Spoiler(View(partner), legal));
            Assert.Null(bot.ChooseAction(View(partner), legal));
        }
    }
}
