using Root33.Core;

namespace Root33.Sim;

public enum CardKind { Item, Persistent, Immediate, Ambush, Dominance }

/// <summary>One card of Root's 54-card base deck. Card ids are the index in <see cref="Cards.All"/> (0-53).</summary>
public sealed class CardDef
{
    public int Id { get; init; }
    /// <summary>Stable name key shared by copies ("tax-collector"); used for the no-duplicate rule and effects.</summary>
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public Suit Suit { get; init; }
    public CardKind Kind { get; init; }
    /// <summary>Crafting cost: one crafting piece per entry, of that suit.</summary>
    public Suit[] Cost { get; init; } = Array.Empty<Suit>();
    /// <summary>Crafting pieces of any suit on top of <see cref="Cost"/> (Royal Claim: 4).</summary>
    public int AnyCost { get; init; }
    public ItemType? Item { get; init; }
    public int Vp { get; init; }
    public string Text { get; init; } = "";
    public bool Craftable => Kind is CardKind.Item or CardKind.Persistent or CardKind.Immediate;
}

/// <summary>
/// Root's base (Woodland) deck: 54 cards (fox 14, rabbit 13, mouse 13, bird 14), 5 ambushes, 4 dominance cards.
/// The Root Database's deck page shows the cards as images, so names, costs and texts follow the printed base deck
/// (see docs/SIM.md "Cards"). Effect keys handled by the engine: armorers, brutal-tactics, sappers, scouting-party,
/// royal-claim, stand-and-deliver, tax-collector, codebreakers, better-burrow-bank, cobbler, command-warren, favor-*.
/// </summary>
public static class Cards
{
    public static readonly IReadOnlyList<CardDef> All = Build();

    public static CardDef Get(int id) => All[id];

