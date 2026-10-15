using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Characters.Lune;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Characters
{
    /// <summary>Every test the Lune rows name: win.lune, tweak.lune.stains, spawn.parents-journal and bot.lune's rules.</summary>
    public class LuneTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static ClearingId C(int i) => new ClearingId(i);
        private const string Tablet = LuneRelics.Tablet, Figure = LuneRelics.Figure, Jewelry = LuneRelics.Jewelry;

        private static ModuleRegistry Registry() => new ModuleRegistry().Register(CharacterIds.Lune, () => new LuneModule());

        private static TestTable Start(FakeBoard b, int act = 1, Difficulty d = Difficulty.Normal, ModuleRegistry registry = null)
        {
            var t = TestTable.Start(b, registry ?? Registry(), d);
            if (act != 1)
            {
                t.Ctx.ActClock.Restore(act);
                t.State.GlobalAct = t.Ctx.ActClock.Act;
            }
            return t;
        }

        /// <summary>Lune (seat 0, Keepers) and a Marquise (seat 1).</summary>
        private static FakeBoard Board(bool bot = false) => FakeBoard.Autumn()
            .Seat(0, EngineId.Keepers, CharacterIds.Lune, bot)
            .Seat(1, EngineId.Marquise);

        /// <summary>Forest relics: a value-3 tablet (1), a figure (2), a jewelry (3) and a value-1 tablet (4).</summary>
        private static FakeBoard RelicBoard(bool bot = false) => Board(bot)
            .Relic(1, Tablet, 3, 4).Relic(2, Figure, 1, 5).Relic(3, Jewelry, 2, 6).Relic(4, Tablet, 1, 7);

        /// <summary>Act I game, then the act opens (story.act2.parents-journal tags the Journal).</summary>
        private static TestTable StartActTwo(FakeBoard b)
        {
            var t = Start(b);
            t.Game.OnActChanged(2);
            return t;
        }

        private static bool Won(FakeBoard b) => b.Winners.Any(w => w.Key == S(0) && w.Value == Ids.WinConditions.Lune);

        private static string Glow(int forest) => FakeBoard.OverlayKey(Ids.Overlays.LuneJournalGlow, null, null, forest);

        // ================================================================= win.lune

        [Fact]
        public void LuneWin_WinsWithThreeTypesIncludingTaggedJournal()
        {
            var b = RelicBoard();
            var t = StartActTwo(b);
            Assert.Equal(1, t.State.LuneTaggedRelic);
            b.RecoverRelic(1, 0).RecoverRelic(2, 0);
            t.PlayTurn(0);
            Assert.False(Won(b));
            Assert.Equal(1, new LuneWin().UnitsMissing(t.Ctx, S(0)));
            b.RecoverRelic(3, 0);
            Assert.True(WinRules.QuestPasses(t.Ctx, S(0), new LuneWin()));
            t.PlayTurn(1);
            Assert.False(Won(b));          // only at the end of her own turn
            t.PlayTurn(0);
            Assert.True(Won(b));
            Assert.Empty(b.Banners);       // a quest: no countdown banner
        }

        [Fact]
        public void LuneWin_NoWinWithoutTaggedJournal()
        {
            var b = RelicBoard();
            var t = StartActTwo(b);
            // Three types, but the tablet is the value-1 one, not the Parents' Journal.
            b.RecoverRelic(4, 0).RecoverRelic(2, 0).RecoverRelic(3, 0);
            t.PlayTurn(0);
            Assert.False(Won(b));
            Assert.Equal(1, new LuneWin().UnitsMissing(t.Ctx, S(0)));
            // The Journal alone (with fewer than three types) is not enough either.
            var b2 = RelicBoard();
            var t2 = StartActTwo(b2);
            b2.RecoverRelic(1, 0).RecoverRelic(2, 0);
            t2.PlayTurn(0);
            Assert.False(Won(b2));
            // With no Journal tagged at all (no relic in a forest at Act II), she cannot win.
            var b3 = Board().Relic(1, Tablet, 3, 4).Relic(2, Figure, 1, 5).Relic(3, Jewelry, 2, 6)
                .RecoverRelic(1, 0).RecoverRelic(2, 0).RecoverRelic(3, 0);
            var t3 = StartActTwo(b3);
            Assert.Null(t3.State.LuneTaggedRelic);
            t3.PlayTurn(0);
            Assert.False(Won(b3));
        }

        [Fact]
        public void LuneWin_BlockedInActOne()
        {
            var b = RelicBoard().RecoverRelic(1, 0).RecoverRelic(2, 0).RecoverRelic(3, 0);
            var t = Start(b);
            t.State.LuneTaggedRelic = 1;
            Assert.True(new LuneWin().QuestComplete(t.Ctx, S(0)));
            t.PlayTurn(0);
            Assert.False(Won(b));

            // Gate: an active dominance card switches the win off (offWithDominance).
            var d = RelicBoard();
            var td = StartActTwo(d);
            d.RecoverRelic(1, 0).RecoverRelic(2, 0).RecoverRelic(3, 0).Dominance(0, Suit.Fox);
            td.PlayTurn(0);
            Assert.False(Won(d));
            d.Dominance(0, null);
            td.PlayTurn(1).PlayTurn(0);
            Assert.True(Won(d));
        }

        [Fact]
        public void LuneWin_DroppedJournalKeepsTag()
        {
            var b = RelicBoard();
            var t = StartActTwo(b);
            Assert.Equal(1, b.RelicForest(1));                 // moved beside Sirène
            b.CarryRelic(1, "autumn.10");
            t.Game.OnRelicMoved(1);
            // An enemy knocks it loose (Prized Trophies): face up into a forest, still tagged.
            b.DropRelic(1, 3, faceUp: true);
            t.Game.OnRelicMoved(1);
            Assert.Equal(1, t.State.LuneTaggedRelic);
            Assert.Contains(1, b.TaggedRelics);
            Assert.Contains(Glow(3), b.Overlays);
            Assert.DoesNotContain(Glow(1), b.Overlays);
            // The other tablet does not stand in for it; the dropped Journal, recovered later, does.
            b.RecoverRelic(4, 0).RecoverRelic(2, 0).RecoverRelic(3, 0);
            t.PlayTurn(0);
            Assert.False(Won(b));
            b.RecoverRelic(1, 0);
            t.PlayTurn(1).PlayTurn(0);
            Assert.True(Won(b));
        }

        // ================================================================= tweak.lune.stains

        [Fact]
        public void Stains_RecoveredRelicGivesMatchingStain()
        {
            var b = RelicBoard();
            var t = Start(b);
            t.Game.OnRelicRecovered(S(0), 3, Jewelry);
            t.Game.OnRelicRecovered(S(0), 4, Tablet);
            t.Game.OnRelicRecovered(S(0), 2, Figure);
            t.Game.OnRelicRecovered(S(0), 1, Tablet);
            Assert.Equal(1, t.State.LuneStains["fire"]);
            Assert.Equal(2, t.State.LuneStains["ice"]);
            Assert.Equal(1, t.State.LuneStains["lightning"]);
            // Another Keepers seat's recovery gives her nothing.
            t.Game.OnRelicRecovered(S(1), 3, Jewelry);
            Assert.Equal(1, t.State.LuneStains["fire"]);
            Assert.True(b.OverlayShown(Ids.Overlays.LuneStains));
            // Stains survive a save.
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(2, loaded.State.LuneStains["ice"]);
        }

        [Fact]
        public void Stains_ArmedStainAddsHitAndIsSpent()
        {
            var b = RelicBoard();
            var t = Start(b);
            t.Game.OnRelicRecovered(S(0), 3, Jewelry);
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("fire"), true);
            Assert.Equal("fire", t.State.LuneStainArmed);
            t.Game.OnBattleStarting(S(0), S(1), C(5));
            Assert.Equal(Tuning.LuneStainHits, b.ExtraHits[S(0)]);
            t.Game.OnBattleResolved(new BattleResult { Clearing = C(5), Attacker = S(0), Defender = S(1) });
            Assert.Equal(0, t.State.LuneStains["fire"]);
            Assert.Null(t.State.LuneStainArmed);
            // Spent: the next battle has no extra hit.
            t.Game.OnBattleStarting(S(0), S(1), C(5));
            Assert.Equal(Tuning.LuneStainHits, b.ExtraHits[S(0)]);

            // An armed Stain also applies when she defends, and a battle she is not in leaves it armed.
            t.Game.OnRelicRecovered(S(0), 2, Figure);
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("lightning"), true);
            t.Game.OnBattleResolved(new BattleResult { Clearing = C(6), Attacker = S(1), Defender = S(2) });
            Assert.Equal("lightning", t.State.LuneStainArmed);
            t.Game.OnBattleStarting(S(1), S(0), C(6));
            Assert.Equal(2 * Tuning.LuneStainHits, b.ExtraHits[S(0)]);
            t.Game.OnBattleResolved(new BattleResult { Clearing = C(6), Attacker = S(1), Defender = S(0) });
            Assert.Equal(0, t.State.LuneStains["lightning"]);
            Assert.Null(t.State.LuneStainArmed);
        }

        [Fact]
        public void Stains_CannotArmWithoutStain()
        {
            var b = RelicBoard();
            var t = Start(b);
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("ice"), true);
            Assert.Null(t.State.LuneStainArmed);
            t.Game.OnBattleStarting(S(0), S(1), C(5));
            Assert.False(b.ExtraHits.ContainsKey(S(0)));
            // Another seat cannot arm her Stains; switching off disarms.
            t.Game.OnRelicRecovered(S(0), 1, Tablet);
            t.Game.OnToggle(S(1), LuneTweaks.StainToggle("ice"), true);
            Assert.Null(t.State.LuneStainArmed);
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("ice"), true);
            Assert.Equal("ice", t.State.LuneStainArmed);
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("ice"), false);
            Assert.Null(t.State.LuneStainArmed);
            // A loaded armed Stain without a Stain of that element is dropped.
            t.Game.OnToggle(S(0), LuneTweaks.StainToggle("ice"), true);
            var loaded = t.SaveAndLoad(Registry(), text => text.Replace("ice=1", "ice=0"));
            Assert.Null(loaded.State.LuneStainArmed);
        }

        // ================================================================= spawn.parents-journal

        [Fact]
        public void ParentsJournal_TagsHighestValueForestTablet()
        {
            // Two value-3 tablets in forests (lowest id wins), a value-3 jewelry, and a carried value-3 tablet.
            var b = Board().Relic(1, Tablet, 1, 4).Relic(2, Tablet, 3, 6).Relic(3, Tablet, 3, 7).Relic(4, Jewelry, 3, 5)
                .Relic(5, Tablet, 3, 2).CarryRelic(5, "autumn.08");
            var t = StartActTwo(b);
            Assert.Equal(2, t.State.LuneTaggedRelic);
            Assert.Contains(2, b.TaggedRelics);
            Assert.Contains("MoveRelic 2 f1", b.Log);                // the forest touching Sirène and the Monolith
            Assert.Equal(1, b.RelicForest(2));
            Assert.Contains(Glow(1), b.Overlays);
            Assert.Contains(Ids.StoryEvents.Act2ParentsJournal, t.Ctx.StoryLog.Select(e => e.EventId));

            // Fallback: relics cannot be moved, so it is tagged where it lies.
            var u = Board().Relic(1, Tablet, 1, 4).Relic(2, Tablet, 3, 6).Unsupported(Ids.BoardActions.MoveRelic);
            var tu = StartActTwo(u);
            Assert.Equal(2, tu.State.LuneTaggedRelic);
            Assert.Equal(6, u.RelicForest(2));
            Assert.Contains(Glow(6), u.Overlays);

            // Not in Act I, and never when Lune is not seated.
            var early = Board().Relic(1, Tablet, 3, 4);
            Assert.Null(Start(early).State.LuneTaggedRelic);
            var absent = FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie).Relic(1, Tablet, 3, 4);
            var ta = Start(absent);
            ta.Game.OnActChanged(2);
            Assert.Empty(absent.TaggedRelics);
        }

        [Fact]
        public void ParentsJournal_FallsBackToHighestValueAnyType()
        {
            // No tablet left in the forests (one recovered, one carried): the highest-value relic of any type.
            var b = Board().Relic(1, Figure, 2, 4).Relic(2, Jewelry, 3, 6).Relic(3, Figure, 3, 7).Relic(4, Tablet, 3, 5).Relic(5, Tablet, 2, 2)
                .RecoverRelic(4, 0).CarryRelic(5, "autumn.07");
            var t = StartActTwo(b);
            Assert.Equal(2, t.State.LuneTaggedRelic);
            Assert.Equal(1, b.RelicForest(2));
            // Nothing in any forest: nothing is tagged.
            var none = Board().Relic(1, Tablet, 3, 4).RecoverRelic(1, 0);
            Assert.Null(StartActTwo(none).State.LuneTaggedRelic);
        }

        [Fact]
        public void ParentsJournal_TagSurvivesPrizedTrophies()
        {
            var b = RelicBoard();
            var t = StartActTwo(b);
            Assert.Equal(1, t.State.LuneTaggedRelic);
            b.CarryRelic(1, "autumn.04");
            t.Game.OnRelicMoved(1);
            Assert.DoesNotContain(Glow(1), b.Overlays);         // carried: no forest glows
            b.DropRelic(1, 2, faceUp: true);                    // Prized Trophies
            t.Game.OnRelicMoved(1);
            Assert.Equal(1, t.State.LuneTaggedRelic);
            Assert.True(b.RelicFaceUp(1));
            Assert.Equal(3, b.Log.Count(l => l == "TagRelic 1"));  // tagged, then re-attached after each move
            Assert.Contains(Glow(2), b.Overlays);
            // Another relic moving changes nothing.
            t.Game.OnRelicMoved(4);
            Assert.Equal(1, t.State.LuneTaggedRelic);
            // The tag survives a save and load.
            var loaded = t.SaveAndLoad(Registry());
            Assert.Equal(1, loaded.State.LuneTaggedRelic);
            Assert.Contains(Glow(2), b.Overlays);
        }

        // ================================================================= bot.lune

        private static BotView View(TestTable t) => new BotView(t.Ctx, S(0));

        [Fact]
        public void LuneBot_ActOneDelvesMissingRelicTypesFirst()
        {
            Assert.Empty(ModuleRegistry.MissingBotRules(new LuneBot()));
            // She has a tablet. Forest 5 (around her warriors in 5): a tablet (duplicate), a face-down figure and a face-up
            // value-3 figure; forest 4 (one step away): a face-up jewelry.
            var b = Board(bot: true).Warriors(0, "autumn.05", 2)
                .Relic(9, Tablet, 2, 7).RecoverRelic(9, 0)
                .Relic(1, Tablet, 3, 5, faceUp: true).Relic(2, Figure, 3, 5).Relic(4, Figure, 2, 5, faceUp: true).Relic(3, Jewelry, 1, 4, faceUp: true);
            var t = Start(b);
            var bot = new LuneBot();
            Assert.Equal(new[] { 4, 2, 3 }, bot.Act1(View(t)).Select(r => r.Item));
            Assert.Equal("delve:5:4", bot.ChooseAction(View(t), new[] { "delve:5:1", "delve:5:2", "delve:5:4" }));
            Assert.Equal("delve:5:2", bot.ChooseAction(View(t), new[] { "delve:5:1", "delve:5:2" }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "delve:5:1" }));
            // From Act II the step gives way to step 2.
            Assert.Empty(bot.Act1(View(Start(b, act: 2))));
        }

        [Fact]
        public void LuneBot_ActTwoChasesJournalOrMissingTypeWhicheverCloser()
        {
            // The Journal (1) in forest 1, three steps from her warriors in 5; a figure (2) in forest 5, beside them.
            var b = Board(bot: true).Warriors(0, "autumn.05", 3).Relic(1, Tablet, 3, 1).Relic(2, Figure, 1, 5);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.True(bot.InGoalMode(View(t)));
            Assert.Equal(new[] { 2, 1 }, bot.Act2Goals(View(t)).Select(r => r.Item));
            // The Journal knocked into forest 4 (also beside her): equally close, so the Journal comes first.
            b.DropRelic(1, 5, faceUp: true);
            Assert.Equal(new[] { 1, 2 }, bot.Act2Goals(View(t)).Select(r => r.Item));
            // Carried by her: measured to its clearing.
            b.CarryRelic(1, "autumn.10");
            Assert.Equal(new[] { 2, 1 }, bot.Act2Goals(View(t)).Select(r => r.Item));
            Assert.Empty(bot.Act2Goals(View(Start(b, act: 1))));
        }

        [Fact]
        public void LuneBot_RetinueAddsCardsMatchingRouteSuits()
        {
            // Route from 5 (fox) to the Journal's forest 1: 5 -> 1 (rabbit) -> 8 (fox) -> 10 (rabbit, touches forest 1).
            var b = Board(bot: true).Warriors(0, "autumn.05", 3).Relic(1, Tablet, 3, 1)
                .Hand(0, Suit.Mouse, Suit.Fox, Suit.Rabbit, Suit.Fox, Suit.Rabbit);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.Equal(new[] { "retinue:move:fox", "retinue:move:rabbit", "retinue:move:fox", "retinue:battle-delve:rabbit" },
                bot.Retinue(View(t)).Select(c => c.Key));
            Assert.Equal("retinue:move:fox", bot.ChooseAction(View(t), new[] { "retinue:battle-delve:mouse", "retinue:move:fox" }));
            // A card the Retinue already holds is not added again; a bird fills a missing suit.
            b.Column(0, LuneAction.MoveColumn, Suit.Fox).Hand(0, Suit.Bird, Suit.Rabbit, Suit.Fox, Suit.Rabbit);
            Assert.Equal(new[] { "retinue:move:rabbit", "retinue:move:fox", "retinue:battle-delve:rabbit" }, bot.Retinue(View(t)).Select(c => c.Key));
        }

        [Fact]
        public void LuneBot_NeverAddsUnresolvableRetinueCard()
        {
            // Same route, but no card for the first step (5, fox): her warriors never reach a rabbit clearing this turn,
            // so the rabbit Move and Delve cards could not resolve.
            var b = Board(bot: true).Warriors(0, "autumn.05", 3).Relic(1, Tablet, 3, 1).Hand(0, Suit.Rabbit, Suit.Rabbit);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.Empty(bot.Retinue(View(t)));
            Assert.False(bot.Lookahead(View(t), new RetinueCard(LuneAction.MoveColumn, Suit.Rabbit), new[] { C(5) }));
            Assert.Null(bot.ChooseAction(View(t), new[] { "retinue:move:rabbit", "retinue:battle-delve:rabbit" }));
            // With a fox card for the first step the rabbit Move resolves; the Delve still needs the fox step from 8 first.
            b.Hand(0, Suit.Fox, Suit.Rabbit, Suit.Rabbit);
            Assert.Equal(new[] { "retinue:move:fox", "retinue:move:rabbit" }, bot.Retinue(View(t)).Select(c => c.Key));
            b.Hand(0, Suit.Fox, Suit.Rabbit, Suit.Fox, Suit.Rabbit);
            Assert.Equal(new[] { "retinue:move:fox", "retinue:move:rabbit", "retinue:move:fox", "retinue:battle-delve:rabbit" },
                bot.Retinue(View(t)).Select(c => c.Key));
        }

        [Fact]
        public void LuneBot_DelvesJournalOnlyWithThreeWarriorsAdjacent()
        {
            var b = Board(bot: true).Relic(1, Tablet, 3, 1).Warriors(0, "autumn.10", 2);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.False(bot.Delve(View(t)));
            Assert.Null(bot.ChooseAction(View(t), new[] { "delve:10:1" }));
            b.Warriors(0, "autumn.11", 1);                       // 2 + 1 around forest 1
            Assert.Equal(Tuning.LuneDelveEscort, b.ForestClearings(1).Sum(c => b.Warriors(S(0), c)));
            Assert.True(bot.Delve(View(t)));
            Assert.Equal("delve:10:1", bot.ChooseAction(View(t), new[] { "delve:10:1" }));
        }

        [Fact]
        public void LuneBot_EscortsCarriersInGroupsOfTwo()
        {
            // The Journal carried in 8 with three warriors; a tablet Research Camp in Lumière (1).
            var b = Board(bot: true).Relic(1, Tablet, 3, 1).CarryRelic(1, "autumn.08").Warriors(0, "autumn.08", 3)
                .Building(0, "autumn.01", PieceTypes.Waystation + "." + Tablet);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            var plan = bot.Escort(View(t), new[] { 1 }).Single();
            Assert.Equal(new[] { C(8), C(1) }, plan.Route);
            Assert.Equal(EscortPlan.RecoverFinish, plan.Finish);
            Assert.Equal(Tuning.LuneConvoySize, plan.ConvoySize);
            var legal = new[] { "move:8:1:1:1", "move:8:1:2:1", "move:8:1:3:1" };
            var pick = LuneAction.Parse(bot.ChooseAction(View(t), legal));
            Assert.True(pick.Count >= Tuning.LuneConvoySize);
            Assert.Null(bot.ChooseAction(View(t), new[] { "move:8:1:1:1" }));   // never a lone carrier on Normal
            // At the camp she recovers it.
            Assert.Equal("recover:8:1", bot.ChooseAction(View(t), new[] { "recover:8:1" }));
            // Easy sends single-warrior carriers.
            var e = Start(b, act: 2, d: Difficulty.Easy);
            e.State.LuneTaggedRelic = 1;
            Assert.Equal("move:8:1:1:1", bot.ChooseAction(View(e), legal));
            // With one warrior on Normal she gathers an escort first.
            b.Warriors(0, "autumn.08", 1).Warriors(0, "autumn.12", 2);
            Assert.False(bot.Escort(View(t), new[] { 1 }).Single().Ready);
            Assert.Equal("move:12:8:2", bot.ChooseAction(View(t), new[] { "move:8:1:1:1", "move:12:8:2" }));
        }

        [Fact]
        public void LuneBot_AvoidsClearingsWhereEnemiesOutnumberEscort()
        {
            // Carried in 5 with two warriors, tablet camp in 8: 5-1-8 or 5-6-8.
            var b = Board(bot: true).Relic(1, Tablet, 3, 1).CarryRelic(1, "autumn.05").Warriors(0, "autumn.05", 2)
                .Building(0, "autumn.08", PieceTypes.Waystation + "." + Tablet).Warriors(1, "autumn.01", 2);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.Equal(new[] { C(5), C(1), C(8) }, bot.Escort(View(t), new[] { 1 }).Single().Route);   // 2 do not outnumber 2
            b.Warriors(1, "autumn.01", 3);
            Assert.Equal(new[] { C(5), C(6), C(8) }, bot.Escort(View(t), new[] { 1 }).Single().Route);
            Assert.Equal("move:5:6:2:1", bot.ChooseAction(View(t), new[] { "move:5:1:2:1", "move:5:6:2:1" }));
            // No safe way to a tablet camp: the nearest safe clearing where she can build one.
            b.Warriors(1, "autumn.06", 3);
            var plan = bot.Escort(View(t), new[] { 1 }).Single();
            Assert.Equal(new[] { C(5) }, plan.Route);
            Assert.Equal(EscortPlan.BuildFinish, plan.Finish);
        }

        [Fact]
        public void LuneBot_HardRunsTwoConvoys()
        {
            var b = Board(bot: true)
                .Relic(1, Tablet, 3, 1).CarryRelic(1, "autumn.05").Warriors(0, "autumn.05", 2)
                .Relic(2, Figure, 2, 4).CarryRelic(2, "autumn.12").Warriors(0, "autumn.12", 2)
                .Building(0, "autumn.08", PieceTypes.Waystation + "." + Tablet).Building(0, "autumn.08", PieceTypes.Waystation + "." + Figure);
            var bot = new LuneBot();
            var hard = Start(b, act: 2, d: Difficulty.Hard);
            hard.State.LuneTaggedRelic = 1;
            Assert.Equal(new[] { 1, 2 }, bot.Escort(View(hard), new[] { 2, 1 }).Select(p => p.Relic));
            var normal = Start(b, act: 2);
            normal.State.LuneTaggedRelic = 1;
            Assert.Equal(new[] { 1 }, bot.Escort(View(normal), new[] { 2, 1 }).Select(p => p.Relic));
            Assert.Equal(new[] { 1, 2 }, bot.Carried(View(hard), LuneAction.ParseAll(new[] { "move:12:8:2:2" })));
        }

        [Fact]
        public void LuneBot_SkipsDuplicateTypesWhileGoalRelicsRemain()
        {
            // She has a tablet; the Journal (1) is in forest 1; another tablet (2) lies beside her in forest 5.
            var b = Board(bot: true).Warriors(0, "autumn.05", 3).Relic(9, Tablet, 1, 7).RecoverRelic(9, 0)
                .Relic(1, Tablet, 3, 1).Relic(2, Tablet, 3, 5);
            var t = Start(b, act: 2);
            t.State.LuneTaggedRelic = 1;
            var bot = new LuneBot();
            Assert.True(bot.Duplicates(View(t), 2));
            Assert.False(bot.Duplicates(View(t), 1));            // never the Journal
            Assert.Null(bot.ChooseAction(View(t), new[] { "delve:5:2" }));
            // Both ways out of 5 are held by more enemies than a convoy: no goal reachable, she needs VP.
            b.Warriors(1, "autumn.01", 3).Warriors(1, "autumn.06", 3);
            Assert.False(bot.Duplicates(View(t), 2));
        }

        // ------------------------------------------------------------ spoiler layer

        /// <summary>A stand-in seat whose win reports one missing unit at a key clearing.</summary>
        private sealed class ThreatModule : CharacterModule
        {
            private readonly string _id;
            public ThreatModule(string id, params ClearingId[] keys) { _id = id; Win = new ThreatWin(Data.Characters[id].WinCondition, keys); }
            public override string CharacterId => _id;
            public override IWinCondition Win { get; }
        }

        private sealed class ThreatWin : WinConditionBase, IGoalProgress
        {
            private readonly ClearingId[] _keys;
            public ThreatWin(string id, ClearingId[] keys) : base(id) { _keys = keys; }
            public int UnitsMissing(GameContext ctx, SeatId seat) => 1;
            public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat) => _keys;
        }

        /// <summary>Lune (0) with warriors in 8 (fox) and 7 (mouse); a one-step threat (1) with a warrior in its key clearing 8;
        /// a figure in forest 6 that she can delve from 7.</summary>
        private static (TestTable t, FakeBoard b) SpoilerTable(string threat, EngineId engine, int act, Difficulty d)
        {
            var b = FakeBoard.Autumn().Seat(0, EngineId.Keepers, CharacterIds.Lune, bot: true).Seat(1, engine, threat)
                .Warriors(0, "autumn.08", 2).Warriors(0, "autumn.07", 1).Warriors(1, "autumn.08", 1).Vp(1, 10)
                .Relic(2, Figure, 2, 6).Hand(0, Suit.Fox);
            var reg = Registry().Register(threat, () => new ThreatModule(threat, new ClearingId(8)));
            return (Start(b, act, d, reg), b);
        }

        [Fact]
        public void LuneBot_SpoilerNormalAnswersOneStepWithFreeActions()
        {
            var (t, _) = SpoilerTable(CharacterIds.Paintress, EngineId.Eyrie, 2, Difficulty.Normal);
            var bot = new LuneBot();
            var legal = new[] { "delve:7:2", "retinue:battle-delve:fox" };
            var answer = bot.Spoiler(View(t), legal);
            Assert.NotNull(answer);
            Assert.Equal(S(1), answer.Threat.Seat);
            Assert.Equal("retinue:battle-delve:fox", answer.ActionKey);
            Assert.False(answer.Preempts);
            Assert.Equal("delve:7:2", bot.ChooseAction(View(t), legal));                        // the steps go first
            Assert.Equal("retinue:battle-delve:fox", bot.ChooseAction(View(t), new[] { "retinue:battle-delve:fox", "retinue:move:mouse" })); // free action
            // A card she could not resolve is never the answer (no warriors of hers in a matching key clearing).
            Assert.Null(bot.Spoiler(View(t), new[] { "retinue:battle-delve:rabbit" }));
            // Hard: a one-step threat pre-empts the steps.
            var (h, _) = SpoilerTable(CharacterIds.Paintress, EngineId.Eyrie, 2, Difficulty.Hard);
            Assert.True(bot.Spoiler(View(h), legal).Preempts);
            Assert.Equal("retinue:battle-delve:fox", bot.ChooseAction(View(h), legal));
            // Easy answers banners only.
            var (e, _) = SpoilerTable(CharacterIds.Paintress, EngineId.Eyrie, 2, Difficulty.Easy);
            Assert.Null(bot.Spoiler(View(e), legal));
        }

        [Fact]
        public void LuneBot_SpoilerIgnoresWinsLockedInCurrentAct()
        {
            var bot = new LuneBot();
            var legal = new[] { "retinue:battle-delve:fox" };
            var (a1, _) = SpoilerTable(CharacterIds.Paintress, EngineId.Eyrie, 1, Difficulty.Hard);
            Assert.Null(bot.Spoiler(View(a1), legal));
            // The Curator's win opens only in Act III.
            var (c2, _) = SpoilerTable(CharacterIds.Curator, EngineId.Duchy, 2, Difficulty.Hard);
            Assert.Null(bot.Spoiler(View(c2), legal));
            var (c3, _) = SpoilerTable(CharacterIds.Curator, EngineId.Duchy, 3, Difficulty.Hard);
            Assert.Equal("retinue:battle-delve:fox", bot.Spoiler(View(c3), legal)?.ActionKey);
        }
    }
}
