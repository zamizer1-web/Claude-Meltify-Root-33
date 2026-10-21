using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Clea
{
    /// <summary>
    /// Clea (characters.json 'clea', Lizard Cult): win.clea (Nevron Tide), tweak.clea.home-flying-manor, state.clea.covered
    /// and bot.clea. Clea has no spawns rows.
    /// </summary>
    public sealed class CleaModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Clea;
        public override IWinCondition Win { get; } = new CleaWin();
        public override IBotDirector Bot => Director;

        public CleaBot Director { get; } = new CleaBot();

        /// <summary>tweak.clea.home-flying-manor (hook.setup-home).</summary>
        public override void OnSetupChoice(SeatId seat)
        {
            if (IsMe(seat)) CleaTweaks.RestrictHome(Ctx, Seat);
        }

        /// <summary>state.clea.covered and trigger.coverage-count at the end of her own turn.</summary>
        public override void OnEveningEnd(SeatId seat)
        {
            if (IsMe(seat)) CleaTweaks.EndOfTurn(Ctx, Seat);
        }

        /// <summary>hook.battle-legal, for her own bot seat only: the Lizards battle only through Crusade, and step 3
        /// (bot.clea.03-monolith) allows Crusade only when the Monolith or a clearing next to it is the Outcast suit; this also
        /// binds the decisions Root's own AI plays for her. A human Clea is never limited.</summary>
        public override bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) =>
            !IsMe(attacker) || !Board.IsBot(Seat) || Director.CrusadeAllowed(new BotView(Ctx, Seat));

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => CleaTweaks.RecheckOnLoad(Ctx, Seat, loadedKeys);

        /// <summary>ui.clea.ending when the Nevron Tide was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Clea))
                Actions.ShowOverlay(Ids.Overlays.CleaEnding, Seat, null, null);
        }
    }
}
