using System;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Tests.Seating
{
    /// <summary>Character select and table seating (sys.table-seating, sys.dlc-detection, characters/engines/dlc sheets).</summary>
    public class SeatingTests
    {
        private static readonly string[] BaseOnly = new string[0];
        private static readonly string[] AllDlc = Data.Dlc.Keys.ToArray();

        [Fact]
        public void Seating_BaseGameTableIsTheSixFirstCharacters()
        {
            var seatable = Data.Characters.Keys.Where(c => TableSeating.Seatable(c, BaseOnly, SpoilerMode.Safe)).OrderBy(c => c).ToArray();
            Assert.Equal(new[] { "esquie", "gustave", "maelle", "paintress", "sciel", "verso" }, seatable);
        }

        [Fact]
        public void Seating_ACharacterNeedsItsEnginesDlc()
        {
            Assert.Equal(Ids.Dlc.Underworld, TableSeating.DlcOf("curator"));
            Assert.Contains(TableSeating.Problems(new[] { "gustave", "curator" }, BaseOnly, SpoilerMode.Safe), p => p.Contains("underworld"));
            Assert.True(TableSeating.IsLegal(new[] { "gustave", "curator" }, new[] { Ids.Dlc.Underworld }, SpoilerMode.Safe));
            // a missing DLC hides the card
            Assert.DoesNotContain(TableSeating.Cards(BaseOnly, SpoilerMode.Finished), c => c.CharacterId == "lune");
            Assert.Contains(TableSeating.Cards(new[] { Ids.Dlc.Marauder }, SpoilerMode.Finished), c => c.CharacterId == "lune" && c.Dlc == Ids.Dlc.Marauder);
        }

        [Fact]
        public void Seating_TwoToFourSeats()
        {
            Assert.NotEmpty(TableSeating.Problems(new[] { "gustave" }, BaseOnly, SpoilerMode.Safe));
            Assert.True(TableSeating.IsLegal(new[] { "gustave", "paintress", "sciel", "esquie" }, BaseOnly, SpoilerMode.Safe));
            Assert.NotEmpty(TableSeating.Problems(new[] { "gustave", "paintress", "sciel", "esquie", "verso" }, BaseOnly, SpoilerMode.Safe));
            Assert.NotEmpty(TableSeating.Problems(new[] { "gustave", "gustave" }, BaseOnly, SpoilerMode.Safe));
            Assert.Throws<ArgumentOutOfRangeException>(() => TableSeating.FillTable("gustave", 5, BaseOnly, SpoilerMode.Safe));
        }

        [Fact]
        public void Seating_AtMostTwoVagabondCharactersWithoutAnyDlc()
        {
            Assert.True(TableSeating.IsLegal(new[] { "esquie", "verso" }, BaseOnly, SpoilerMode.Safe));
            Assert.True(TableSeating.IsLegal(new[] { "verso", "maelle", "gustave" }, BaseOnly, SpoilerMode.Safe));
            Assert.Contains(TableSeating.Problems(new[] { "esquie", "verso", "maelle" }, BaseOnly, SpoilerMode.Safe), p => p.Contains("Vagabond"));
            var t = TableSeating.FillTable("verso", 4, BaseOnly, SpoilerMode.Safe, new[] { "verso", "maelle", "esquie", "gustave" });
            Assert.Equal(new[] { "verso", "maelle", "gustave" }, t);
        }

        [Fact]
        public void Seating_FillTableSeatsALoreRivalOfTheHumansPick()
        {
            Assert.Equal(new[] { "gustave", "paintress" }, TableSeating.FillTable("gustave", 2, BaseOnly, SpoilerMode.Safe));
            Assert.Equal(new[] { "verso", "maelle" }, TableSeating.FillTable("verso", 2, BaseOnly, SpoilerMode.Safe));
            Assert.Equal(new[] { "white-haired-man", "gustave" }, TableSeating.FillTable("white-haired-man", 2, AllDlc, SpoilerMode.Safe));
            var four = TableSeating.FillTable("sciel", 4, BaseOnly, SpoilerMode.Safe);   // no rival: build order
            Assert.Equal(new[] { "sciel", "paintress", "maelle", "gustave" }, four);
            Assert.True(TableSeating.IsLegal(four, BaseOnly, SpoilerMode.Safe));
        }

        [Fact]
        public void Seating_CleaIsALockedCardAndNeverSeatedInSafeMode()
        {
            var card = TableSeating.Cards(AllDlc, SpoilerMode.Safe).Single(c => c.CharacterId == "clea");
            Assert.True(card.Locked);
            Assert.Equal("???", card.Name);
            Assert.Equal("", card.Summary);
            Assert.False(TableSeating.Seatable("clea", AllDlc, SpoilerMode.Safe));
            Assert.DoesNotContain("clea", TableSeating.FillTable("esquie", 4, AllDlc, SpoilerMode.Safe));
            Assert.Equal(new[] { "esquie", "clea" }, TableSeating.FillTable("esquie", 2, AllDlc, SpoilerMode.Finished));
            var fin = TableSeating.Cards(AllDlc, SpoilerMode.Finished).Single(c => c.CharacterId == "clea");
            Assert.False(fin.Locked);
            Assert.Equal("Clea", fin.Name);
        }

        [Fact]
        public void Seating_CardsShowSpoilerTieredNamesInBuildOrder()
        {
            var safe = TableSeating.Cards(BaseOnly, SpoilerMode.Safe);
            Assert.Equal(new[] { "paintress", "sciel", "maelle", "gustave", "esquie", "verso" }, safe.Select(c => c.CharacterId));
            Assert.Equal("The Paintress", safe[0].Name);
            Assert.Equal("Aline Dessendre", TableSeating.Cards(BaseOnly, SpoilerMode.Finished)[0].Name);
        }

        [Fact]
        public void Seating_LakeAndMountainNeedTheUnderworld()
        {
            Assert.True(TableSeating.MapAllowed("autumn", BaseOnly));
            Assert.True(TableSeating.MapAllowed("winter", BaseOnly));
            Assert.False(TableSeating.MapAllowed("lake", BaseOnly));
            Assert.False(TableSeating.MapAllowed("mountain", BaseOnly));
            Assert.True(TableSeating.MapAllowed("lake", new[] { Ids.Dlc.Underworld }));
        }
    }
}
