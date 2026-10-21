using System;
using System.Collections.Generic;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// One character's code: its themed win, its rule tweaks and spawns (as hook handlers) and its bot director.
    /// <see cref="Root33Game"/> creates one instance per game for each seated character (through <see cref="ModuleRegistry"/>),
    /// binds it to its seat, and forwards every game hook to it. Handlers see every seat's events, not only their own:
    /// compare the seat argument with <see cref="Seat"/>. Everything that must survive save/load lives in
    /// <see cref="GameContext.State"/>, never in module fields (a load re-creates the modules).
    /// Derive from <see cref="CharacterModule"/>, which implements every handler as a no-op.
    /// </summary>
    public interface ICharacterModule
    {
        /// <summary>characters.json id, e.g. "gustave".</summary>
        string CharacterId { get; }
        /// <summary>Dispatch order inside one hook (lower first; ties by seat). hook.evening-end needs Gustave's flags
        /// before Esquie's Soarrie hand-off and early-Urrie check; see <see cref="HookOrder"/>.</summary>
        int Order { get; }
        /// <summary>The themed win (win_conditions row), or null.</summary>
        IWinCondition Win { get; }
        /// <summary>The bot plan (bot_plans row), or null; used only when the seat is a bot.</summary>
        IBotDirector Bot { get; }

        GameContext Ctx { get; }
        SeatId Seat { get; }
        void Bind(GameContext ctx, SeatId seat);

        void OnGameStart();
        void OnMapLoaded(string mapName);
        /// <summary>Map roles were assigned (at map load, when Gustave's keep was placed, or recomputed on load).</summary>
        void OnRolesAssigned();
        void OnSetupChoice(SeatId seat);
        void OnSetupComplete();
        void OnRoundStart(int round);
        void OnBirdsongStart(SeatId seat);
        void OnEveningStart(SeatId seat);
        /// <summary>Rule tweaks at the end of a turn. Runs before the framework's themed-win check for that seat.</summary>
        void OnEveningEnd(SeatId seat);
        /// <summary>Per-turn resets. Runs before the act clock.</summary>
        void OnTurnEnd(SeatId seat);
        void OnActChanged(int act);
        /// <summary>A story event of the new act (or of Act I at setup) whose owner is this character, or that has no owner.</summary>
        void OnStoryEvent(StoryEventDef storyEvent);
        void OnActionResolved(SeatId seat);
        void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count);
        void OnPiecePlaced(SeatId owner, PieceKind kind, string type, ClearingId c);
        void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c);
        /// <summary>hook.battle-legal: false forbids the battle (all modules must allow it).</summary>
        bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike);
        void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c);
        void OnBattleResolved(BattleResult r);
        /// <summary>hook.guard-battle-legal: false forbids battling a rock's neutral guards (all modules must allow it).</summary>
        bool CanBattleGuards(SeatId attacker, ClearingId c);
        void OnGuardBattleStarting(SeatId attacker, ClearingId c);
        void OnGuardBattleResolved(SeatId attacker, ClearingId c);
        void OnExploreResolved(SeatId vagabond, ClearingId c);
        void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c);
        /// <summary>hook.hostility-change: false skips the change (all modules must allow it).</summary>
        bool AllowHostile(SeatId vagabond, SeatId faction, ClearingId battleClearing);
        void OnRelationshipChanged(SeatId vagabond, SeatId faction, Relationship before, Relationship after);
        void OnItemChanged(SeatId vagabond, ItemState? before, ItemState? after);
        void OnWarlordRemoved(SeatId hundreds, ClearingId c);
        void OnRelicRecovered(SeatId keepers, int relicId, string relicType);
        void OnRelicMoved(int relicId);
        void OnVpChanged(SeatId seat, int before, int after);
        void OnTurmoil(SeatId eyrie);
        void OnConvert(SeatId lizards, SeatId victim, ClearingId c);
        void OnToggle(SeatId seat, string toggleId, bool on);
        void OnGameOver(IReadOnlyList<SeatId> winners);
        /// <summary>hook.load: the state was restored; apply this character's state.json 'onLoad' re-checks against the board.
        /// <paramref name="loadedKeys"/> holds the state row ids the save carried (a missing one means 'the save has none').
        /// Runs before the framework re-checks countdowns.</summary>
        void OnStateLoaded(IReadOnlyCollection<string> loadedKeys);
    }

    /// <summary>Well-known <see cref="ICharacterModule.Order"/> values (hook.evening-end ordering).</summary>
    public static class HookOrder
    {
        /// <summary>Gustave plants his flags first.</summary>
        public const int Gustave = 10;
        /// <summary>Then Esquie's Soarrie hand-off and the early-Urrie check.</summary>
        public const int Esquie = 20;
        /// <summary>Everyone else.</summary>
        public const int Default = 50;
    }

    /// <summary>Base class for character modules: every handler is a no-op, every veto allows.</summary>
    public abstract class CharacterModule : ICharacterModule
    {
        public abstract string CharacterId { get; }
        public virtual int Order => HookOrder.Default;
        public virtual IWinCondition Win => null;
        public virtual IBotDirector Bot => null;

        public GameContext Ctx { get; private set; }
        public SeatId Seat { get; private set; }

        public void Bind(GameContext ctx, SeatId seat)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Seat = seat;
            OnBound();
        }

        /// <summary>Called after <see cref="Bind"/>; create the win condition and bot director here if they need the context.</summary>
        protected virtual void OnBound() { }

        /// <summary>Shortcuts.</summary>
        protected IBoardView Board => Ctx.Board;
        protected IBoardActions Actions => Ctx.Actions;
        protected ModState State => Ctx.State;
        protected bool IsMe(SeatId seat) => seat == Seat;

        public virtual void OnGameStart() { }
        public virtual void OnMapLoaded(string mapName) { }
        public virtual void OnRolesAssigned() { }
        public virtual void OnSetupChoice(SeatId seat) { }
        public virtual void OnSetupComplete() { }
        public virtual void OnRoundStart(int round) { }
        public virtual void OnBirdsongStart(SeatId seat) { }
        public virtual void OnEveningStart(SeatId seat) { }
        public virtual void OnEveningEnd(SeatId seat) { }
        public virtual void OnTurnEnd(SeatId seat) { }
        public virtual void OnActChanged(int act) { }
        public virtual void OnStoryEvent(StoryEventDef storyEvent) { }
        public virtual void OnActionResolved(SeatId seat) { }
        public virtual void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count) { }
        public virtual void OnPiecePlaced(SeatId owner, PieceKind kind, string type, ClearingId c) { }
        public virtual void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c) { }
        public virtual bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) => true;
        public virtual void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c) { }
        public virtual void OnBattleResolved(BattleResult r) { }
        public virtual bool CanBattleGuards(SeatId attacker, ClearingId c) => true;
        public virtual void OnGuardBattleStarting(SeatId attacker, ClearingId c) { }
        public virtual void OnGuardBattleResolved(SeatId attacker, ClearingId c) { }
        public virtual void OnExploreResolved(SeatId vagabond, ClearingId c) { }
        public virtual void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c) { }
        public virtual bool AllowHostile(SeatId vagabond, SeatId faction, ClearingId battleClearing) => true;
        public virtual void OnRelationshipChanged(SeatId vagabond, SeatId faction, Relationship before, Relationship after) { }
        public virtual void OnItemChanged(SeatId vagabond, ItemState? before, ItemState? after) { }
        public virtual void OnWarlordRemoved(SeatId hundreds, ClearingId c) { }
        public virtual void OnRelicRecovered(SeatId keepers, int relicId, string relicType) { }
        public virtual void OnRelicMoved(int relicId) { }
        public virtual void OnVpChanged(SeatId seat, int before, int after) { }
        public virtual void OnTurmoil(SeatId eyrie) { }
        public virtual void OnConvert(SeatId lizards, SeatId victim, ClearingId c) { }
        public virtual void OnToggle(SeatId seat, string toggleId, bool on) { }
        public virtual void OnGameOver(IReadOnlyList<SeatId> winners) { }
        public virtual void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) { }
    }
}
