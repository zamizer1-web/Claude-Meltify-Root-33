using Root33.Core;

namespace Root33.Sim;

/// <summary>A crafting piece that can be activated this turn: a stable key and the suit it gives (its clearing's suit).</summary>
public sealed record CraftPiece(string Key, Suit Suit);

/// <summary>
/// One seat's faction. One instance per seat (two Vagabonds are two instances); instances hold no game state: all of it
/// lives in SimState (shared pieces, hand, VP) and in the faction's typed data (SimGame.Data&lt;T&gt;(seat)).
/// Derive from <see cref="FactionBase"/> and override what the faction changes. Owner of this file: engine (lead).
/// </summary>
public interface IFaction
{
    /// <summary>FactionRegistry id: marquise, eyrie, alliance, vagabond.</summary>
    string Id { get; }
    /// <summary>Root's name for the faction ("Marquise de Cat").</summary>
    string Name { get; }
    /// <summary>Setup letter (Law 5.1.7): Marquise A, Eyrie B, Vagabond C, Alliance D.</summary>
    char SetupOrder { get; }

    /// <summary>Register every frame kind the faction uses (prefix kinds with the faction id: "eyrie.decree").</summary>
    void RegisterSteps(StepRegistry r);
    /// <summary>At game creation: fill the seat's supply and typed data (no pieces on the map yet).</summary>
    void InitSeat(SimGame g, int seat);
    /// <summary>The seat's setup (Law 5.1.7): push the frames that place its starting pieces. Use g.HomeChoices for homes.</summary>
    void Setup(SimGame g, int seat);
    /// <summary>Push the frames of one phase of the seat's own turn (may push none). Daylight decisions between actions set Decision.Open.</summary>
    void BeginPhase(SimGame g, int seat, TurnPhase phase);

