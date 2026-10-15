using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>
    /// The White-Haired Man (Corvid Conspiracy): win.white-haired-man, tweak.white-haired-man.unbeatable-act1 and
    /// bot.white-haired-man. tweak.white-haired-man.monolith-barrier and state.white-haired-man.barrier-down are a table rule
    /// (opt.monolith-barrier) that <see cref="Root33Game"/> applies whether or not he is seated
    /// (<see cref="WhiteHairedManTweaks.BarrierAllows"/>, <see cref="WhiteHairedManTweaks.BarrierTurnEnd"/>); the saved flag is
    /// restored as is on load and never goes back up. He owns no spawns.
    /// </summary>
    public sealed class WhiteHairedManModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.WhiteHairedMan;
        public override IWinCondition Win { get; } = new WhiteHairedManWin();
        public override IBotDirector Bot => Director;

        public WhiteHairedManBot Director { get; } = new WhiteHairedManBot();

        /// <summary>hook.battle-start: Unbeatable in Act I.</summary>
        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) =>
            WhiteHairedManTweaks.UnbeatableAtBattleStart(Ctx, Seat, attacker, defender, c);

        /// <summary>hook.battle-resolved: do.ignore-first-hit's fallback (one removed warrior returned from supply).</summary>
        public override void OnBattleResolved(BattleResult r) => WhiteHairedManTweaks.UnbeatableFallback(Ctx, Seat, r);

        /// <summary>trigger.goal-move for a bot White-Haired Man: he names the Ring clearing he is about to fix.</summary>
        public override void OnBirdsongStart(SeatId seat)
        {
            if (!IsMe(seat) || !Board.IsBot(Seat)) return;
            if (Director.GoalMoveTarget(new BotView(Ctx, Seat)).HasValue) Ctx.Barks.Say(Seat, Ids.BarkTriggers.GoalMove);
        }

        /// <summary>trigger.monolith-approach: an enemy steps into a Ring clearing where he has pieces, or a Vagabond into the Monolith.</summary>
        public override void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count)
        {
            if (WhiteHairedManTweaks.IsMonolithApproach(Ctx, Seat, owner, kind, from, to)) Ctx.Barks.Say(Seat, Ids.BarkTriggers.MonolithApproach);
        }

        public override void OnRolesAssigned() => WhiteHairedManTweaks.RefreshRing(Ctx, Seat);

        /// <summary>hook.turn-end: the Ring tracker is redrawn.</summary>
        public override void OnTurnEnd(SeatId seat) => WhiteHairedManTweaks.RefreshRing(Ctx, Seat);

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => WhiteHairedManTweaks.RefreshRing(Ctx, Seat);

        /// <summary>ui.white-haired-man.ending when Nobody Reaches Her was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.WhiteHairedMan))
                Actions.ShowOverlay(Ids.Overlays.WhiteHairedManEnding, Seat, null, null);
        }
    }
}
