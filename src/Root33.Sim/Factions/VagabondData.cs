using Root33.Core;

namespace Root33.Sim;

/// <summary>One item on a Vagabond's faction board. Face up = not exhausted. Owner: engineer C.</summary>
public sealed class VbItem
{
    public ItemType Type { get; set; }
    /// <summary>Face down (Law 9.2.5): exhausted items cannot be used until refreshed.</summary>
    public bool Exhausted { get; set; }
    /// <summary>In the Damaged box (Law 9.2.7): cannot be used until repaired.</summary>
    public bool Damaged { get; set; }

    public VbItem() { }
    public VbItem(ItemType type) { Type = type; }

    public ItemState ToState() => new(Type, Damaged, Exhausted);
}

/// <summary>
/// A Vagabond seat's own state (read with g.Data&lt;VagabondData&gt;(seat), saved with the game). Item tracks and the
/// Satchel are not stored: a face-up undamaged Tea, Coins or Bag sits on its track while the track has room (3), every
/// other item is in the Satchel or, when damaged, in the Damaged box (Law 9.2.5.I-II).
/// </summary>
public sealed class VagabondData
{
    public List<VbItem> Items { get; set; } = new();
    /// <summary>Relationship marker per non-Vagabond seat (Law 9.2.9). Vagabond seats have no marker.</summary>
    public Dictionary<int, Relationship> Relations { get; set; } = new();
    /// <summary>Quests this Vagabond completed (quest ids, in its play area).</summary>
    public List<int> Completed { get; set; } = new();

    /// <summary>The quest deck (hidden order). Only the quest holder's (the lowest Vagabond seat's) copy is used: with
    /// two Vagabonds they share the deck and the three face-up quests (Law 9.7.2).</summary>
    public List<int> QuestDeck { get; set; } = new();
    /// <summary>The face-up quests (quest holder only).</summary>
    public List<int> QuestsUp { get; set; } = new();

    /// <summary>The Allied faction whose warriors fight as this Vagabond's own in the battle it is attacking in (Law 9.2.9.IIc).</summary>
    public int? BattleAlly { get; set; }
    /// <summary>Hits taken in that battle by removing Allied warriors / by damaging items (Law 9.2.9.IId).</summary>
    public int AllyHits { get; set; }
    public int ItemHits { get; set; }
}

/// <summary>One base-game quest card: its suit and the two items exhausted to complete it.</summary>
public sealed record QuestDef(int Id, string Name, Suit Suit, ItemType A, ItemType B);

/// <summary>
/// The 15 quest cards of Root's base game (5 fox, 5 rabbit, 5 mouse). The Root Database shows quest cards only as
/// images, so the list follows the printed base deck (docs/SIM.md "Vagabond").
/// </summary>
public static class VagabondQuests
{
    public static readonly IReadOnlyList<QuestDef> All = Build();

    static List<QuestDef> Build()
    {
        const Suit F = Suit.Fox, R = Suit.Rabbit, M = Suit.Mouse;
        const ItemType Tea = ItemType.Tea, Coins = ItemType.Coins, Boots = ItemType.Boots, Bag = ItemType.Bag,
            Torch = ItemType.Torch, Hammer = ItemType.Hammer, Sword = ItemType.Sword, Crossbow = ItemType.Crossbow;
        var l = new List<QuestDef>();
        void Add(string name, Suit s, ItemType a, ItemType b) => l.Add(new QuestDef(l.Count, name, s, a, b));
        Add("Fundraising", F, Tea, Coins);
        Add("Errand", F, Tea, Boots);
        Add("Logistics Help", F, Boots, Bag);
        Add("Repair a Shed", F, Torch, Hammer);
        Add("Give a Speech", F, Torch, Tea);
        Add("Guard Duty", R, Torch, Sword);
        Add("Errand", R, Tea, Boots);
        Add("Give a Speech", R, Torch, Tea);
        Add("Fend Off a Bear", R, Torch, Crossbow);
        Add("Expel Bandits", R, Sword, Sword);
        Add("Expel Bandits", M, Sword, Sword);
        Add("Guard Duty", M, Torch, Sword);
        Add("Fend Off a Bear", M, Torch, Crossbow);
        Add("Escort", M, Boots, Boots);
        Add("Logistics Help", M, Boots, Bag);
        return l;
    }

    public static QuestDef Get(int id) => All[id];

    /// <summary>"[q3] Repair a Shed (fox: torch + hammer)".</summary>
    public static string Label(int id)
    {
        var q = All[id];
        return $"[q{id}] {q.Name} ({Cards.SuitName(q.Suit)}: {Vagabond.ItemName(q.A)} + {Vagabond.ItemName(q.B)})";
    }
}
