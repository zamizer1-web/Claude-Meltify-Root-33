using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Esquie
{
    /// <summary>Esquie (Tinker Vagabond): win.esquie, tweak.esquie.*, spawn.florrie/dorrie/soarrie/urrie/francois-decoy and
    /// bot.esquie. Everything that must survive a save lives in state.esquie.rocks / decoy / target.</summary>
    public sealed class EsquieModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Esquie;
        public override int Order => HookOrder.Esquie;
        public override IWinCondition Win { get; } = new EsquieWin();
        public override IBotDirector Bot { get; } = new EsquieBot();

        public override void OnSetupChoice(SeatId seat) { if (IsMe(seat)) EsquieTweaks.StartNearNest(Ctx, Seat); }
        public override void OnSetupComplete() => EsquieTweaks.Setup(Ctx, Seat);
        public override void OnActChanged(int act) => EsquieTweaks.ActChanged(Ctx, Seat, act);
        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) =>
            EsquieTweaks.BellyBounce(Ctx, Seat, attacker, defender, c);
        /// <summary>Guards are counted only here (spawn.florrie / spawn.urrie claimRule), once per removed host warrior.</summary>
        public override void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c) => EsquieTweaks.PieceRemoved(Ctx, piece, c);
        public override void OnActionResolved(SeatId seat) => EsquieTweaks.AfterAction(Ctx, Seat, seat);
        public override void OnExploreResolved(SeatId vagabond, ClearingId c) => EsquieTweaks.ExploreResolved(Ctx, Seat, vagabond, c);
        public override void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c) => EsquieTweaks.AidResolved(Ctx, Seat, vagabond, receiver, c);
        public override void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count) =>
            EsquieTweaks.PieceMoved(Ctx, Seat, owner, kind, to);
        public override void OnEveningEnd(SeatId seat) { if (IsMe(seat)) EsquieTweaks.EveningEnd(Ctx, Seat); }
        public override bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) =>
            !IsMe(attacker) || EsquieTweaks.NeverMadAllows(Ctx, defender, c);
        public override bool AllowHostile(SeatId vagabond, SeatId faction, ClearingId battleClearing) =>
            EsquieTweaks.FriendsNotFoesAllows(State, battleClearing);
        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => EsquieTweaks.Loaded(Ctx, Seat);
    }
}
