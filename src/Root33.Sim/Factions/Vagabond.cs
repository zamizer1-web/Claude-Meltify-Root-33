using System.Text;
using Root33.Core;

namespace Root33.Sim;

/// <summary>
/// The Vagabond (Law 9): the Tinker (esquie), Ranger (verso) and Thief (maelle), by g.Seat(seat).Engine.
/// Setup: pawn in a forest, starting items, quests, every relationship Indifferent. Birdsong: refresh 3 items plus 2
/// per Tea on its track, then slip. Daylight: exhaust items to move, battle, explore, aid, quest, strike, repair, craft
/// and the character's special action. Evening: rest in a forest, draw 1 plus 1 per Coins on its track, discard to 5,
/// item limit 6 plus 2 per Bag on its track. Relationships, Hostile and Infamy, Allied moves and battles, coalitions.
/// Steps are in VagabondActions.cs; data and quest cards in VagabondData.cs. Owner: engineer C.
/// </summary>
public sealed partial class Vagabond : FactionBase
{
    public override string Id => "vagabond";
    public override string Name => "Vagabond";
    public override char SetupOrder => 'C';
    public override bool IgnoresRuleToMove => true;
    public override bool UsesDominanceNormally => false;
    public override string? TakeHitsKind => "vagabond.hits";

    /// <summary>Items that sit on a track when face up and undamaged (Law 9.2.5.I).</summary>
    public static readonly ItemType[] TrackTypes = { ItemType.Tea, ItemType.Coins, ItemType.Bag };
    /// <summary>Aids needed in one turn to leave Indifferent, Amiable, Friendly (Law 9.2.9.Ia; base board 1, 2, 2).</summary>
    public static readonly int[] AidCost = { 1, 2, 2 };
    /// <summary>VP scored on reaching Amiable, Friendly, Allied (base board 1, 2, 2).</summary>
    public static readonly int[] StepVp = { 1, 2, 2 };
    public const int AlliedAidVp = 2;

    /// <summary>Starting items ("S" items) of each base character (Law 9.3.5).</summary>
    public static IReadOnlyList<ItemType> StartingItems(string engine) => engine switch
    {
        "vagabond-tinker" => new[] { ItemType.Boots, ItemType.Torch, ItemType.Bag, ItemType.Hammer },
        "vagabond-ranger" => new[] { ItemType.Boots, ItemType.Torch, ItemType.Crossbow, ItemType.Sword },
        _ => new[] { ItemType.Boots, ItemType.Torch, ItemType.Tea, ItemType.Sword },
    };

    public static string Character(SimGame g, int seat) => g.Seat(seat).Engine switch
    {
        "vagabond-tinker" => "Tinker",
        "vagabond-ranger" => "Ranger",
        _ => "Thief",
    };

    // ============================================================================================ data and items

    public static VagabondData Data(SimGame g, int seat) => g.Data<VagabondData>(seat);
    public static bool IsVagabond(SimGame g, int seat) => g.Seat(seat).Faction == "vagabond";

    public static string ItemName(ItemType t) => t.ToString().ToLowerInvariant();

    /// <summary>Face-up undamaged items of a type (usable now).</summary>
    public static int Ready(SimGame g, int seat, ItemType t) => Data(g, seat).Items.Count(i => i.Type == t && !i.Damaged && !i.Exhausted);
    public static int Undamaged(SimGame g, int seat, ItemType t) => Data(g, seat).Items.Count(i => i.Type == t && !i.Damaged);
    public static int DamagedCount(SimGame g, int seat) => Data(g, seat).Items.Count(i => i.Damaged);
    /// <summary>Items of a type face up on its track (at most 3; 0 for Satchel items).</summary>
    public static int OnTrack(SimGame g, int seat, ItemType t) => TrackTypes.Contains(t) ? Math.Min(3, Ready(g, seat, t)) : 0;
    /// <summary>Item limit (Law 9.6.4): six plus two per Bag face up on its track.</summary>
    public static int ItemLimit(SimGame g, int seat) => 6 + 2 * OnTrack(g, seat, ItemType.Bag);
    /// <summary>Items in the Satchel and the Damaged box (what the item limit counts).</summary>
    public static int CountedItems(SimGame g, int seat) => Data(g, seat).Items.Count - TrackTypes.Sum(t => OnTrack(g, seat, t));

