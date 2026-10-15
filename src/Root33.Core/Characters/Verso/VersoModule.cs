using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Verso
{
    /// <summary>
    /// Verso (characters.json 'verso', Ranger Vagabond): win.verso (The Last Note), tweak.verso.perfection,
    /// tweak.verso.soarrie-handoff and bot.verso. state.verso.rank starts at 0 (D) and is clamped on load.
    /// </summary>
    public sealed class VersoModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Verso;
        public override IWinCondition Win { get; } = new VersoWin();
        // bot.verso (VersoBot) is not written yet: Root's own AI plays the seat until it is.

        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) => VersoTweaks.Perfection(Ctx, Seat, attacker);

        public override void OnBattleResolved(BattleResult r) => VersoTweaks.UpdateRank(Ctx, Seat, r);

        public override void OnActChanged(int act) => VersoTweaks.SoarrieAppears(Ctx, Seat, act);

        public override void OnEveningEnd(SeatId seat) => VersoTweaks.SoarrieHandoff(Ctx, Seat, seat);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => VersoTweaks.ClampOnLoad(Ctx);
    }
}