    // ---- shared-rule hooks (defaults are Root's general rules)
    /// <summary>Lords of the Forest: wins rule ties it is part of (Eyrie).</summary>
    bool WinsRuleTies { get; }
    /// <summary>Moves regardless of rule (Vagabond's Lone Wanderer).</summary>
    bool IgnoresRuleToMove { get; }
    /// <summary>True when this faction's pieces stop another seat placing in c (the Marquise's Keep).</summary>
    bool BlocksPlacement(SimGame g, int self, int placer, int c);
    /// <summary>Unactivated crafting pieces are filtered by the engine; return every crafting piece the seat has.</summary>
    IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat);
    /// <summary>Activate crafting pieces (default: mark their keys used this turn; Vagabond: exhaust hammers).</summary>
    void SpendCraftingPieces(SimGame g, int seat, IReadOnlyList<CraftPiece> pieces);
    /// <summary>A crafted item arrives (default: the Crafted Items box; Vagabond: onto its character card).</summary>
    void GainCraftedItem(SimGame g, int seat, ItemType item);
    /// <summary>VP for crafting an item card (default: the card's VP; Eyrie: Disdain for Trade).</summary>
    int CraftVp(SimGame g, int seat, CardDef card);
    /// <summary>Whether the seat may attack from c (default: it has warriors there; Vagabond: pawn and an undamaged sword).</summary>
    bool CanAttackFrom(SimGame g, int seat, int c);
    /// <summary>Whether the seat has a piece the defender could hit in c (default: warriors, buildings, tokens, or its pawn).</summary>
    bool PresentIn(SimGame g, int seat, int c);
    /// <summary>Maximum rolled hits in battle (Law 4.3.3.I; default warriors in c; Vagabond: undamaged swords).</summary>
    int MaxRolledHits(SimGame g, int seat, int c);
    /// <summary>Defenseless (Law 4.3.5.II; default no warriors in c).</summary>
    bool IsDefenseless(SimGame g, int seat, int c);
    /// <summary>Attacking pieces left after an ambush (default warriors; Vagabond: 1 for its pawn).</summary>
    int AttackingPieces(SimGame g, int seat, int c);
    /// <summary>A frame kind that takes hits for this seat instead of the shared "hits" frame (Vagabond: damage items), or null.</summary>
    string? TakeHitsKind { get; }
    /// <summary>As defender, deals hits equal to the higher roll and the attacker the lower (Alliance's Guerrilla War).</summary>
    bool DefenderTakesHigherRoll { get; }
    /// <summary>Order a non-active seat loses buildings and tokens in (lower goes first).</summary>
    int LossPriority(string type);
    /// <summary>Whether the seat activates dominance cards normally (false for the Vagabond, which forms coalitions).</summary>
    bool UsesDominanceNormally { get; }
    /// <summary>Extra options at an open point of the seat's own turn; keys must start with "open:".</summary>
    IEnumerable<Option> OpenOptions(SimGame g, int seat);
    void ChooseOpen(SimGame g, int seat, Option o);

    // ---- notifications: every faction hears every event (self = this faction's seat)
    void OnPiecePlaced(SimGame g, int self, int owner, PieceKind kind, string type, int c);
    void OnPieceRemoved(SimGame g, int self, RemovedRecord r);
    /// <summary>Warriors of owner removed from c at once (after OnPieceRemoved for each). The Marquise's Field Hospitals.</summary>
    void OnWarriorsRemoved(SimGame g, int self, int owner, int c, int count, int? remover);
    void OnPiecesMoved(SimGame g, int self, int owner, int from, int to, int count);
    void OnBattleResolved(SimGame g, int self, BattleState b);

    // ---- text
    string PieceName(string type);
    /// <summary>The faction board as every player sees it (public).</summary>
    string BoardText(SimGame g, int seat);
    /// <summary>The faction board as its player reads it: phases and actions in plain words.</summary>
    string RulesText(SimGame g, int seat);

    // ---- reads for the core bridge (IBoardView rows); defaults fit a faction without the feature
    IReadOnlyList<ItemState> Items(SimGame g, int seat);
    Relationship RelationshipWith(SimGame g, int seat, int faction);
    string EyrieLeader(SimGame g, int seat);
    IReadOnlyList<Suit> DecreeColumn(SimGame g, int seat, string column);
    IReadOnlyList<Suit> Supporters(SimGame g, int seat);
    int Officers(SimGame g, int seat);
}

