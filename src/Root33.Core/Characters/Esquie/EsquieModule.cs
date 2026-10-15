using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Esquie
{
    /// <summary>Esquie (Tinker Vagabond): win.esquie, tweak.esquie.*, spawn.florrie/dorrie/soarrie/urrie/francois-decoy.
    /// TODO (next run): bot.esquie director (EsquieBot) is not written yet.</summary>
    public sealed class EsquieModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Esquie;
        public override int Order => HookOrder.Esquie;
        public override IWinCondition Win { get; } = new EsquieWin();

        public override void OnSetupChoice(SeatId seat) { if (IsMe(seat)) EsquieTweaks.StartNearNest(Ctx, Seat); }
        public override void OnSetupComplete() => EsquieTweaks.Setup(Ctx, Seat);
        public override void OnActChanged(int act) => EsquieTweaks.ActChanged(Ctx, Seat, act);
        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) => EsquieTweaks.BellyBounce(Ctx, Seat, attacker, defender, c);
        public override void OnBattleResolved(BattleResult r) => EsquieTweaks.BattleResolved(Ctx, r);
        public override void OnActionResolved(SeatId seat) => EsquieTweaks.AfterAction(Ctx, Seat, seat);
        public override void OnExploreResolved(SeatId vagabond, ClearingId c) => EsquieTweaks.ExploreResolved(Ctx, Seat, vagabond, c);
        public override void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c) => EsquieTweaks.AidResolved(Ctx, Seat, vagabond, receiver, c);
        public override void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count) =>
            EsquieTweaks.PieceMoved(Ctx, Seat, owner, kind, to);
        public override void OnEveningEnd(SeatId seat) { if (IsMe(seat)) EsquieTweaks.EveningEnd(Ctx, Seat); }
        public override bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) =>
            !IsMe(attacker) || EsquieTweaks.NeverMadAllows(Ctx, defender, c);
        public override bool AllowHostile(SeatId vagabond, SeatId faction, ClearingId battleClearing) =>
            !IsMe(vagabond) || EsquieTweaks.GuardedRockAt(State, battleClearing) < 0;
        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => EsquieTweaks.Loaded(Ctx, Seat);
    }
}
