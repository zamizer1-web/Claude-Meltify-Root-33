using System;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.Options
{
    /// <summary>sheets/options.json: values, defaults, visibility and when each option takes effect (sys.settings).</summary>
    public class OptionsTests
    {
        [Fact]
        public void Options_DefaultsMatchTheSheet()
        {
            var plain = new GameOptions();
            var fromSheet = OptionRules.Defaults();
            foreach (var row in Data.Options.Values)
            {
                Assert.Contains(row.Default, row.Values);
                Assert.Equal(row.Default, OptionRules.Get(plain, row.Id));
                Assert.Equal(row.Default, OptionRules.Get(fromSheet, row.Id));
            }
        }

        [Fact]
        public void Options_EverySheetValueRoundTripsAndOthersAreRefused()
        {
            foreach (var row in Data.Options.Values)
            {
                var o = new GameOptions();
                foreach (var v in row.Values)
                {
                    OptionRules.Set(o, row.Id, v);
                    Assert.Equal(v, OptionRules.Get(o, row.Id));
                }
                Assert.Throws<ArgumentException>(() => OptionRules.Set(o, row.Id, "maybe"));
            }
            Assert.Throws<ArgumentException>(() => OptionRules.Set(new GameOptions(), "opt.nope", "on"));
        }

        [Fact]
        public void Options_FaithfulGustaveIsHiddenAndOffInSafeMode()
        {
            Assert.False(OptionRules.Visible(Ids.Options.FaithfulGustave, SpoilerMode.Safe));
            Assert.True(OptionRules.Visible(Ids.Options.FaithfulGustave, SpoilerMode.Finished));
            Assert.All(Data.Options.Keys.Where(k => k != Ids.Options.FaithfulGustave), k => Assert.True(OptionRules.Visible(k, SpoilerMode.Safe)));
            var o = new GameOptions { FaithfulGustave = true };
            Assert.False(o.FaithfulGustaveActive(SpoilerMode.Safe));
            Assert.True(o.FaithfulGustaveActive(SpoilerMode.Finished));
        }

        [Fact]
        public void Options_SpoilerModeChangeAppliesFromTheNextGame()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true));
            t.Game.AnswerSpoilerPrompt(SpoilerMode.Finished);
            Assert.Equal(SpoilerMode.Safe, t.Ctx.Spoilers);
            Assert.Equal(SpoilerMode.Finished, t.Game.NextGameOptions.Spoilers);
            Assert.Equal(SpoilerMode.Finished, t.Game.ProfileSpoilerMode);
            Assert.False(t.Board.OverlayShown(Ids.Overlays.GlobalSpoilerPrompt));
        }

        [Fact]
        public void Options_SpoilerPromptOnlyOnFirstLaunch()
        {
            var board = FakeBoard.Autumn();
            var game = new Root33Game(board, board, new ModuleRegistry());
            game.OnTitleScreenShown(true);
            Assert.True(board.OverlayShown(Ids.Overlays.GlobalTitle));
            Assert.True(board.OverlayShown(Ids.Overlays.GlobalSpoilerPrompt));
            game.AnswerSpoilerPrompt(SpoilerMode.Safe);
            game.OnTitleScreenShown(false);
            Assert.False(board.OverlayShown(Ids.Overlays.GlobalSpoilerPrompt));
        }

        [Fact]
        public void Options_DifficultyAndSpoilerModeAreCopiedIntoTheGame()
        {
            var t = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true),
                difficulty: Difficulty.Hard, spoilers: SpoilerMode.Finished);
            Assert.Equal(Difficulty.Hard, t.State.GlobalDifficulty);
            Assert.Equal(SpoilerMode.Finished, t.State.GlobalSpoilerMode);
            Assert.Equal(Difficulty.Hard, new BotView(t.Ctx, new SeatId(1)).Difficulty);
        }
    }
}
