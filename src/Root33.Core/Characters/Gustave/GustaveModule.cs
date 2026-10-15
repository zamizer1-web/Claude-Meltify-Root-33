using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Gustave
{
    /// <summary>
    /// Gustave (Marquise de Cat): win.gustave, tweak.gustave.overcharge, tweak.gustave.keep-in-lumiere, state.gustave.* and
    /// bot.gustave. He owns no spawns. His flags are planted first at hook.evening-end (<see cref="HookOrder.Gustave"/>).
    /// </summary>
    public sealed class GustaveModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Gustave;
        public override int Order => HookOrder.Gustave;
        public override IWinCondition Win { get; } = new GustaveWin();
        public override IBotDirector Bot => Director;

        public GustaveBot Director { get; } = new GustaveBot();

        /// <summary>hook.setup-home: on Autumn the keep goes in Lumière.</summary>
        public override void OnSetupChoice(SeatId seat)
        {
            if (IsMe(seat)) GustaveTweaks.RestrictHome(Ctx, Seat);
        }

        /// <summary>hook.setup-complete: Lumière starts flagged.</summary>
        public override void OnSetupComplete()
        {
            GustaveTweaks.EnsureLumiereFlag(Ctx);
            GustaveTweaks.Refresh(Ctx, Seat);
        }

        /// <summary>hook.evening-end of his turn: plant the flags before the win check.</summary>
        public override void OnEveningEnd(SeatId seat)
        {
            if (IsMe(seat)) GustaveTweaks.PlantFlags(Ctx, Seat);
        }

        public override void OnToggle(SeatId seat, string toggleId, bool on) => GustaveTweaks.Toggle(Ctx, Seat, seat, toggleId, on);

        /// <summary>hook.battle-start: a bot Gustave arms Overcharge here when step 5 allows it, then the toggle is read.</summary>
        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c)
        {
            if (attacker == Seat && Board.IsBot(Seat) && !State.GustaveOverchargeArmed
                && Director.Overcharge(new BotView(Ctx, Seat), c, defender))
                GustaveTweaks.Toggle(Ctx, Seat, Seat, GustaveTweaks.OverchargeToggle, true);
            GustaveTweaks.BattleStarting(Ctx, Seat, attacker);
        }

        public override void OnBattleResolved(BattleResult r) => GustaveTweaks.BattleResolved(Ctx, Seat, r);

        public override void OnTurnEnd(SeatId seat) => GustaveTweaks.TurnEnd(Ctx, Seat, seat);

        /// <summary>hook.action-resolved: Hard re-plans the Route when an action raised a Route clearing's cost.</summary>
        public override void OnActionResolved(SeatId seat)
        {
            if (Board.IsBot(Seat) && Ctx.Difficulty == Difficulty.Hard) Director.ReplanIfRaised(new BotView(Ctx, Seat), State);
        }

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => GustaveTweaks.RecheckOnLoad(Ctx, Seat);

        /// <summary>ui.gustave.ending when The Road to the Monolith was declared.</summary>
        public override void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (Ctx.DeclaredWins.Any(w => w.Key == Seat && w.Value == Ids.WinConditions.Gustave))
                Actions.ShowOverlay(Ids.Overlays.GustaveEnding, Seat, null, null);
        }
    }
}
