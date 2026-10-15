using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Verso
{
    /// <summary>
    /// Verso (characters.json 'verso', Ranger Vagabond): win.verso (The Last Note), tweak.verso.perfection and bot.verso.
    /// tweak.verso.soarrie-handoff is carried out by Esquie's module (spawn.soarrie), so this module does nothing for it.
    /// state.verso.rank starts at 0 (D) and is clamped on load.
    /// </summary>
    public sealed class VersoModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Verso;
        public override IWinCondition Win { get; } = new VersoWin();
        public override IBotDirector Bot { get; } = new VersoBot();

        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) => VersoTweaks.Perfection(Ctx, Seat, attacker);

        public override void OnBattleResolved(BattleResult r) => VersoTweaks.UpdateRank(Ctx, Seat, r);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => VersoTweaks.ClampOnLoad(Ctx);
    }
}
