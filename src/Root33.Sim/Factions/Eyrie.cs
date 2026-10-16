using Root33.Core;

namespace Root33.Sim;

/// <summary>The Eyrie's saved state: leader, deposed (face-down) leaders and the Decree. Viziers are card ids -1 and -2.</summary>
public sealed class EyrieData
{
    /// <summary>builder, charismatic, commander or despot ("" before setup).</summary>
    public string Leader { get; set; } = "";
    /// <summary>Leaders deposed by Turmoil (face down) since the last New Clutch.</summary>
    public List<string> FaceDown { get; set; } = new();
    /// <summary>Decree columns (recruit, move, battle, build) holding card ids; the two Loyal Viziers are -1 and -2.</summary>
    public Dictionary<string, List<int>> Decree { get; set; } = NewDecree();

    public static Dictionary<string, List<int>> NewDecree() => Eyrie.Columns.ToDictionary(c => c, _ => new List<int>());
    public List<int> Column(string col)
    {
        if (!Decree.TryGetValue(col, out var l)) Decree[col] = l = new List<int>();
        return l;
    }
}

/// <summary>
/// The Eyrie Dynasties (Law 7). Setup: a roost and 6 warriors in a free corner (opposite the keep if possible), a leader
/// and its two Loyal Viziers. Birdsong: Emergency Orders, add 1-2 cards to the Decree (at most one bird), a new roost if
/// none is on the map. Daylight: craft with roosts, then resolve the Decree column by column (recruit, move, battle,
/// build); a card that cannot be resolved means Turmoil. Evening: score the roost track, draw 1 + bonuses, discard to 5.
/// Lords of the Forest (rule ties) and Disdain for Trade (1 VP per crafted item, except under the Builder).
/// Owner: engineer B.
/// </summary>
public sealed class Eyrie : FactionBase
{
    public override string Id => "eyrie";
    public override string Name => "Eyrie Dynasties";
    public override char SetupOrder => 'B';
    public override bool WinsRuleTies => true;

    public static readonly string[] Columns = { "recruit", "move", "battle", "build" };
    public static readonly string[] Leaders = { "builder", "charismatic", "commander", "despot" };
    public static readonly IReadOnlyDictionary<string, string[]> VizierColumns = new Dictionary<string, string[]>
    {
        ["builder"] = new[] { "recruit", "move" },
        ["charismatic"] = new[] { "recruit", "battle" },
        ["commander"] = new[] { "move", "battle" },
        ["despot"] = new[] { "move", "build" },
    };
    public static readonly IReadOnlyDictionary<string, string> LeaderText = new Dictionary<string, string>
    {
        ["builder"] = "Builder: viziers on Recruit and Move; when you craft an item you score its printed VP (ignores Disdain for Trade)",
        ["charismatic"] = "Charismatic: viziers on Recruit and Battle; each Recruit action places two warriors instead of one",
        ["commander"] = "Commander: viziers on Move and Battle; as attacker in battle you deal an extra hit",
        ["despot"] = "Despot: viziers on Move and Build; when you remove at least one enemy building or token in a battle, score one extra VP",
    };
    /// <summary>Evening VP by roosts on the map (the rightmost empty space of the Roosts track: 0,1,2,3,4,4,5).</summary>
    public static readonly int[] RoostVp = { 0, 0, 1, 2, 3, 4, 4, 5 };
    public const int Vizier1 = -1, Vizier2 = -2;

    public override void InitSeat(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        s.SupplyWarriors = 20;
        s.Supply[PieceTypes.Roost] = 7;
        g.Data<EyrieData>(seat);
    }

    // ---------------------------------------------------------------------------------------------- helpers

