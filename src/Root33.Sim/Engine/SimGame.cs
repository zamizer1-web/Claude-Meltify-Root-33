using System.Text.Json;
using Root33.Core;

namespace Root33.Sim;

/// <summary>
/// The tabletop engine: the state, the turn and phase machine, the decision loop, and the shared helpers factions call
/// (cards, VP, pieces, rule). Rules procedures live in Rules.cs (Move, Battle, Craft, Dominance, card effects).
/// Owner: engine (lead). Factions (B, C) and the core bridge (D) call these members and never edit this file.
/// </summary>
public sealed partial class SimGame
{
    public const int WinVp = 30;
    public const int HandLimit = 5;

    public SimState State { get; }
    public SimMap Map { get; } = SimMap.Autumn();
    public CoreBridge Bridge { get; private set; } = null!;
    /// <summary>Tests only: dice results to use before the RNG (each 0-3).</summary>
    public Queue<int> TestDice { get; } = new();

    private readonly List<IFaction> _factions = new();
    private readonly Dictionary<int, object> _data = new();
    private static StepRegistry? _steps;

    public static StepRegistry Steps => _steps ??= BuildSteps();

    private static StepRegistry BuildSteps()
    {
        var r = new StepRegistry();
        RegisterSharedSteps(r);
        foreach (var id in FactionRegistry.Ids) FactionRegistry.Create(id).RegisterSteps(r);
        return r;
    }

    private SimGame(SimState state)
    {
        State = state;
        foreach (var s in state.Seats) _factions.Add(FactionRegistry.Create(s.Faction));
    }

    // =====================================================================================================  creation

    public sealed record SeatSpec(string Character, bool Human);

    /// <summary>A new game: seats in clockwise order, first player and ruins by the seed, setup ready to run.</summary>
    public static SimGame New(IReadOnlyList<SeatSpec> seats, int seed, string spoiler = "safe")
    {
        var st = new SimState { Seed = seed, Spoiler = spoiler };
        st.Rng = SplitMix((ulong)(uint)seed * 2654435761UL + 0x9E3779B97F4A7C15UL);
        st.BotRng = SplitMix(st.Rng ^ 0xD1B54A32D192ED03UL);
        for (int i = 0; i < seats.Count; i++)
        {
            var engine = FactionRegistry.EngineOf(seats[i].Character);
            st.Seats.Add(new SeatState
            {
                Seat = i, Character = seats[i].Character, Engine = engine,
                Faction = FactionRegistry.FactionOfEngine(engine), Human = seats[i].Human,
            });
        }
        var g = new SimGame(st);
        g.Bridge = new CoreBridge(g);
        st.FirstSeat = g.Random(seats.Count);

        foreach (var c in g.Map.Clearings.Values)
            st.Clearings[c.Id] = new ClearingState { Id = c.Id, Ruin = c.Ruin };
        // Law 5.1.4: the four ruins hold a Bag, Boots, a Hammer and a Sword, face down.
        var ruinItems = new List<ItemType> { ItemType.Bag, ItemType.Boots, ItemType.Hammer, ItemType.Sword };
        foreach (var c in st.Clearings.Values.Where(c => c.Ruin).OrderBy(c => c.Id))
        {
            int k = g.Random(ruinItems.Count);
            c.RuinItem = ruinItems[k]; ruinItems.RemoveAt(k);
        }
        st.ItemSupply = new List<ItemType>
        {
            ItemType.Boots, ItemType.Boots, ItemType.Bag, ItemType.Bag, ItemType.Crossbow, ItemType.Hammer,
            ItemType.Sword, ItemType.Sword, ItemType.Tea, ItemType.Tea, ItemType.Coins, ItemType.Coins,
        };
        // Law 5.1.3: two players play without the dominance cards.
        st.Deck = Cards.All.Where(c => seats.Count > 2 || c.Kind != CardKind.Dominance).Select(c => c.Id).ToList();
        g.Shuffle(st.Deck);
        for (int i = 0; i < seats.Count; i++) g.Faction(i).InitSeat(g, i);

        g.Log($"New game, seed {seed}, spoiler mode {spoiler}. Seats (clockwise): " +
              string.Join(", ", st.Seats.Select(s => $"{g.SeatName(s.Seat)} {FactionRegistry.EngineName(s.Engine)}" + (s.Human ? "" : " [bot]"))) +
              $". {g.SeatName(st.FirstSeat)} takes the first turn.");
        foreach (var s in g.TurnOrder()) g.Draw(s, 3);

        for (int i = 0; i < seats.Count; i++) g.Bridge.OnSeatAssigned(i);
        g.Bridge.OnGameStart();
        g.Bridge.OnMapLoaded();

        st.SetupQueue = g.TurnOrder().OrderBy(s => g.Faction(s).SetupOrder).ToList();
        g.Advance();
        return g;
    }

