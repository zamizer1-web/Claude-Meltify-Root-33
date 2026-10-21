using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Tests.State
{
    /// <summary>ModState: sheet initial values and the save format (sys.saved-state).</summary>
    public class StateTests
    {
        private static ClearingId Cl(int i) => new ClearingId(i);
        private static SeatId S(int i) => new SeatId(i);

        /// <summary>A state with every field away from its initial value, including strings holding the format's separators.</summary>
        private static ModState Busy()
        {
            var m = new ModState();
            m.GlobalAct = 3;
            m.GlobalRound = 9;
            m.GlobalCountdowns = new HashSet<SeatId> { S(2), S(0) };
            m.GlobalSpoilerMode = SpoilerMode.Finished;
            m.GlobalDifficulty = Difficulty.Hard;
            m.GlobalSeatCharacters = new Dictionary<SeatId, string> { [S(0)] = "gustave", [S(1)] = "white-haired-man", [S(3)] = "odd;name=x|y,z%~" };
            m.SetRoles(new Dictionary<RoleId, IReadOnlyList<ClearingId>>
            {
                [new RoleId(Ids.MapRoles.Lumiere)] = new[] { Cl(4) },
                [new RoleId(Ids.MapRoles.Sirene)] = new ClearingId[0],
                [new RoleId(Ids.MapRoles.SacredRiver)] = new[] { Cl(1), Cl(6), Cl(11) },
            });
            m.GlobalE33Mode = true;
            m.GlobalCampaignGame = 2;
            m.GlobalCampaignJournal = true;
            m.GlobalPendingVp = new Dictionary<SeatId, int> { [S(1)] = 2 };
            m.GlobalPendingDraws = new Dictionary<SeatId, int> { [S(3)] = 1, [S(0)] = 4 };
            m.GustaveFlags.Add(Cl(5));
            m.GustaveArm = 3;
            m.GustaveOverchargeArmed = true;
            m.GustaveRoute = new[] { Cl(4), Cl(12), Cl(8) };
            m.EsquieRocks[ModState.Florrie] = new RockState { Status = RockStatus.Guarded, Clearing = Cl(6), GuardsLeft = 2 };
            m.EsquieRocks[ModState.Soarrie] = new RockState { Status = RockStatus.WithVerso, HostSeat = S(2) };
            m.EsquieRocks[ModState.Dorrie] = new RockState { Status = RockStatus.Held };
            m.EsquieDecoy = true;
            m.EsquieTarget = 3;
            m.PaintressStillLifeUsed = true;
            m.PaintressRepaintAvoid = new Dictionary<ClearingId, int> { [Cl(7)] = 2, [Cl(2)] = 1 };
            m.ScielSun = true;
            m.ScielMoon = true;
            m.ScielTwilightArmed = true;
            m.VersoRank = 4;
            m.MaelleStance = "virtuose";
            m.MaelleStanceTurn = new HashSet<string> { "battled", "aided" };
            m.MaelleFamily = new HashSet<SeatId> { S(1), S(3) };
            m.LuneTaggedRelic = 7;
            m.LuneStains["ice"] = 2;
            m.LuneStainArmed = "fire";
            m.MonocoFeet[ModState.Agile] = new FootState { Earned = true, Faction = S(1), Order = 1 };
            m.MonocoFeet[ModState.Almighty] = new FootState { Earned = true, Faction = S(0), Order = 2 };
            m.CuratorCellCrack = "dig-owed";
            m.CleaCovered = new HashSet<ClearingId> { Cl(3), Cl(9) };
            m.WhiteHairedManBarrierDown = true;
            return m;
        }

        [Fact]
        public void ModState_SavesOneEntryPerStateRow()
        {
            Assert.Equal(Data.State.Keys.OrderBy(k => k), ModState.SavedKeys.OrderBy(k => k));
            Assert.Equal(Data.State.Count, new ModState().Save().Count);
        }

        [Fact]
        public void ModState_ResetSetsEverySheetInitialValue()
        {
            var m = Busy();
            m.Reset();
            Assert.Equal(1, m.GlobalAct);
            Assert.Equal(1, m.GlobalRound);
            Assert.Empty(m.GlobalCountdowns);
            Assert.Equal(SpoilerMode.Safe, m.GlobalSpoilerMode);
            Assert.Equal(Difficulty.Normal, m.GlobalDifficulty);
            Assert.Empty(m.GlobalSeatCharacters);
            Assert.Empty(m.GlobalRoles);
            Assert.False(m.GlobalE33Mode);
            Assert.Equal(0, m.GlobalCampaignGame);
            Assert.False(m.GlobalCampaignJournal);
            Assert.Empty(m.GlobalPendingVp);
            Assert.Empty(m.GlobalPendingDraws);
            Assert.Empty(m.GustaveFlags);
            Assert.Equal(0, m.GustaveArm);
            Assert.False(m.GustaveOverchargeArmed);
            Assert.Empty(m.GustaveRoute);
            Assert.Equal(Tuning.EsquieRocks, m.EsquieRocks.Length);
            Assert.All(m.EsquieRocks, r => { Assert.Equal(RockStatus.NotSpawned, r.Status); Assert.Null(r.Clearing); Assert.Null(r.HostSeat); Assert.Equal(0, r.GuardsLeft); });
            Assert.False(m.EsquieDecoy);
            Assert.Null(m.EsquieTarget);
            Assert.False(m.PaintressStillLifeUsed);
            Assert.Empty(m.PaintressRepaintAvoid);
            Assert.False(m.ScielSun);
            Assert.False(m.ScielMoon);
            Assert.False(m.ScielTwilightArmed);
            Assert.Equal(0, m.VersoRank);
            Assert.Equal("none", m.MaelleStance);
            Assert.Empty(m.MaelleStanceTurn);
            Assert.Empty(m.MaelleFamily);
            Assert.Null(m.LuneTaggedRelic);
            Assert.Equal(new[] { "fire", "ice", "lightning" }, m.LuneStains.Keys.OrderBy(k => k));
            Assert.All(m.LuneStains.Values, v => Assert.Equal(0, v));
            Assert.Null(m.LuneStainArmed);
            Assert.Equal(Tuning.MonocoFeet, m.MonocoFeet.Length);
            Assert.All(m.MonocoFeet, f => { Assert.False(f.Earned); Assert.Null(f.Faction); Assert.Equal(0, f.Order); });
            Assert.Equal("none", m.CuratorCellCrack);
            Assert.Empty(m.CleaCovered);
            Assert.False(m.WhiteHairedManBarrierDown);

            // On Autumn the roles start as the fixed roles, and Gustave's flags hold Lumière.
            m.Reset("autumn");
            Assert.Equal(AutumnMap.FixedRoles().Count, m.GlobalRoles.Count);
            Assert.Equal(new[] { AutumnMap.ClearingOf("autumn.01") }, m.GustaveFlags.ToArray());
        }

        [Fact]
        public void ModState_SaveLoadRoundTripsEveryField()
        {
            var original = Busy();
            var saved = original.Save();
            var copy = new ModState();
            var loaded = copy.Load(saved);
            Assert.Equal(saved.Count, loaded.Count);
            Assert.Equal(saved, copy.Save());

            Assert.Equal("odd;name=x|y,z%~", copy.GlobalSeatCharacters[S(3)]);
            Assert.Equal(new[] { Cl(4), Cl(12), Cl(8) }, copy.GustaveRoute);
            Assert.Empty(copy.GlobalRoles[new RoleId(Ids.MapRoles.Sirene)]);
            Assert.Equal(RockStatus.Guarded, copy.EsquieRocks[ModState.Florrie].Status);
            Assert.Equal(Cl(6), copy.EsquieRocks[ModState.Florrie].Clearing);
            Assert.Null(copy.EsquieRocks[ModState.Florrie].HostSeat);
            Assert.Equal(S(2), copy.EsquieRocks[ModState.Soarrie].HostSeat);
            Assert.Equal(2, copy.EsquieRocks[ModState.Florrie].GuardsLeft);
            Assert.Null(copy.EsquieRocks[ModState.Soarrie].Clearing);
            Assert.Equal(S(0), copy.MonocoFeet[ModState.Almighty].Faction);
            Assert.Equal("fire", copy.LuneStainArmed);
            Assert.Equal(3, copy.EsquieTarget);
            Assert.Equal(Difficulty.Hard, copy.GlobalDifficulty);
        }

        [Fact]
        public void ModState_TextRoundTripSurvivesSeparatorsInStrings()
        {
            var original = Busy();
            original.MaelleStance = "line1\nline2 = 100% ~";
            var text = original.ToText();
            Assert.StartsWith(ModState.TextHeader + "\n", text);
            Assert.Equal(Data.State.Count + 2, text.Split('\n').Length); // header, one line per row, trailing newline

            var copy = new ModState();
            copy.Load(ModState.FromText(text));
            Assert.Equal(original.Save(), copy.Save());
            Assert.Equal("line1\nline2 = 100% ~", copy.MaelleStance);
        }

        [Fact]
        public void ModState_LoadKeepsInitialValueForMissingOrBrokenEntries()
        {
            var saved = Busy().Save();
            saved.Remove(Ids.State.VersoRank);
            saved[Ids.State.GlobalAct] = "three";
            saved[Ids.State.EsquieRocks] = "held|~|~|0"; // one rock where four are required
            var m = new ModState();
            var loaded = m.Load(saved);
            Assert.DoesNotContain(Ids.State.VersoRank, loaded);
            Assert.DoesNotContain(Ids.State.GlobalAct, loaded);
            Assert.DoesNotContain(Ids.State.EsquieRocks, loaded);
            Assert.Equal(0, m.VersoRank);
            Assert.Equal(1, m.GlobalAct);
            Assert.All(m.EsquieRocks, r => Assert.Equal(RockStatus.NotSpawned, r.Status));
            Assert.Equal(3, m.EsquieTarget); // everything else still loads
            Assert.Empty(ModState.FromText(""));
            Assert.Empty(ModState.FromText("{\"not\":\"ours\"}"));
        }
    }
}