    public static EyrieData D(SimGame g, int seat) => g.Data<EyrieData>(seat);
    public static int RoostsOnMap(SimGame g, int seat) => g.ClearingIds.Sum(c => g.BuildingsOf(seat, c, PieceTypes.Roost));
    /// <summary>Draw bonuses uncovered on the Roosts track (third and sixth roost on the map).</summary>
    public static int DrawBonus(SimGame g, int seat) { int r = RoostsOnMap(g, seat); return (r >= 3 ? 1 : 0) + (r >= 6 ? 1 : 0); }
    public static int EveningVp(SimGame g, int seat) => RoostVp[Math.Min(RoostsOnMap(g, seat), RoostVp.Length - 1)];
    public static Suit CardSuit(int id) => id < 0 ? Suit.Bird : Cards.Get(id).Suit;
    public static string CardText(int id) => id < 0 ? "Loyal Vizier (bird)" : Cards.Label(id);
    public static bool IsBird(int id) => CardSuit(id) == Suit.Bird;
    static bool SuitFits(SimGame g, Suit s, int c) => s == Suit.Bird || g.SuitOf(c) == s;
    static string Title(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    static void TuckViziers(SimGame g, int seat)
    {
        var d = D(g, seat);
        foreach (var col in Columns) d.Column(col).RemoveAll(x => x < 0);
        if (!VizierColumns.TryGetValue(d.Leader, out var cols)) return;
        d.Column(cols[0]).Insert(0, Vizier1);
        d.Column(cols[1]).Insert(0, Vizier2);
    }

    static void SetLeader(SimGame g, int seat, string leader)
    {
        D(g, seat).Leader = leader;
        TuckViziers(g, seat);
        var cols = VizierColumns[leader];
        g.Log($"{g.SeatName(seat)}'s new leader is the {Title(leader)}; the Loyal Viziers are tucked into {Title(cols[0])} and {Title(cols[1])}.");
    }

    // ---------------------------------------------------------------------------------------------- shared-rule hooks

    public override IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat)
    {
        foreach (var c in g.ClearingIds)
            for (int i = 0; i < g.BuildingsOf(seat, c, PieceTypes.Roost); i++)
                yield return new CraftPiece($"roost:{c}:{i}", g.SuitOf(c));
    }

    /// <summary>Disdain for Trade: one VP per crafted item, unless the Builder leads.</summary>
    public override int CraftVp(SimGame g, int seat, CardDef card) => D(g, seat).Leader == "builder" ? card.Vp : 1;

    /// <summary>Despot: one extra VP when the Eyrie removed at least one enemy building or token in this battle.</summary>
    public override void OnBattleResolved(SimGame g, int self, BattleState b)
    {
        if (b.Attacker != self && b.Defender != self) return;
        if (D(g, self).Leader != "despot") return;
        if (b.Removed.Any(r => r.Remover == self && r.Owner != self && r.Kind is PieceKind.Building or PieceKind.Token))
            g.AddVp(self, 1, "Despot: removed an enemy building or token in battle");
    }

    public override string PieceName(string type) => type;

    public override void Setup(SimGame g, int seat) => g.Push(new Frame("eyrie.setup", seat));