/// <summary>Root's general rules as defaults for every hook.</summary>
public abstract class FactionBase : IFaction
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract char SetupOrder { get; }
    public virtual void RegisterSteps(StepRegistry r) { }
    public virtual void InitSeat(SimGame g, int seat) { }
    public virtual void Setup(SimGame g, int seat) { }
    public virtual void BeginPhase(SimGame g, int seat, TurnPhase phase) { }

    public virtual bool WinsRuleTies => false;
    public virtual bool IgnoresRuleToMove => false;
    public virtual bool BlocksPlacement(SimGame g, int self, int placer, int c) => false;
    public virtual IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat) => Array.Empty<CraftPiece>();
    public virtual void SpendCraftingPieces(SimGame g, int seat, IReadOnlyList<CraftPiece> pieces)
    {
        foreach (var p in pieces) g.State.Turn.UsedCraftPieces.Add(p.Key);
    }
    public virtual void GainCraftedItem(SimGame g, int seat, ItemType item) => g.Seat(seat).CraftedItems.Add(item);
    public virtual int CraftVp(SimGame g, int seat, CardDef card) => card.Vp;
    public virtual bool CanAttackFrom(SimGame g, int seat, int c) => g.Warriors(seat, c) > 0;
    public virtual bool PresentIn(SimGame g, int seat, int c)
    {
        var cs = g.Clearing(c);
        return cs.WarriorsOf(seat) > 0 || cs.Buildings.Any(p => p.Seat == seat) || cs.Tokens.Any(p => p.Seat == seat)
               || g.Seat(seat).PawnClearing == c;
    }
    public virtual int MaxRolledHits(SimGame g, int seat, int c) => g.Warriors(seat, c);
    public virtual bool IsDefenseless(SimGame g, int seat, int c) => g.Warriors(seat, c) == 0;
    public virtual int AttackingPieces(SimGame g, int seat, int c) => g.Warriors(seat, c);
    public virtual string? TakeHitsKind => null;
    public virtual bool DefenderTakesHigherRoll => false;
    public virtual int LossPriority(string type) => 5;
    public virtual bool UsesDominanceNormally => true;
    public virtual IEnumerable<Option> OpenOptions(SimGame g, int seat) => Array.Empty<Option>();
    public virtual void ChooseOpen(SimGame g, int seat, Option o) { }

    public virtual void OnPiecePlaced(SimGame g, int self, int owner, PieceKind kind, string type, int c) { }
    public virtual void OnPieceRemoved(SimGame g, int self, RemovedRecord r) { }
    public virtual void OnWarriorsRemoved(SimGame g, int self, int owner, int c, int count, int? remover) { }
    public virtual void OnPiecesMoved(SimGame g, int self, int owner, int from, int to, int count) { }
    public virtual void OnBattleResolved(SimGame g, int self, BattleState b) { }

    public virtual string PieceName(string type) => type.Replace('.', ' ');
    public virtual string BoardText(SimGame g, int seat) => $"{Name}: (no faction board text yet)";
    public virtual string RulesText(SimGame g, int seat) => $"{Name}: (no rules text yet)";

    public virtual IReadOnlyList<ItemState> Items(SimGame g, int seat) => Array.Empty<ItemState>();
    public virtual Relationship RelationshipWith(SimGame g, int seat, int faction) => Relationship.Indifferent;
    public virtual string EyrieLeader(SimGame g, int seat) => "";
    public virtual IReadOnlyList<Suit> DecreeColumn(SimGame g, int seat, string column) => Array.Empty<Suit>();
    public virtual IReadOnlyList<Suit> Supporters(SimGame g, int seat) => Array.Empty<Suit>();
    public virtual int Officers(SimGame g, int seat) => 0;
}

/// <summary>Faction id to implementation, and E33 character to faction (sheets/characters.json engine column).</summary>
public static class FactionRegistry
{
    private static readonly Dictionary<string, Func<IFaction>> Factories = new()
    {
        ["marquise"] = () => new Marquise(),
        ["eyrie"] = () => new Eyrie(),
        ["alliance"] = () => new Alliance(),
        ["vagabond"] = () => new Vagabond(),
    };

    public static IEnumerable<string> Ids => Factories.Keys;
    public static IFaction Create(string factionId) =>
        Factories.TryGetValue(factionId, out var f) ? f() : throw new ArgumentException("unknown faction " + factionId);

    /// <summary>characters.json engine of a character ("vagabond-tinker").</summary>
    public static string EngineOf(string character) => global::Root33.Core.Sheets.Data.Characters[character].Engine;

    /// <summary>Faction id for an engine: marquise, eyrie, alliance, or vagabond for every vagabond-*.</summary>
    public static string FactionOfEngine(string engine) => engine.StartsWith("vagabond") ? "vagabond" : engine;

    public static string FactionOfCharacter(string character) => FactionOfEngine(EngineOf(character));

    /// <summary>Root's printed name for an engine ("Vagabond (Tinker)").</summary>
    public static string EngineName(string engine) => engine switch
    {
        "marquise" => "Marquise de Cat",
        "eyrie" => "Eyrie Dynasties",
        "alliance" => "Woodland Alliance",
        "vagabond-tinker" => "Vagabond (Tinker)",
        "vagabond-ranger" => "Vagabond (Ranger)",
        "vagabond-thief" => "Vagabond (Thief)",
        _ => engine,
    };
}
