using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.StoryEvents
{
    /// <summary>sheets/story_events.json: owners, tiers and the shared effects of each act's events.</summary>
    public class StoryEventsTests
    {
        private static SeatId S(int i) => new SeatId(i);
        private static string[] Fired(TestTable t) => t.Ctx.StoryLog.Select(e => e.EventId).ToArray();

        /// <summary>Reaches Act III: 25 VP ends Act I at the first turn end and Act II at the next.</summary>
        private static TestTable ToActThree(TestTable t, int vpSeat = 0)
        {
            t.Board.Vp(vpSeat, 25);
            t.PlayTurn(0).PlayTurn(1);
            Assert.Equal(3, t.Ctx.Act);
            return t;
        }

        [Fact]
        public void StoryEvents_EveryActEventIsARowOfThatAct()
        {
            var listed = Data.Acts.Values.SelectMany(a => a.Events.Select(e => (act: a.Id, e))).ToList();
            Assert.Equal(Data.StoryEvents.Count, listed.Count);
            foreach (var (act, e) in listed) Assert.Equal(act, Data.StoryEvents[e].Act);
            foreach (var e in Data.StoryEvents.Values.Where(e => e.Kind == "spawn")) Assert.False(string.IsNullOrEmpty(e.Spawn), e.Id);
        }

        [Fact]
        public void StoryEvents_OwnerComesFromTheSpawnOrTheSeatedClause()
        {
            string Owner(string id) => Root33.Core.Engine.StoryEvents.OwnerOf(Data.StoryEvents[id]);
            Assert.Equal("esquie", Owner(Ids.StoryEvents.Act1Florrie));
            Assert.Equal("lune", Owner(Ids.StoryEvents.Act2ParentsJournal));
            Assert.Equal("curator", Owner(Ids.StoryEvents.Act3CellCrack));
            Assert.Equal("gustave", Owner(Ids.StoryEvents.Act2Beach));
            Assert.Equal("maelle", Owner(Ids.StoryEvents.Act3Alicia));
            Assert.Equal("monoco", Owner(Ids.StoryEvents.Act3Almighty));
            Assert.Null(Owner(Ids.StoryEvents.Act1Landing));
            Assert.Null(Owner(Ids.StoryEvents.Act2UnlockWins));
            Assert.Null(Owner(Ids.StoryEvents.Act3Fall));
        }

        [Fact]
        public void StoryEvents_BeachFiresOnlyInFinishedModeWithGustaveSeated()
        {
            FakeBoard WithGustave() => FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true);
            var finished = TestTable.Start(WithGustave(), spoilers: SpoilerMode.Finished);
            finished.Board.Vp(1, 10);
            finished.PlayTurn(0);
            Assert.Contains(Ids.StoryEvents.Act2Beach, Fired(finished));

            var safe = TestTable.Start(WithGustave(), spoilers: SpoilerMode.Safe);
            safe.Board.Vp(1, 10);
            safe.PlayTurn(0);
            Assert.Equal(2, safe.Ctx.Act);
            Assert.DoesNotContain(Ids.StoryEvents.Act2Beach, Fired(safe));

            var noGustave = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Alliance, "sciel").Seat(1, EngineId.Eyrie, "paintress", bot: true),
                spoilers: SpoilerMode.Finished);
            noGustave.Board.Vp(1, 10);
            noGustave.PlayTurn(0);
            Assert.DoesNotContain(Ids.StoryEvents.Act2Beach, Fired(noGustave));
            Assert.Contains(Ids.StoryEvents.Act2UnlockWins, Fired(noGustave));
        }

        [Fact]
        public void StoryEvents_PortraitAndUnlockEventsNeedTheirCharacter()
        {
            var t = ToActThree(TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true)));
            Assert.Contains(Ids.StoryEvents.Act3Fall, Fired(t));
            Assert.DoesNotContain(Ids.StoryEvents.Act3Alicia, Fired(t));
            Assert.DoesNotContain(Ids.StoryEvents.Act3Almighty, Fired(t));
            var m = ToActThree(TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondThief, "maelle", bot: true)));
            Assert.Contains(Ids.StoryEvents.Act3Alicia, Fired(m));
        }

        [Fact]
        public void StoryEvents_LandingShowsTheActBannerVignetteAndMonolithNumber()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            Assert.Equal(Ids.StoryEvents.Act1Landing, Fired(t)[0]);
            Assert.True(t.Board.OverlayShown(Ids.Overlays.GlobalActBanner));
            Assert.True(t.Board.OverlayShown(Ids.Overlays.GlobalVignette));
            Assert.True(t.Board.OverlayShown(Ids.Overlays.GlobalMonolithNumber));
            Assert.False(t.Board.OverlayShown(Ids.Overlays.GlobalFinalBattlefield));
        }

        [Fact]
        public void StoryEvents_FallMarksLumiereAsTheFinalBattlefield()
        {
            var t = ToActThree(TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true)));
            Assert.True(t.Board.OverlayShown(Ids.Overlays.GlobalFinalBattlefield));
            var lumiere = t.Ctx.Role(Ids.MapRoles.Lumiere).Value;
            Assert.Contains(t.Board.Overlays, o => o.StartsWith(Ids.Overlays.GlobalFinalBattlefield + "@") && o.Contains(lumiere.Value.ToString()));
        }

        [Fact]
        public void StoryEvents_FaithfulGustaveRetitlesHisSeatFromActTwoInFinishedModeOnly()
        {
            foreach (var mode in new[] { SpoilerMode.Finished, SpoilerMode.Safe })
            {
                var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true),
                    spoilers: mode, beforeSetup: g => g.Context.Options.FaithfulGustave = true);
                Assert.Equal("Gustave", t.Board.SeatTitles[S(0)]);
                t.Board.Vp(1, 10);
                t.PlayTurn(0);
                Assert.Equal(mode == SpoilerMode.Finished ? SpoilerText.FaithfulGustaveTitle : "Gustave", t.Board.SeatTitles[S(0)]);
            }
        }

        [Fact]
        public void StoryEvents_TrueNamesFollowSpoilerModeAndRevealEvents()
        {
            var safe = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Eyrie, "paintress").Seat(1, EngineId.VagabondThief, "maelle", bot: true));
            Assert.Equal("The Paintress", safe.Board.SeatTitles[S(0)]);
            Assert.Equal("Maelle", safe.Board.SeatTitles[S(1)]);

            var fin = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Eyrie, "paintress").Seat(1, EngineId.VagabondThief, "maelle", bot: true),
                spoilers: SpoilerMode.Finished);
            Assert.Equal("Aline Dessendre", fin.Board.SeatTitles[S(0)]);
            Assert.Equal("Maelle", fin.Board.SeatTitles[S(1)]);                     // until story.act3.alicia
            ToActThree(fin);
            Assert.Equal("Alicia Dessendre", fin.Board.SeatTitles[S(1)]);

            fin.Board.SeatTitles.Clear();
            var loaded = fin.SaveAndLoad();
            Assert.True(Root33.Core.Engine.StoryEvents.HasFired(loaded.Ctx, Ids.StoryEvents.Act3Alicia));
            Assert.Equal("Alicia Dessendre", loaded.Board.SeatTitles[S(1)]);
        }

        [Fact]
        public void StoryEvents_SharedEffectsNeverChangeVpOrRelationships()
        {
            var t = ToActThree(TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.VagabondThief, "maelle", bot: true),
                spoilers: SpoilerMode.Finished));
            Assert.Empty(t.Board.ScoredVp);
            Assert.DoesNotContain(t.Board.Log, l => l.StartsWith("ScoreVp") || l.Contains("Relationship"));
        }
    }
}
