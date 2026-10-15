using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// A character's bot plan (bot_plans row). Each bot_rules row of the plan is one public method on the director, marked
    /// <c>[BotRule("bot.x.nn-name")]</c> and named after the row's name part in PascalCase (bot.gustave.01-route → Route).
    /// Rule methods are pure: they read only a <see cref="BotView"/> (public board state plus the bot's own hand) and
    /// return a ranking or a decision; they never call <see cref="IBoardActions"/>. Difficulty comes from
    /// <see cref="BotView.Difficulty"/>.
    /// </summary>
    public interface IBotDirector
    {
        /// <summary>bot_plans id, e.g. "bot.gustave".</summary>
        string PlanId { get; }
        /// <summary>The plan's goalModeTrigger for the view's difficulty.</summary>
        bool InGoalMode(BotView view);
        /// <summary>hook.birdsong-start for the bot's own seat (after countdown confirmation): recompute what the plan keeps
        /// in <see cref="ModState"/> (routes, targets, family picks). The only director method that writes state.</summary>
        void PlanTurn(BotView view, ModState state);
        /// <summary>hook.bot-decision: one of <paramref name="legalActions"/>, or null to let Root's own AI decide.</summary>
        string ChooseAction(BotView view, IReadOnlyList<string> legalActions);
    }

    /// <summary>Marks the method that implements one bot_rules row.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class BotRuleAttribute : Attribute
    {
        public BotRuleAttribute(string ruleId) { RuleId = ruleId; }
        public string RuleId { get; }
    }

    /// <summary>One ranked option: higher score first.</summary>
    public readonly struct Ranked<T>
    {
        public readonly T Item;
        public readonly double Score;
        public Ranked(T item, double score) { Item = item; Score = score; }
        public override string ToString() => Item + " (" + Score + ")";
    }

    public static class Rank
    {
        /// <summary>Highest score first; ties keep a deterministic order through <paramref name="tieBreak"/> (lower first).</summary>
        public static List<Ranked<T>> Descending<T>(IEnumerable<T> items, Func<T, double> score, Func<T, int> tieBreak) =>
            items.Select(i => new Ranked<T>(i, score(i))).OrderByDescending(r => r.Score).ThenBy(r => tieBreak(r.Item)).ToList();

        /// <summary>Clearings ranked by score, ties toward the lower clearing number.</summary>
        public static List<Ranked<ClearingId>> Clearings(IEnumerable<ClearingId> clearings, Func<ClearingId, double> score) =>
            Descending(clearings, score, c => c.Value);
    }

    /// <summary>
    /// What a bot may know: the board through <see cref="PublicBoardView"/> (hidden information of other seats is refused),
    /// its own seat and hand, the mod's public state, the roles, the act and the difficulty.
    /// </summary>
    public sealed class BotView
    {
        private readonly GameContext _ctx;

        public BotView(GameContext ctx, SeatId seat)
        {
            _ctx = ctx;
            Seat = seat;
            Board = new PublicBoardView(ctx.Board, seat);
        }

        public SeatId Seat { get; }
        public IBoardView Board { get; }
        public Difficulty Difficulty => _ctx.Difficulty;
        public int Act => _ctx.Act;
        public int Round => _ctx.Round;
        /// <summary>The mod's state. Only public parts may drive decisions about other seats (all of it is shown on the Goal Tracker).</summary>
        public ModState State => _ctx.State;
        /// <summary>Seats with a countdown banner up (public).</summary>
        public IReadOnlyCollection<SeatId> Countdowns => _ctx.State.GlobalCountdowns;
        public string CharacterOf(SeatId seat) => _ctx.CharacterOf(seat);
        public SeatId? SeatOf(string characterId) => _ctx.SeatOf(characterId);
        public ClearingId? Role(string roleId) => _ctx.Role(roleId);
        public IReadOnlyList<ClearingId> RoleClearings(string roleId) => _ctx.RoleClearings(roleId);
        public IReadOnlyList<ClearingId> MonolithNeighbours() => _ctx.MonolithNeighbours();
        public bool IsVagabond(SeatId seat) => _ctx.IsVagabond(seat);
        public int HighestVp() => _ctx.HighestVp();
        /// <summary>Another seat's themed win (public: its row and gates).</summary>
        public IWinCondition WinOf(SeatId seat) => _ctx.WinOf(seat);
        /// <summary>The public Goal Tracker: units of a seat's themed test not met yet (<see cref="IGoalProgress"/>) plus the
        /// row's VP gate as one more unit, or null when its win does not report progress.</summary>
        public int? UnitsMissing(SeatId seat)
        {
            var win = _ctx.WinOf(seat);
            if (!(win is IGoalProgress p)) return null;
            return p.UnitsMissing(_ctx, seat) + (win.VpMin > 0 && _ctx.Board.Vp(seat) < win.VpMin ? 1 : 0);
        }
        /// <summary>The clearings where a seat's win can be answered (<see cref="IGoalProgress"/>), or empty.</summary>
        public IReadOnlyList<ClearingId> KeyClearings(SeatId seat) =>
            _ctx.WinOf(seat) is IGoalProgress p ? p.KeyClearings(_ctx, seat) : (IReadOnlyList<ClearingId>)new ClearingId[0];
    }

    /// <summary>An <see cref="IBoardView"/> that refuses another seat's hidden information (hand, supporters, hidden plots)
    /// and hides face-down relic values (read.relic-value), so a bot can only use what a player sitting in its seat could see.</summary>
    public sealed class PublicBoardView : IBoardView
    {
        private readonly IBoardView _b;
        private readonly SeatId _self;

        public PublicBoardView(IBoardView board, SeatId self) { _b = board; _self = self; }

        private void Own(SeatId seat, string what)
        {
            if (seat != _self) throw new InvalidOperationException("a bot may not read another seat's " + what);
        }

        public IReadOnlyList<SeatId> Seats() => _b.Seats();
        public EngineId EngineOf(SeatId seat) => _b.EngineOf(seat);
        public bool IsBot(SeatId seat) => _b.IsBot(seat);
        public SeatId CurrentSeat() => _b.CurrentSeat();
        public TurnPhase Phase() => _b.Phase();
        public int Round() => _b.Round();
        public int Vp(SeatId seat) => _b.Vp(seat);
        public Suit? ActiveDominance(SeatId seat) => _b.ActiveDominance(seat);
        public SeatId? CoalitionPartner(SeatId seat) => _b.CoalitionPartner(seat);
        public int HandSize(SeatId seat) => _b.HandSize(seat);
        public IReadOnlyList<Suit> HandSuits(SeatId seat) { Own(seat, "hand"); return _b.HandSuits(seat); }
        public IReadOnlyList<string> LegalActions(SeatId seat) { Own(seat, "legal actions"); return _b.LegalActions(seat); }
        public string MapName() => _b.MapName();
        public IReadOnlyList<ClearingId> Clearings() => _b.Clearings();
        public IReadOnlyList<Suit> SuitsOf(ClearingId c) => _b.SuitsOf(c);
        public IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) => _b.PathNeighbours(c);
        public IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => _b.RiverNeighbours(c);
        public bool OnRiver(ClearingId c) => _b.OnRiver(c);
        public IReadOnlyList<ClearingId> Corners() => _b.Corners();
        public ClearingId? OppositeCorner(ClearingId corner) => _b.OppositeCorner(corner);
        public bool HasRuin(ClearingId c) => _b.HasRuin(c);
        public int TotalSlots(ClearingId c) => _b.TotalSlots(c);
        public int FreeSlots(ClearingId c) => _b.FreeSlots(c);
        public IReadOnlyList<int> ForestsAround(ClearingId c) => _b.ForestsAround(c);
        public IReadOnlyList<ClearingId> ForestClearings(int forest) => _b.ForestClearings(forest);
        public SeatId? Ruler(ClearingId c) => _b.Ruler(c);
        public int Warriors(SeatId seat, ClearingId c) => _b.Warriors(seat, c);
        public int Buildings(SeatId seat, ClearingId c) => _b.Buildings(seat, c);
        public int BuildingsOfType(SeatId seat, ClearingId c, string type) => _b.BuildingsOfType(seat, c, type);
        public int Tokens(SeatId seat, ClearingId c) => _b.Tokens(seat, c);
        public int TokensOfType(SeatId seat, ClearingId c, string type) => _b.TokensOfType(seat, c, type);
        public ClearingId? PawnClearing(SeatId seat) => _b.PawnClearing(seat);
        public int? PawnForest(SeatId seat) => _b.PawnForest(seat);
        public int SupplyWarriors(SeatId seat) => _b.SupplyWarriors(seat);
        public int SupplyPieces(SeatId seat, string type) => _b.SupplyPieces(seat, type);
        public IReadOnlyList<ItemState> Items(SeatId seat) => _b.Items(seat);
        public Relationship RelationshipOf(SeatId vagabond, SeatId faction) => _b.RelationshipOf(vagabond, faction);
        public IReadOnlyList<ItemType> CraftedItems(SeatId seat) => _b.CraftedItems(seat);
        public string EyrieLeader(SeatId eyrie) => _b.EyrieLeader(eyrie);
        public IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column) => _b.DecreeColumn(eyrie, column);
        public IReadOnlyList<Suit> Supporters(SeatId alliance) { Own(alliance, "supporters"); return _b.Supporters(alliance); }
        public int Officers(SeatId alliance) => _b.Officers(alliance);
        public IReadOnlyList<int> RelicsInForest(int forest) => _b.RelicsInForest(forest);
        public string? RelicType(int relicId) => _b.RelicType(relicId);
        public int? RelicValue(int relicId) => _b.RelicFaceUp(relicId) ? _b.RelicValue(relicId) : null;
        public bool RelicFaceUp(int relicId) => _b.RelicFaceUp(relicId);
        public int? RelicForest(int relicId) => _b.RelicForest(relicId);
        public ClearingId? RelicClearing(int relicId) => _b.RelicClearing(relicId);
        public IReadOnlyList<int> RecoveredRelics(SeatId keepers) => _b.RecoveredRelics(keepers);
        public IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column) => _b.RetinueColumn(keepers, column);
        public int BurrowWarriors(SeatId duchy) => _b.BurrowWarriors(duchy);
        public IReadOnlyList<string> SwayedMinisters(SeatId duchy) => _b.SwayedMinisters(duchy);
        public string? FaceUpPlot(SeatId corvids, ClearingId c) => _b.FaceUpPlot(corvids, c);
        public string? HiddenPlotType(SeatId corvids, ClearingId c) { Own(corvids, "hidden plots"); return _b.HiddenPlotType(corvids, c); }
        public Suit? OutcastSuit(SeatId lizards) => _b.OutcastSuit(lizards);
        public bool OutcastHated(SeatId lizards) => _b.OutcastHated(lizards);
        public int Acolytes(SeatId lizards) => _b.Acolytes(lizards);
        public string Mood(SeatId hundreds) => _b.Mood(hundreds);
        public int HoardTrack(SeatId hundreds, string track) => _b.HoardTrack(hundreds, track);
    }
}