    /// <summary>Stable option key of an item's kind and state: "sword", "tea-exhausted", "boots-damaged-exhausted".</summary>
    public static string Key(VbItem i) => ItemName(i.Type) + (i.Damaged ? "-damaged" : "") + (i.Exhausted ? "-exhausted" : "");
    public static string ItemText(VbItem i) =>
        ItemName(i.Type) + (i.Damaged && i.Exhausted ? " (damaged, face down)" : i.Damaged ? " (damaged)" : i.Exhausted ? " (exhausted)" : "");
    public static VbItem? ByKey(SimGame g, int seat, string key) => Data(g, seat).Items.FirstOrDefault(i => Key(i) == key);

    /// <summary>Change one item and tell the core (hook.item-changed).</summary>
    public static void ChangeItem(SimGame g, int seat, VbItem item, Action<VbItem> change)
    {
        var before = item.ToState();
        change(item);
        g.Bridge.OnItemChanged(seat, before, item.ToState());
    }

    /// <summary>Exhaust one face-up undamaged item of a type (a Tea, Coins or Bag on a track moves to the Satchel).</summary>
    public static bool Exhaust(SimGame g, int seat, ItemType t)
    {
        var it = Data(g, seat).Items.FirstOrDefault(i => i.Type == t && !i.Damaged && !i.Exhausted);
        if (it == null) return false;
        ChangeItem(g, seat, it, x => x.Exhausted = true);
        return true;
    }

    /// <summary>Gain an item face up (Satchel, or its track when it has room).</summary>
    public static void GainItem(SimGame g, int seat, ItemType t, string how)
    {
        var it = new VbItem(t);
        Data(g, seat).Items.Add(it);
        var where = TrackTypes.Contains(t) && Ready(g, seat, t) <= 3 ? $"on its {ItemName(t)} track" : "in the Satchel";
        g.Log($"{g.SeatName(seat)} {how}: a {ItemName(t)}, placed face up {where}.");
        g.Bridge.OnItemChanged(seat, null, it.ToState());
    }

    public static void RemoveItem(SimGame g, int seat, VbItem it, string why)
    {
        Data(g, seat).Items.Remove(it);
        g.Log($"{g.SeatName(seat)} removes a {ItemText(it)} from the game ({why}).");
        g.Bridge.OnItemChanged(seat, it.ToState(), null);
    }

    public static void Repair(SimGame g, int seat, VbItem it) => ChangeItem(g, seat, it, x => x.Damaged = false);

    /// <summary>Record the damaged-item counts of a Vagabond fighting in the current battle (BattleState.DamagedBefore/After).</summary>
    public static void NoteBattle(SimGame g, int seat)
    {
        if (g.State.Battle is not BattleState b || (b.Attacker != seat && b.Defender != seat)) return;
        int dmg = DamagedCount(g, seat);
        if (!b.DamagedBefore.ContainsKey(seat)) b.DamagedBefore[seat] = dmg;
        b.DamagedAfter[seat] = dmg;
    }

    // ============================================================================================ relationships

    /// <summary>Whether this Vagabond has a relationship marker for that seat (every non-Vagabond faction, Law 9.2.9).</summary>
    public static bool HasMarker(SimGame g, int seat, int other) => other != seat && other >= 0 && other < g.SeatCount && !IsVagabond(g, other);

    public static Relationship Rel(SimGame g, int seat, int other) =>
        Data(g, seat).Relations.TryGetValue(other, out var r) ? r : Relationship.Indifferent;

    public static string RelName(Relationship r) => r.ToString();

    public static void SetRel(SimGame g, int seat, int other, Relationship r, string why)
    {
        var before = Rel(g, seat, other);
        if (before == r) return;
        Data(g, seat).Relations[other] = r;
        g.Log($"{g.SeatName(seat)}'s relationship with {g.SeatName(other)} ({g.Faction(other).Name}) goes from {RelName(before)} to {RelName(r)} ({why}).");
        g.Bridge.OnRelationshipChanged(seat, other, before, r);
    }

    /// <summary>Move a faction's marker to the Hostile box (Law 9.2.9.III), unless Root 33 keeps it (hook.hostility-change).</summary>
    public static bool MakeHostile(SimGame g, int seat, int other, int clearing, string why)
    {
        if (!HasMarker(g, seat, other) || Rel(g, seat, other) == Relationship.Hostile) return false;
        if (!g.Bridge.OnHostilityChange(seat, other, clearing))
        {
            g.Log($"(Root 33: {g.SeatName(other)} does not turn Hostile toward {g.SeatName(seat)}.)");
            return false;
        }
        SetRel(g, seat, other, Relationship.Hostile, why);
        return true;
    }