    // =====================================================================================================  save / load

    public string ToJson()
    {
        foreach (var kv in _data)
            State.Seats[kv.Key].FactionData = JsonSerializer.SerializeToNode(kv.Value, kv.Value.GetType(), SimJson.Options);
        State.Mod.CoreState = Bridge.OnSave() ?? "";
        return JsonSerializer.Serialize(State, SimJson.Options);
    }

    public static SimGame FromJson(string json)
    {
        var st = JsonSerializer.Deserialize<SimState>(json, SimJson.Options) ?? throw new InvalidDataException("empty save");
        var g = new SimGame(st);
        g.Bridge = new CoreBridge(g);
        g.Bridge.OnLoad(st.Mod.CoreState ?? "");
        return g;
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public static SimGame Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>A faction's typed data for a seat (created empty on first use, saved with the game).</summary>
    public T Data<T>(int seat) where T : class, new()
    {
        if (_data.TryGetValue(seat, out var o)) return (T)o;
        var node = State.Seats[seat].FactionData;
        var v = node == null ? new T() : node.Deserialize<T>(SimJson.Options) ?? new T();
        _data[seat] = v;
        return v;
    }

    // =====================================================================================================  seats

    public int SeatCount => State.Seats.Count;
    public SeatState Seat(int seat) => State.Seats[seat];
    public IFaction Faction(int seat) => _factions[seat];
    public IEnumerable<int> SeatsOf(string factionId) => State.Seats.Where(s => s.Faction == factionId).Select(s => s.Seat);
    public int ActiveSeat => State.SetupDone ? State.Turn.Seat : State.SetupSeat;
    public int NextSeat(int seat) => (seat + 1) % SeatCount;
    /// <summary>All seats in turn order starting from the first player (or from a given seat).</summary>
    public IEnumerable<int> TurnOrder(int? from = null)
    {
        int s = from ?? State.FirstSeat;
        for (int i = 0; i < SeatCount; i++) yield return (s + i) % SeatCount;
    }
    public string SeatName(int seat) => Render.Who(this, seat);
    public string ClearingName(int c) => $"{Map.Clearings[c].Name} [c{c}, {Cards.SuitName(Map.Clearings[c].Suit)}]";
    public Suit SuitOf(int c) => Map.Clearings[c].Suit;
    public ClearingState Clearing(int c) => State.Clearings[c];
    public IEnumerable<int> ClearingIds => Map.Clearings.Keys;

    // =====================================================================================================  RNG

    private static ulong SplitMix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static int Next(ref ulong s, int n)
    {
        s = SplitMix(s);
        return (int)(s % (ulong)n);
    }

    /// <summary>A uniform int in [0, n) from the game RNG (kept in the state, so games reproduce).</summary>
    public int Random(int n) { var s = State.Rng; var r = Next(ref s, n); State.Rng = s; return r; }
    public int BotRandom(int n) { var s = State.BotRng; var r = Next(ref s, n); State.BotRng = s; return r; }
    public int RollDie() => TestDice.Count > 0 ? TestDice.Dequeue() : Random(4);
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = Random(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }

    // =====================================================================================================  log

    public void Log(string text, int? only = null) =>
        State.Log.Add(new LogEntry { N = State.Log.Count + 1, Round = State.Round, Only = only, Text = text });

    public IEnumerable<LogEntry> LogFor(int? seat, int since = 0) =>
        State.Log.Where(e => e.N > since && (e.Only == null || e.Only == seat));

    // =====================================================================================================  decision loop

    public Decision? Pending => State.Pending;

    public void Push(Frame f) => State.Stack.Add(f);
    /// <summary>Push several frames to run in the order given.</summary>
    public void PushSequence(params Frame[] frames) { for (int i = frames.Length - 1; i >= 0; i--) Push(frames[i]); }
    /// <summary>Remove a frame (normally the top one: a step popping itself).</summary>
    public void Pop(Frame f)
    {
        int i = State.Stack.LastIndexOf(f);
        if (i < 0) throw new InvalidOperationException("pop of a frame not on the stack: " + f);
        State.Stack.RemoveAt(i);
    }
    /// <summary>The frame under f (its caller), or null.</summary>
    public Frame? Parent(Frame f)
    {
        int i = State.Stack.LastIndexOf(f);
        return i > 0 ? State.Stack[i - 1] : null;
    }

    /// <summary>Run the game until a decision is owed by the active seat, or the game ends.</summary>
    public void Advance()
    {
        for (int guard = 0; ; guard++)
        {
            if (guard > 200000) throw new InvalidOperationException("engine loop without progress at " + string.Join(" > ", State.Stack));
            if (!State.GameOver && Bridge.IsGameOver(out var modText))
                EndGame(State.Mod.DeclaredWinners.ToList(), modText);
            if (State.GameOver) { State.Pending = null; return; }
            if (State.Stack.Count == 0) { TurnMachine(); continue; }
            var f = State.Stack[^1];
            var d = Steps.Get(f.Kind).Next(this, f);
            if (d == null) continue;
            d.Kind = f.Kind;
            if (d.Open) AddOpenOptions(d);
            if (d.Options.Count == 0) throw new InvalidOperationException($"stuck: {f} asked '{d.Prompt}' with no options");
            if (d.Seat != ActiveSeat)
            {
                var o = (d.DefaultKey != null ? d.Find(d.DefaultKey) : null) ?? d.Options[0];
                Log($"{SeatName(d.Seat)} (not their turn, decided by the default): {d.Prompt} -> {o.Text}");
                Apply(d, o);
                continue;
            }
            if (d.Options.Count == 1 && !d.Open) { Apply(d, d.Options[0]); continue; }
            State.Pending = d;
            return;
        }
    }

    /// <summary>The deciding seat picks an option by number (1-based) or key.</summary>
    public void Choose(int seat, string choice)
    {
        var d = State.Pending ?? throw new SimUsageException(State.GameOver ? "the game is over" : "no decision is pending");
        if (seat != d.Seat) throw new SimUsageException($"it is {SeatName(d.Seat)}'s decision, not seat {seat + 1}'s");
        Option? o = null;
        if (int.TryParse(choice, out var n) && n >= 1 && n <= d.Options.Count) o = d.Options[n - 1];
        o ??= d.Find(choice);
        if (o == null) throw new SimUsageException($"'{choice}' is not one of the {d.Options.Count} options (use a number or a key)");
        Apply(d, o);
        Advance();
    }

    private void Apply(Decision d, Option o)
    {
        State.Pending = null;
        State.DecisionsTaken++;
        if (o.Key.StartsWith("card:")) { ChooseCardOption(d.Seat, o); return; }
        if (o.Key.StartsWith("open:")) { Faction(d.Seat).ChooseOpen(this, d.Seat, o); return; }
        var f = State.Stack[^1];
        Steps.Get(f.Kind).Choose(this, f, o);
    }

    // =====================================================================================================  turn machine

    /// <summary>Runs when the stack is empty: setup seat by seat, then each turn's Birdsong, Daylight and Evening.</summary>
    private void TurnMachine()
    {
        if (!State.SetupDone)
        {
            if (State.SetupQueue.Count > 0)
            {
                int s = State.SetupQueue[0];
                State.SetupQueue.RemoveAt(0);
                State.SetupSeat = s;
                Log($"Setup: {SeatName(s)} sets up the {Faction(s).Name}.");
                Bridge.OnSetupHome(s);
                Faction(s).Setup(this, s);
                return;
            }
            State.SetupDone = true;
            State.SetupSeat = -1;
            Bridge.OnSetupComplete();
            State.Turn = new TurnState { Seat = State.FirstSeat, Phase = TurnPhase.Birdsong, Step = 0 };
            return;
        }

        var t = State.Turn;
        int seat = t.Seat;
        switch (t.Step)
        {
            case 0:
                t.Phase = TurnPhase.Birdsong;
                if (seat == State.FirstSeat)
                {
                    State.Round++;
                    Log($"=== Round {State.Round} ===");
                    Bridge.OnRoundStart(State.Round);
                }
                Log($"--- {SeatName(seat)} ({Faction(seat).Name}): Birdsong ---");
                Bridge.OnBirdsongStart(seat);
                if (State.GameOver) return;
                CheckDominanceWin(seat);
                if (State.GameOver) return;
                t.Step = 1;
                if (HasPersistent(seat, "better-burrow-bank")) Push(new Frame("card.burrow-bank", seat));
                return;
            case 1:
                t.Step = 2;
                Faction(seat).BeginPhase(this, seat, TurnPhase.Birdsong);
                return;
            case 2:
                t.Step = 3;
                Push(new Frame("card.birdsong", seat));
                return;
            case 3:
                t.Phase = TurnPhase.Daylight;
                Log($"--- {SeatName(seat)}: Daylight ---");
                t.Step = 4;
                if (HasPersistent(seat, "command-warren")) Push(new Frame("card.command-warren", seat));
                return;
            case 4:
                t.Step = 5;
                Faction(seat).BeginPhase(this, seat, TurnPhase.Daylight);
                return;
            case 5:
                t.Phase = TurnPhase.Evening;
                Log($"--- {SeatName(seat)}: Evening ---");
                Bridge.OnEveningStart(seat);
                t.Step = 6;
                if (HasPersistent(seat, "cobbler")) Push(new Frame("card.cobbler", seat));
                return;
            case 6:
                t.Step = 7;
                Faction(seat).BeginPhase(this, seat, TurnPhase.Evening);
                return;
            default:
                Bridge.OnEveningEnd(seat);
                if (State.GameOver) return;
                Bridge.OnTurnEnd(seat);
                State.Turn = new TurnState { Seat = NextSeat(seat), Phase = TurnPhase.Birdsong, Step = 0 };
                return;
        }
    }

    /// <summary>Call after each of a seat's actions resolves (hook.action-resolved).</summary>
    public void ActionResolved(int seat) => Bridge.OnActionResolved(seat);

    // =====================================================================================================  VP and game end

    /// <summary>Score (or lose, n &lt; 0) VP. A seat with an active dominance card scores nothing. 30 VP wins at once.</summary>
    public void AddVp(int seat, int n, string why)
    {
        if (n == 0 || State.GameOver) return;
        var s = Seat(seat);
        if (s.Dominance != null && n > 0) { Log($"{SeatName(seat)} would score {n} VP ({why}) but has an active dominance card."); return; }
        int before = s.Vp;
        s.Vp = Math.Max(0, s.Vp + n);
        if (s.Vp == before) return;
        Log($"{SeatName(seat)} {(n > 0 ? "scores" : "loses")} {Math.Abs(s.Vp - before)} VP ({why}): now {s.Vp} VP.");
        Bridge.OnVpChanged(seat, before, s.Vp);
        if (s.Vp >= WinVp && !State.GameOver)
            EndGame(WithPartners(seat), $"{SeatName(seat)} as the {Faction(seat).Name} reached {s.Vp} victory points");
    }

    /// <summary>The winner plus any Vagabond in coalition with it.</summary>
    public List<int> WithPartners(int seat)
    {
        var l = new List<int> { seat };
        l.AddRange(State.Seats.Where(s => s.CoalitionWith == seat).Select(s => s.Seat));
        return l;
    }

    public void EndGame(List<int> winners, string text)
    {
        if (State.GameOver) return;
        State.GameOver = true;
        State.Winners = winners;
        State.GameOverText = text;
        State.Pending = null;
        Log("GAME OVER: " + text + ".");
        Bridge.OnGameOver(winners);
    }

    // =====================================================================================================  cards

    /// <summary>Draw n cards (reshuffling the discard pile into the deck when it runs out).</summary>
    public int Draw(int seat, int n)
    {
        int drawn = 0;
        var names = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (State.Deck.Count == 0)
            {
                if (State.Discard.Count == 0) { Log("The deck and the discard pile are empty: no card to draw."); break; }
                State.Deck.AddRange(State.Discard);
                State.Discard.Clear();
                Shuffle(State.Deck);
                Log("The deck ran out: the discard pile is shuffled into a new deck.");
            }
            int c = State.Deck[^1];
            State.Deck.RemoveAt(State.Deck.Count - 1);
            Seat(seat).Hand.Add(c);
            names.Add(Cards.Label(c));
            drawn++;
        }
        if (drawn > 0)
        {
            Log($"{SeatName(seat)} draws {drawn} card{(drawn == 1 ? "" : "s")} (hand {Seat(seat).Hand.Count}).");
            Log($"You drew: {string.Join(", ", names)}.", seat);
        }
        return drawn;
    }

