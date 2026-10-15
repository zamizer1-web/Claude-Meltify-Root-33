using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Paintress
{
    /// <summary>
    /// The Paintress (Eyrie Dynasties): win.paintress, tweak.paintress.roost-in-monolith, tweak.paintress.still-life,
    /// state.paintress.still-life-used, state.paintress.repaint-avoid and bot.paintress. She owns no spawns.
    /// </summary>
    public sealed class PaintressModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Paintress;
        public override IWinCondition Win { get; } = new PaintressWin();
        public override IBotDirector Bot => Director;

        public PaintressBot Director { get; } = new PaintressBot();

        /// <summary>hook.setup-home: her first roost goes in the Monolith.</summary>
        public override void OnSetupChoice(SeatId seat)
        {
            if (IsMe(seat)) PaintressTweaks.RestrictHome(Ctx, Seat);
        }

        /// <summary>trigger.goal-move for a bot Paintress: she names her next easel (Easy a turn early).</summary>
        public override void OnBirdsongStart(SeatId seat)
        {
            if (!IsMe(seat) || !Board.IsBot(Seat)) return;
            if (Director.NextPortraitBark(new BotView(Ctx, Seat)).HasValue) Actions.ShowBubble(Seat, Ids.Barks.Paintress05);
        }

        /// <summary>hook.battle-start: Still Life.</summary>
        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) =>
            PaintressTweaks.StillLife(Ctx, Seat, attacker, c);

        /// <summary>hook.piece-removed: a destroyed easel goes into the repaint-avoid list.</summary>
        public override void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c) =>
            PaintressTweaks.PortraitRemoved(Ctx, Seat, piece, c);

        /// <summary>hook.turn-end: Still Life resets, repaint-avoid counts down on her own turns, the portraits are redrawn.</summary>
        public override void OnTurnEnd(SeatId seat)
        {
            PaintressTweaks.TurnEnd(Ctx, Seat, seat);
            PaintressTweaks.RefreshPortraits(Ctx, Seat);
        }

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys)
        {
            PaintressTweaks.RecheckOnLoad(Ctx);
            PaintressTweaks.RefreshPortraits(Ctx, Seat);
        }

        /// <summary>ui.paintress.ending when The Family Portrait was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Paintress))
                Actions.ShowOverlay(Ids.Overlays.PaintressEnding, Seat, null, null);
        }
    }
}
