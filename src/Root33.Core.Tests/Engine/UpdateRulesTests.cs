using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.Engine
{
    /// <summary>The internal update-rules switch (builder-questions C1) and the framework side of the A1-A3 sheet fixes.</summary>
    public class UpdateRulesTests
    {
        private static FakeBoard Plain() => FakeBoard.Autumn().Seat(0, EngineId.Marquise).Seat(1, EngineId.Eyrie);

        [Fact]
        public void UpdateRules_OffByDefaultAndOnlyV11RowsWait()
        {
            var t = TestTable.Start(Plain());
            Assert.False(new GameOptions().UpdateRules);
            Assert.False(t.Ctx.UpdateRules);
            Assert.DoesNotContain(Data.Options.Keys, k => k.Contains("update")); // never a player-facing option
            foreach (var row in Data.RuleTweaks.Values) Assert.Equal(row.Version == "v1", t.Ctx.RuleOn(row.Id));
            Assert.Equal(6, Data.RuleTweaks.Values.Count(r => r.Version == "v1.1"));

            var on = TestTable.Start(Plain(), updateRules: true);
            Assert.All(Data.RuleTweaks.Values, row => Assert.True(on.Ctx.RuleOn(row.Id)));
            Assert.True(on.SaveAndLoad().Ctx.UpdateRules); // the same build loads the save
        }

        [Fact]
        public void Options_GameKeepsFaithfulGustaveAndBarrierItStartedWith()
        {
            var t = TestTable.Start(Plain(), spoilers: SpoilerMode.Finished,
                beforeStart: g => { g.NextGameOptions.FaithfulGustave = true; g.NextGameOptions.MonolithBarrier = true; });
            Assert.True(t.State.GlobalFaithfulGustave);
            Assert.True(t.State.GlobalMonolithBarrier);

            // A change to the settings applies from the next game, not to this one or to its save.
            t.Game.NextGameOptions.FaithfulGustave = false;
            t.Game.NextGameOptions.MonolithBarrier = false;
            Assert.True(t.Ctx.Options.FaithfulGustave);
            Assert.True(t.Ctx.Options.MonolithBarrier);
            var loaded = t.SaveAndLoad();
            Assert.True(loaded.Ctx.Options.FaithfulGustave);
            Assert.True(loaded.Ctx.Options.MonolithBarrier);

            // A save without the rows loads them as false.
            var old = t.SaveAndLoad(edit: x => string.Join("\n", x.Split('\n').Where(l =>
                !l.StartsWith(Ids.State.GlobalFaithfulGustave + "=") && !l.StartsWith(Ids.State.GlobalMonolithBarrier + "="))));
            Assert.False(old.Ctx.Options.FaithfulGustave);
            Assert.False(old.Ctx.Options.MonolithBarrier);

            // Safe mode never copies Faithful Gustave.
            var safe = TestTable.Start(Plain(), beforeStart: g => g.NextGameOptions.FaithfulGustave = true);
            Assert.False(safe.State.GlobalFaithfulGustave);
        }

        [Fact]
        public void StoryEvents_OwnerColumnNamesWhoseEventItIs()
        {
            foreach (var e in Data.StoryEvents.Values)
            {
                if (!string.IsNullOrEmpty(e.Spawn)) Assert.Equal(Data.Spawns[e.Spawn].Owner, e.Owner);
                Assert.Equal(string.IsNullOrEmpty(e.Owner) ? null : e.Owner, Root33.Core.Engine.StoryEvents.OwnerOf(e));
            }
            Assert.Equal(CharacterIds.Gustave, Data.StoryEvents[Ids.StoryEvents.Act2Beach].Owner);
            Assert.Equal(CharacterIds.Maelle, Data.StoryEvents[Ids.StoryEvents.Act3Alicia].Owner);
            Assert.Equal(CharacterIds.Monoco, Data.StoryEvents[Ids.StoryEvents.Act3Almighty].Owner);
            Assert.Null(Data.StoryEvents[Ids.StoryEvents.Act1Landing].Owner);
        }

        [Fact]
        public void StoryEvents_GustavesModuleRetitlesAtTheBeach()
        {
            var reg = new ModuleRegistry().Register(CharacterIds.Gustave, () => new Root33.Core.Characters.Gustave.GustaveModule());
            var b = FakeBoard.Autumn().Seat(0, EngineId.Marquise, CharacterIds.Gustave).Seat(1, EngineId.Eyrie);
            var t = TestTable.Start(b, reg, spoilers: SpoilerMode.Finished, beforeStart: g => g.NextGameOptions.FaithfulGustave = true);
            b.SeatTitles.Clear();
            t.Game.ModuleOf(CharacterIds.Gustave).OnStoryEvent(Data.StoryEvents[Ids.StoryEvents.Act2Beach]);
            Assert.Equal(SpoilerText.FaithfulGustaveTitle, b.SeatTitles[new SeatId(0)]);

            // Faithful Gustave off: the module leaves the title alone (the plugin plays the vignette from the story log).
            var b2 = FakeBoard.Autumn().Seat(0, EngineId.Marquise, CharacterIds.Gustave).Seat(1, EngineId.Eyrie);
            var t2 = TestTable.Start(b2, reg, spoilers: SpoilerMode.Finished);
            b2.SeatTitles.Clear();
            t2.Game.ModuleOf(CharacterIds.Gustave).OnStoryEvent(Data.StoryEvents[Ids.StoryEvents.Act2Beach]);
            Assert.False(b2.SeatTitles.ContainsKey(new SeatId(0)));
        }

        [Fact]
        public void AutumnMap_RiverLinksAndCornerPairsComeFromTheSheet()
        {
            foreach (var r in Data.MapAutumn.Values)
            {
                foreach (var l in r.RiverLinks) Assert.Contains(r.Id, Data.MapAutumn[l].RiverLinks); // links go both ways
                if (r.OppositeCorner != null) Assert.Equal(r.Id, Data.MapAutumn[r.OppositeCorner].OppositeCorner);
            }
            var b = FakeBoard.Autumn();
            Assert.Equal(new[] { 6, 10 }, b.RiverNeighbours(new ClearingId(7)).Select(c => c.Value).OrderBy(x => x));
            Assert.Equal(new ClearingId(3), b.OppositeCorner(new ClearingId(1)));
            Assert.Equal(new ClearingId(4), b.OppositeCorner(new ClearingId(2)));
            Assert.Null(b.OppositeCorner(new ClearingId(5)));
        }
    }
}
