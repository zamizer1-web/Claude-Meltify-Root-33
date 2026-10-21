using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Esquie;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Every test the Esquie rows name: win.esquie, tweak.esquie.*, spawn.florrie/dorrie/soarrie/urrie/francois-decoy,
    /// state.esquie.* and bot.esquie's rules.</summary>
    public class EsquieTests
    {
        // Autumn roles: Lumière 1, Esquie's Nest 2, the Monolith 3, Stone Wave Cliffs 6, Old Lumière 8 (ruin).
        private const int Lumiere = 1, Nest = 2, Monolith = 3, Cliffs = 6, OldLumiere = 8;
        private const int Florrie = EsquieTweaks.Florrie, Dorrie = EsquieTweaks.Dorrie, Soarrie = EsquieTweaks.Soarrie, Urrie = EsquieTweaks.Urrie;

        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);
        private static string A(int c) => "autumn." + c.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Esquie, () => new EsquieModule());

        /// <summary>Esquie (seat 0, Tinker) in Spring Meadows (5), a Marquise (1, 25 in supply) and an Eyrie (2, 20 in supply).
        /// Esquie's rock guards belong to none of them.</summary>
        private static FakeBoard Table(bool bot = false) => FakeBoard.Autumn()
            .Seat(0, EngineId.VagabondTinker, CharacterIds.Esquie, bot)
            .Seat(1, EngineId.Marquise).Seat(2, EngineId.Eyrie)
            .Supply(1, 25).Supply(2, 20)
            .Pawn(0, "autumn.05");

        /// <summary>Starts the table. <paramref name="guardsUp"/>: the guards have taken post as if he had collected his second
        /// rock (the rock counts stay as the test sets them); false keeps the real start, with no guards before two rocks.</summary>
        private static TestTable Start(FakeBoard b, int act = 1, Difficulty d = Difficulty.Normal, ModuleRegistry registry = null,
            bool updateRules = false, bool guardsUp = true)
        {
            var t = TestTable.Start(b, registry ?? Registry(), d, updateRules: updateRules);
            if (act > 1) t.Game.OnActChanged(act);
            if (guardsUp) EsquieTweaks.GuardsTakePost(t.Ctx, S(0));
            return t;
        }

        private static RockState R(TestTable t, int i) => t.State.EsquieRocks[i];

        private static void SetRock(TestTable t, int i, string status, int? clearing = null, int? host = null, int guards = 0)
        {
            var r = R(t, i);
            r.Status = status;
            r.Clearing = clearing.HasValue ? C(clearing.Value) : (ClearingId?)null;
            r.HostSeat = host.HasValue ? S(host.Value) : (SeatId?)null;
            r.GuardsLeft = guards;
        }

        private static void Hold(TestTable t, params int[] rocks) { foreach (var i in rocks) SetRock(t, i, RockStatus.Held); }

        /// <summary>Esquie ends an action in a clearing.</summary>
        private static void ActIn(TestTable t, int clearing)
        {
            t.Board.Pawn(0, A(clearing));
            t.Game.OnActionResolved(S(0));
        }

        private static BattleResult Battle(int clearing, int attacker, int defender, params int[] removedWarriorsOf) => new BattleResult
        {
            Clearing = C(clearing),
            Attacker = S(attacker),
            Defender = S(defender),
            Removed = removedWarriorsOf.Select(o => new RemovedPiece(S(o), PieceKind.Warrior, null)).ToList(),
        };

        private static bool Won(FakeBoard b) => b.Winners.Any(w => w.Key == S(0) && w.Value == Ids.WinConditions.Esquie);

        private static BotView View(TestTable t, int seat = 0) => new BotView(t.Ctx, S(seat));

        // ================================================================= win.esquie

        [Fact]
        public void EsquieWin_WinsAtTurnEndWithFourRocks()
        {
            var b = Table();
            var t = Start(b, act: 2);
            Hold(t, Florrie, Dorrie, Soarrie);
            SetRock(t, Urrie, RockStatus.Loose, Nest);
            Assert.False(new EsquieWin().QuestComplete(t.Ctx, S(0)));

            // Claim: his action in the Nest picks up the loose Urrie; the quest wins at the end of that turn.
            t.PlayTurn(0, x => ActIn(x, Nest));
            Assert.Equal(RockStatus.Held, R(t, Urrie).Status);
            Assert.True(Won(b));
            Assert.Equal(Ids.WinConditions.Esquie, b.Winners.Single().Value);

            // Gate: no themed win in Act I (actMin 2), even with four rocks.
            var b1 = Table();
            var t1 = Start(b1);
            Hold(t1, Florrie, Dorrie, Soarrie, Urrie);
            Assert.False(WinRules.QuestPasses(t1.Ctx, S(0), new EsquieWin()));
            t1.PlayTurn(0);
            Assert.False(Won(b1));

            // Three rocks are not enough; a quest needs no banner.
            var b3 = Table();
            var t3 = Start(b3, act: 2);
            Hold(t3, Florrie, Dorrie, Soarrie);
            t3.PlayTurn(0);
            Assert.False(Won(b3));
            Assert.Empty(b3.Banners);
        }

        [Fact]
        public void EsquieWin_GuardsLeftDropsAsEsquieDefeatsGuards()
        {
            var b = Table().Item(0, ItemType.Sword);
            var t = Start(b);
            Assert.Equal(2, R(t, Florrie).GuardsLeft);
            Assert.Equal(2, b.Guards(C(Cliffs)));

            // His guard battle removes one guard: GuardsLeft follows the guards still standing.
            t.Game.OnGuardBattleStarting(S(0), C(Cliffs));
            b.Guards(A(Cliffs), 1);
            t.Game.OnGuardBattleResolved(S(0), C(Cliffs));
            Assert.Equal(1, R(t, Florrie).GuardsLeft);
            Assert.Equal(RockStatus.Guarded, R(t, Florrie).Status);

            // Other seats' battles there change nothing: the guards are nobody's and nobody else fights them.
            t.Game.OnBattleResolved(Battle(Cliffs, 1, 2, 2));
            Assert.Equal(1, R(t, Florrie).GuardsLeft);

            // The last guard falls: the rock is loose and its badge comes down.
            b.Guards(A(Cliffs), 0);
            t.Game.OnGuardBattleResolved(S(0), C(Cliffs));
            Assert.Equal(0, R(t, Florrie).GuardsLeft);
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));

            // Esquie picks it up after his next action there.
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Held, R(t, Florrie).Status);
        }

        [Fact]
        public void EsquieWin_GuardsTakePostWithTheSecondRock()
        {
            var b = Table().Pawn(0, A(Cliffs));
            var t = Start(b, act: 2, guardsUp: false);
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);

            // First rock: Florrie, unguarded. Still no guards.
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Held, R(t, Florrie).Status);
            Assert.False(t.State.EsquieGuardsActive);

            // Second rock: Dorrie from the Old Lumière ruin. The guards are now active; Urrie (Act III) comes guarded.
            b.Pawn(0, A(OldLumiere)).RuinEmptied(A(OldLumiere));
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
            Assert.True(t.State.EsquieGuardsActive);
            t.Game.OnActChanged(3);
            Assert.Equal(RockStatus.Guarded, R(t, Urrie).Status);
            Assert.Equal(Tuning.EsquieGuardsUrrie, b.Guards(C(Nest)));

            // A save keeps them active.
            var loaded = t.SaveAndLoad(Registry());
            Assert.True(loaded.State.EsquieGuardsActive);
        }

        [Fact]
        public void EsquieWin_OffInCoalition()
        {
            var b = Table().Coalition(0, 1);
            var t = Start(b, act: 2);
            Hold(t, Florrie, Dorrie, Soarrie, Urrie);
            Assert.True(new EsquieWin().QuestComplete(t.Ctx, S(0)));
            Assert.False(WinRules.QuestPasses(t.Ctx, S(0), new EsquieWin()));
            t.PlayTurn(0);
            Assert.False(Won(b));

            b.Coalition(0, null);
            t.PlayTurn(1).PlayTurn(2).PlayTurn(0);
            Assert.True(Won(b));
        }

        [Fact]
        public void EsquieWin_DorrieBlockedByCorvidWarriors()
        {
            var b = FakeBoard.Autumn()
                .Seat(0, EngineId.VagabondTinker, CharacterIds.Esquie).Seat(1, EngineId.Marquise)
                .Seat(2, EngineId.Corvids, CharacterIds.WhiteHairedMan)
                .Supply(1, 25).Supply(2, 15).Pawn(0, A(OldLumiere)).Warriors(2, A(OldLumiere), 1);
            var t = Start(b, act: 2);
            Hold(t, Florrie, Soarrie, Urrie);
            Assert.Equal(RockStatus.Hidden, R(t, Dorrie).Status);

            // Exploring while Corvid warriors stand there leaves Dorrie loose, and no action claims it.
            b.RuinEmptied(A(OldLumiere));
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);
            t.PlayTurn(0, x => ActIn(x, OldLumiere));
            Assert.NotEqual(RockStatus.Held, R(t, Dorrie).Status);
            Assert.False(Won(b));

            // Once the Corvid warriors are gone, his next action there claims it and he wins at the end of that turn.
            b.Warriors(2, A(OldLumiere), 0);
            t.PlayTurn(1).PlayTurn(2).PlayTurn(0, x => ActIn(x, OldLumiere));
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
            Assert.True(Won(b));
        }

        [Fact]
        public void EsquieWin_WinsSameTurnSoarrieHandedOver()
        {
            var b = Table().Seat(3, EngineId.VagabondRanger, CharacterIds.Verso).Pawn(0, "autumn.07").Pawn(3, "autumn.07");
            var t = Start(b, act: 2);
            Assert.Equal(RockStatus.WithVerso, R(t, Soarrie).Status);
            Hold(t, Florrie, Dorrie, Urrie);

            // Ending his turn in Verso's clearing hands Soarrie over (hook.evening-end, before the quest check): win that turn.
            t.PlayTurn(0);
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
            Assert.True(Won(b));
            Assert.Contains(new KeyValuePair<SeatId, int>(S(3), Tuning.VersoSoarrieVp), b.ScoredVp);
        }

        // ================================================================= tweak.esquie.belly-bounce

        [Fact]
        public void BellyBounce_BagAddsMaxHitAgainstGuards()
        {
            var b = Table().Item(0, ItemType.Bag).Item(0, ItemType.Bag).Item(0, ItemType.Sword);
            var t = Start(b);
            t.Game.OnGuardBattleStarting(S(0), C(Cliffs));
            Assert.Equal(Tuning.EsquieBellyBounceSwords, b.ExtraMaxHits[S(0)]);   // one sword however many Bags

            // Under the no-spawn fallback (no guards): his battle in a clearing with an unclaimed Florrie.
            var f = Table().Unsupported(Ids.BoardActions.PlaceGuards).Item(0, ItemType.Bag);
            var tf = Start(f);
            Assert.Equal(RockStatus.Loose, R(tf, Florrie).Status);
            tf.Game.OnBattleStarting(S(0), S(1), C(Cliffs));
            Assert.Equal(Tuning.EsquieBellyBounceSwords, f.ExtraMaxHits[S(0)]);
        }

        [Fact]
        public void BellyBounce_NoEffectOutsideRockClearings()
        {
            var b = Table().Item(0, ItemType.Bag);
            var t = Start(b);
            t.Game.OnBattleStarting(S(0), S(2), C(5));
            t.Game.OnBattleStarting(S(0), S(2), C(Cliffs));   // a guarded rock's clearing, but not a guard battle
            t.Game.OnGuardBattleStarting(S(0), C(5));          // no guarded rock there
            Assert.False(b.ExtraMaxHits.ContainsKey(S(0)));

            // A rock already claimed leaves no guards to bounce on.
            SetRock(t, Florrie, RockStatus.Held);
            t.Game.OnGuardBattleStarting(S(0), C(Cliffs));
            Assert.False(b.ExtraMaxHits.ContainsKey(S(0)));
        }

        [Fact]
        public void BellyBounce_DamagedBagDoesNotCount()
        {
            var b = Table().Item(0, ItemType.Bag, damaged: true);
            var t = Start(b);
            t.Game.OnGuardBattleStarting(S(0), C(Cliffs));
            Assert.False(b.ExtraMaxHits.ContainsKey(S(0)));
        }

        // ================================================================= tweak.esquie.never-mad

        [Fact]
        public void NeverMad_AllowsBattleAgainstGuards()
        {
            var t = Start(Table().Item(0, ItemType.Sword).Warriors(2, A(Cliffs), 1));
            Assert.True(t.Game.CanBattleGuards(S(0), C(Cliffs)));
            Assert.False(t.Game.CanStartBattle(S(0), S(2), C(Cliffs), false));   // never a faction, even beside the guards

            // No-spawn fallback: no guards, so any faction's warriors in a clearing with an unclaimed Florrie.
            var f = Table().Unsupported(Ids.BoardActions.PlaceGuards).Warriors(1, A(Cliffs), 2);
            var tf = Start(f);
            Assert.False(tf.Game.CanBattleGuards(S(0), C(Cliffs)));
            Assert.True(tf.Game.CanStartBattle(S(0), S(1), C(Cliffs), false));
            Assert.False(tf.Game.CanStartBattle(S(0), S(2), C(Cliffs), false));   // no Eyrie warriors there
        }

        [Fact]
        public void NeverMad_BlocksEveryBattleAgainstAFaction()
        {
            var b = Table().Item(0, ItemType.Sword).Warriors(1, "autumn.05", 2).Warriors(2, "autumn.05", 1).Warriors(2, A(Cliffs), 3);
            var t = Start(b);
            Assert.False(t.Game.CanStartBattle(S(0), S(1), C(5), false));
            Assert.False(t.Game.CanStartBattle(S(0), S(2), C(5), false));
            Assert.False(t.Game.CanStartBattle(S(0), S(2), C(Cliffs), false));
            Assert.True(t.Game.CanStartBattle(S(1), S(2), C(5), false));   // other seats battle as usual

            // Once the rock is loose there are no guards left to fight.
            SetRock(t, Florrie, RockStatus.Loose, Cliffs);
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));
        }

        [Fact]
        public void NeverMad_BlocksStrike()
        {
            var b = Table().Item(0, ItemType.Crossbow).Warriors(1, A(Cliffs), 1).Building(1, A(Cliffs), PieceTypes.Sawmill);
            var t = Start(b);
            Assert.False(t.Game.CanStartBattle(S(0), S(1), C(Cliffs), true));
            Assert.False(t.Game.CanStartBattle(S(0), S(1), C(5), true));

            // Not even under the no-spawn fallback, where he may battle there.
            var f = Table().Unsupported(Ids.BoardActions.PlaceGuards).Warriors(1, A(Cliffs), 1);
            var tf = Start(f);
            Assert.True(tf.Game.CanStartBattle(S(0), S(1), C(Cliffs), false));
            Assert.False(tf.Game.CanStartBattle(S(0), S(1), C(Cliffs), true));
        }

        // ================================================================= tweak.esquie.start-near-nest

        [Fact]
        public void EsquieSetup_StartsInForestTouchingNest()
        {
            var b = Table();
            var t = Start(b);
            t.Game.OnSetupChoice(S(0));
            var allowed = b.StartForestRestrictions[S(0)];
            Assert.NotEmpty(allowed);
            Assert.Equal(b.ForestsAround(C(Nest)).OrderBy(f => f), allowed.OrderBy(f => f));
            Assert.All(allowed, f => Assert.Contains(C(Nest), b.ForestClearings(f)));

            // The bot picks the allowed forest closest to Stone Wave Cliffs.
            var pick = EsquieBot.StartForest(View(t));
            Assert.NotNull(pick);
            Assert.Contains(C(Cliffs), b.ForestClearings(pick.Value));

            // Another seat's setup choice is not restricted by him.
            t.Game.OnSetupChoice(S(1));
            Assert.False(b.StartForestRestrictions.ContainsKey(S(1)));
        }

        // ================================================================= tweak.esquie.neutral-guards

        [Fact]
        public void NeutralGuards_OnlyEsquieMayBattleThem()
        {
            var b = Table().Item(0, ItemType.Sword).Seat(3, EngineId.VagabondThief, CharacterIds.Maelle).Item(3, ItemType.Sword);
            var t = Start(b);
            Assert.True(t.Game.CanBattleGuards(S(0), C(Cliffs)));
            Assert.False(t.Game.CanBattleGuards(S(1), C(Cliffs)));   // every other faction ignores them
            Assert.False(t.Game.CanBattleGuards(S(2), C(Cliffs)));
            Assert.False(t.Game.CanBattleGuards(S(3), C(Cliffs)));   // another Vagabond too
            Assert.False(t.Game.CanBattleGuards(S(0), C(5)));        // no guards there
        }

        [Fact]
        public void NeutralGuards_NeedAnUndamagedSword()
        {
            var b = Table();
            var t = Start(b);
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));   // no sword
            b.Item(0, ItemType.Sword, damaged: true);
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));
            b.Item(0, ItemType.Bag);                                  // Belly Bounce adds hits but is no sword
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));
            b.Item(0, ItemType.Sword);
            Assert.True(t.Game.CanBattleGuards(S(0), C(Cliffs)));
        }

        [Fact]
        public void NeutralGuards_NeverCountAsAnySeatsWarriors()
        {
            // Clea (Lizards) is seated, with the smallest supply: she does not host them either.
            var b = Table().Seat(3, EngineId.Lizards, CharacterIds.Clea).Supply(3, 4);
            var t = Start(b, act: 3);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, b.Guards(C(Cliffs)));
            Assert.Equal(Tuning.EsquieGuardsUrrie, b.Guards(C(Nest)));
            foreach (var c in new[] { Cliffs, Nest })
            {
                Assert.All(b.Seats(), s => Assert.Equal(0, b.Warriors(s, C(c))));   // nobody's warriors
                Assert.Null(b.Ruler(C(c)));                                          // they rule nothing
            }
            Assert.Equal(25, b.SupplyWarriors(S(1)));                                // no seat paid for them
            Assert.Equal(20, b.SupplyWarriors(S(2)));
            Assert.Equal(4, b.SupplyWarriors(S(3)));
            Assert.Null(R(t, Florrie).HostSeat);
            Assert.Null(R(t, Urrie).HostSeat);
            Assert.DoesNotContain(b.Log, l => l.StartsWith("PlaceWarriors", StringComparison.Ordinal));
        }

        [Fact]
        public void NeutralGuards_OtherFactionsCannotTargetOrRemoveThem()
        {
            var b = Table().Item(0, ItemType.Sword).Seat(3, EngineId.VagabondThief, CharacterIds.Maelle).Item(3, ItemType.Sword)
                .Warriors(1, A(Cliffs), 3);
            var t = Start(b);
            Assert.All(new[] { 1, 2, 3 }, s => Assert.False(t.Game.CanBattleGuards(S(s), C(Cliffs))));

            // Whatever another seat does there (a battle, a removal, even a guard battle reported for it), the guards stay.
            t.Game.OnBattleResolved(Battle(Cliffs, 1, 2, 2));
            t.Game.OnPieceRemoved(S(1), new RemovedPiece(S(2), PieceKind.Warrior, null), C(Cliffs));
            b.Guards(A(Cliffs), 0);
            t.Game.OnGuardBattleResolved(S(3), C(Cliffs));
            Assert.Equal(RockStatus.Guarded, R(t, Florrie).Status);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, R(t, Florrie).GuardsLeft);
            Assert.False(t.Game.CanBattleGuards(S(1), C(Cliffs)));
        }

        [Fact]
        public void NeutralGuards_NeverStartABattle()
        {
            // Esquie stands on the guards with everything undamaged; Gustave's warriors stand there too. Full rounds pass.
            var b = Table().Item(0, ItemType.Sword).Item(0, ItemType.Boots).Pawn(0, A(Cliffs)).Warriors(1, A(Cliffs), 2);
            var t = Start(b, act: 2);
            for (var round = 0; round < 3; round++) t.PlayTurn(0).PlayTurn(1).PlayTurn(2);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, b.Guards(C(Cliffs)));   // never moved or lost
            Assert.Equal(Tuning.EsquieGuardsFlorrie, R(t, Florrie).GuardsLeft);
            Assert.All(b.Items(S(0)), i => Assert.False(i.Damaged));       // they never hit him unless he attacks
            Assert.Equal(2, b.Warriors(S(1), C(Cliffs)));
            Assert.Empty(b.ExtraHits);
            Assert.Single(b.Log.Where(l => l.StartsWith("PlaceGuards", StringComparison.Ordinal)));
        }

        [Fact]
        public void NeutralGuards_GuardsLeftFollowsTheBoardAfterHisBattle()
        {
            var b = Table().Item(0, ItemType.Sword);
            var t = Start(b, act: 3);
            Assert.Equal(Tuning.EsquieGuardsUrrie, R(t, Urrie).GuardsLeft);

            // Two of Urrie's three guards fall in his battle at the Nest; Florrie's guards are untouched.
            b.Guards(A(Nest), 1);
            t.Game.OnGuardBattleResolved(S(0), C(Nest));
            Assert.Equal(1, R(t, Urrie).GuardsLeft);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, R(t, Florrie).GuardsLeft);

            // The count never rises again from the board.
            b.Guards(A(Nest), 3);
            t.Game.OnGuardBattleResolved(S(0), C(Nest));
            Assert.Equal(1, R(t, Urrie).GuardsLeft);

            // On load: min(saved, the guards on the board).
            b.Guards(A(Cliffs), 1);
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(1, loaded.State.EsquieRocks[Florrie].GuardsLeft);
            Assert.Equal(1, loaded.State.EsquieRocks[Urrie].GuardsLeft);
        }

        [Fact]
        public void NeutralGuards_RockLooseWhenLastGuardFalls()
        {
            var b = Table().Item(0, ItemType.Sword);
            var t = Start(b);
            b.Guards(A(Cliffs), 0);
            t.Game.OnGuardBattleResolved(S(0), C(Cliffs));
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);
            Assert.Equal(0, R(t, Florrie).GuardsLeft);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieRocks));
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));

            // No-spawn fallback: no rock is ever guarded and no guard is ever placed.
            var f = Table().Unsupported(Ids.BoardActions.PlaceGuards);
            var tf = Start(f, act: 3);
            Assert.Equal(0, f.Guards(C(Cliffs)) + f.Guards(C(Nest)));
            Assert.DoesNotContain(tf.State.EsquieRocks, r => r.Status == RockStatus.Guarded);
        }

        // ================================================================= rock powers (v1.1)

        [Fact]
        public void RockPowers_FlorrieLetsRiversCountAsPaths()
        {
            var b = Table();
            var t = Start(b, updateRules: true); // v1.1 row: the update rules on
            Assert.DoesNotContain((S(0), EsquieTweaks.AbilityRiversAsPaths), b.Abilities);
            SetRock(t, Florrie, RockStatus.Loose, Cliffs);
            ActIn(t, Cliffs);
            Assert.Contains((S(0), EsquieTweaks.AbilityRiversAsPaths), b.Abilities);

            // Re-granted on load; left out when the plugin cannot grant abilities (the rock still counts).
            b.Abilities.Clear();
            t.SaveAndLoad(Registry());
            Assert.Contains((S(0), EsquieTweaks.AbilityRiversAsPaths), b.Abilities);

            var u = Table().Unsupported(Ids.BoardActions.GrantAbility);
            var tu = Start(u, updateRules: true);
            SetRock(tu, Florrie, RockStatus.Loose, Cliffs);
            ActIn(tu, Cliffs);
            Assert.Equal(RockStatus.Held, R(tu, Florrie).Status);
            Assert.Empty(u.Abilities);
        }

        [Fact]
        public void RockPowers_DorrieWaivesHostileBoot()
        {
            var b = Table().Pawn(0, A(OldLumiere));
            var t = Start(b, act: 2, updateRules: true); // v1.1 row: the update rules on
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
            Assert.Contains((S(0), EsquieTweaks.AbilityNoHostileBoot), b.Abilities);
            Assert.DoesNotContain((S(0), EsquieTweaks.AbilityGlide), b.Abilities);
        }

        [Fact]
        public void RockPowers_SoarrieGlideOncePerTurn()
        {
            var b = Table();
            var t = Start(b, act: 2, updateRules: true); // v1.1 row: the update rules on
            Assert.Equal(RockStatus.HeldByRuler, R(t, Soarrie).Status);
            ActIn(t, Monolith);   // nobody rules the Monolith
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
            Assert.Contains((S(0), EsquieTweaks.AbilityGlide), b.Abilities);

            // One grant: the 'glide' ability itself (the plugin's Harrier Glide) limits it to once per turn; later actions do
            // not grant it again.
            ActIn(t, Monolith);
            Assert.Single(b.Log.Where(l => l == "GrantAbility seat0 " + EsquieTweaks.AbilityGlide));
        }

        // ================================================================= spawn.florrie

        [Fact]
        public void FlorrieSpawn_PlacedAtCliffsWithTwoGuards()
        {
            var b = Table();
            var t = Start(b);
            var r = R(t, Florrie);
            Assert.Equal(RockStatus.Guarded, r.Status);
            Assert.Equal(C(Cliffs), r.Clearing);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, r.GuardsLeft);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, b.Guards(C(Cliffs)));
            Assert.Contains($"PlaceGuards c{Cliffs} {Tuning.EsquieGuardsFlorrie}", b.Log);
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieRocks));
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
            Assert.All(new[] { Dorrie, Soarrie, Urrie }, i => Assert.Equal(RockStatus.NotSpawned, R(t, i).Status));
        }

        [Fact]
        public void FlorrieSpawn_UnguardedUntilEsquieHoldsTwoRocks()
        {
            var b = Table().Item(0, ItemType.Sword);
            var t = Start(b, guardsUp: false);
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);
            Assert.Equal(C(Cliffs), R(t, Florrie).Clearing);
            Assert.Equal(0, b.Guards(C(Cliffs)));
            Assert.False(t.State.EsquieGuardsActive);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
            Assert.DoesNotContain(b.Log, l => l.StartsWith("PlaceGuards", StringComparison.Ordinal));
            Assert.False(t.Game.CanBattleGuards(S(0), C(Cliffs)));

            // He just picks it up, even beside other warriors; one rock does not wake the guards.
            b.Warriors(1, A(Cliffs), 2);
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Held, R(t, Florrie).Status);
            Assert.False(t.State.EsquieGuardsActive);

            // The no-spawn fallback's clearing rule stands in for the guards, so it waits for two rocks too.
            var f = Table().Unsupported(Ids.BoardActions.PlaceGuards).Warriors(1, A(Cliffs), 1);
            var tf = Start(f, guardsUp: false);
            ActIn(tf, Cliffs);
            Assert.Equal(RockStatus.Held, R(tf, Florrie).Status);
        }

        [Fact]
        public void FlorrieSpawn_GuardsAppearWithTheSecondRock()
        {
            var b = Table().Pawn(0, A(Monolith));
            var t = Start(b, act: 3, guardsUp: false);
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);
            Assert.Equal(RockStatus.Loose, R(t, Urrie).Status);
            Hold(t, Dorrie);

            // Soarrie in the unruled Monolith is his second rock: guards take post on Florrie and Urrie at once.
            ActIn(t, Monolith);
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
            Assert.True(t.State.EsquieGuardsActive);
            Assert.Equal(RockStatus.Guarded, R(t, Florrie).Status);
            Assert.Equal(Tuning.EsquieGuardsFlorrie, b.Guards(C(Cliffs)));
            Assert.Equal(RockStatus.Guarded, R(t, Urrie).Status);
            Assert.Equal(Tuning.EsquieGuardsUrrie, b.Guards(C(Nest)));
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieFrancoisBadge));

            // Now he can no longer just pick Florrie up.
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Guarded, R(t, Florrie).Status);
        }

        [Fact]
        public void FlorrieSpawn_GuardsBelongToNoSeat()
        {
            // Whoever is seated (Clea, a faction with a big supply, only a Marquise and another Vagabond), the same neutral
            // guards spawn and no seat hosts or pays for them.
            var tables = new[]
            {
                Table().Seat(3, EngineId.Lizards, CharacterIds.Clea).Supply(3, 3),
                Table().Seat(3, EngineId.Alliance).Supply(3, 10),
                FakeBoard.Autumn().Seat(0, EngineId.VagabondTinker, CharacterIds.Esquie).Seat(1, EngineId.Marquise)
                    .Seat(2, EngineId.VagabondRanger, CharacterIds.Verso).Supply(1, 25).Pawn(0, "autumn.05"),
            };
            foreach (var b in tables)
            {
                var supply = b.Seats().Select(s => b.SupplyWarriors(s)).ToArray();
                var t = Start(b);
                Assert.Equal(RockStatus.Guarded, R(t, Florrie).Status);
                Assert.Null(R(t, Florrie).HostSeat);
                Assert.Equal(Tuning.EsquieGuardsFlorrie, b.Guards(C(Cliffs)));
                Assert.All(b.Seats(), s => Assert.Equal(0, b.Warriors(s, C(Cliffs))));
                Assert.Equal(supply, b.Seats().Select(s => b.SupplyWarriors(s)).ToArray());
            }
        }

        [Fact]
        public void FlorrieSpawn_ShortPlacementSetsGuardsLeftToPlaced()
        {
            var b = Table();
            b.GuardsPlaceable = 1;
            var t = Start(b);
            Assert.Equal(1, R(t, Florrie).GuardsLeft);
            Assert.Equal(1, b.Guards(C(Cliffs)));

            // Nothing placed: loose at once, with no badge.
            var e = Table();
            e.GuardsPlaceable = 0;
            var te = Start(e);
            Assert.Equal(RockStatus.Loose, R(te, Florrie).Status);
            Assert.Equal(0, R(te, Florrie).GuardsLeft);
            Assert.False(e.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
        }

        [Fact]
        public void FlorrieSpawn_NoSpawnFallbackClaimsWhenClearingFree()
        {
            var b = Table().Unsupported(Ids.BoardActions.PlaceGuards).Warriors(1, A(Cliffs), 1);
            var t = Start(b);
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);
            Assert.Equal(0, b.Guards(C(Cliffs)));

            ActIn(t, Cliffs);   // a Marquise warrior is there
            Assert.Equal(RockStatus.Loose, R(t, Florrie).Status);

            b.Warriors(1, A(Cliffs), 0);
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Held, R(t, Florrie).Status);
        }

        // ================================================================= spawn.dorrie

        [Fact]
        public void DorrieSpawn_ClaimedByExploringOldLumiere()
        {
            var b = Table().Pawn(0, A(OldLumiere));
            var t = Start(b);
            t.Game.OnExploreResolved(S(0), C(OldLumiere));   // Act I: nothing there yet
            Assert.Equal(RockStatus.NotSpawned, R(t, Dorrie).Status);

            t.Game.OnActChanged(2);
            Assert.Equal(RockStatus.Hidden, R(t, Dorrie).Status);
            Assert.Equal(C(OldLumiere), R(t, Dorrie).Clearing);
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
        }

        [Fact]
        public void DorrieSpawn_LooseWhenRuinAlreadyEmpty()
        {
            var b = Table().RuinEmptied(A(OldLumiere));
            var t = Start(b, act: 2);
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);
            ActIn(t, OldLumiere);   // ending any action there claims a loose Dorrie
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
        }

        [Fact]
        public void DorrieSpawn_BlockedWhileCorvidWarriorsPresent()
        {
            var b = FakeBoard.Autumn()
                .Seat(0, EngineId.VagabondTinker, CharacterIds.Esquie).Seat(1, EngineId.Marquise)
                .Seat(2, EngineId.Corvids, CharacterIds.WhiteHairedMan)
                .Supply(1, 25).Supply(2, 15).Pawn(0, A(OldLumiere)).Warriors(2, A(OldLumiere), 2);
            var t = Start(b, act: 2);
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);
            ActIn(t, OldLumiere);
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);

            // Other factions' warriors do not block it.
            b.Warriors(2, A(OldLumiere), 0).Warriors(1, A(OldLumiere), 3);
            ActIn(t, OldLumiere);
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
        }

        [Fact]
        public void DorrieSpawn_LooseAfterAnotherVagabondExplores()
        {
            var b = Table().Seat(3, EngineId.VagabondThief, CharacterIds.Maelle);
            var t = Start(b, act: 2);
            b.RuinEmptied(A(OldLumiere));
            t.Game.OnExploreResolved(S(3), C(OldLumiere));
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);
            Assert.Equal(C(OldLumiere), R(t, Dorrie).Clearing);
            ActIn(t, OldLumiere);
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
        }

        [Fact]
        public void DorrieSpawn_StaysHiddenWhileTwoVagabondRuinHoldsAnItem()
        {
            var b = Table().Seat(3, EngineId.VagabondThief, CharacterIds.Maelle);
            var t = Start(b, act: 2);
            t.Game.OnExploreResolved(S(3), C(OldLumiere));   // the ruin still holds its second item
            t.Game.OnActionResolved(S(3));
            Assert.Equal(RockStatus.Hidden, R(t, Dorrie).Status);
            ActIn(t, OldLumiere);   // walking in does not find a hidden rock
            Assert.Equal(RockStatus.Hidden, R(t, Dorrie).Status);

            // The polling fallback: once the ruin is empty, the next action makes it loose.
            b.RuinEmptied(A(OldLumiere));
            t.Game.OnActionResolved(S(3));
            Assert.Equal(RockStatus.Loose, R(t, Dorrie).Status);
        }

        // ================================================================= spawn.soarrie

        [Fact]
        public void SoarrieSpawn_StartsOnVersoWhenSeated()
        {
            var b = Table().Seat(3, EngineId.VagabondRanger, CharacterIds.Verso).Pawn(3, "autumn.11");
            var t = Start(b);
            Assert.Equal(RockStatus.NotSpawned, R(t, Soarrie).Status);
            t.Game.OnActChanged(2);
            Assert.Equal(RockStatus.WithVerso, R(t, Soarrie).Status);
            Assert.Equal(S(3), R(t, Soarrie).HostSeat);
            Assert.Null(R(t, Soarrie).Clearing);
            Assert.Equal(C(11), EsquieTweaks.RockSite(t.Ctx, Soarrie));

            ActIn(t, Monolith);   // without Verso's hand-off the Monolith gives nothing
            Assert.Equal(RockStatus.WithVerso, R(t, Soarrie).Status);
        }

        [Fact]
        public void SoarrieSpawn_HeldByMonolithRulerWithoutVerso()
        {
            var b = Table().Building(2, A(Monolith), PieceTypes.Roost).Warriors(2, A(Monolith), 1);
            var t = Start(b, act: 2);
            Assert.Equal(RockStatus.HeldByRuler, R(t, Soarrie).Status);
            Assert.Null(R(t, Soarrie).HostSeat);
            Assert.Equal(C(Monolith), EsquieTweaks.RockSite(t.Ctx, Soarrie));

            ActIn(t, Monolith);   // the Eyrie rules the Monolith: walking in is not enough
            Assert.Equal(RockStatus.HeldByRuler, R(t, Soarrie).Status);
        }

        [Fact]
        public void SoarrieSpawn_ClaimedByAidingRulerInMonolith()
        {
            var b = Table().Building(2, A(Monolith), PieceTypes.Roost).Warriors(2, A(Monolith), 1).Warriors(1, A(Monolith), 1);
            var t = Start(b, act: 2);
            b.Pawn(0, A(Monolith));
            t.Game.OnAidResolved(S(0), S(1), C(Monolith));   // not the ruler
            Assert.Equal(RockStatus.HeldByRuler, R(t, Soarrie).Status);
            t.Game.OnAidResolved(S(0), S(2), C(Monolith));
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
        }

        [Fact]
        public void SoarrieSpawn_ClaimedFreeWhenMonolithUnruled()
        {
            var b = Table();
            var t = Start(b, act: 2);
            Assert.Null(b.Ruler(C(Monolith)));
            ActIn(t, Monolith);
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
        }

        // ================================================================= spawn.urrie

        [Fact]
        public void UrrieSpawn_AppearsAtActThreeWithThreeGuards()
        {
            var b = Table();
            var t = Start(b, act: 2);
            Assert.Equal(RockStatus.NotSpawned, R(t, Urrie).Status);
            t.Game.OnActChanged(3);
            var r = R(t, Urrie);
            Assert.Equal(RockStatus.Guarded, r.Status);
            Assert.Equal(C(Nest), r.Clearing);
            Assert.Null(r.HostSeat);
            Assert.Equal(Tuning.EsquieGuardsUrrie, r.GuardsLeft);
            Assert.Equal(Tuning.EsquieGuardsUrrie, b.Guards(C(Nest)));
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieFrancoisBadge));
        }

        [Fact]
        public void UrrieSpawn_AppearsEarlyWhenOtherThreeHeld()
        {
            var b = Table();
            var t = Start(b, act: 2);
            Hold(t, Florrie, Dorrie);
            Assert.Equal(RockStatus.NotSpawned, R(t, Urrie).Status);
            ActIn(t, Monolith);   // claims Soarrie in the unruled Monolith
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
            Assert.Equal(RockStatus.Guarded, R(t, Urrie).Status);
            Assert.Equal(Tuning.EsquieGuardsUrrie, R(t, Urrie).GuardsLeft);
            Assert.Equal(2, t.Ctx.Act);
        }

        [Fact]
        public void UrrieSpawn_UnguardedInActThreeBeforeTwoRocks()
        {
            var b = Table();
            var t = Start(b, act: 3, guardsUp: false);
            Assert.Equal(RockStatus.Loose, R(t, Urrie).Status);
            Assert.Equal(0, b.Guards(C(Nest)));

            // He takes Urrie free, then Florrie: his second rock wakes the guards, but no Florrie or Urrie is left for them.
            ActIn(t, Nest);
            ActIn(t, Cliffs);
            Assert.Equal(2, EsquieTweaks.HeldCount(t.State));
            Assert.True(t.State.EsquieGuardsActive);
            Assert.Equal(0, b.Guards(C(Nest)) + b.Guards(C(Cliffs)));
            Assert.DoesNotContain(b.Log, l => l.StartsWith("PlaceGuards", StringComparison.Ordinal));
        }

        [Fact]
        public void UrrieSpawn_NoSpawnFallbackClaimsWhenNestFree()
        {
            var b = Table().Unsupported(Ids.BoardActions.PlaceGuards).Warriors(1, A(Nest), 2);
            var t = Start(b, act: 3);
            Assert.Equal(RockStatus.Loose, R(t, Urrie).Status);
            Assert.Null(R(t, Urrie).HostSeat);
            ActIn(t, Nest);
            Assert.Equal(RockStatus.Loose, R(t, Urrie).Status);
            b.Warriors(1, A(Nest), 0);
            ActIn(t, Nest);
            Assert.Equal(RockStatus.Held, R(t, Urrie).Status);
        }

        // ================================================================= spawn.francois-decoy

        [Fact]
        public void FrancoisDecoy_EnteringNestGivesNoRockAndRemovesDecoy()
        {
            var b = Table();
            var t = Start(b);
            Assert.True(t.State.EsquieDecoy);
            Assert.True(b.OverlayShown(Ids.Overlays.EsquieFrancoisDecoy));

            b.Pawn(0, A(Nest));
            t.Game.OnPieceMoved(S(0), PieceKind.Pawn, C(Cliffs), C(Nest), 1);
            t.Game.OnActionResolved(S(0));
            Assert.Contains(new KeyValuePair<SeatId, string>(S(0), Ids.Barks.Esquie02), b.Bubbles);
            Assert.False(t.State.EsquieDecoy);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieFrancoisDecoy));
            Assert.Equal(0, EsquieTweaks.HeldCount(t.State));
            Assert.Equal(RockStatus.NotSpawned, R(t, Urrie).Status);

            // Only once.
            t.Game.OnPieceMoved(S(0), PieceKind.Pawn, C(7), C(Nest), 1);
            Assert.Single(b.Bubbles.Where(x => x.Value == Ids.Barks.Esquie02));
        }

        [Fact]
        public void FrancoisDecoy_GoneAtActTwo()
        {
            var b = Table();
            var t = Start(b);
            Assert.True(t.State.EsquieDecoy);
            t.Game.OnActChanged(2);
            Assert.False(t.State.EsquieDecoy);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieFrancoisDecoy));
            t.Game.OnPieceMoved(S(0), PieceKind.Pawn, C(Cliffs), C(Nest), 1);
            Assert.DoesNotContain(b.Bubbles, x => x.Value == Ids.Barks.Esquie02);

            // A loaded Act II save never brings it back.
            var loaded = t.SaveAndLoad(Registry(), text => text.Replace("state.esquie.decoy=false", "state.esquie.decoy=true"));
            Assert.False(loaded.State.EsquieDecoy);
        }

        // ================================================================= state.esquie.* onLoad

        [Fact]
        public void EsquieState_LoadClampsGuardsAndClearsTarget()
        {
            var b = Table(bot: true);
            var t = Start(b, act: 2);
            t.State.EsquieTarget = Florrie;
            b.Guards(A(Cliffs), 1);   // one guard left the board without a hook
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(1, loaded.State.EsquieRocks[Florrie].GuardsLeft);
            Assert.Null(loaded.State.EsquieTarget);
            Assert.Equal(RockStatus.Hidden, loaded.State.EsquieRocks[Dorrie].Status);
            Assert.Equal(RockStatus.NotSpawned, loaded.State.EsquieRocks[Urrie].Status);

            b.Guards(A(Cliffs), 0);
            var again = loaded.SaveAndLoad(Registry());
            Assert.Equal(RockStatus.Loose, again.State.EsquieRocks[Florrie].Status);
            Assert.False(b.OverlayShown(Ids.Overlays.EsquieLampmasterBadge));
        }

        // ================================================================= bot.esquie

        [Fact]
        public void EsquieBot_EveryRuleHasAMethod()
        {
            Assert.Empty(ModuleRegistry.MissingBotRules(new EsquieBot()));
            Assert.Equal(Ids.BotPlans.Esquie, new EsquieModule().Bot.PlanId);
        }

        [Fact]
        public void EsquieBot_TargetCostIsDistancePlusTwiceGuardsMinusMaxHits()
        {
            var b = Table(bot: true).Pawn(0, A(Lumiere)).Item(0, ItemType.Sword).Item(0, ItemType.Bag);
            var t = Start(b);
            var v = View(t);
            var d = EsquieBot.Distance(v, C(Cliffs));
            Assert.Equal(2, d);
            Assert.Equal(2, EsquieBot.MaxHits(v, C(Cliffs)));   // sword + Belly Bounce in the guarded-rock clearing
            Assert.Equal(1, EsquieBot.MaxHits(v, C(5)));
            Assert.Equal(d + Tuning.EsquieBotGuardWeight * 2 - 2, EsquieBot.TargetCost(v, Florrie));

            b.ClearItems(0).Item(0, ItemType.Sword, damaged: true).Item(0, ItemType.Bag, damaged: true);
            Assert.Equal(d + Tuning.EsquieBotGuardWeight * 2, EsquieBot.TargetCost(v, Florrie));

            // From a forest: from the nearest clearing touching it, +1 (forest 5 touches the Cliffs).
            b.PawnInForest(0, 5).ClearItems(0).Item(0, ItemType.Sword);
            Assert.Equal(1, EsquieBot.Distance(v, C(Cliffs)));
            Assert.Equal(1 + Tuning.EsquieBotGuardWeight * 2 - 1, EsquieBot.TargetCost(v, Florrie));

            var plan = new EsquieBot().Target(v);
            Assert.Equal(Florrie, plan.Rock);
            Assert.Equal(C(Cliffs), plan.Destination);
            new EsquieBot().PlanTurn(v, t.State);
            Assert.Equal(Florrie, t.State.EsquieTarget);
        }

        [Fact]
        public void EsquieBot_LooseRocksBeforeGuardedRocks()
        {
            var b = Table(bot: true).Pawn(0, "autumn.09");
            var t = Start(b, act: 3);
            SetRock(t, Florrie, RockStatus.Loose, Cliffs);
            var v = View(t);
            var bot = new EsquieBot();

            // Loose Florrie (2 away) before Soarrie in the unruled Monolith (1 away) and the guarded Urrie next door;
            // a hidden Dorrie without a ready torch is not claimable.
            var plan = bot.Target(v);
            Assert.Equal(new[] { Florrie, Soarrie, Urrie }, plan.Ranking.Select(r => r.Item).ToArray());
            Assert.Equal(Florrie, plan.Rock);

            // With an undamaged, unexhausted torch Dorrie comes right after the loose rocks.
            b.Item(0, ItemType.Torch);
            Assert.Equal(new[] { Florrie, Dorrie, Soarrie, Urrie }, bot.Target(v).Ranking.Select(r => r.Item).ToArray());
            b.ClearItems(0).Item(0, ItemType.Torch, exhausted: true);
            Assert.DoesNotContain(Dorrie, bot.Target(v).Ranking.Select(r => r.Item));
        }

        [Fact]
        public void EsquieBot_EasyVisitsFrancoisDecoyOnceInActOne()
        {
            var b = Table(bot: true).Pawn(0, A(OldLumiere));
            var t = Start(b, d: Difficulty.Easy);
            var bot = new EsquieBot();
            var plan = bot.Target(View(t));
            Assert.True(plan.Decoy);
            Assert.Equal(C(Nest), plan.Destination);
            Assert.Null(plan.Rock);
            Assert.Equal("move:8:7", bot.ChooseAction(View(t), new[] { "move:8:6", "move:8:7", "move:8:1" }));

            // Entering the Nest removes the decoy: the walk happens once.
            b.Pawn(0, A(Nest));
            t.Game.OnPieceMoved(S(0), PieceKind.Pawn, C(7), C(Nest), 1);
            Assert.False(bot.Target(View(t)).Decoy);

            // Normal never walks to the decoy.
            var n = Start(Table(bot: true).Pawn(0, A(OldLumiere)));
            Assert.True(n.State.EsquieDecoy);
            Assert.False(bot.Target(View(n)).Decoy);
        }

        [Fact]
        public void EsquieBot_RepairsInForestAtTwoDamagedItems()
        {
            var b = Table(bot: true).Item(0, ItemType.Sword, damaged: true).Item(0, ItemType.Boots, damaged: true).Item(0, ItemType.Bag);
            var t = Start(b);
            var bot = new EsquieBot();
            Assert.True(bot.Gear(View(t)).Repair);
            Assert.Equal("move:5:f5", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1", "move:5:f5" }));

            // In the forest he stays for the Evening rest.
            b.PawnInForest(0, 5);
            Assert.Equal("pass", bot.ChooseAction(View(t), new[] { "move:f5:6", "pass" }));

            // One damaged item is not enough.
            b.Pawn(0, "autumn.05").ClearItems(0).Item(0, ItemType.Sword, damaged: true).Item(0, ItemType.Boots).Item(0, ItemType.Bag);
            Assert.False(bot.Gear(View(t)).Repair);
            Assert.NotEqual("move:5:f5", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1", "move:5:f5" }));
        }

        [Fact]
        public void EsquieBot_SeeksSwordWithoutAnUndamagedSword()
        {
            var b = Table(bot: true).Item(0, ItemType.Boots).Item(0, ItemType.Torch).Legal(0, "move:5:6", "craft:sword", "move:5:1");
            var t = Start(b);
            var bot = new EsquieBot();
            var gear = bot.Gear(View(t));
            Assert.True(gear.SeekSword);
            Assert.True(gear.CraftSword);
            Assert.Null(bot.Hunt(View(t)) is EsquieHuntOrder h && h.Go ? (object)h : null);
            Assert.Equal("craft:sword", bot.ChooseAction(View(t), new[] { "move:5:6", "craft:sword", "move:5:1" }));

            // Nothing craftable: Aid the nearest faction holding a crafted sword (the Marquise in Lumière), never attack.
            b.Legal(0, "move:5:6", "move:5:1").Crafted(1, ItemType.Sword).Warriors(1, A(Lumiere), 2).Warriors(1, "autumn.12", 1);
            gear = bot.Gear(View(t));
            Assert.Equal(S(1), gear.AidFor);
            Assert.Equal(C(Lumiere), gear.AidAt);
            Assert.Equal("move:5:1", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1" }));
            b.Pawn(0, A(Lumiere));
            Assert.Equal("aid:1:1:sword", bot.ChooseAction(View(t), new[] { "aid:1:1:boots", "aid:1:1:sword", "move:1:5" }));

            // An undamaged Bag (Belly Bounce) adds hits but is no sword: a guard battle still needs one.
            b.Item(0, ItemType.Bag);
            Assert.True(bot.Gear(View(t)).SeekSword);
            b.Item(0, ItemType.Sword);
            Assert.False(bot.Gear(View(t)).SeekSword);
        }

        [Fact]
        public void EsquieBot_HardCraftsSwordEarly()
        {
            var b = Table(bot: true).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Legal(0, "move:5:1", "craft:sword");
            var bot = new EsquieBot();
            var hard = Start(b, d: Difficulty.Hard);
            Assert.True(bot.Gear(View(hard)).CraftSword);
            Assert.Equal("craft:sword", bot.ChooseAction(View(hard), new[] { "move:5:1", "craft:sword" }));

            var nb = Table(bot: true).Item(0, ItemType.Sword).Item(0, ItemType.Boots).Legal(0, "move:5:1", "craft:sword");
            var normal = Start(nb);
            Assert.False(bot.Gear(View(normal)).CraftSword);
            Assert.NotEqual("craft:sword", bot.ChooseAction(View(normal), new[] { "move:5:1", "craft:sword" }));
        }

        [Fact]
        public void EsquieBot_HuntsOnlyWithAllItemsUndamaged()
        {
            var b = Table(bot: true).Pawn(0, A(Cliffs)).Item(0, ItemType.Sword).Item(0, ItemType.Sword).Item(0, ItemType.Boots, damaged: true);
            var t = Start(b);
            var bot = new EsquieBot();
            var h = bot.Hunt(View(t));
            Assert.False(h.AllItemsUndamaged);
            Assert.False(h.Go);
            Assert.Null(bot.ChooseAction(View(t), new[] { "battle:6:guards" }));

            b.ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Sword).Item(0, ItemType.Boots);
            Assert.True(bot.Hunt(View(t)).Go);
            Assert.Equal("battle:6:guards", bot.ChooseAction(View(t), new[] { "battle:6:2", "battle:6:guards" }));
        }

        [Fact]
        public void EsquieBot_HuntsWhenExpectedHitsNearlyFinishGuards()
        {
            Assert.Equal(15.0 / 16, EsquieBot.ExpectedRollHits(1), 6);
            Assert.Equal(27.0 / 16, EsquieBot.ExpectedRollHits(2), 6);
            Assert.Equal(34.0 / 16, EsquieBot.ExpectedRollHits(3), 6);

            // Florrie, 2 guards: one sword (0.94 expected hits) is short of GuardsLeft - 1; sword + Belly Bounce (1.69) is enough.
            var b = Table(bot: true).Pawn(0, "autumn.05").Item(0, ItemType.Sword).Item(0, ItemType.Boots);
            var t = Start(b);
            var bot = new EsquieBot();
            Assert.False(bot.Hunt(View(t)).Go);
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1" }));   // he does not walk into the guards
            b.Item(0, ItemType.Bag);
            Assert.True(bot.Hunt(View(t)).Go);
            Assert.Equal("move:5:6", bot.ChooseAction(View(t), new[] { "move:5:6", "move:5:1" }));

            // Urrie, 3 guards: needs 2 expected hits.
            SetRock(t, Florrie, RockStatus.Held);
            SetRock(t, Dorrie, RockStatus.Held);
            SetRock(t, Soarrie, RockStatus.Held);
            SetRock(t, Urrie, RockStatus.Guarded, Nest, guards: 3);
            b.Guards(A(Nest), 3);
            Assert.False(bot.Hunt(View(t)).Go);   // 2 max hits: 1.69
            b.Item(0, ItemType.Sword);
            Assert.True(bot.Hunt(View(t)).Go);    // 3 max hits: 2.13
        }

        [Fact]
        public void EsquieBot_HardTakesTwoGuardBattlesPerTurn()
        {
            TestTable Urrie3(Difficulty d, int swords)
            {
                var b = Table(bot: true).Pawn(0, A(Nest));
                for (var i = 0; i < swords; i++) b.Item(0, ItemType.Sword);
                var t = Start(b, d: d);
                Hold(t, Florrie, Dorrie, Soarrie);
                SetRock(t, Urrie, RockStatus.Guarded, Nest, guards: 3);
                b.Guards(A(Nest), 3);
                return t;
            }
            var legal = new[] { "battle:2:guards", "move:2:7" };

            // 2 swords: Normal's one battle (1.69 hits) cannot nearly finish 3 guards; Hard's two (3.38) can.
            var normal = Urrie3(Difficulty.Normal, 2);
            Assert.Equal(Tuning.EsquieBotGuardBattles, new EsquieBot().Hunt(View(normal)).Battles);
            Assert.False(new EsquieBot().Hunt(View(normal)).Go);

            var hard = Urrie3(Difficulty.Hard, 2);
            var bot = new EsquieBot();
            bot.PlanTurn(View(hard), hard.State);
            Assert.Equal(Tuning.EsquieHardGuardBattles, bot.Hunt(View(hard)).Battles);
            Assert.Equal("battle:2:guards", bot.ChooseAction(View(hard), legal));
            hard.State.EsquieRocks[Urrie].GuardsLeft = 2;
            Assert.Equal("battle:2:guards", bot.ChooseAction(View(hard), legal));
            hard.State.EsquieRocks[Urrie].GuardsLeft = 1;
            Assert.NotEqual("battle:2:guards", bot.ChooseAction(View(hard), legal));   // two guard battles this turn already

            // A new turn allows two again.
            bot.PlanTurn(View(hard), hard.State);
            Assert.Equal("battle:2:guards", bot.ChooseAction(View(hard), legal));

            // Normal with 3 swords: one battle, then no second that turn.
            var n3 = Urrie3(Difficulty.Normal, 3);
            var nb = new EsquieBot();
            nb.PlanTurn(View(n3), n3.State);
            Assert.Equal("battle:2:guards", nb.ChooseAction(View(n3), legal));
            n3.State.EsquieRocks[Urrie].GuardsLeft = 2;
            Assert.NotEqual("battle:2:guards", nb.ChooseAction(View(n3), legal));
        }

        [Fact]
        public void EsquieBot_EndgameGoesStraightForLastRock()
        {
            var b = Table(bot: true).Pawn(0, "autumn.04");
            var t = Start(b);
            Hold(t, Florrie, Dorrie, Soarrie);
            SetRock(t, Urrie, RockStatus.Loose, Nest);
            var bot = new EsquieBot();
            var v = View(t);
            Assert.Equal(4, EsquieBot.Distance(v, C(Nest)));   // beyond tune.esquie-bot-rock-range
            Assert.Null(bot.Target(v).Rock);
            var end = bot.Endgame(v);
            Assert.Equal(Urrie, end.Rock);
            Assert.Equal(C(Nest), end.Destination);
            Assert.False(bot.VagabondVp(v));
            Assert.True(bot.InGoalMode(v));
            Assert.Equal("move:4:10", bot.ChooseAction(v, new[] { "move:4:10", "move:4:11", "move:4:12" }));
            bot.PlanTurn(v, t.State);
            Assert.Equal(Urrie, t.State.EsquieTarget);

            // With only two rocks held the same far rock is out of range: Root's Vagabond AI plays.
            SetRock(t, Soarrie, RockStatus.WithVerso);
            Assert.Null(bot.Endgame(v));
            Assert.Null(bot.ChooseAction(v, new[] { "move:4:10", "move:4:11", "move:4:12" }));
        }

        [Fact]
        public void EsquieBot_PlaysVagabondVpWhenNoRockWithinThreeMoves()
        {
            var b = Table(bot: true).Seat(3, EngineId.VagabondRanger, CharacterIds.Verso).PawnInForest(3, 1).Pawn(0, "autumn.11");
            var t = Start(b, act: 2);
            var bot = new EsquieBot();
            var v = View(t);
            Assert.Equal(4, EsquieBot.Distance(v, C(Cliffs)));
            Assert.True(bot.VagabondVp(v));
            Assert.Null(bot.Target(v).Rock);
            Assert.False(bot.InGoalMode(v));
            Assert.Null(bot.ChooseAction(v, new[] { "move:11:3", "move:11:4" }));
            bot.PlanTurn(v, t.State);
            Assert.Null(t.State.EsquieTarget);

            b.Pawn(0, "autumn.04");   // 3 moves from the Cliffs
            Assert.False(bot.VagabondVp(v));
            Assert.Equal(Florrie, bot.Target(v).Rock);
        }

        private static ModuleRegistry WithStub(string character) =>
            Registry().Register(character, () => new StubModule(character));

        /// <summary>Esquie (bot, seat 0) next to the Monolith in the Visages (9), the White-Haired Man (Corvids, seat 2) with a
        /// banner up, Act II. Soarrie already held, so his own target is Florrie at the Cliffs.</summary>
        private static TestTable SpoilerTable(FakeBoard b, Difficulty d = Difficulty.Normal)
        {
            var t = Start(b, act: 2, d: d, registry: WithStub(CharacterIds.WhiteHairedMan));
            SetRock(t, Soarrie, RockStatus.Held);
            t.State.GlobalCountdowns.Add(S(2));
            return t;
        }

        private static FakeBoard WhmBoard() => FakeBoard.Autumn()
            .Seat(0, EngineId.VagabondTinker, CharacterIds.Esquie, bot: true).Seat(1, EngineId.Marquise)
            .Seat(2, EngineId.Corvids, CharacterIds.WhiteHairedMan).Supply(1, 25).Supply(2, 15)
            .Pawn(0, "autumn.09").Warriors(2, "autumn.09", 2).Item(0, ItemType.Sword).Item(0, ItemType.Bag);

        [Fact]
        public void EsquieBot_SpoilerBlocksOnlyByMovingPawn()
        {
            var b = WhmBoard();
            var t = SpoilerTable(b);
            var bot = new EsquieBot();
            var sp = bot.Spoiler(View(t));
            Assert.Equal(S(2), sp.Threat);
            Assert.True(sp.Banner);
            Assert.True(sp.PreEmpts);
            Assert.Equal(C(Monolith), sp.Destination);
            var legal = new[] { "battle:9:2", "move:9:2", "move:9:3", "move:9:7" };
            Assert.Equal("move:9:3", bot.ChooseAction(View(t), legal));
            Assert.False(t.Game.CanStartBattle(S(0), S(2), C(9), false));   // Never Mad: no spoiling battle is even offered

            // Already in the Monolith: he stays there and never attacks.
            b.Pawn(0, A(Monolith)).Warriors(2, A(Monolith), 1);
            Assert.Equal("pass", bot.ChooseAction(View(t), new[] { "battle:3:2", "move:3:9", "pass" }));

            // Without the banner he follows his own plan (toward Florrie).
            b.Pawn(0, "autumn.09");
            t.State.GlobalCountdowns.Clear();
            Assert.Null(bot.Spoiler(View(t)));
            Assert.Equal("move:9:2", bot.ChooseAction(View(t), legal));

            // A threat he cannot answer with his pawn (Gustave's flags): no answer, and still no battle.
            var g = Table(bot: true).Seat(1, EngineId.Marquise, CharacterIds.Gustave).Pawn(0, "autumn.09").Warriors(1, "autumn.09", 1);
            var tg = Start(g, act: 2, registry: WithStub(CharacterIds.Gustave));
            SetRock(tg, Soarrie, RockStatus.Held);
            tg.State.GlobalCountdowns.Add(S(1));
            var gs = bot.Spoiler(View(tg));
            Assert.Equal(S(1), gs.Threat);
            Assert.Null(gs.Destination);
            Assert.NotEqual("battle:9:1", bot.ChooseAction(View(tg), new[] { "battle:9:1", "move:9:2", "move:9:3" }));
        }

        [Fact]
        public void EsquieBot_SpoilerYieldsToOwnQuestWinThisTurn()
        {
            var b = WhmBoard().Item(0, ItemType.Boots);
            var t = SpoilerTable(b);
            Hold(t, Florrie, Dorrie);
            SetRock(t, Urrie, RockStatus.Loose, Nest);
            var bot = new EsquieBot();
            Assert.True(bot.CanFinishQuestThisTurn(View(t)));
            var sp = bot.Spoiler(View(t));
            Assert.False(sp.PreEmpts);
            Assert.Equal("move:9:2", bot.ChooseAction(View(t), new[] { "move:9:2", "move:9:3", "move:9:7" }));

            // No boots left to reach the rock this turn: the banner comes first.
            b.ClearItems(0).Item(0, ItemType.Sword).Item(0, ItemType.Boots, exhausted: true);
            Assert.False(bot.CanFinishQuestThisTurn(View(t)));
            Assert.True(bot.Spoiler(View(t)).PreEmpts);
            Assert.Equal("move:9:3", bot.ChooseAction(View(t), new[] { "move:9:2", "move:9:3", "move:9:7" }));
        }

        // ================================================================= v1.1 rock powers are off in the first release

        [Fact]
        public void RockPowers_FlorrieGrantsNothingWithoutUpdateRules()
        {
            var b = Table();
            var t = Start(b);
            Assert.False(t.Ctx.UpdateRules); // builder-questions C1 (a): off in the first release
            SetRock(t, Florrie, RockStatus.Loose, Cliffs);
            ActIn(t, Cliffs);
            Assert.Equal(RockStatus.Held, R(t, Florrie).Status); // the rock still counts for the win
            Assert.Empty(b.Abilities);
            t.SaveAndLoad(Registry());                            // and nothing is granted on load either
            Assert.Empty(b.Abilities);
        }

        [Fact]
        public void RockPowers_DorrieGrantsNothingWithoutUpdateRules()
        {
            var b = Table().Pawn(0, A(OldLumiere));
            var t = Start(b, act: 2);
            t.Game.OnExploreResolved(S(0), C(OldLumiere));
            Assert.Equal(RockStatus.Held, R(t, Dorrie).Status);
            Assert.Empty(b.Abilities);
        }

        [Fact]
        public void RockPowers_SoarrieGrantsNothingWithoutUpdateRules()
        {
            var b = Table();
            var t = Start(b, act: 2);
            ActIn(t, Monolith);
            Assert.Equal(RockStatus.Held, R(t, Soarrie).Status);
            Assert.Empty(b.Abilities);
            Assert.DoesNotContain(b.Log, l => l.StartsWith("GrantAbility", StringComparison.Ordinal));
        }
    }
}