    public override void BeginPhase(SimGame g, int seat, TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.Birdsong: g.Push(new Frame("eyrie.birdsong", seat)); break;
            case TurnPhase.Daylight: g.Push(new Frame("eyrie.daylight", seat)); break;
            case TurnPhase.Evening: g.Push(new Frame("eyrie.evening", seat)); break;
        }
    }

    // ---------------------------------------------------------------------------------------------- steps

    public override void RegisterSteps(StepRegistry r)
    {
        r.Add("eyrie.setup", SetupNext, SetupChoose);
        r.Add("eyrie.birdsong", BirdsongNext, BirdsongChoose);
        r.Add("eyrie.daylight", DaylightNext, DaylightChoose);
        r.Add("eyrie.turmoil", TurmoilNext, (g, f, o) =>
        {
            g.Pop(f);
            SetLeader(g, f.Seat, o.Key[7..]);
        });
        r.Add("eyrie.evening", (g, f) =>
        {
            int seat = f.Seat;
            switch (f.Step)
            {
                case 0:
                {
                    int roosts = RoostsOnMap(g, seat), vp = EveningVp(g, seat);
                    if (vp > 0) g.AddVp(seat, vp, $"Roosts track, {roosts} roost{(roosts == 1 ? "" : "s")} on the map");
                    else g.Log($"{g.SeatName(seat)} scores nothing from the Roosts track ({roosts} roost{(roosts == 1 ? "" : "s")} on the map).");
                    f.Step = 1;
                    return null;
                }
                case 1:
                {
                    int bonus = DrawBonus(g, seat);
                    g.Log($"{g.SeatName(seat)} draws 1 card plus {bonus} bonus (uncovered on the Roosts track).");
                    g.Draw(seat, 1 + bonus);
                    f.Step = 2;
                    g.Push(new Frame("discard", seat).Set("limit", SimGame.HandLimit));
                    return null;
                }
                default:
                    g.Pop(f);
                    return null;
            }
        });
    }

    // setup: step 0 the starting corner (roost and 6 warriors), step 1 the leader

    /// <summary>Corner clearings that are another player's starting corner (the Marquise's keep).</summary>
    static List<int> StartingCorners(SimGame g, int self) =>
        g.Map.Clearings.Values.Where(c => c.Corner && g.Clearing(c.Id).Tokens.Any(t => t.Seat != self && t.Type == PieceTypes.Keep)).Select(c => c.Id).ToList();

    /// <summary>Law 7.3.2: a corner that is not another player's starting corner and, if possible, is opposite one.</summary>
    public static List<int> LegalHomes(SimGame g, int seat)
    {
        var taken = StartingCorners(g, seat);
        var free = g.Map.Clearings.Values.Where(c => c.Corner && !taken.Contains(c.Id) && g.FreeSlots(c.Id) > 0 && g.CanPlace(seat, c.Id)).ToList();
        var opposite = free.Where(c => c.OppositeCorner is int o && taken.Contains(o)).ToList();
        return (opposite.Count > 0 ? opposite : free).Select(c => c.Id).OrderBy(c => c).ToList();
    }

    static Decision? SetupNext(SimGame g, Frame f)
    {
        if (f.Step == 0)
        {
            var homes = g.HomeChoices(f.Seat, LegalHomes(g, f.Seat));
            if (homes.Count == 0) { g.Log("No corner is free for the Eyrie's starting roost."); f.Step = 1; return null; }
            var d = new Decision(f.Seat, "Eyrie setup: place a roost and 6 warriors in your starting corner");
            foreach (var c in homes) d.Add($"home:{c}", $"Start in {g.ClearingName(c)}");
            return d;
        }
        if (f.Step == 1)
        {
            var d = new Decision(f.Seat, "Eyrie setup: choose your first leader (the Loyal Viziers go into the two Decree columns it names)");
            foreach (var l in Leaders) d.Add($"leader:{l}", LeaderText[l]);
            return d;
        }
        g.Pop(f);
        return null;
    }

    static void SetupChoose(SimGame g, Frame f, Option o)
    {
        if (f.Step == 0)
        {
            int c = int.Parse(o.Key[5..]);
            g.PlaceBuilding(f.Seat, c, PieceTypes.Roost);
            g.PlaceWarriors(f.Seat, c, 6);
        }
        else SetLeader(g, f.Seat, o.Key[7..]);
        f.Step++;
    }

    // Birdsong: 0 Emergency Orders, 1 first Decree card, 2 optional second card, 3 A New Roost

    static Decision? BirdsongNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        var hand = g.Seat(seat).Hand;
        switch (f.Step)
        {
            case 0:
                if (hand.Count == 0)
                {
                    g.Log($"{g.SeatName(seat)} has no cards: Emergency Orders, draw one card.");
                    g.Draw(seat, 1);
                }
                f.Step = 1;
                return null;
            case 1:
            case 2:
            {
                bool second = f.Step == 2;
                var cards = hand.Where(c => !(second && f.Get("bird") == 1 && Cards.Get(c).Suit == Suit.Bird)).ToList();
                if (cards.Count == 0)
                {
                    if (!second) g.Log($"{g.SeatName(seat)} has no card to add to the Decree.");
                    f.Step = 3;
                    return null;
                }
                var d = new Decision(seat, second
                    ? "Add to the Decree: add a second card (only one added card may be a bird), or stop"
                    : "Add to the Decree: choose a card and the column it goes into (you must add one or two cards)");
                foreach (var c in cards)
                    foreach (var col in Columns)
                        d.Add($"add:{c}:{col}", $"{Cards.Label(c)} into {Title(col)}");
                if (second) d.Add("done", "Do not add a second card");
                return d;
            }
            case 3:
            {
                if (RoostsOnMap(g, seat) > 0 || g.Supply(seat, PieceTypes.Roost) == 0) { g.Pop(f); return null; }
                var spots = NewRoostSpots(g, seat);
                if (spots.Count == 0) { g.Log($"{g.SeatName(seat)} has no roost on the map and no clearing to place a new one."); g.Pop(f); return null; }
                var d = new Decision(seat, "A New Roost: you have no roost on the map; place a roost and 3 warriors in a clearing with the fewest warriors");
                foreach (var c in spots) d.Add($"roost:{c}", $"New roost in {g.ClearingName(c)}");
                return d;
            }
            default:
                g.Pop(f);
                return null;
        }
    }

    /// <summary>Law 7.4.3: clearings with the fewest warriors (of every player) where a roost and warriors can be placed.</summary>
    public static List<int> NewRoostSpots(SimGame g, int seat)
    {
        var ok = g.ClearingIds.Where(c => g.FreeSlots(c) > 0 && g.CanPlace(seat, c)).ToList();
        if (ok.Count == 0) return ok;
        int Total(int c) => g.Clearing(c).Warriors.Values.Sum();
        int min = ok.Min(Total);
        return ok.Where(c => Total(c) == min).OrderBy(c => c).ToList();
    }

    static void BirdsongChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        if (o.Key == "done") { f.Step = 3; return; }
        if (o.Key.StartsWith("roost:"))
        {
            int c = int.Parse(o.Key[6..]);
            g.Log($"{g.SeatName(seat)} has no roost on the map: A New Roost.");
            g.PlaceBuilding(seat, c, PieceTypes.Roost);
            g.PlaceWarriors(seat, c, 3);
            g.Pop(f);
            return;
        }
        var p = o.Key.Split(':');
        int card = int.Parse(p[1]);
        string col = p[2];
        g.Seat(seat).Hand.Remove(card);
        D(g, seat).Column(col).Add(card);
        g.Log($"{g.SeatName(seat)} adds {Cards.Label(card)} to the Decree's {Title(col)} column.");
        if (Cards.Get(card).Suit == Suit.Bird) f.Set("bird", 1);
        f.Step = f.Step == 1 ? 2 : 3;
    }

    // Daylight: 0 craft, 1 resolve the Decree (col in I["col"], resolved card ids of that column in L)

    /// <summary>The unresolved cards of the column being resolved.</summary>
    static List<int> Unresolved(SimGame g, Frame f) =>
        D(g, f.Seat).Column(Columns[f.Get("col")]).Where(x => !f.L.Contains(x)).ToList();

    /// <summary>Every way to resolve one unresolved card of the current column now (keys action:suit[:args]).</summary>
    public static List<Option> DecreeOptions(SimGame g, int seat, string col, IEnumerable<int> unresolved)
    {
        var l = new List<Option>();
        var leader = D(g, seat).Leader;
        foreach (var suit in unresolved.Select(CardSuit).Distinct().OrderBy(s => s == Suit.Bird ? 1 : 0))
        {
            string sn = Cards.SuitName(suit);
            switch (col)
            {
                case "recruit":
                {
                    if (g.Seat(seat).SupplyWarriors == 0) break;
                    int n = leader == "charismatic" ? 2 : 1;
                    foreach (var c in g.ClearingIds.Where(c => g.BuildingsOf(seat, c, PieceTypes.Roost) > 0 && SuitFits(g, suit, c) && g.CanPlace(seat, c)))
                        l.Add(new Option($"recruit:{sn}:{c}", $"Recruit ({sn} card): place {n} warrior{(n == 1 ? "" : "s")} at the roost in {g.ClearingName(c)}"));
                    break;
                }
                case "move":
                    if (Moves.Legal(g, seat, suit).Count > 0)
                        l.Add(new Option($"move:{sn}", $"Move ({sn} card): move warriors out of a {(suit == Suit.Bird ? "" : sn + " ")}clearing"));
                    break;
                case "battle":
                    foreach (var (c, def) in Battles.Targets(g, seat, suit))
                        l.Add(new Option($"battle:{sn}:{c}:{def}", $"Battle ({sn} card): {Battles.TargetText(g, c, def)}"));
                    break;
                case "build":
                    if (g.Supply(seat, PieceTypes.Roost) == 0) break;
                    foreach (var c in g.ClearingIds.Where(c => SuitFits(g, suit, c) && g.Rules(seat, c) && g.BuildingsOf(seat, c, PieceTypes.Roost) == 0
                                                              && g.FreeSlots(c) > 0 && g.CanPlace(seat, c)))
                        l.Add(new Option($"build:{sn}:{c}", $"Build ({sn} card): place a roost in {g.ClearingName(c)}"));
                    break;
            }
        }
        return l;
    }

    static string Why(string col, Suit suit) => col switch
    {
        "recruit" => $"recruit with a {Cards.SuitName(suit)} card (no roost in a matching clearing where a warrior can be placed, or no warriors left)",
        "move" => $"move with a {Cards.SuitName(suit)} card (no legal move out of a matching clearing)",
        "battle" => $"battle with a {Cards.SuitName(suit)} card (no enemy to fight in a matching clearing)",
        _ => $"build with a {Cards.SuitName(suit)} card (no matching clearing you rule with a free slot and no roost, or no roosts left)",
    };

    static Decision? DaylightNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        if (f.Step == 0) { f.Step = 1; g.Push(Crafting.Frame(seat)); return null; }
        if (f.Str("pending") != null)
        {
            f.SetStr("pending", null);
            g.ActionResolved(seat);
        }
        if (f.Str("turmoil") is string why) { FallIntoTurmoil(g, f, why); return null; }
        int col = f.Get("col");
        if (col >= Columns.Length)
        {
            g.Log($"{g.SeatName(seat)} has resolved the whole Decree.");
            g.Pop(f);
            return null;
        }
        var left = Unresolved(g, f);
        if (left.Count == 0) { f.Set("col", col + 1); f.L.Clear(); return null; }
        var opts = DecreeOptions(g, seat, Columns[col], left);
        if (opts.Count == 0)
        {
            FallIntoTurmoil(g, f, Why(Columns[col], CardSuit(left[0])));
            return null;
        }
        var d = new Decision(seat, $"Resolve the Decree, {Title(Columns[col])} column: {left.Count} card{(left.Count == 1 ? "" : "s")} left ({string.Join(", ", left.Select(CardText))}); choose one to resolve") { Open = true };
        d.Options.AddRange(opts);
        return d;
    }

    static void DaylightChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        var p = o.Key.Split(':');
        var suit = Enum.Parse<Suit>(p[1], ignoreCase: true);
        int card = Unresolved(g, f).First(x => CardSuit(x) == suit);
        f.L.Add(card);
        f.SetStr("pending", p[0]);
        switch (p[0])
        {
            case "recruit":
            {
                int c = int.Parse(p[2]);
                int n = D(g, seat).Leader == "charismatic" ? 2 : 1;
                int placed = g.PlaceWarriors(seat, c, n);
                if (placed < n) f.SetStr("turmoil", $"fully recruit with a {p[1]} card (only {placed} of {n} warriors left in supply)");
                break;
            }
            case "move":
                g.Push(Moves.Frame(seat, optional: false, fromSuit: suit, label: $"Decree Move ({p[1]} card)"));
                break;
            case "battle":
            {
                int c = int.Parse(p[2]), def = int.Parse(p[3]);
                Battles.Start(g, seat, def, c);
                break;
            }
            case "build":
                g.PlaceBuilding(seat, int.Parse(p[2]), PieceTypes.Roost);
                break;
        }
    }

    // Turmoil (Law 7.7): humiliate, purge, depose (choose a new leader), rest (Daylight ends).

    static void FallIntoTurmoil(SimGame g, Frame daylight, string why)
    {
        int seat = daylight.Seat;
        g.Pop(daylight);
        g.Log($"{g.SeatName(seat)} cannot {why}: the Eyrie fall into TURMOIL.");
        g.Bridge.OnTurmoil(seat);
        var d = D(g, seat);
        int birds = Columns.Sum(c => d.Column(c).Count(IsBird));
        if (birds > 0) g.AddVp(seat, -birds, $"Turmoil, humiliated: {birds} bird card{(birds == 1 ? "" : "s")} in the Decree");
        var purged = Columns.SelectMany(c => d.Column(c)).Where(x => x >= 0).ToList();
        foreach (var x in purged) g.ToDiscard(x);
        foreach (var c in Columns) d.Column(c).RemoveAll(x => x >= 0);
        g.Log(purged.Count > 0
            ? $"{g.SeatName(seat)} purges the Decree, discarding {string.Join(", ", purged.Select(Cards.Label))} (the Loyal Viziers stay)."
            : $"{g.SeatName(seat)} purges the Decree (only the Loyal Viziers were in it).");
        if (d.Leader != "")
        {
            g.Log($"{g.SeatName(seat)}'s {Title(d.Leader)} is deposed (face down).");
            d.FaceDown.Add(d.Leader);
            d.Leader = "";
        }
        if (Leaders.All(l => d.FaceDown.Contains(l)))
        {
            g.Log($"{g.SeatName(seat)} has no face-up leader left: A New Clutch, every leader is face up again.");
            d.FaceDown.Clear();
        }
        g.Push(new Frame("eyrie.turmoil", seat));
    }

    static Decision? TurmoilNext(SimGame g, Frame f)
    {
        var d = D(g, f.Seat);
        var dec = new Decision(f.Seat, "Turmoil, depose: choose a new leader from the face-up leaders (then Daylight ends)");
        foreach (var l in Leaders.Where(l => !d.FaceDown.Contains(l))) dec.Add($"leader:{l}", LeaderText[l]);
        return dec;
    }

    // ---------------------------------------------------------------------------------------------- reads and text

    public override string EyrieLeader(SimGame g, int seat) => D(g, seat).Leader;

    /// <summary>Commander (Eyrie leader): one extra hit as attacker in every battle, Decree or card (e.g. Command Warren).</summary>
    public override int ExtraHits(SimGame g, int seat, bool attacker)
    {
        if (!attacker || D(g, seat).Leader != "commander") return 0;
        g.Log($"{g.SeatName(seat)}'s Commander deals an extra hit.");
        return 1;
    }
    public override IReadOnlyList<Suit> DecreeColumn(SimGame g, int seat, string column) =>
        D(g, seat).Decree.TryGetValue(column, out var l) ? l.Select(CardSuit).ToList() : Array.Empty<Suit>();

    /// <summary>Every real card in the Decree (not the viziers): cards held by the faction outside the hand.</summary>
    public static IEnumerable<int> DecreeCards(SimGame g, int seat) => Columns.SelectMany(c => D(g, seat).Column(c)).Where(x => x >= 0);

    public override string BoardText(SimGame g, int seat)
    {
        var d = D(g, seat);
        int roosts = RoostsOnMap(g, seat);
        var lines = new List<string> { $"{Name} board:" };
        lines.Add(d.Leader == "" ? "  leader: none" : $"  leader: {LeaderText[d.Leader]}");
        var up = Leaders.Where(l => l != d.Leader && !d.FaceDown.Contains(l)).Select(Title).ToList();
        lines.Add($"  other leaders face up: {(up.Count == 0 ? "none" : string.Join(", ", up))}; deposed (face down): {(d.FaceDown.Count == 0 ? "none" : string.Join(", ", d.FaceDown.Select(Title)))}");
        lines.Add("  Decree: " + string.Join(" | ", Columns.Select(c => $"{Title(c)}: {(d.Column(c).Count == 0 ? "-" : string.Join(", ", d.Column(c).Select(CardText)))}")));
        int birds = Columns.Sum(c => d.Column(c).Count(IsBird));
        lines.Add($"  bird cards in the Decree: {birds} (VP lost in Turmoil)");
        lines.Add($"  roosts: {roosts} on the map, {g.Supply(seat, PieceTypes.Roost)} on the track; Evening scores {EveningVp(g, seat)} VP and draws {1 + DrawBonus(g, seat)} card(s)");
        lines.Add($"  warriors in supply: {g.Seat(seat).SupplyWarriors}/20");
        return string.Join("\n", lines);
    }

    public override string RulesText(SimGame g, int seat) =>