    static List<CardDef> Build()
    {
        var l = new List<CardDef>();
        void Add(int copies, string key, string name, Suit suit, CardKind kind, Suit[]? cost, int any, ItemType? item, int vp, string text)
        {
            for (int i = 0; i < copies; i++)
                l.Add(new CardDef { Id = l.Count, Key = key, Name = name, Suit = suit, Kind = kind, Cost = cost ?? Array.Empty<Suit>(), AnyCost = any, Item = item, Vp = vp, Text = text });
        }
        const Suit F = Suit.Fox, R = Suit.Rabbit, M = Suit.Mouse, B = Suit.Bird;
        const string AmbushText = "Ambush: as defender, play before rolling to deal two hits (attacker may foil it with a matching ambush). Cannot be crafted.";
        const string DomText = "Dominance: in Daylight with 10+ VP, activate it instead of scoring; win at the start of your Birdsong if you rule three clearings of this suit. Can be spent for its suit. Cannot be crafted.";
        const string BirdDomText = "Dominance: in Daylight with 10+ VP, activate it instead of scoring; win at the start of your Birdsong if you rule two clearings in opposite corners. Cannot be crafted.";
        const string Stand = "In Birdsong, you may take a random card from another player. That player scores one point.";
        const string Tax = "Once in Daylight, you may remove one of your warriors from the map to draw a card.";
        const string Bank = "At start of Birdsong, you and another player of your choice draw a card.";
        const string Cobbler = "At start of Evening, you may take a move.";
        const string Warren = "At start of Daylight, you may initiate a battle.";
        const string Scouting = "As attacker in battle, you are not affected by ambush cards.";
        const string Codebreakers = "Once in Daylight, you may look at another player's hand.";
        const string Armorers = "In battle, you may discard this card to ignore all rolled hits taken.";
        const string Brutal = "In battle as attacker, you may deal an extra hit, but the defender scores one point.";
        const string Sappers = "In battle as defender, you may discard this card to deal an extra hit.";
        const string Claim = "In Birdsong, you may discard this card to score one point per clearing you rule.";
        string Favor(string s) => $"Immediately upon crafting, remove all enemy pieces in {s} clearings, then discard this card.";

        // Fox (14)
        Add(1, "ambush", "Ambush!", F, CardKind.Ambush, null, 0, null, 0, AmbushText);
        Add(1, "dominance", "Fox Dominance", F, CardKind.Dominance, null, 0, null, 0, DomText);
        Add(1, "gently-used-knapsack", "Gently Used Knapsack", F, CardKind.Item, new[] { M }, 0, ItemType.Bag, 1, "Craft a Bag.");
        Add(1, "root-tea", "Root Tea", F, CardKind.Item, new[] { M }, 0, ItemType.Tea, 2, "Craft a Tea.");
        Add(1, "travel-gear", "Travel Gear", F, CardKind.Item, new[] { R }, 0, ItemType.Boots, 1, "Craft Boots.");
        Add(1, "protection-racket", "Protection Racket", F, CardKind.Item, new[] { R, R }, 0, ItemType.Coins, 3, "Craft Coins.");
        Add(1, "foxfolk-steel", "Foxfolk Steel", F, CardKind.Item, new[] { F, F }, 0, ItemType.Sword, 2, "Craft a Sword.");
        Add(1, "anvil", "Anvil", F, CardKind.Item, new[] { F }, 0, ItemType.Hammer, 2, "Craft a Hammer.");
        Add(2, "stand-and-deliver", "Stand and Deliver!", F, CardKind.Persistent, new[] { M, M, M }, 0, null, 0, Stand);
        Add(3, "tax-collector", "Tax Collector", F, CardKind.Persistent, new[] { F, R, M }, 0, null, 0, Tax);
        Add(1, "favor-of-the-foxes", "Favor of the Foxes", F, CardKind.Immediate, new[] { F, F, F }, 0, null, 0, Favor("fox"));
        // Rabbit (13)
        Add(1, "ambush", "Ambush!", R, CardKind.Ambush, null, 0, null, 0, AmbushText);
        Add(1, "dominance", "Rabbit Dominance", R, CardKind.Dominance, null, 0, null, 0, DomText);
        Add(1, "smugglers-trail", "Smuggler's Trail", R, CardKind.Item, new[] { M }, 0, ItemType.Bag, 1, "Craft a Bag.");
        Add(1, "root-tea", "Root Tea", R, CardKind.Item, new[] { M }, 0, ItemType.Tea, 2, "Craft a Tea.");
        Add(1, "a-visit-to-friends", "A Visit to Friends", R, CardKind.Item, new[] { R }, 0, ItemType.Boots, 1, "Craft Boots.");
        Add(1, "bake-sale", "Bake Sale", R, CardKind.Item, new[] { R, R }, 0, ItemType.Coins, 3, "Craft Coins.");
        Add(2, "command-warren", "Command Warren", R, CardKind.Persistent, new[] { R, R }, 0, null, 0, Warren);
        Add(2, "better-burrow-bank", "Better Burrow Bank", R, CardKind.Persistent, new[] { R, R }, 0, null, 0, Bank);
        Add(2, "cobbler", "Cobbler", R, CardKind.Persistent, new[] { R, R }, 0, null, 0, Cobbler);
        Add(1, "favor-of-the-rabbits", "Favor of the Rabbits", R, CardKind.Immediate, new[] { R, R, R }, 0, null, 0, Favor("rabbit"));
        // Mouse (13)
        Add(1, "ambush", "Ambush!", M, CardKind.Ambush, null, 0, null, 0, AmbushText);
        Add(1, "dominance", "Mouse Dominance", M, CardKind.Dominance, null, 0, null, 0, DomText);
        Add(1, "mouse-in-a-sack", "Mouse-in-a-Sack", M, CardKind.Item, new[] { M }, 0, ItemType.Bag, 1, "Craft a Bag.");
        Add(1, "root-tea", "Root Tea", M, CardKind.Item, new[] { M }, 0, ItemType.Tea, 2, "Craft a Tea.");
        Add(1, "travel-gear", "Travel Gear", M, CardKind.Item, new[] { R }, 0, ItemType.Boots, 1, "Craft Boots.");
        Add(1, "investments", "Investments", M, CardKind.Item, new[] { R, R }, 0, ItemType.Coins, 3, "Craft Coins.");
        Add(1, "sword", "Sword", M, CardKind.Item, new[] { F, F }, 0, ItemType.Sword, 2, "Craft a Sword.");
        Add(1, "crossbow", "Crossbow", M, CardKind.Item, new[] { F }, 0, ItemType.Crossbow, 1, "Craft a Crossbow.");
        Add(2, "scouting-party", "Scouting Party", M, CardKind.Persistent, new[] { M, M }, 0, null, 0, Scouting);
        Add(2, "codebreakers", "Codebreakers", M, CardKind.Persistent, new[] { M }, 0, null, 0, Codebreakers);
        Add(1, "favor-of-the-mice", "Favor of the Mice", M, CardKind.Immediate, new[] { M, M, M }, 0, null, 0, Favor("mouse"));
        // Bird (14)
        Add(2, "ambush", "Ambush!", B, CardKind.Ambush, null, 0, null, 0, AmbushText);
        Add(1, "dominance", "Bird Dominance", B, CardKind.Dominance, null, 0, null, 0, BirdDomText);
        Add(1, "birdy-bindle", "Birdy Bindle", B, CardKind.Item, new[] { M }, 0, ItemType.Bag, 1, "Craft a Bag.");
        Add(1, "woodland-runners", "Woodland Runners", B, CardKind.Item, new[] { R }, 0, ItemType.Boots, 1, "Craft Boots.");
        Add(1, "arms-trader", "Arms Trader", B, CardKind.Item, new[] { F, F }, 0, ItemType.Sword, 2, "Craft a Sword.");
        Add(1, "crossbow", "Crossbow", B, CardKind.Item, new[] { F }, 0, ItemType.Crossbow, 1, "Craft a Crossbow.");
        Add(2, "armorers", "Armorers", B, CardKind.Persistent, new[] { F }, 0, null, 0, Armorers);
        Add(2, "brutal-tactics", "Brutal Tactics", B, CardKind.Persistent, new[] { F, F }, 0, null, 0, Brutal);
        Add(1, "royal-claim", "Royal Claim", B, CardKind.Persistent, null, 4, null, 0, Claim);
        Add(2, "sappers", "Sappers", B, CardKind.Persistent, new[] { M }, 0, null, 0, Sappers);
        return l;
    }

