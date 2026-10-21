namespace Root33.Sim;

/// <summary>One legal choice: a stable key (what `do` accepts besides the number) and plain-English text.</summary>
public sealed class Option
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public Option() { }
    public Option(string key, string text) { Key = key; Text = text; }
    public override string ToString() => $"{Key}: {Text}";
}

/// <summary>
/// The one pending question of the game: owned by one seat, with numbered options (1-based in the CLI).
/// A decision owed by a seat that is not the active seat is asked of that seat when it is human (Root asks the defender
/// about ambushes, Field Hospitals and so on); for a bot seat the engine applies <see cref="DefaultKey"/> (or the first
/// option) and logs it. A decision with exactly one option and <see cref="Open"/> false is applied at once.
/// </summary>
public sealed class Decision
{
    public int Seat { get; set; }
    /// <summary>The frame kind that asked (filled by the engine).</summary>
    public string Kind { get; set; } = "";
    public string Prompt { get; set; } = "";
    public List<Option> Options { get; set; } = new();
    /// <summary>The documented default for a non-active seat (null: the first option).</summary>
    public string? DefaultKey { get; set; }
    /// <summary>
    /// True at a point between actions in the active seat's Birdsong or Daylight: the engine adds the card options that
    /// may be used "in Daylight" (activate or take a dominance card, Tax Collector, Codebreakers) and the faction's
    /// <see cref="IFaction.OpenOptions"/>. Their keys start with "card:" or "open:" and never reach the frame.
    /// </summary>
    public bool Open { get; set; }

    public Decision() { }
    public Decision(int seat, string prompt) { Seat = seat; Prompt = prompt; }
    public Decision Add(string key, string text) { Options.Add(new Option(key, text)); return this; }
    public Option? Find(string key) => Options.FirstOrDefault(o => o.Key == key);
}

/// <summary>
/// One running procedure on the decision stack (a phase, an action, a battle step). Its handler is the
/// <see cref="IStep"/> registered under <see cref="Kind"/>. Arguments live in the I (ints), S (strings) and L (list)
/// bags so every frame serialises without per-type JSON contracts.
/// </summary>
public sealed class Frame
{
    public string Kind { get; set; } = "";
    public int Seat { get; set; }
    public int Step { get; set; }
    public Dictionary<string, int> I { get; set; } = new();
    public Dictionary<string, string> S { get; set; } = new();
    public List<int> L { get; set; } = new();

    public Frame() { }
    public Frame(string kind, int seat) { Kind = kind; Seat = seat; }

    public int Get(string k, int dflt = 0) => I.TryGetValue(k, out var v) ? v : dflt;
    public Frame Set(string k, int v) { I[k] = v; return this; }
    public bool Has(string k) => I.ContainsKey(k);
    public string? Str(string k) => S.TryGetValue(k, out var v) ? v : null;
    public Frame SetStr(string k, string? v) { if (v == null) S.Remove(k); else S[k] = v; return this; }
    public override string ToString() => $"{Kind}@seat{Seat}#{Step}";
}

/// <summary>
/// A procedure's handler. Contract: <see cref="Next"/> either changes the state (pops itself, pushes frames, advances
/// its Step) and returns null, or returns a decision WITHOUT changing anything (no RNG, no pieces). The engine then
/// calls <see cref="Choose"/> with the option picked, on the same frame.
/// </summary>
public interface IStep
{
    string Kind { get; }
    Decision? Next(SimGame g, Frame f);
    void Choose(SimGame g, Frame f, Option o);
}

public sealed class LambdaStep : IStep
{
    private readonly Func<SimGame, Frame, Decision?> _next;
    private readonly Action<SimGame, Frame, Option> _choose;
    public LambdaStep(string kind, Func<SimGame, Frame, Decision?> next, Action<SimGame, Frame, Option>? choose)
    {
        Kind = kind; _next = next;
        _choose = choose ?? ((_, f, o) => throw new InvalidOperationException($"{f.Kind} asked nothing but got {o.Key}"));
    }
    public string Kind { get; }
    public Decision? Next(SimGame g, Frame f) => _next(g, f);
    public void Choose(SimGame g, Frame f, Option o) => _choose(g, f, o);
}

/// <summary>Frame kind to handler. Shared steps register in SimGame; each faction registers its own in RegisterSteps.</summary>
public sealed class StepRegistry
{
    private readonly Dictionary<string, IStep> _steps = new();
    public void Add(IStep s)
    {
        if (_steps.ContainsKey(s.Kind)) throw new InvalidOperationException("duplicate step kind " + s.Kind);
        _steps[s.Kind] = s;
    }
    public void Add(string kind, Func<SimGame, Frame, Decision?> next, Action<SimGame, Frame, Option>? choose = null) =>
        Add(new LambdaStep(kind, next, choose));
    public IStep Get(string kind) =>
        _steps.TryGetValue(kind, out var s) ? s : throw new InvalidOperationException("no step registered for frame kind " + kind);
    public bool Contains(string kind) => _steps.ContainsKey(kind);
    public IEnumerable<string> Kinds => _steps.Keys;
}
