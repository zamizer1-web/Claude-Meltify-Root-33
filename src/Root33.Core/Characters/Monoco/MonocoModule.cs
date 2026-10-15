using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Monoco
{
    /// <summary>
    /// Monoco (characters.json 'monoco', Lord of the Hundreds): win.monoco (The Bestial Wheel), the feet of state.monoco.feet,
    /// tweak.monoco.sacred-river-rebirth and bot.monoco. Monoco has no spawns rows.
    /// </summary>
    public sealed class MonocoModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Monoco;
        public override IWinCondition Win { get; } = new MonocoWin();
        public override IBotDirector Bot { get; } = new MonocoBot();

        public override void OnBattleResolved(BattleResult r) => MonocoTweaks.EarnFeet(Ctx, Seat, r);

        public override void OnWarlordRemoved(SeatId hundreds, ClearingId c) => MonocoTweaks.SacredRiverRebirth(Ctx, Seat, hundreds);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => MonocoTweaks.CheckOnLoad(Ctx);

        /// <summary>ui.monoco.ending when The Bestial Wheel was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Monoco))
                Actions.ShowOverlay(Ids.Overlays.MonocoEnding, Seat, null, null);
        }
    }
}