    public static string SuitName(Suit s) => s.ToString().ToLowerInvariant();

    public static string CostText(CardDef c)
    {
        var parts = c.Cost.Select(SuitName).ToList();
        for (int i = 0; i < c.AnyCost; i++) parts.Add("any");
        return parts.Count == 0 ? "cannot be crafted" : string.Join("+", parts);
    }

    /// <summary>Short label: "[12] Tax Collector (fox)".</summary>
    public static string Label(int id) => $"[{id}] {All[id].Name} ({SuitName(All[id].Suit)})";

    /// <summary>Full description for a hand listing.</summary>
    public static string Describe(int id)
    {
        var c = All[id];
        var what = c.Kind switch
        {
            CardKind.Item => $"item {c.Item}, {c.Vp} VP",
            CardKind.Persistent => "persistent",
            CardKind.Immediate => "immediate",
            CardKind.Ambush => "ambush",
            _ => "dominance",
        };
        var craft = c.Craftable ? $"; craft: {CostText(c)}" : "";
        return $"{Label(id)} - {what}{craft}. {c.Text}";
    }

    /// <summary>Whether a card may be spent or discarded as the given suit (birds are wild, Law 2.1.1).</summary>
    public static bool Matches(int id, Suit suit) => All[id].Suit == suit || All[id].Suit == Suit.Bird;
}