    /// <summary>Put a card on the discard pile (a dominance card becomes available instead, Law 3.3.3).</summary>
    public void ToDiscard(int card)
    {
        if (Cards.Get(card).Kind == CardKind.Dominance) State.AvailableDominance.Add(card);
        else State.Discard.Add(card);
    }

    /// <summary>Spend or discard a card from a seat's hand (public).</summary>
    public void DiscardFromHand(int seat, int card, string why)
    {
        if (!Seat(seat).Hand.Remove(card)) throw new InvalidOperationException($"{SeatName(seat)} does not hold card {card}");
        ToDiscard(card);
        Log($"{SeatName(seat)} {why}: {Cards.Label(card)}.");
    }

    /// <summary>Discard a persistent card from the play area.</summary>
    public void DiscardFromPlay(int seat, int card, string why)
    {
        Seat(seat).PlayArea.Remove(card);
        ToDiscard(card);
        Log($"{SeatName(seat)} {why}: {Cards.Label(card)}.");
    }

    public bool HasPersistent(int seat, string key) => Seat(seat).PlayArea.Any(c => Cards.Get(c).Key == key);
    public int? PersistentCard(int seat, string key) => Seat(seat).PlayArea.Where(c => Cards.Get(c).Key == key).Select(c => (int?)c).FirstOrDefault();
    /// <summary>Cards in hand that may be spent as the suit (birds wild; Law 2.1.1).</summary>
    public IEnumerable<int> CardsMatching(int seat, Suit suit) => Seat(seat).Hand.Where(c => Cards.Matches(c, suit));

    // =====================================================================================================  mod effects

    public bool HasAbility(int seat, string ability) => State.Mod.Abilities.TryGetValue(seat, out var l) && l.Contains(ability);
    public int GuardFloor(int seat, int c) => State.Mod.GuardFloors.Where(g => g.Seat == seat && g.Clearing == c).Select(g => g.Floor).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The home choices for a seat's setup: the faction's legal homes, narrowed to CoreBridge.ForcedHome(seat) when the
    /// core forces one (clearings, or forests for a Vagabond). If nothing legal is left, the legal homes are kept and a
    /// warning is logged.
    /// </summary>
    public List<int> HomeChoices(int seat, IEnumerable<int> legal)
    {
        var l = legal.ToList();
        var forced = Bridge.ForcedHome(seat);
        if (forced == null) return l;
        var both = l.Where(forced.Contains).ToList();
        if (both.Count > 0) return both;
        Log($"(Root 33 asked {SeatName(seat)} to start in {string.Join("/", forced)}, which is not legal now; any legal home is allowed.)");
        return l;
    }
}

public sealed class SimUsageException : Exception
{
    public SimUsageException(string m) : base(m) { }
}
