using System;
using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>
    /// Sciel (characters.json 'sciel', Woodland Alliance): win.sciel (Twilight Harvest), tweak.sciel.twilight and bot.sciel.
    /// She has no spawns rows. Her state (state.sciel.sun, .moon, .twilight-armed) is restored from the save as is.
    /// </summary>
    public sealed class ScielModule : CharacterModule
    {
        private static readonly Lazy<ModuleRegistry> AllModules = new Lazy<ModuleRegistry>(ModuleRegistry.Default);
        private readonly Dictionary<string, IWinCondition> _otherWins = new Dictionary<string, IWinCondition>(StringComparer.Ordinal);
        private readonly ScielBot _bot = new ScielBot();

        public override string CharacterId => CharacterIds.Sciel;
        public override IWinCondition Win { get; } = new ScielWin();
        public override IBotDirector Bot => _bot;

        /// <summary>The spoiler layer reads other seats' public progress through their wins (<see cref="IGoalProgress"/>).</summary>
        protected override void OnBound()
        {
            _bot.UnitsMissingOf = s => WinOf(s) is IGoalProgress p ? p.UnitsMissing(Ctx, s) : (int?)null;
            _bot.KeyClearingsOf = s => WinOf(s) is IGoalProgress p ? p.KeyClearings(Ctx, s) : null;
        }

        private IWinCondition WinOf(SeatId seat)
        {
            var ch = Ctx.CharacterOf(seat);
            if (ch == null) return null;
            if (!_otherWins.TryGetValue(ch, out var w))
            {
                w = AllModules.Value.Has(ch) ? AllModules.Value.Create(ch)?.Win : null;
                _otherWins[ch] = w;
            }
            return w;
        }

        public override void OnPiecePlaced(SeatId owner, PieceKind kind, string type, ClearingId c) =>
            ScielTweaks.OnPiecePlaced(Ctx, Seat, owner, kind, type);

        public override void OnEveningStart(SeatId seat) { if (IsMe(seat)) ScielTweaks.ArmAtEveningStart(Ctx); }

        public override void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) => ScielTweaks.BoostBattle(Ctx, Seat, attacker);

        public override void OnBattleResolved(BattleResult r) => ScielTweaks.AfterBattle(Ctx, Seat, r);

        public override void OnTurnEnd(SeatId seat) { if (IsMe(seat)) ScielTweaks.EndTurn(Ctx); }
    }
}
