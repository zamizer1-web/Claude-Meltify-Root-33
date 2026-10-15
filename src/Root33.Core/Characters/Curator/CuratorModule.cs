using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Curator
{
    /// <summary>
    /// The Curator (Underground Duchy): win.curator, tweak.curator.quiet-helper, tweak.curator.home-corner, spawn.cell-crack,
    /// state.curator.cell-crack and bot.curator.
    /// </summary>
    public sealed class CuratorModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Curator;
        public override IWinCondition Win { get; } = new CuratorWin();
        public override IBotDirector Bot => Director;

        public CuratorBot Director { get; } = new CuratorBot();

        public override void OnSetupChoice(SeatId seat)
        {
            if (IsMe(seat)) CuratorTweaks.RestrictHome(Ctx, Seat);
        }

        public override void OnSetupComplete() => CuratorTweaks.CrackAtSetup(Ctx, Seat);

        public override void OnStoryEvent(StoryEventDef storyEvent)
        {
            if (storyEvent.Id == Ids.StoryEvents.Act3CellCrack) CuratorTweaks.OpenCrack(Ctx, Seat);
        }

        /// <summary>hook.battle-legal, for his own bot seat only: step 1's limits (bot.curator.01-helper: no big fights and no
        /// battle against the Paintress before she is one step from her win, in Acts I-II) also bind the decisions Root's own
        /// AI plays for him. Humans and other seats are never limited.</summary>
        public override bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) =>
            !IsMe(attacker) || !Board.IsBot(Seat) || Director.CanBattle(new BotView(Ctx, Seat), c, defender);

        public override void OnActionResolved(SeatId seat) => CuratorTweaks.ActionResolved(Ctx, Seat, seat);

        public override void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c) =>
            CuratorTweaks.PieceRemoved(Ctx, Seat, remover, piece, c);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => CuratorTweaks.RecheckOnLoad(Ctx, Seat, loadedKeys);

        /// <summary>ui.curator.ending when Curtain Call was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Curator))
                Actions.ShowOverlay(Ids.Overlays.CuratorEnding, Seat, null, null);
        }
    }
}
