using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Lune
{
    /// <summary>
    /// Lune (characters.json 'lune', Keepers in Iron): win.lune (Her Parents' Work), tweak.lune.stains,
    /// spawn.parents-journal (story.act2.parents-journal), state.lune.* and bot.lune.
    /// </summary>
    public sealed class LuneModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Lune;
        public override IWinCondition Win { get; } = new LuneWin();
        public override IBotDirector Bot => Director;

        public LuneBot Director { get; } = new LuneBot();

        /// <summary>The public thesis tracker and Stains gauge from setup.</summary>
        public override void OnSetupComplete()
        {
            Actions.ShowOverlay(Ids.Overlays.LuneThesis, Seat, null, null);
            Actions.ShowOverlay(Ids.Overlays.LuneStains, Seat, null, null);
        }

        /// <summary>story.act2.parents-journal (hook.act-change into Act II): tag the Parents' Journal.</summary>
        public override void OnStoryEvent(StoryEventDef storyEvent)
        {
            if (storyEvent.Id == Ids.StoryEvents.Act2ParentsJournal && !State.LuneTaggedRelic.HasValue) LuneTweaks.TagParentsJournal(Ctx, Seat);
        }

        public override void OnRelicRecovered(SeatId keepers, int relicId, string relicType) => LuneTweaks.Recovered(Ctx, Seat, keepers, relicId, relicType);

        public override void OnRelicMoved(int relicId) => LuneTweaks.Moved(Ctx, Seat, relicId);

        public override void OnToggle(SeatId seat, string toggleId, bool on) => LuneTweaks.Toggle(Ctx, Seat, seat, toggleId, on);

        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) => LuneTweaks.BattleStarting(Ctx, Seat, attacker, defender);

        public override void OnBattleResolved(BattleResult r) => LuneTweaks.BattleResolved(Ctx, Seat, r);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => LuneTweaks.RecheckOnLoad(Ctx, Seat, loadedKeys);

        /// <summary>ui.lune.ending when Her Parents' Work was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Lune))
                Actions.ShowOverlay(Ids.Overlays.LuneEnding, Seat, null, null);
        }
    }
}
