using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Overlays
{
    /// <summary>sheets/overlays.json as data the UI reads: owners, public flags, the state each overlay reads.</summary>
    public class OverlaysTests
    {
        [Fact]
        public void Overlays_EveryOwnerIsGlobalOrACharacterAndEveryStateRefIsAStateRow()
        {
            foreach (var o in OverlayCatalog.All)
            {
                Assert.True(o.Owner == OverlayCatalog.GlobalOwner || Data.Characters.ContainsKey(o.Owner), o.Id);
                Assert.StartsWith("ui." + o.Owner + ".", o.Id);
                foreach (var s in o.State ?? new string[0]) Assert.True(Data.State.ContainsKey(s), o.Id + " reads " + s);
                foreach (var a in o.Art ?? new string[0]) Assert.True(Data.Assets.ContainsKey(a), o.Id + " draws " + a);
            }
        }

        [Fact]
        public void Overlays_GoalTrackerAndBannersArePublic()
        {
            Assert.True(OverlayCatalog.Get(Ids.Overlays.GlobalGoalTracker).Public);
            Assert.True(OverlayCatalog.Get(Ids.Overlays.GlobalCountdownBanner).Public);
            Assert.Equal(OverlayCatalog.All.Count(o => o.Public), OverlayCatalog.PublicOverlays.Count);
        }

        [Fact]
        public void Overlays_ReadingListsWhatToRedrawWhenAStateRowChanges()
        {
            var countdowns = OverlayCatalog.Reading(Ids.State.GlobalCountdowns).Select(o => o.Id).ToList();
            Assert.Contains(Ids.Overlays.GlobalCountdownBanner, countdowns);
            Assert.Contains(Ids.Overlays.GlobalGoalTracker, countdowns);
            Assert.Contains(Ids.Overlays.GustaveArm, OverlayCatalog.Reading(Ids.State.GustaveArm).Select(o => o.Id));
            Assert.Empty(OverlayCatalog.Reading("state.nobody.nothing"));
        }

        [Fact]
        public void Overlays_ForTableHasGlobalOverlaysAndSeatedCharactersOnly()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            var ids = OverlayCatalog.ForTable(t.Ctx).Select(o => o.Id).ToList();
            Assert.Contains(Ids.Overlays.GlobalTitle, ids);
            Assert.Contains(Ids.Overlays.GustaveFlags, ids);
            Assert.Contains(Ids.Overlays.PaintressPortraits, ids);
            Assert.DoesNotContain(ids, id => id.StartsWith("ui.esquie."));
            Assert.True(OverlayCatalog.OnTable(t.Ctx, Ids.Overlays.GustaveArm));
            Assert.False(OverlayCatalog.OnTable(t.Ctx, Ids.Overlays.EsquieRocks));
        }

        [Fact]
        public void Overlays_StateForReturnsTheSavedValuesItReads()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            t.State.GustaveArm = 2;
            var arm = OverlayCatalog.StateFor(t.Ctx, Ids.Overlays.GustaveArm);
            Assert.Equal(new[] { Ids.State.GustaveArm, Ids.State.GustaveOverchargeArmed }.OrderBy(x => x), arm.Keys.OrderBy(x => x));
            Assert.Equal("2", arm[Ids.State.GustaveArm]);
            Assert.Equal("1", OverlayCatalog.StateFor(t.Ctx, Ids.Overlays.GlobalActBanner)[Ids.State.GlobalAct]);
            Assert.Empty(OverlayCatalog.StateFor(t.Ctx, Ids.Overlays.GlobalTitle));
        }
    }
}