    /// <summary>A clearing holds warriors of a faction Hostile to this Vagabond (Law 9.2.9.IIIb).</summary>
    public static bool HostileWarriorsIn(SimGame g, int seat, int c) =>
        Enumerable.Range(0, g.SeatCount).Any(o => HasMarker(g, seat, o) && Rel(g, seat, o) == Relationship.Hostile && g.Warriors(o, c) > 0);

    public static IEnumerable<int> AlliedSeats(SimGame g, int seat) =>
        Enumerable.Range(0, g.SeatCount).Where(o => HasMarker(g, seat, o) && Rel(g, seat, o) == Relationship.Allied);

    // ============================================================================================ quests

    /// <summary>The seat whose data holds the shared quest deck and face-up quests: the lowest Vagabond seat.</summary>
    public static int QuestHolder(SimGame g) => g.State.Seats.Where(s => s.Faction == "vagabond").Select(s => s.Seat).DefaultIfEmpty(-1).Min();
    public static List<int> QuestsUp(SimGame g) => Data(g, QuestHolder(g)).QuestsUp;
    public static List<int> QuestDeck(SimGame g) => Data(g, QuestHolder(g)).QuestDeck;

    // ============================================================================================ pawn and map

    public static string ForestText(int f) => $"forest {f} (touching clearings {string.Join(",", SimMap.ForestDefs[f])})";

    public static string Where(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        if (s.PawnClearing is int c) return g.ClearingName(c);
        if (s.PawnForest is int f) return ForestText(f);
        return "off the map";
    }

    /// <summary>Forests separated from this one by a single path (Law 2.4.1): they share both ends of a path.</summary>
    public static IEnumerable<int> AdjacentForests(SimGame g, int forest)
    {
        var mine = SimMap.ForestDefs[forest];
        foreach (var (f, cs) in SimMap.ForestDefs)
        {
            if (f == forest) continue;
            var shared = mine.Where(cs.Contains).ToList();
            if (shared.Any(a => shared.Any(b => a < b && g.Map.Clearings[a].Paths.Contains(b)))) yield return f;
        }
    }

    /// <summary>Move the pawn (to a clearing or a forest) and tell the core and the factions.</summary>
    public static void MovePawn(SimGame g, int seat, int? toClearing, int? toForest, string how)
    {
        var s = g.Seat(seat);
        int? from = s.PawnClearing;
        var fromText = Where(g, seat);
        s.PawnClearing = toClearing;
        s.PawnForest = toForest;
        g.Log($"{g.SeatName(seat)} {how} from {fromText} to {Where(g, seat)}.");
        g.NotifyMoved(seat, PieceKind.Pawn, from, toClearing, 1);
    }

    /// <summary>
    /// Law 9.2.2.I Full Removal: an enemy effect that removes all enemy pieces from a clearing with the Vagabond in it
    /// (a Favor card, an Alliance revolt) makes the Vagabond damage three items. For the owners of those effects to call.
    /// </summary>
    public static void FullRemoval(SimGame g, int vagabond, int clearing, int? remover)
    {
        if (!IsVagabond(g, vagabond) || g.Seat(vagabond).PawnClearing != clearing || remover == vagabond) return;
        g.Log($"{g.SeatName(vagabond)} is caught in the removal in {g.ClearingName(clearing)}: they damage three items.");
        g.Push(new Frame("vagabond.hits", vagabond).Set("c", clearing).Set("n", 3).Set("remover", remover ?? -1).Set("full", 1));
    }

    // ============================================================================================ setup and phases

    public override void InitSeat(SimGame g, int seat)
    {
        var d = Data(g, seat);
        d.Items = StartingItems(g.Seat(seat).Engine).Select(t => new VbItem(t)).ToList();
        for (int o = 0; o < g.SeatCount; o++)
            if (HasMarker(g, seat, o)) d.Relations[o] = Relationship.Indifferent;
        if (QuestHolder(g) == seat)
        {
            d.QuestDeck = VagabondQuests.All.Select(q => q.Id).ToList();
            g.Shuffle(d.QuestDeck);
            for (int i = 0; i < 3; i++) { d.QuestsUp.Add(d.QuestDeck[^1]); d.QuestDeck.RemoveAt(d.QuestDeck.Count - 1); }
        }
    }

