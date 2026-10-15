using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>The player's choices for one game (sheets/options.json). Copied into <see cref="ModState"/> when a game is created.</summary>
    public sealed class GameOptions
    {
        /// <summary>opt.spoiler-mode (default safe).</summary>
        public SpoilerMode Spoilers = SpoilerMode.Safe;
        /// <summary>opt.difficulty (default normal).</summary>
        public Difficulty Difficulty = Difficulty.Normal;
        /// <summary>opt.faithful-gustave (default off; treated as off in safe mode).</summary>
        public bool FaithfulGustave;
        /// <summary>opt.campaign (default off = Free Play).</summary>
        public bool Campaign;
        /// <summary>opt.monolith-barrier (v1.1, default off).</summary>
        public bool MonolithBarrier;

        /// <summary>Faithful Gustave as the rules read it: finished mode only.</summary>
        public bool FaithfulGustaveActive(SpoilerMode game) => FaithfulGustave && game == SpoilerMode.Finished;
    }

    /// <summary>
    /// Which board actions (sheets/board_actions.json) the plugin can perform. Every action is assumed supported until the
    /// plugin marks it unsupported; code that has a sheet fallback checks <see cref="Supports"/> and takes the fallback.
    /// </summary>
    public sealed class Capabilities
    {
        private readonly HashSet<string> _unsupported = new HashSet<string>(StringComparer.Ordinal);

        public bool Supports(string boardActionId) => !_unsupported.Contains(boardActionId);

        public Capabilities Unsupported(params string[] boardActionIds)
        {
            foreach (var id in boardActionIds) _unsupported.Add(id);
            return this;
        }

        public IReadOnlyCollection<string> UnsupportedActions => _unsupported;
    }

    /// <summary>One story event that fired (or was skipped) when an act opened.</summary>
    public readonly struct StoryEventRecord
    {
        public readonly string EventId;
        public readonly int Act;
        public readonly string Owner;
        public StoryEventRecord(string eventId, int act, string owner) { EventId = eventId; Act = act; Owner = owner; }
        public override string ToString() => EventId;
    }

    /// <summary>
    /// Everything a character module, win condition or bot director may use: the board (read), the actions (write), the
    /// saved <see cref="ModState"/>, the shared <see cref="Countdown"/> and <see cref="ActClock"/>, the seat to character map,
    /// the map roles and the game's options. One per game; <see cref="Root33Game"/> owns it.
    /// </summary>
    public sealed class GameContext
    {
        public GameContext(IBoardView board, IBoardActions actions, Capabilities capabilities = null)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            Capabilities = capabilities ?? new Capabilities();
            State = new ModState();
            Countdown = new Countdown();
            ActClock = NewActClock();
            Options = new GameOptions();
            TurnOrder = new SeatId[0];
        }

        public IBoardView Board { get; }
        public IBoardActions Actions { get; }
        public Capabilities Capabilities { get; }
        public ModState State { get; internal set; }
        public Countdown Countdown { get; }
        public ActClock ActClock { get; internal set; }
        public GameOptions Options { get; internal set; }
        public IReadOnlyList<SeatId> TurnOrder { get; internal set; }

        /// <summary>True once a winner was declared or hook.game-over arrived; later hooks change nothing.</summary>
        public bool GameOver { get; internal set; }
        /// <summary>Themed wins declared through <see cref="DeclareWinner"/> (seat, win id), in order.</summary>
        public List<KeyValuePair<SeatId, string>> DeclaredWins { get; } = new List<KeyValuePair<SeatId, string>>();
        /// <summary>Story events that fired, in order (the plugin shows vignettes from this log).</summary>
        public List<StoryEventRecord> StoryLog { get; } = new List<StoryEventRecord>();
        /// <summary>Warnings for the player (for example a loaded seat whose engine no longer matches its character).</summary>
        public List<string> Warnings { get; } = new List<string>();

        public static ActClock NewActClock() =>
            new ActClock(new[] { Tuning.Act1EndVp, Tuning.Act2EndVp, 0 }, new[] { Tuning.Act1EndRound, Tuning.Act2EndRound, 0 });

        // ------------------------------------------------------------ game facts

        public int Act => State.GlobalAct;
        public int Round => State.GlobalRound;
        public SpoilerMode Spoilers => State.GlobalSpoilerMode;
        public Difficulty Difficulty => State.GlobalDifficulty;
        public bool E33Mode => State.GlobalE33Mode;

        public bool SupportsAction(string boardActionId) => Capabilities.Supports(boardActionId);

        // ------------------------------------------------------------ seats and characters

        /// <summary>The character id a seat plays (characters.json), or null for a seat playing plain Root.</summary>
        public string CharacterOf(SeatId seat) => State.GlobalSeatCharacters.TryGetValue(seat, out var c) ? c : null;

        /// <summary>The seat playing a character, or null when it is not seated.</summary>
        public SeatId? SeatOf(string characterId)
        {
            foreach (var kv in State.GlobalSeatCharacters.OrderBy(kv => kv.Key.Value))
                if (kv.Value == characterId) return kv.Key;
            return null;
        }

        public bool IsSeated(string characterId) => SeatOf(characterId).HasValue;

        /// <summary>The themed win of the module bound to a seat, or null (set by <see cref="Root33Game"/>).</summary>
        public IWinCondition WinOf(SeatId seat) => WinLookup?.Invoke(seat);

        internal Func<SeatId, IWinCondition> WinLookup { get; set; }

        public static bool IsVagabondEngine(EngineId e) =>
            e == EngineId.VagabondTinker || e == EngineId.VagabondRanger || e == EngineId.VagabondThief || e == EngineId.VagabondOther;

        public bool IsVagabond(SeatId seat) => IsVagabondEngine(Board.EngineOf(seat));

        /// <summary>The highest VP of any seat without an active dominance card (read.vp is undefined for those).</summary>
        public int HighestVp()
        {
            var best = 0;
            foreach (var s in Board.Seats())
                if (!Board.ActiveDominance(s).HasValue) best = Math.Max(best, Board.Vp(s));
            return best;
        }

        /// <summary>The Monolith Number: tune.monolith-number-start (33) minus the top score.</summary>
        public int MonolithNumber => Tuning.MonolithNumberStart - HighestVp();

        /// <summary>The last fully completed round when <paramref name="seatEndingTurn"/> has just finished its turn
        /// ('round N ends' = the last seat in turn order has finished its turn in round N).</summary>
        public int CompletedRoundsAfterTurnOf(SeatId seatEndingTurn)
        {
            var order = TurnOrder.Count > 0 ? TurnOrder : Board.Seats();
            var last = order.Count > 0 && order[order.Count - 1] == seatEndingTurn;
            return last ? Round : Round - 1;
        }

        // ------------------------------------------------------------ map roles

        /// <summary>All clearings of a role (map_roles id, e.g. Ids.MapRoles.Monolith); empty when unassigned.</summary>
        public IReadOnlyList<ClearingId> RoleClearings(string roleId) =>
            State.GlobalRoles.TryGetValue(new RoleId(roleId), out var l) ? l : (IReadOnlyList<ClearingId>)new ClearingId[0];

        /// <summary>The clearing of a single-clearing role, or null when the map has none or roles are not assigned yet.</summary>
        public ClearingId? Role(string roleId)
        {
            var l = RoleClearings(roleId);
            return l.Count > 0 ? l[0] : (ClearingId?)null;
        }

        public bool HasRole(ClearingId c, string roleId) => RoleClearings(roleId).Contains(c);

        public bool RolesAssigned => State.GlobalRoles.Count > 0;

        /// <summary>Path neighbours of the Monolith (the Ring), or empty before roles are assigned.</summary>
        public IReadOnlyList<ClearingId> MonolithNeighbours()
        {
            var m = Role(Ids.MapRoles.Monolith);
            return m.HasValue ? Board.PathNeighbours(m.Value) : (IReadOnlyList<ClearingId>)new ClearingId[0];
        }

        // ------------------------------------------------------------ actions with sheet fallbacks

        /// <summary>do.declare-winner for a themed win. Ends the game for the core: later hooks change nothing.</summary>
        public void DeclareWinner(SeatId seat, string winConditionId)
        {
            if (GameOver) return;
            GameOver = true;
            DeclaredWins.Add(new KeyValuePair<SeatId, string>(seat, winConditionId));
            Actions.DeclareWinner(seat, winConditionId);
        }

        /// <summary>do.score-vp, or its fallback: queue in state.global.pending-vp when the plugin cannot score outside
        /// that seat's turn (paid at its next Birdsong).</summary>
        public void ScoreVp(SeatId seat, int vp)
        {
            if (vp <= 0) return;
            if (SupportsAction(Ids.BoardActions.ScoreVp) || Board.CurrentSeat() == seat) Actions.ScoreVp(seat, vp);
            else State.GlobalPendingVp[seat] = (State.GlobalPendingVp.TryGetValue(seat, out var q) ? q : 0) + vp;
        }

        /// <summary>do.draw-cards, or its fallback: queue in state.global.pending-draws (added to that seat's next Evening draw).</summary>
        public void DrawCards(SeatId seat, int count)
        {
            if (count <= 0) return;
            if (SupportsAction(Ids.BoardActions.DrawCards) || Board.CurrentSeat() == seat) Actions.DrawCards(seat, count);
            else State.GlobalPendingDraws[seat] = (State.GlobalPendingDraws.TryGetValue(seat, out var q) ? q : 0) + count;
        }
    }
}
