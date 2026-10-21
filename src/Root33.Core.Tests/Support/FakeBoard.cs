using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Tests.Support
{
    /// <summary>
    /// A complete in-memory Root board for tests: implements <see cref="IBoardView"/> and <see cref="IBoardActions"/> with
    /// plain collections. Build it fluently:
    /// <code>
    /// var b = FakeBoard.Autumn()
    ///     .Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress", bot: true)
    ///     .Warriors(0, "Lumière", 3).Building(0, "role.lumiere", PieceTypes.Sawmill).Vp(0, 12);
    /// </code>
    /// A clearing can be named by number (3), by id ("autumn.03"), by role ("role.monolith") or by Continent name
    /// ("The Monolith"); see <see cref="C"/>. Every action is applied to the board and also recorded (see the public logs).
    /// Rule: the seat with the most warriors plus buildings rules (at least one); a tie rules nobody, except that the Eyrie
    /// rules a tie it is part of (Lords of the Forest); a Lizard garden always rules its clearing (read.ruler).
    /// </summary>
    public sealed class FakeBoard : IBoardView, IBoardActions
    {
        // ------------------------------------------------------------ map
        private string _mapName = "custom";
        private readonly List<ClearingId> _clearings = new List<ClearingId>();
        private readonly Dictionary<ClearingId, List<Suit>> _suits = new Dictionary<ClearingId, List<Suit>>();
        private readonly Dictionary<ClearingId, int> _slots = new Dictionary<ClearingId, int>();
        private readonly Dictionary<ClearingId, SortedSet<int>> _paths = new Dictionary<ClearingId, SortedSet<int>>();
        private readonly HashSet<(int, int)> _closedPaths = new HashSet<(int, int)>();
        private readonly Dictionary<ClearingId, SortedSet<int>> _rivers = new Dictionary<ClearingId, SortedSet<int>>();
        private readonly HashSet<ClearingId> _onRiver = new HashSet<ClearingId>();
        private readonly List<ClearingId> _corners = new List<ClearingId>();
        private readonly Dictionary<ClearingId, ClearingId> _opposite = new Dictionary<ClearingId, ClearingId>();
        private readonly HashSet<ClearingId> _ruins = new HashSet<ClearingId>();
        private readonly SortedDictionary<int, List<ClearingId>> _forests = new SortedDictionary<int, List<ClearingId>>();
        private readonly Dictionary<string, ClearingId> _names = new Dictionary<string, ClearingId>(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------ seats and pieces
        private readonly List<SeatId> _seats = new List<SeatId>();
        private readonly Dictionary<SeatId, EngineId> _engines = new Dictionary<SeatId, EngineId>();
        private readonly HashSet<SeatId> _bots = new HashSet<SeatId>();
        private readonly Dictionary<SeatId, int> _vp = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, Suit> _dominance = new Dictionary<SeatId, Suit>();
        private readonly Dictionary<SeatId, SeatId> _coalition = new Dictionary<SeatId, SeatId>();
        private readonly Dictionary<SeatId, List<Suit>> _hands = new Dictionary<SeatId, List<Suit>>();
        private readonly Dictionary<SeatId, List<string>> _legal = new Dictionary<SeatId, List<string>>();
        private readonly Dictionary<(SeatId, ClearingId), int> _warriors = new Dictionary<(SeatId, ClearingId), int>();
        private readonly Dictionary<ClearingId, int> _guards = new Dictionary<ClearingId, int>();
        private readonly Dictionary<(SeatId, ClearingId), List<string>> _buildings = new Dictionary<(SeatId, ClearingId), List<string>>();
        private readonly Dictionary<(SeatId, ClearingId), List<string>> _tokens = new Dictionary<(SeatId, ClearingId), List<string>>();
        private readonly Dictionary<SeatId, ClearingId> _pawnClearing = new Dictionary<SeatId, ClearingId>();
        private readonly Dictionary<SeatId, int> _pawnForest = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, int> _supplyWarriors = new Dictionary<SeatId, int>();
        private readonly Dictionary<(SeatId, string), int> _supplyPieces = new Dictionary<(SeatId, string), int>();
        private readonly Dictionary<SeatId, List<ItemState>> _items = new Dictionary<SeatId, List<ItemState>>();
        private readonly Dictionary<(SeatId, SeatId), Relationship> _relationships = new Dictionary<(SeatId, SeatId), Relationship>();
        private readonly Dictionary<SeatId, List<ItemType>> _crafted = new Dictionary<SeatId, List<ItemType>>();
        private readonly Dictionary<SeatId, string> _eyrieLeader = new Dictionary<SeatId, string>();
        private readonly Dictionary<(SeatId, string), List<Suit>> _columns = new Dictionary<(SeatId, string), List<Suit>>();
        private readonly Dictionary<SeatId, List<Suit>> _supporters = new Dictionary<SeatId, List<Suit>>();
        private readonly Dictionary<SeatId, int> _officers = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, int> _burrow = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, List<string>> _ministers = new Dictionary<SeatId, List<string>>();
        private readonly Dictionary<(SeatId, ClearingId), string> _faceUpPlots = new Dictionary<(SeatId, ClearingId), string>();
        private readonly Dictionary<(SeatId, ClearingId), string> _hiddenPlots = new Dictionary<(SeatId, ClearingId), string>();
        private readonly Dictionary<SeatId, Suit> _outcast = new Dictionary<SeatId, Suit>();
        private readonly HashSet<SeatId> _outcastHated = new HashSet<SeatId>();
        private readonly Dictionary<SeatId, int> _acolytes = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, string> _mood = new Dictionary<SeatId, string>();
        private readonly Dictionary<(SeatId, string), int> _hoard = new Dictionary<(SeatId, string), int>();
        private readonly SortedDictionary<int, FakeRelic> _relics = new SortedDictionary<int, FakeRelic>();
        private SeatId _current;
        private TurnPhase _phase = TurnPhase.Birdsong;
        private int _rootRound;

        /// <summary>Characters given to <see cref="Seat"/> (used by <see cref="TestTable"/> to seat them in the mod).</summary>
        public Dictionary<SeatId, string> Characters { get; } = new Dictionary<SeatId, string>();

        /// <summary>board_actions ids the fake pretends it cannot do (they then do nothing and return false/0).</summary>
        public HashSet<string> UnsupportedActions { get; } = new HashSet<string>(StringComparer.Ordinal);

        // ------------------------------------------------------------ action logs
        public List<string> Log { get; } = new List<string>();
        public List<KeyValuePair<SeatId, string>> Winners { get; } = new List<KeyValuePair<SeatId, string>>();
        public Dictionary<SeatId, string> Banners { get; } = new Dictionary<SeatId, string>();
        public List<KeyValuePair<SeatId, int>> ScoredVp { get; } = new List<KeyValuePair<SeatId, int>>();
        public List<KeyValuePair<SeatId, int>> Draws { get; } = new List<KeyValuePair<SeatId, int>>();
        public Dictionary<SeatId, int> ExtraHits { get; } = new Dictionary<SeatId, int>();
        public Dictionary<SeatId, int> ExtraMaxHits { get; } = new Dictionary<SeatId, int>();
        public HashSet<SeatId> IgnoresFirstHit { get; } = new HashSet<SeatId>();
        public Dictionary<SeatId, IReadOnlyList<ClearingId>> HomeRestrictions { get; } = new Dictionary<SeatId, IReadOnlyList<ClearingId>>();
        public Dictionary<SeatId, IReadOnlyList<int>> StartForestRestrictions { get; } = new Dictionary<SeatId, IReadOnlyList<int>>();
        public HashSet<(SeatId, string)> Abilities { get; } = new HashSet<(SeatId, string)>();
        public List<KeyValuePair<SeatId, string>> Bubbles { get; } = new List<KeyValuePair<SeatId, string>>();
        /// <summary>Overlays currently shown, as "overlayId@anchor" (see <see cref="OverlayKey"/>).</summary>
        public HashSet<string> Overlays { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<SeatId, string> SeatTitles { get; } = new Dictionary<SeatId, string>();
        public bool ClockworkHidden { get; private set; }
        public List<KeyValuePair<SeatId, string>> SubmittedActions { get; } = new List<KeyValuePair<SeatId, string>>();
        public List<SeatId> NativeAiSteps { get; } = new List<SeatId>();
        public HashSet<int> TaggedRelics { get; } = new HashSet<int>();
        public List<KeyValuePair<SeatId, ClearingId>> FreeDigs { get; } = new List<KeyValuePair<SeatId, ClearingId>>();

        public sealed class FakeRelic
        {
            public int Id;
            public string Type;
            public int Value;
            public bool FaceUp;
            public int? Forest;
            public ClearingId? Clearing;
            public SeatId? RecoveredBy;
        }

        // ================================================================= construction

        /// <summary>An empty map with this name ('winter', 'lake', 'mountain', or anything for a made-up map).</summary>
        public static FakeBoard Map(string mapName) => new FakeBoard { _mapName = mapName };

        /// <summary>
        /// Root's Autumn map from sheets/map_autumn.json: 12 clearings (autumn.NN is clearing NN), suits, slots, paths,
        /// river flags, river links (riverLinks), diagonal corner pairs (oppositeCorner), ruins, corners, Continent names and
        /// fixed roles. Only the forests have no sheet column yet; they come from design/autumn-map-verified.json, numbered
        /// here 1-7 as the faces the printed paths enclose (Root's own forest ids are still unknown, builder-questions D2).
        /// </summary>
        public static FakeBoard Autumn()
        {
            var b = new FakeBoard { _mapName = AutumnMap.MapName };
            foreach (var r in AutumnMap.Rows)
            {
                var c = AutumnMap.ClearingOf(r.Id);
                b.Clearing(c.Value, AutumnMap.SuitOf(r), r.Slots, r.Corner, r.Ruin, r.River);
                b._names[r.Id] = c;
                b._names[r.ContinentName] = c;
                if (!string.IsNullOrEmpty(r.Role)) b._names[r.Role] = c;
            }
            foreach (var r in AutumnMap.Rows)
                foreach (var p in r.Paths) b.Path(AutumnMap.ClearingOf(r.Id).Value, AutumnMap.ClearingOf(p).Value);
            foreach (var r in AutumnMap.Rows)
            {
                var c = AutumnMap.ClearingOf(r.Id).Value;
                foreach (var l in r.RiverLinks)
                    if (string.CompareOrdinal(r.Id, l) < 0) b.River(c, AutumnMap.ClearingOf(l).Value);
                if (!string.IsNullOrEmpty(r.OppositeCorner) && string.CompareOrdinal(r.Id, r.OppositeCorner) < 0)
                    b.Opposite(c, AutumnMap.ClearingOf(r.OppositeCorner).Value);
            }
            b.Forest(1, 4, 11, 3, 10).Forest(2, 4, 10, 8, 12).Forest(3, 3, 10, 8, 7, 9).Forest(4, 12, 8, 1)
             .Forest(5, 8, 1, 5, 6).Forest(6, 8, 7, 2, 6).Forest(7, 7, 9, 2);
            return b;
        }

        /// <summary>Renames the map (e.g. the Autumn graph as 'winter', to run the other-map role rules on it).</summary>
        public FakeBoard MapNamed(string mapName) { _mapName = mapName; return this; }

        /// <summary>Adds a clearing.</summary>
        public FakeBoard Clearing(int id, Suit suit, int slots = 2, bool corner = false, bool ruin = false, bool river = false)
        {
            var c = new ClearingId(id);
            if (!_clearings.Contains(c)) _clearings.Add(c);
            _suits[c] = new List<Suit> { suit };
            _slots[c] = slots;
            if (!_paths.ContainsKey(c)) _paths[c] = new SortedSet<int>();
            if (!_rivers.ContainsKey(c)) _rivers[c] = new SortedSet<int>();
            if (corner && !_corners.Contains(c)) _corners.Add(c);
            if (ruin) _ruins.Add(c); else _ruins.Remove(c);
            if (river) _onRiver.Add(c); else _onRiver.Remove(c);
            return this;
        }

        /// <summary>Adds clearings 1..count with suits fox, rabbit, mouse in turn and 2 slots each.</summary>
        public FakeBoard Clearings(int count)
        {
            for (var i = 1; i <= count; i++) Clearing(i, (Suit)((i - 1) % 3));
            return this;
        }

        public FakeBoard Corners(params int[] ids)
        {
            foreach (var id in ids)
                if (!_corners.Contains(new ClearingId(id))) _corners.Add(new ClearingId(id));
            return this;
        }

        public FakeBoard Ruins(params int[] ids) { foreach (var id in ids) _ruins.Add(new ClearingId(id)); return this; }
        public FakeBoard NoRuin(int id) { _ruins.Remove(new ClearingId(id)); return this; }
        public FakeBoard OnRiverAt(params int[] ids) { foreach (var id in ids) _onRiver.Add(new ClearingId(id)); return this; }
        public FakeBoard Suits(int id, params Suit[] suits) { _suits[new ClearingId(id)] = suits.ToList(); return this; }
        public FakeBoard Slots(int id, int slots) { _slots[new ClearingId(id)] = slots; return this; }

        public FakeBoard Path(int a, int b)
        {
            _paths[new ClearingId(a)].Add(b);
            _paths[new ClearingId(b)].Add(a);
            return this;
        }

        /// <summary>Paths as "1-2 2-3 3-4".</summary>
        public FakeBoard Paths(string spec)
        {
            foreach (var pair in spec.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var ab = pair.Split('-');
                Path(int.Parse(ab[0], CultureInfo.InvariantCulture), int.Parse(ab[1], CultureInfo.InvariantCulture));
            }
            return this;
        }

        /// <summary>A path that exists but is closed (Mountain): left out of PathNeighbours until <see cref="OpenPath"/>.</summary>
        public FakeBoard ClosedPath(int a, int b) { Path(a, b); _closedPaths.Add(Key(a, b)); return this; }
        public FakeBoard OpenPath(int a, int b) { _closedPaths.Remove(Key(a, b)); return this; }
        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        public FakeBoard River(int a, int b)
        {
            _rivers[new ClearingId(a)].Add(b);
            _rivers[new ClearingId(b)].Add(a);
            _onRiver.Add(new ClearingId(a));
            _onRiver.Add(new ClearingId(b));
            return this;
        }

        public FakeBoard Opposite(int a, int b)
        {
            _opposite[new ClearingId(a)] = new ClearingId(b);
            _opposite[new ClearingId(b)] = new ClearingId(a);
            return this;
        }

        public FakeBoard Forest(int forest, params int[] clearings)
        {
            _forests[forest] = clearings.Select(i => new ClearingId(i)).ToList();
            return this;
        }

        /// <summary>Gives a clearing an extra name for <see cref="C"/>.</summary>
        public FakeBoard Name(string name, int id) { _names[name] = new ClearingId(id); return this; }

        /// <summary>Resolves a clearing by number ("3"), map_autumn id, role id or Continent name.</summary>
        public ClearingId C(string where)
        {
            if (_names.TryGetValue(where, out var c)) return c;
            if (int.TryParse(where, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return new ClearingId(n);
            throw new ArgumentException("unknown clearing '" + where + "'");
        }

        public ClearingId C(int id) => new ClearingId(id);

        // ================================================================= seats and pieces (builder)

        /// <summary>Adds a seat in turn order. <paramref name="character"/> is the mod character it plays (or null for plain Root).</summary>
        public FakeBoard Seat(int seat, EngineId engine, string character = null, bool bot = false)
        {
            var s = new SeatId(seat);
            if (!_seats.Contains(s)) _seats.Add(s);
            _engines[s] = engine;
            if (bot) _bots.Add(s); else _bots.Remove(s);
            if (character != null) Characters[s] = character; else Characters.Remove(s);
            if (!_supplyWarriors.ContainsKey(s)) _supplyWarriors[s] = GameContext.IsVagabondEngine(engine) ? 0 : 20;
            if (_seats.Count == 1) _current = s;
            return this;
        }

        public FakeBoard Bot(int seat, bool bot = true) { if (bot) _bots.Add(new SeatId(seat)); else _bots.Remove(new SeatId(seat)); return this; }
        public FakeBoard Vp(int seat, int vp) { _vp[new SeatId(seat)] = vp; return this; }
        public FakeBoard Dominance(int seat, Suit? suit) { if (suit.HasValue) _dominance[new SeatId(seat)] = suit.Value; else _dominance.Remove(new SeatId(seat)); return this; }
        public FakeBoard Coalition(int vagabond, int? partner) { if (partner.HasValue) _coalition[new SeatId(vagabond)] = new SeatId(partner.Value); else _coalition.Remove(new SeatId(vagabond)); return this; }
        public FakeBoard Hand(int seat, params Suit[] suits) { _hands[new SeatId(seat)] = suits.ToList(); return this; }
        public FakeBoard Legal(int seat, params string[] actions) { _legal[new SeatId(seat)] = actions.ToList(); return this; }
        public FakeBoard Turn(int seat, TurnPhase phase = TurnPhase.Birdsong) { _current = new SeatId(seat); _phase = phase; return this; }
        /// <summary>Root's own round counter (read.round); 0 = Root keeps none.</summary>
        public FakeBoard RootRound(int round) { _rootRound = round; return this; }

        /// <summary>Sets a seat's warrior count in a clearing (absolute, not added).</summary>
        public FakeBoard Warriors(int seat, string where, int count) => Warriors(seat, C(where), count);
        public FakeBoard Warriors(int seat, int clearing, int count) => Warriors(seat, new ClearingId(clearing), count);

        /// <summary>Esquie's neutral rock guards in a clearing (read.guards; do.place-guards adds to them).</summary>
        public FakeBoard Guards(string where, int count) => Guards(C(where), count);
        public FakeBoard Guards(int clearing, int count) => Guards(new ClearingId(clearing), count);

        public FakeBoard Guards(ClearingId c, int count)
        {
            if (count <= 0) _guards.Remove(c); else _guards[c] = count;
            return this;
        }

        public FakeBoard Warriors(int seat, ClearingId c, int count)
        {
            var k = (new SeatId(seat), c);
            if (count <= 0) _warriors.Remove(k); else _warriors[k] = count;
            return this;
        }

        /// <summary>Adds one building of a type ('sawmill', 'roost', 'base.fox', 'garden', ...).</summary>
        public FakeBoard Building(int seat, string where, string type) => Building(seat, C(where), type);
        public FakeBoard Building(int seat, int clearing, string type) => Building(seat, new ClearingId(clearing), type);
        public FakeBoard Building(int seat, ClearingId c, string type) { Pieces(_buildings, new SeatId(seat), c).Add(type); return this; }

        /// <summary>Removes one building of a type (bare types match suited forms).</summary>
        public FakeBoard RemoveBuilding(int seat, string where, string type) { RemoveOne(_buildings, new SeatId(seat), C(where), type); return this; }

        /// <summary>Adds one token of a type ('keep', 'wood', 'sympathy', 'tunnel', 'plot', ...).</summary>
        public FakeBoard Token(int seat, string where, string type) => Token(seat, C(where), type);
        public FakeBoard Token(int seat, int clearing, string type) => Token(seat, new ClearingId(clearing), type);
        public FakeBoard Token(int seat, ClearingId c, string type) { Pieces(_tokens, new SeatId(seat), c).Add(type); return this; }
        public FakeBoard RemoveToken(int seat, string where, string type) { RemoveOne(_tokens, new SeatId(seat), C(where), type); return this; }

        public FakeBoard Pawn(int seat, string where) { var s = new SeatId(seat); _pawnClearing[s] = C(where); _pawnForest.Remove(s); return this; }
        public FakeBoard PawnInForest(int seat, int forest) { var s = new SeatId(seat); _pawnForest[s] = forest; _pawnClearing.Remove(s); return this; }
        public FakeBoard NoPawn(int seat) { var s = new SeatId(seat); _pawnForest.Remove(s); _pawnClearing.Remove(s); return this; }
        public FakeBoard Supply(int seat, int warriors) { _supplyWarriors[new SeatId(seat)] = warriors; return this; }
        public FakeBoard SupplyOf(int seat, string type, int count) { _supplyPieces[(new SeatId(seat), type)] = count; return this; }
        public FakeBoard Item(int seat, ItemType type, bool damaged = false, bool exhausted = false) { List(_items, new SeatId(seat)).Add(new ItemState(type, damaged, exhausted)); return this; }
        public FakeBoard ClearItems(int seat) { _items.Remove(new SeatId(seat)); return this; }
        public FakeBoard Relationship(int vagabond, int faction, Relationship r) { _relationships[(new SeatId(vagabond), new SeatId(faction))] = r; return this; }
        public FakeBoard Crafted(int seat, params ItemType[] items) { _crafted[new SeatId(seat)] = items.ToList(); return this; }
        public FakeBoard EyrieLeaderIs(int seat, string leader) { _eyrieLeader[new SeatId(seat)] = leader; return this; }
        /// <summary>A Decree (Eyrie) or Retinue (Keepers) column.</summary>
        public FakeBoard Column(int seat, string column, params Suit[] suits) { _columns[(new SeatId(seat), column)] = suits.ToList(); return this; }
        public FakeBoard SupportersAre(int seat, params Suit[] suits) { _supporters[new SeatId(seat)] = suits.ToList(); return this; }
        public FakeBoard OfficersAre(int seat, int n) { _officers[new SeatId(seat)] = n; return this; }
        public FakeBoard Burrow(int seat, int n) { _burrow[new SeatId(seat)] = n; return this; }
        public FakeBoard Ministers(int seat, params string[] ids) { _ministers[new SeatId(seat)] = ids.ToList(); return this; }
        public FakeBoard FaceUpPlotAt(int seat, string where, string type) { _faceUpPlots[(new SeatId(seat), C(where))] = type; Token(seat, where, PieceTypes.Plot); return this; }
        public FakeBoard HiddenPlotAt(int seat, string where, string type) { _hiddenPlots[(new SeatId(seat), C(where))] = type; Token(seat, where, PieceTypes.Plot); return this; }
        public FakeBoard Outcast(int seat, Suit? suit, bool hated = false) { var s = new SeatId(seat); if (suit.HasValue) _outcast[s] = suit.Value; else _outcast.Remove(s); if (hated) _outcastHated.Add(s); else _outcastHated.Remove(s); return this; }
        public FakeBoard AcolytesAre(int seat, int n) { _acolytes[new SeatId(seat)] = n; return this; }
        public FakeBoard MoodIs(int seat, string mood) { _mood[new SeatId(seat)] = mood; return this; }
        public FakeBoard Hoard(int seat, string track, int level) { _hoard[(new SeatId(seat), track)] = level; return this; }
        /// <summary>Explores or raids a ruin: it no longer holds its item (read.has-ruin false) and frees its slot.</summary>
        public FakeBoard RuinEmptied(string where) { _ruins.Remove(C(where)); return this; }

        /// <summary>A relic lying in a forest (Keepers).</summary>
        public FakeBoard Relic(int id, string type, int value, int forest, bool faceUp = false)
        {
            _relics[id] = new FakeRelic { Id = id, Type = type, Value = value, Forest = forest, FaceUp = faceUp };
            return this;
        }

        public FakeRelic RelicById(int id) => _relics[id];
        public FakeBoard CarryRelic(int id, string where) { var r = _relics[id]; r.Forest = null; r.Clearing = C(where); return this; }
        public FakeBoard RecoverRelic(int id, int keepers) { var r = _relics[id]; r.Forest = null; r.Clearing = null; r.RecoveredBy = new SeatId(keepers); r.FaceUp = true; return this; }
        public FakeBoard DropRelic(int id, int forest, bool faceUp) { var r = _relics[id]; r.Forest = forest; r.Clearing = null; r.RecoveredBy = null; r.FaceUp = faceUp; return this; }

        public FakeBoard Unsupported(params string[] boardActionIds) { foreach (var id in boardActionIds) UnsupportedActions.Add(id); return this; }

        private static List<string> Pieces(Dictionary<(SeatId, ClearingId), List<string>> d, SeatId s, ClearingId c)
        {
            if (!d.TryGetValue((s, c), out var l)) d[(s, c)] = l = new List<string>();
            return l;
        }

        private static List<T> List<T>(Dictionary<SeatId, List<T>> d, SeatId s)
        {
            if (!d.TryGetValue(s, out var l)) d[s] = l = new List<T>();
            return l;
        }

        private static bool TypeMatches(string actual, string wanted) =>
            actual == wanted || actual.StartsWith(wanted + ".", StringComparison.Ordinal);

        private static void RemoveOne(Dictionary<(SeatId, ClearingId), List<string>> d, SeatId s, ClearingId c, string type)
        {
            if (!d.TryGetValue((s, c), out var l)) return;
            var i = l.FindIndex(t => TypeMatches(t, type));
            if (i >= 0) l.RemoveAt(i);
        }

        private int Count(Dictionary<(SeatId, ClearingId), List<string>> d, SeatId s, ClearingId c, string type) =>
            d.TryGetValue((s, c), out var l) ? (type == null ? l.Count : l.Count(t => TypeMatches(t, type))) : 0;

        // ================================================================= IBoardView

        public IReadOnlyList<SeatId> Seats() => _seats.ToArray();
        public EngineId EngineOf(SeatId seat) => _engines[seat];
        public bool IsBot(SeatId seat) => _bots.Contains(seat);
        public SeatId CurrentSeat() => _current;
        public TurnPhase Phase() => _phase;
        public int Round() => _rootRound;
        public int Vp(SeatId seat) => _vp.TryGetValue(seat, out var v) ? v : 0;
        public Suit? ActiveDominance(SeatId seat) => _dominance.TryGetValue(seat, out var s) ? s : (Suit?)null;
        public SeatId? CoalitionPartner(SeatId seat) => _coalition.TryGetValue(seat, out var p) ? p : (SeatId?)null;
        public int HandSize(SeatId seat) => _hands.TryGetValue(seat, out var h) ? h.Count : 0;
        public IReadOnlyList<Suit> HandSuits(SeatId seat) => _hands.TryGetValue(seat, out var h) ? h.ToArray() : new Suit[0];
        public IReadOnlyList<string> LegalActions(SeatId seat) => _legal.TryGetValue(seat, out var l) ? l.ToArray() : new string[0];
        public string MapName() => _mapName;
        public IReadOnlyList<ClearingId> Clearings() => _clearings.OrderBy(c => c.Value).ToArray();
        public IReadOnlyList<Suit> SuitsOf(ClearingId c) => _suits[c].ToArray();

        public IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) =>
            _paths[c].Where(n => !_closedPaths.Contains(Key(c.Value, n))).Select(n => new ClearingId(n)).ToArray();

        public IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => _rivers[c].Select(n => new ClearingId(n)).ToArray();
        public bool OnRiver(ClearingId c) => _onRiver.Contains(c);
        public IReadOnlyList<ClearingId> Corners() => _corners.OrderBy(c => c.Value).ToArray();
        public ClearingId? OppositeCorner(ClearingId corner) => _opposite.TryGetValue(corner, out var o) ? o : (ClearingId?)null;
        public bool HasRuin(ClearingId c) => _ruins.Contains(c);
        public int TotalSlots(ClearingId c) => _slots[c];

        public int FreeSlots(ClearingId c) =>
            Math.Max(0, _slots[c] - _seats.Sum(s => Count(_buildings, s, c, null)) - (HasRuin(c) ? 1 : 0));

        public IReadOnlyList<int> ForestsAround(ClearingId c) => _forests.Where(kv => kv.Value.Contains(c)).Select(kv => kv.Key).ToArray();
        public IReadOnlyList<ClearingId> ForestClearings(int forest) => _forests.TryGetValue(forest, out var l) ? l.ToArray() : new ClearingId[0];

        public SeatId? Ruler(ClearingId c)
        {
            foreach (var s in _seats)
                if (_engines[s] == EngineId.Lizards && Count(_buildings, s, c, PieceTypes.Garden) > 0) return s;
            var presence = _seats.Select(s => (seat: s, n: Warriors(s, c) + Buildings(s, c))).Where(x => x.n > 0).ToList();
            if (presence.Count == 0) return null;
            var max = presence.Max(x => x.n);
            var top = presence.Where(x => x.n == max).Select(x => x.seat).ToList();
            if (top.Count == 1) return top[0];
            var eyrie = top.Where(s => _engines[s] == EngineId.Eyrie).ToList();
            return eyrie.Count == 1 ? eyrie[0] : (SeatId?)null;
        }

        public int Warriors(SeatId seat, ClearingId c) => _warriors.TryGetValue((seat, c), out var n) ? n : 0;
        public int Guards(ClearingId c) => _guards.TryGetValue(c, out var n) ? n : 0;
        public int Buildings(SeatId seat, ClearingId c) => Count(_buildings, seat, c, null);
        public int BuildingsOfType(SeatId seat, ClearingId c, string type) => Count(_buildings, seat, c, type);
        public int Tokens(SeatId seat, ClearingId c) => Count(_tokens, seat, c, null);
        public int TokensOfType(SeatId seat, ClearingId c, string type) => Count(_tokens, seat, c, type);
        public ClearingId? PawnClearing(SeatId seat) => _pawnClearing.TryGetValue(seat, out var c) ? c : (ClearingId?)null;
        public int? PawnForest(SeatId seat) => _pawnForest.TryGetValue(seat, out var f) ? f : (int?)null;
        public int SupplyWarriors(SeatId seat) => _supplyWarriors.TryGetValue(seat, out var n) ? n : 0;
        public int SupplyPieces(SeatId seat, string type) => _supplyPieces.TryGetValue((seat, type), out var n) ? n : 0;
        public IReadOnlyList<ItemState> Items(SeatId seat) => _items.TryGetValue(seat, out var l) ? l.ToArray() : new ItemState[0];
        public Relationship RelationshipOf(SeatId vagabond, SeatId faction) => _relationships.TryGetValue((vagabond, faction), out var r) ? r : Root33.Core.Relationship.Indifferent;
        public IReadOnlyList<ItemType> CraftedItems(SeatId seat) => _crafted.TryGetValue(seat, out var l) ? l.ToArray() : new ItemType[0];
        public string EyrieLeader(SeatId eyrie) => _eyrieLeader.TryGetValue(eyrie, out var l) ? l : "builder";
        public IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column) => _columns.TryGetValue((eyrie, column), out var l) ? l.ToArray() : new Suit[0];
        public IReadOnlyList<Suit> Supporters(SeatId alliance) => _supporters.TryGetValue(alliance, out var l) ? l.ToArray() : new Suit[0];
        public int Officers(SeatId alliance) => _officers.TryGetValue(alliance, out var n) ? n : 0;
        public IReadOnlyList<int> RelicsInForest(int forest) => _relics.Values.Where(r => r.Forest == forest).Select(r => r.Id).ToArray();
        public string? RelicType(int relicId) => _relics.TryGetValue(relicId, out var r) ? r.Type : null;
        public int? RelicValue(int relicId) => _relics.TryGetValue(relicId, out var r) ? r.Value : (int?)null;
        public bool RelicFaceUp(int relicId) => _relics.TryGetValue(relicId, out var r) && r.FaceUp;
        public int? RelicForest(int relicId) => _relics.TryGetValue(relicId, out var r) ? r.Forest : null;
        public ClearingId? RelicClearing(int relicId) => _relics.TryGetValue(relicId, out var r) ? r.Clearing : null;
        public IReadOnlyList<int> RecoveredRelics(SeatId keepers) => _relics.Values.Where(r => r.RecoveredBy == keepers).Select(r => r.Id).ToArray();
        public IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column) => _columns.TryGetValue((keepers, column), out var l) ? l.ToArray() : new Suit[0];
        public int BurrowWarriors(SeatId duchy) => _burrow.TryGetValue(duchy, out var n) ? n : 0;
        public IReadOnlyList<string> SwayedMinisters(SeatId duchy) => _ministers.TryGetValue(duchy, out var l) ? l.ToArray() : new string[0];
        public string? FaceUpPlot(SeatId corvids, ClearingId c) => _faceUpPlots.TryGetValue((corvids, c), out var t) ? t : null;
        public string? HiddenPlotType(SeatId corvids, ClearingId c) => _hiddenPlots.TryGetValue((corvids, c), out var t) ? t : null;
        public Suit? OutcastSuit(SeatId lizards) => _outcast.TryGetValue(lizards, out var s) ? s : (Suit?)null;
        public bool OutcastHated(SeatId lizards) => _outcastHated.Contains(lizards);
        public int Acolytes(SeatId lizards) => _acolytes.TryGetValue(lizards, out var n) ? n : 0;
        public string Mood(SeatId hundreds) => _mood.TryGetValue(hundreds, out var m) ? m : "";
        public int HoardTrack(SeatId hundreds, string track) => _hoard.TryGetValue((hundreds, track), out var n) ? n : 0;

        // ================================================================= IBoardActions

        private bool Can(string actionId, string entry)
        {
            Log.Add(entry);
            return !UnsupportedActions.Contains(actionId);
        }

        public static string OverlayKey(string overlayId, SeatId? seat, ClearingId? c, int? forest) =>
            overlayId + "@" + (seat.HasValue ? seat.Value.ToString() : "") + "/" + (c.HasValue ? c.Value.ToString() : "") + "/" + (forest.HasValue ? "f" + forest.Value : "");

        public int PlaceWarriors(SeatId seat, ClearingId c, int count)
        {
            if (!Can(Ids.BoardActions.PlaceWarriors, $"PlaceWarriors {seat} {c} {count}")) return 0;
            var placed = Math.Max(0, Math.Min(count, SupplyWarriors(seat)));
            _supplyWarriors[seat] = SupplyWarriors(seat) - placed;
            Warriors(seat.Value, c, Warriors(seat, c) + placed);
            return placed;
        }

        public bool MoveWarriors(SeatId seat, ClearingId from, ClearingId to, int count)
        {
            if (!Can(Ids.BoardActions.MoveWarriors, $"MoveWarriors {seat} {from} {to} {count}")) return false;
            if (Warriors(seat, from) < count) return false;
            Warriors(seat.Value, from, Warriors(seat, from) - count);
            Warriors(seat.Value, to, Warriors(seat, to) + count);
            return true;
        }

        public bool PlaceTunnel(SeatId duchy, ClearingId c, bool extraPiece)
        {
            if (!Can(Ids.BoardActions.PlaceTunnel, $"PlaceTunnel {duchy} {c} {extraPiece}")) return false;
            var left = SupplyPieces(duchy, PieceTypes.Tunnel);
            if (left > 0) _supplyPieces[(duchy, PieceTypes.Tunnel)] = left - 1;
            else if (!extraPiece) return false;
            Token(duchy.Value, c, PieceTypes.Tunnel);
            return true;
        }

        public bool GrantFreeDig(SeatId duchy, ClearingId c)
        {
            if (!Can(Ids.BoardActions.GrantFreeDig, $"GrantFreeDig {duchy} {c}")) return false;
            FreeDigs.Add(new KeyValuePair<SeatId, ClearingId>(duchy, c));
            return true;
        }

        public bool MoveRelicToForest(int relicId, int forest)
        {
            if (!Can(Ids.BoardActions.MoveRelic, $"MoveRelic {relicId} f{forest}")) return false;
            if (!_relics.TryGetValue(relicId, out var r)) return false;
            r.Forest = forest;
            r.Clearing = null;
            return true;
        }

        public bool TagRelic(int relicId)
        {
            if (!Can(Ids.BoardActions.TagRelic, $"TagRelic {relicId}")) return false;
            return TaggedRelics.Add(relicId) || true;
        }

        public void DeclareWinner(SeatId seat, string winConditionId)
        {
            Log.Add($"DeclareWinner {seat} {winConditionId}");
            Winners.Add(new KeyValuePair<SeatId, string>(seat, winConditionId));
        }

        public void ScoreVp(SeatId seat, int vp)
        {
            Log.Add($"ScoreVp {seat} {vp}");
            ScoredVp.Add(new KeyValuePair<SeatId, int>(seat, vp));
            _vp[seat] = Vp(seat) + vp;
        }

        public void DrawCards(SeatId seat, int count)
        {
            Log.Add($"DrawCards {seat} {count}");
            Draws.Add(new KeyValuePair<SeatId, int>(seat, count));
        }

        public void AddHits(SeatId seat, int hits) { Log.Add($"AddHits {seat} {hits}"); ExtraHits[seat] = (ExtraHits.TryGetValue(seat, out var h) ? h : 0) + hits; }
        public void AddMaxHits(SeatId vagabond, int extra) { Log.Add($"AddMaxHits {vagabond} {extra}"); ExtraMaxHits[vagabond] = (ExtraMaxHits.TryGetValue(vagabond, out var h) ? h : 0) + extra; }
        public void IgnoreFirstHit(SeatId seat) { Log.Add($"IgnoreFirstHit {seat}"); IgnoresFirstHit.Add(seat); }

        /// <summary>do.place-guards: places them all unless <see cref="GuardsPlaceable"/> caps the count.</summary>
        public int PlaceGuards(ClearingId c, int count)
        {
            if (!Can(Ids.BoardActions.PlaceGuards, $"PlaceGuards {c} {count}")) return 0;
            var placed = Math.Max(0, GuardsPlaceable.HasValue ? Math.Min(count, GuardsPlaceable.Value) : count);
            Guards(c, Guards(c) + placed);
            return placed;
        }

        /// <summary>How many guards do.place-guards may still place (null: no limit), to test a short placement.</summary>
        public int? GuardsPlaceable { get; set; }

        public void RestrictHomeChoice(SeatId seat, IReadOnlyList<ClearingId> allowed) { Log.Add($"RestrictHome {seat} {string.Join(",", allowed)}"); HomeRestrictions[seat] = allowed.ToArray(); }
        public void RestrictStartForest(SeatId vagabond, IReadOnlyList<int> forests) { Log.Add($"RestrictStartForest {vagabond} {string.Join(",", forests)}"); StartForestRestrictions[vagabond] = forests.ToArray(); }
        public void GrantAbility(SeatId seat, string abilityKey) { Log.Add($"GrantAbility {seat} {abilityKey}"); Abilities.Add((seat, abilityKey)); }
        public void ShowBanner(SeatId seat, string winConditionId) { Log.Add($"ShowBanner {seat} {winConditionId}"); Banners[seat] = winConditionId; }
        public void ClearBanner(SeatId seat) { Log.Add($"ClearBanner {seat}"); Banners.Remove(seat); }
        public void ShowBubble(SeatId seat, string barkId) { Log.Add($"ShowBubble {seat} {barkId}"); Bubbles.Add(new KeyValuePair<SeatId, string>(seat, barkId)); }

        public void ShowOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest)
        {
            Log.Add("ShowOverlay " + OverlayKey(overlayId, seat, c, forest));
            Overlays.Add(OverlayKey(overlayId, seat, c, forest));
        }

        public void HideOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest)
        {
            Log.Add("HideOverlay " + OverlayKey(overlayId, seat, c, forest));
            Overlays.Remove(OverlayKey(overlayId, seat, c, forest));
        }

        /// <summary>Whether an overlay is shown at any anchor.</summary>
        public bool OverlayShown(string overlayId) => Overlays.Any(o => o.StartsWith(overlayId + "@", StringComparison.Ordinal));

        public void SetSeatTitle(SeatId seat, string title) { Log.Add($"SetSeatTitle {seat} {title}"); SeatTitles[seat] = title; }
        public void HideClockworkBots() { Log.Add("HideClockworkBots"); ClockworkHidden = true; }

        public bool SubmitAction(SeatId seat, string actionKey)
        {
            if (!Can(Ids.BoardActions.SubmitAction, $"SubmitAction {seat} {actionKey}")) return false;
            SubmittedActions.Add(new KeyValuePair<SeatId, string>(seat, actionKey));
            return LegalActions(seat).Contains(actionKey);
        }

        public void UseNativeAi(SeatId seat) { Log.Add($"UseNativeAi {seat}"); NativeAiSteps.Add(seat); }
    }
}