@"EYRIE DYNASTIES (faction board)
Your pieces: 20 warriors, 7 roosts (buildings). Two Loyal Viziers (bird cards that never leave the Decree).
Lords of the Forest: you rule a clearing when tied for most warriors plus buildings there (with a piece there).
Disdain for Trade: crafting an item scores only 1 VP (the Builder scores the card's printed VP).
LEADERS (one at a time; the viziers start in the two columns listed):
  Builder (Recruit, Move): crafted items score their printed VP.
  Charismatic (Recruit, Battle): each Recruit places two warriors.
  Commander (Move, Battle): as attacker in battle you deal an extra hit.
  Despot (Move, Build): removing at least one enemy building or token in a battle scores one extra VP.
BIRDSONG: 1. Emergency Orders: with no cards in hand, draw one. 2. Add one or two cards from your hand to the
  Decree, each into any column (Recruit, Move, Battle, Build); at most one of them may be a bird. 3. A New Roost: if
  no roost is on the map, place a roost and 3 warriors in a clearing with the fewest warriors that has a free slot.
DAYLIGHT: 1. Craft with your roosts (each roost gives its clearing's suit once per turn). 2. Resolve the Decree from
  left to right; in each column resolve every card, in any order. A card's suit picks the clearing (birds: any):
  - Recruit: place a warrior in a matching clearing with a roost.
  - Move: move at least one warrior out of a matching clearing.
  - Battle: battle in a matching clearing.
  - Build: place a roost in a matching clearing you rule that has no roost and a free slot.
  If you cannot fully take a card's action, you fall into TURMOIL: lose 1 VP per bird card in the Decree (viziers
  included), discard every Decree card except the viziers, depose your leader (face down) and choose a face-up one
  (all face up again when none is left), tuck the viziers as it says, and Daylight ends.
EVENING: score the Roosts track by roosts on the map (1:0, 2:1, 3:2, 4:3, 5:4, 6:4, 7:5 VP), then draw one card
  plus one per uncovered draw bonus (3rd and 6th roost on the map) and discard down to 5.
SCORING: the Roosts track, 1 VP per enemy building or token removed, crafted items (1 VP each). 30 VP wins.";
}
