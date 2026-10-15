using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Maelle
{
    /// <summary>
    /// Maelle (characters.json 'maelle', Thief Vagabond): win.maelle (A Home in the Canvas), tweak.maelle.stance-chip and
    /// bot.maelle. She has no spawns rows. state.maelle.stance and .stance-turn are restored as they are;
    /// state.maelle.family drops seats that are now Hostile on load (the bot re-picks at her next Birdsong).
    /// </summary>
    public sealed class MaelleModule : CharacterModule
    {
        public override string CharacterId => CharacterIds.Maelle;
        public override IWinCondition Win { get; } = new MaelleWin();
        public override IBotDirector Bot { get; } = new MaelleBot();

        public override void OnBattleResolved(BattleResult r) => MaelleTweaks.AfterBattle(Ctx, Seat, r);

        public override void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c) => MaelleTweaks.AfterAid(Ctx, Seat, vagabond);

        public override void OnTurnEnd(SeatId seat) { if (IsMe(seat)) MaelleTweaks.EndTurn(Ctx); }

        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => MaelleTweaks.DropHostileFamily(Ctx, Seat);
    }
}
