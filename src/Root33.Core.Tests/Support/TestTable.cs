using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Tests.Support
{
    /// <summary>
    /// Drives a <see cref="Root33Game"/> over a <see cref="FakeBoard"/> the way the plugin will: character select, seats,
    /// game start, map, setup, then turns (round start, Birdsong, Evening start, Evening end, turn end).
    /// </summary>
    public sealed class TestTable
    {
        private int _round;

        private TestTable(FakeBoard board, Root33Game game) { Board = board; Game = game; }

        public FakeBoard Board { get; }
        public Root33Game Game { get; }
        public GameContext Ctx => Game.Context;
        public ModState State => Game.Context.State;

        /// <summary>Seats every character the board was built with (<see cref="FakeBoard.Seat"/>) and runs game creation,
        /// map load and setup. <paramref name="registry"/> defaults to an empty one (no character modules).</summary>
        public static TestTable Start(FakeBoard board, ModuleRegistry registry = null, Difficulty difficulty = Difficulty.Normal,
            SpoilerMode spoilers = SpoilerMode.Safe, Action<Root33Game> beforeSetup = null, bool runSetup = true,
            bool updateRules = false, Action<Root33Game> beforeStart = null)
        {
            var game = new Root33Game(board, board, registry ?? new ModuleRegistry(),
                new Capabilities().Unsupported(board.UnsupportedActions.ToArray()));
            game.NextGameOptions.UpdateRules = updateRules; // the internal v1.1 switch (off in the first release)
            beforeStart?.Invoke(game);
            game.OnCharacterSelectShown(Enum.GetValues(typeof(EngineId)).Cast<EngineId>().ToArray(), 4);
            foreach (var s in board.Seats())
            {
                if (board.Characters.TryGetValue(s, out var ch)) game.SelectCharacter(s, ch);
                game.OnSeatAssigned(s, board.EngineOf(s), board.IsBot(s));
            }
            game.OnGameStart(board.Seats(), difficulty, spoilers);
            game.OnMapLoaded(board.MapName());
            beforeSetup?.Invoke(game);
            if (runSetup) game.OnSetupComplete();
            return new TestTable(board, game);
        }

        /// <summary>One whole turn of a seat; starts a new round when the seat is first in turn order.</summary>
        public TestTable PlayTurn(int seat, Action<TestTable> during = null)
        {
            var s = new SeatId(seat);
            if (Ctx.TurnOrder.Count > 0 && Ctx.TurnOrder[0] == s) Game.OnRoundStart(++_round);
            Board.Turn(seat, TurnPhase.Birdsong);
            Game.OnBirdsongStart(s);
            Board.Turn(seat, TurnPhase.Daylight);
            during?.Invoke(this);
            Board.Turn(seat, TurnPhase.Evening);
            Game.OnEveningStart(s);
            Game.OnEveningEnd(s);
            Game.OnTurnEnd(s);
            return this;
        }

        /// <summary>Every seat's turn, in turn order.</summary>
        public TestTable PlayRound(Action<TestTable, int> during = null)
        {
            foreach (var s in Ctx.TurnOrder.ToArray())
            {
                var seat = s.Value;
                PlayTurn(seat, during == null ? (Action<TestTable>)null : t => during(t, seat));
            }
            return this;
        }

        public TestTable PlayRounds(int n)
        {
            for (var i = 0; i < n; i++) PlayRound();
            return this;
        }

        /// <summary>Saves and loads the game through hook.save / hook.load into a fresh <see cref="Root33Game"/> on the same board.</summary>
        public TestTable SaveAndLoad(ModuleRegistry registry = null, Func<string, string> edit = null)
        {
            var text = Game.OnSave();
            if (edit != null) text = edit(text);
            var game = new Root33Game(Board, Board, registry ?? new ModuleRegistry(),
                new Capabilities().Unsupported(Board.UnsupportedActions.ToArray()));
            game.NextGameOptions.UpdateRules = Ctx.Options.UpdateRules; // the same build loads the save
            game.OnLoad(text);
            var t = new TestTable(Board, game);
            t._round = t.State.GlobalRound;
            return t;
        }
    }

    /// <summary>A configurable character module for framework tests: records the hooks it sees and wins on demand.</summary>
    public sealed class StubModule : CharacterModule
    {
        private readonly string _id;
        private readonly int _order;

        public StubModule(string characterId, int order = HookOrder.Default, List<string> log = null)
        {
            _id = characterId;
            Seen = log ?? new List<string>();
            _order = order;
            StubWin = new StubWin(Data.Characters[characterId].WinCondition) { Log = Seen };
        }

        public override string CharacterId => _id;
        public override int Order => _order;
        public override IWinCondition Win => StubWin;
        public override IBotDirector Bot => StubBot;
        public StubWin StubWin { get; }
        public IBotDirector StubBot { get; set; }

        /// <summary>Every hook this module saw, as "character:Hook:args" strings. Pass one list to several stubs to see the order.</summary>
        public List<string> Seen { get; }
        public List<StoryEventDef> StoryEvents { get; } = new List<StoryEventDef>();
        public Func<SeatId, SeatId, ClearingId, bool> BattleVeto { get; set; }

        private void Note(string s) => Seen.Add(_id + ":" + s);

        public override void OnGameStart() => Note("GameStart");
        public override void OnRolesAssigned() => Note("RolesAssigned");
        public override void OnSetupComplete() => Note("SetupComplete");
        public override void OnBirdsongStart(SeatId seat) => Note("Birdsong:" + seat.Value);
        public override void OnEveningEnd(SeatId seat) => Note("EveningEnd:" + seat.Value + ":act" + Ctx.Act);
        public override void OnTurnEnd(SeatId seat) => Note("TurnEnd:" + seat.Value + ":act" + Ctx.Act);
        public override void OnActChanged(int act) => Note("ActChanged:" + act);
        public override void OnStoryEvent(StoryEventDef storyEvent) { StoryEvents.Add(storyEvent); Note("Story:" + storyEvent.Id); }
        public override void OnStateLoaded(IReadOnlyCollection<string> loadedKeys) => Note("StateLoaded");
        public override bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike) =>
            BattleVeto == null || BattleVeto(attacker, defender, c);
    }

    /// <summary>A win condition whose themed tests are plain flags; the framework still applies the real row's gates.</summary>
    public sealed class StubWin : WinConditionBase
    {
        public StubWin(string winId) : base(winId) { }
        public bool AnnounceResult { get; set; } = true;
        public bool ConfirmResult { get; set; } = true;
        public bool QuestResult { get; set; } = true;
        /// <summary>Where the stub notes each themed test it ran (the owning module's <see cref="StubModule.Seen"/>).</summary>
        public List<string> Log { get; set; } = new List<string>();

        public override bool Announce(GameContext ctx, SeatId seat) { Log.Add(Row.Character + ":Announce:act" + ctx.Act); return AnnounceResult; }
        public override bool Confirm(GameContext ctx, SeatId seat) { Log.Add(Row.Character + ":Confirm"); return ConfirmResult; }
        public override bool QuestComplete(GameContext ctx, SeatId seat) { Log.Add(Row.Character + ":Quest:act" + ctx.Act); return QuestResult; }
    }
}