    public override void Setup(SimGame g, int seat) => g.Push(new Frame("vagabond.setup", seat));

    public override void BeginPhase(SimGame g, int seat, TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.Birdsong: g.Push(new Frame("vagabond.birdsong", seat)); break;
            case TurnPhase.Daylight: g.Push(new Frame("vagabond.daylight", seat)); break;
            case TurnPhase.Evening: g.Push(new Frame("vagabond.evening", seat)); break;
        }
    }

    // ============================================================================================ shared-rule hooks

    /// <summary>Crafting (Law 9.2.1): each face-up undamaged Hammer is a crafting piece of the pawn's clearing's suit.</summary>
    public override IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat)
    {
        if (g.Seat(seat).PawnClearing is not int c) yield break;
        int n = Ready(g, seat, ItemType.Hammer);
        for (int i = 0; i < n; i++) yield return new CraftPiece($"hammer:{i}", g.SuitOf(c));
    }

    public override void SpendCraftingPieces(SimGame g, int seat, IReadOnlyList<CraftPiece> pieces)
    {
        foreach (var _ in pieces) Exhaust(g, seat, ItemType.Hammer);
    }

    public override void GainCraftedItem(SimGame g, int seat, ItemType item) => GainItem(g, seat, item, "takes the crafted item");

    public override bool CanAttackFrom(SimGame g, int seat, int c) => g.Seat(seat).PawnClearing == c && Ready(g, seat, ItemType.Sword) > 0;

    public override int MaxRolledHits(SimGame g, int seat, int c)
    {
        NoteBattle(g, seat);
        int n = Undamaged(g, seat, ItemType.Sword);
        if (g.State.Battle is BattleState b && b.Attacker == seat && b.Clearing == c && Data(g, seat).BattleAlly is int a) n += g.Warriors(a, c);
        return n;
    }

    public override bool IsDefenseless(SimGame g, int seat, int c) => Undamaged(g, seat, ItemType.Sword) == 0;

    public override int AttackingPieces(SimGame g, int seat, int c)
    {
        int n = g.Seat(seat).PawnClearing == c ? 1 : 0;
        if (Data(g, seat).BattleAlly is int a) n += g.Warriors(a, c);
        return n;
    }

    public override string PieceName(string type) => type == "pawn" ? "pawn" : base.PieceName(type);

    /// <summary>Hostile (Law 9.2.9.III) and Infamy (9.2.9.IIIa) from pieces this Vagabond removes.</summary>
    public override void OnPieceRemoved(SimGame g, int self, RemovedRecord r)
    {
        if (r.Remover != self || !HasMarker(g, self, r.Owner) || !g.State.SetupDone) return;
        if (Rel(g, self, r.Owner) == Relationship.Hostile)
        {
            if (g.State.Battle != null && g.State.Turn.Seat == self)
                g.AddVp(self, 1, $"Infamy: removed a piece of Hostile {g.SeatName(r.Owner)} in battle");
            return;
        }
        if (r.Kind == PieceKind.Warrior) MakeHostile(g, self, r.Owner, r.Clearing, "the Vagabond removed one of their warriors");
    }

    /// <summary>Taking Hits with Ally (Law 9.2.9.IId): more hits taken with Allied warriors than with items makes them Hostile.</summary>
    public override void OnBattleResolved(SimGame g, int self, BattleState b)
    {
        var d = Data(g, self);
        if (b.Attacker != self || d.BattleAlly is not int a) return;
        if (d.AllyHits > d.ItemHits)
            MakeHostile(g, self, a, b.Clearing, $"{d.AllyHits} hit(s) were taken with their warriors but only {d.ItemHits} with items");
        d.BattleAlly = null;
        d.AllyHits = 0;
        d.ItemHits = 0;
    }

    // ============================================================================================ coalitions (Law 9.2.8)

    public static List<int> CoalitionCandidates(SimGame g, int seat)
    {
        var others = Enumerable.Range(0, g.SeatCount).Where(o => o != seat && g.Seat(o).Dominance == null).ToList();
        if (others.Count == 0) return new List<int>();
        int min = others.Min(o => g.Seat(o).Vp);
        return others.Where(o => g.Seat(o).Vp == min && !IsVagabond(g, o) && !g.State.Seats.Any(s => s.CoalitionWith == o)).ToList();
    }

    public override IEnumerable<Option> OpenOptions(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        if (g.State.Turn.Phase != TurnPhase.Daylight || g.SeatCount < 4 || s.Dominance != null || s.CoalitionWith != null || s.Vp < 10) yield break;
        var doms = s.Hand.Where(c => Cards.Get(c).Kind == CardKind.Dominance).ToList();
        if (doms.Count == 0) yield break;
        foreach (var dom in doms)
            foreach (var o in CoalitionCandidates(g, seat))
                yield return new Option($"open:coalition:{dom}:{o}",
                    $"Activate {Cards.Label(dom)} to form a coalition with {g.SeatName(o)} ({g.Seat(o).Vp} VP, the fewest): you stop scoring and win if they win");
    }

    public override void ChooseOpen(SimGame g, int seat, Option o)
    {
        var p = o.Key.Split(':');
        if (p.Length != 4 || p[1] != "coalition") throw new InvalidOperationException("unknown Vagabond option " + o.Key);
        int dom = int.Parse(p[2]), partner = int.Parse(p[3]);
        var s = g.Seat(seat);
        s.Hand.Remove(dom);
        s.PlayArea.Add(dom);
        s.Dominance = dom;
        s.CoalitionWith = partner;
        g.Log($"{g.SeatName(seat)} activates {Cards.Label(dom)} and forms a coalition with {g.SeatName(partner)}: their score marker leaves the track; they win if {g.SeatName(partner)} wins.");
        if (Rel(g, seat, partner) == Relationship.Hostile) SetRel(g, seat, partner, Relationship.Indifferent, "coalition with a Hostile faction");
    }

    // ============================================================================================ reads for the core

    public override IReadOnlyList<ItemState> Items(SimGame g, int seat) => Data(g, seat).Items.Select(i => i.ToState()).ToList();
    public override Relationship RelationshipWith(SimGame g, int seat, int faction) => HasMarker(g, seat, faction) ? Rel(g, seat, faction) : Relationship.Indifferent;

    // ============================================================================================ text

    public static string ReadySummary(SimGame g, int seat)
    {
        var ready = Data(g, seat).Items.Where(i => !i.Damaged && !i.Exhausted).GroupBy(i => i.Type).OrderBy(x => x.Key)
            .Select(x => $"{x.Count()} {ItemName(x.Key)}").ToList();
        return ready.Count == 0 ? "no face-up items" : "face up: " + string.Join(", ", ready);
    }

    static string Group(IEnumerable<VbItem> items)
    {
        var l = items.GroupBy(ItemText).OrderBy(x => x.Key).Select(x => x.Count() == 1 ? x.Key : $"{x.Count()} x {x.Key}").ToList();
        return l.Count == 0 ? "none" : string.Join(", ", l);
    }

    public override string BoardText(SimGame g, int seat)
    {
        var d = Data(g, seat);
        var sb = new StringBuilder();
        sb.AppendLine($"{FactionRegistry.EngineName(g.Seat(seat).Engine)} board:");
        sb.AppendLine($"  pawn: {Where(g, seat)}");
        var onTrack = new List<VbItem>();
        foreach (var t in TrackTypes) onTrack.AddRange(d.Items.Where(i => i.Type == t && !i.Damaged && !i.Exhausted).Take(3));
        var satchel = d.Items.Where(i => !i.Damaged && !onTrack.Contains(i)).ToList();
        sb.AppendLine($"  tracks: tea {OnTrack(g, seat, ItemType.Tea)}/3 (refresh {3 + 2 * OnTrack(g, seat, ItemType.Tea)}), coins {OnTrack(g, seat, ItemType.Coins)}/3 (evening draw {1 + OnTrack(g, seat, ItemType.Coins)}), bags {OnTrack(g, seat, ItemType.Bag)}/3");
        sb.AppendLine($"  satchel: {Group(satchel)}");
        sb.AppendLine($"  damaged: {Group(d.Items.Where(i => i.Damaged))}");
        sb.AppendLine($"  items counted for the limit: {CountedItems(g, seat)} of {ItemLimit(g, seat)}");
        var rels = Enumerable.Range(0, g.SeatCount).Where(o => HasMarker(g, seat, o)).Select(o => $"{g.SeatName(o)} {RelName(Rel(g, seat, o))}").ToList();
        sb.AppendLine($"  relationships: {(rels.Count == 0 ? "none (no non-Vagabond faction)" : string.Join("; ", rels))}");
        var done = d.Completed.GroupBy(q => VagabondQuests.Get(q).Suit).Select(x => $"{Cards.SuitName(x.Key)} {x.Count()}").ToList();
        sb.AppendLine($"  completed quests: {(done.Count == 0 ? "none" : string.Join(", ", done))}");
        sb.Append($"  face-up quests: {string.Join("; ", QuestsUp(g).Select(VagabondQuests.Label))} (quest deck {QuestDeck(g).Count})");
        return sb.ToString();
    }

    public override string RulesText(SimGame g, int seat)
    {
        var special = Character(g, seat) switch
        {
            "Tinker" => "Day Labor: exhaust a torch to take a card of your clearing's suit (birds always count) from the discard pile.",
            "Ranger" => "Hideout: exhaust a torch to repair 3 damaged items; then Daylight ends at once and Evening begins.",
            _ => "Steal: exhaust a torch to take a random card from a player with pieces in your clearing.",
        };
        var start = string.Join(", ", StartingItems(g.Seat(seat).Engine).Select(ItemName));
        return
$@"VAGABOND ({Character(g, seat).ToUpperInvariant()}) (faction board)
Starting items: {start}. Special action: {special}
Your pawn is not a warrior: it never rules a clearing and is never removed. You move regardless of rule.
ITEMS: face up items are ready; exhausting one flips it face down. Face-up undamaged tea, coins and bags sit on
  their tracks (3 each); everything else is in the Satchel; damaged items go to the Damaged box.
  Each hit you take damages one undamaged item of your choice (no undamaged item left: ignore the hit).
  In battle your maximum rolled hits are your undamaged swords; with no undamaged sword you are defenseless.
RELATIONSHIPS (each non-Vagabond faction starts Indifferent): Aid a faction 1 time in one turn to reach Amiable
  (+1 VP), then 2 times in one turn to reach Friendly (+2 VP), then 2 times in one turn to reach Allied (+2 VP).
  Each Aid to an Allied faction scores 2 VP. Removing a warrior of a non-Hostile faction makes it Hostile for good.
  Infamy: +1 VP for each piece of a Hostile faction you remove in battle on your turn (besides the usual VP).
  Moving into a clearing with Hostile warriors costs one more boot. You may Aid a Hostile faction only for items.
  Allied: when you move you may bring that faction's warriors from your clearing; when you attack (not them) you may
  fight with their warriors in the clearing as your own (they add to your max hits and may take your hits, but if
  they take more hits than your items, they turn Hostile).
BIRDSONG: Refresh 3 exhausted items plus 2 per tea on its track; then you may Slip: move to an adjacent clearing or
  forest for free.
DAYLIGHT, any number of actions, each exhausting items:
  - Move (1 boot, +1 into Hostile warriors): to an adjacent clearing; from a forest only into a clearing it touches.
  - Battle (1 sword) an enemy in your clearing. Strike (1 crossbow): remove an enemy warrior in your clearing, or a
    building or token of an enemy with no warriors there.
  - Explore (1 torch): take the ruin's item in your clearing, score 1 VP; the ruin is removed.
  - Aid (any 1 item): give a card of your clearing's suit (birds wild) to a player with pieces there; you may take an
    item from their Crafted Items; the relationship improves (see above).
  - Quest: in a clearing of the quest's suit, exhaust its two items; score 1 VP per completed quest of that suit
    (this one included) or draw 2 cards; a new quest is turned face up.
  - Repair (1 hammer): one damaged item leaves the Damaged box, keeping its side. Craft: exhaust 1 hammer per
    crafting cost; your hammers match your clearing's suit. A crafted item comes to you face up.
  - Special action (1 torch): see above.
EVENING: if in a forest, repair every damaged item and flip it face up. Draw 1 card plus 1 per coins on its track;
  discard down to 5. Then if the Satchel and Damaged box hold more items than 6 plus 2 per bag on its track, remove
  items of your choice from the game.
COALITION (4+ players): with 10+ VP, in Daylight, activate a dominance card to join the player with the fewest VP:
  you stop scoring and win if they win.
SCORING: relationships, Allied aid, Infamy, explored ruins, quests, crafted items, enemy buildings and tokens removed. 30 VP wins.";
    }
}
