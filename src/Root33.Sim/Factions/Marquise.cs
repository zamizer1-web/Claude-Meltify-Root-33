using Root33.Core;

namespace Root33.Sim;

/// <summary>
/// The Marquise de Cat (Law 6). Setup: keep in a corner, a warrior everywhere but the opposite corner, sawmill,
/// workshop and recruiter in or next to the keep. Birdsong: wood at sawmills. Daylight: craft with workshops, then 3
/// actions plus one per bird card spent (battle, march, recruit once, build, overwork). Evening: draw 1 plus the
/// recruiter track's bonuses, discard to 5. Field Hospitals and the Keep. Owner: engine (lead).
/// </summary>
public sealed class Marquise : FactionBase
{
    public override string Id => "marquise";
    public override string Name => "Marquise de Cat";
    public override char SetupOrder => 'A';

    public static readonly string[] BuildingTypes = { PieceTypes.Sawmill, PieceTypes.Workshop, PieceTypes.Recruiter };
    /// <summary>Wood cost of the next building of a type, by buildings of that type already placed (0-5).</summary>
    public static readonly int[] Cost = { 0, 1, 2, 3, 3, 4 };
    public static readonly Dictionary<string, int[]> TrackVp = new()
    {
        [PieceTypes.Sawmill] = new[] { 0, 1, 2, 3, 4, 5 },
        [PieceTypes.Workshop] = new[] { 0, 2, 2, 3, 4, 5 },
        [PieceTypes.Recruiter] = new[] { 0, 1, 2, 3, 3, 4 },
    };

    public override void InitSeat(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        s.SupplyWarriors = 25;
        foreach (var t in BuildingTypes) s.Supply[t] = 6;
        s.Supply[PieceTypes.Wood] = 8;
        s.Supply[PieceTypes.Keep] = 1;
    }

    public static int Placed(SimGame g, int seat, string type) => 6 - g.Supply(seat, type);
    public static int? KeepClearing(SimGame g, int seat) =>
        g.ClearingIds.Where(c => g.TokensOf(seat, c, PieceTypes.Keep) > 0).Select(c => (int?)c).FirstOrDefault();
    public static int DrawBonus(SimGame g, int seat) => (Placed(g, seat, PieceTypes.Recruiter) >= 3 ? 1 : 0) + (Placed(g, seat, PieceTypes.Recruiter) >= 5 ? 1 : 0);

    public override bool BlocksPlacement(SimGame g, int self, int placer, int c) => placer != self && g.TokensOf(self, c, PieceTypes.Keep) > 0;

    public override IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat)
    {
        foreach (var c in g.ClearingIds)
            for (int i = 0; i < g.BuildingsOf(seat, c, PieceTypes.Workshop); i++)
                yield return new CraftPiece($"workshop:{c}:{i}", g.SuitOf(c));
    }

    public override int LossPriority(string type) => type switch
    {
        PieceTypes.Wood => 0, PieceTypes.Recruiter => 1, PieceTypes.Workshop => 2, PieceTypes.Sawmill => 3, PieceTypes.Keep => 9, _ => 5,
    };

    public override string PieceName(string type) => type;

    /// <summary>Field Hospitals (Law 6.2.3): the Marquise may spend a matching card to put removed warriors at the keep.</summary>
    public override void OnWarriorsRemoved(SimGame g, int self, int owner, int c, int count, int? remover)
    {
        if (owner != self || KeepClearing(g, self) == null || !g.CardsMatching(self, g.SuitOf(c)).Any()) return;
        g.Push(new Frame("marquise.field-hospital", self).Set("c", c).Set("n", count));
    }

    public override void Setup(SimGame g, int seat) => g.Push(new Frame("marquise.setup", seat));

    public override void BeginPhase(SimGame g, int seat, TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.Birdsong: g.Push(new Frame("marquise.birdsong", seat)); break;
            case TurnPhase.Daylight: g.Push(new Frame("marquise.daylight", seat)); break;
            case TurnPhase.Evening: g.Push(new Frame("marquise.evening", seat)); break;
        }
    }

    // ---------------------------------------------------------------------------------------------- building

    /// <summary>Wood that can pay for a building in c: c and every clearing joined to it through clearings the Marquise rules (Law 6.5.4.II), nearest first.</summary>
    public static List<int> WoodSources(SimGame g, int seat, int c)
    {
        var order = new List<int>();
        var seen = new HashSet<int> { c };
        var q = new Queue<int>(); q.Enqueue(c);
        while (q.Count > 0)
        {
            int x = q.Dequeue();
            order.Add(x);
            foreach (var y in g.Map.Clearings[x].Paths.OrderBy(y => y))
                if (!seen.Contains(y) && g.Rules(seat, y)) { seen.Add(y); q.Enqueue(y); }
        }
        return order;
    }

    public static int WoodAvailable(SimGame g, int seat, int c) => WoodSources(g, seat, c).Sum(x => g.TokensOf(seat, x, PieceTypes.Wood));

    public static bool CanBuild(SimGame g, int seat, string type, int c) =>
        g.Supply(seat, type) > 0 && g.Rules(seat, c) && g.FreeSlots(c) > 0 && g.CanPlace(seat, c)
        && WoodAvailable(g, seat, c) >= Cost[Placed(g, seat, type)];

    public static void Build(SimGame g, int seat, string type, int c)
    {
        int k = Placed(g, seat, type);
        int cost = Cost[k], vp = TrackVp[type][k];
        int left = cost;
        foreach (var x in WoodSources(g, seat, c))
            while (left > 0 && g.TokensOf(seat, x, PieceTypes.Wood) > 0)
            {
                g.RemoveToken(seat, x, PieceTypes.Wood, null, "spent to build");
                left--;
            }
        g.PlaceBuilding(seat, c, type);
        g.AddVp(seat, vp, $"built a {type}");
    }

    // ---------------------------------------------------------------------------------------------- steps

    public override void RegisterSteps(StepRegistry r)
    {
        r.Add("marquise.setup", SetupNext, SetupChoose);
        r.Add("marquise.birdsong", BirdsongNext, (g, f, o) =>
        {
            int c = int.Parse(o.Key[5..]);
            f.L.Add(c);
            g.PlaceToken(f.Seat, c, PieceTypes.Wood);
        });
        r.Add("marquise.daylight", DaylightNext, DaylightChoose);
        r.Add("marquise.march", (g, f) =>
        {
            if (f.Step == 0) { f.Step = 1; g.Push(Moves.Frame(f.Seat, true, label: "March, move 1 of 2")); return null; }
            if (f.Step == 1 && f.Get("moves") > 0) { f.Step = 2; g.Push(Moves.Frame(f.Seat, true, label: "March, move 2 of 2")); return null; }
            if (f.Get("moves") == 0 && g.Parent(f) is Frame daylight)
            {
                // Choosing March and then "Do not move" is not an action taken: give it back.
                daylight.Set("actions", daylight.Get("actions") + 1);
                g.Log($"{g.SeatName(f.Seat)} does not march; the action is not spent.");
            }
            g.Pop(f);
            return null;
        });
        r.Add("marquise.recruit", (g, f) =>
        {
            var left = Recruiters(g, f.Seat).Where(c => f.L.Count(x => x == c) < g.BuildingsOf(f.Seat, c, PieceTypes.Recruiter)).ToList();
            if (left.Count == 0 || g.Seat(f.Seat).SupplyWarriors == 0) { g.Pop(f); return null; }
            int need = left.Sum(c => g.BuildingsOf(f.Seat, c, PieceTypes.Recruiter) - f.L.Count(x => x == c));
            if (need <= g.Seat(f.Seat).SupplyWarriors)
            {
                foreach (var c in left) for (int i = f.L.Count(x => x == c); i < g.BuildingsOf(f.Seat, c, PieceTypes.Recruiter); i++) { g.PlaceWarriors(f.Seat, c, 1); f.L.Add(c); }
                return null;
            }
            var d = new Decision(f.Seat, $"Recruit: only {g.Seat(f.Seat).SupplyWarriors} warrior(s) left in supply; choose where the next one goes");
            foreach (var c in left.Distinct()) d.Add($"c:{c}", g.ClearingName(c));
            return d;
        }, (g, f, o) => { int c = int.Parse(o.Key[2..]); g.PlaceWarriors(f.Seat, c, 1); f.L.Add(c); });
        r.Add("marquise.evening", (g, f) =>
        {
            if (f.Step == 0)
            {
                int bonus = DrawBonus(g, f.Seat);
                g.Log($"{g.SeatName(f.Seat)} draws 1 card plus {bonus} bonus (uncovered on the recruiter track).");
                g.Draw(f.Seat, 1 + bonus);
                f.Step = 1;
                g.Push(new Frame("discard", f.Seat).Set("limit", SimGame.HandLimit));
                return null;
            }
            g.Pop(f);
            return null;
        });
        r.Add("marquise.field-hospital", (g, f) =>
        {
            int c = f.Get("c"), n = f.Get("n");
            var keep = KeepClearing(g, f.Seat);
            if (keep == null) { g.Pop(f); return null; }
            var cards = g.CardsMatching(f.Seat, g.SuitOf(c)).ToList();
            if (cards.Count == 0) { g.Pop(f); return null; }
            var d = new Decision(f.Seat, $"Field Hospitals: {n} warrior(s) were removed from {g.ClearingName(c)}. Spend a {Cards.SuitName(g.SuitOf(c))} card to place them at the keep in {g.ClearingName(keep.Value)}?");
            foreach (var x in cards) d.Add($"fh:{x}", $"Spend {Cards.Label(x)}");
            d.Add("no", "Do not use Field Hospitals");
            // Default for a non-active Marquise: use it for 2+ warriors with a matching non-bird card that is not a dominance card.
            var cheap = cards.Where(x => Cards.Get(x).Suit != Suit.Bird && Cards.Get(x).Kind != CardKind.Dominance).ToList();
            d.DefaultKey = n >= 2 && cheap.Count > 0 ? $"fh:{cheap[0]}" : "no";
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            if (o.Key == "no") return;
            int card = int.Parse(o.Key[3..]);
            g.DiscardFromHand(f.Seat, card, "spends for Field Hospitals");
            g.PlaceWarriors(f.Seat, KeepClearing(g, f.Seat)!.Value, f.Get("n"));
        });
    }

    static IEnumerable<int> Recruiters(SimGame g, int seat) => g.ClearingIds.Where(c => g.BuildingsOf(seat, c, PieceTypes.Recruiter) > 0);

    // setup: step 0 keep, steps 1-3 the three buildings
    static Decision? SetupNext(SimGame g, Frame f)
    {
        if (f.Step == 0)
        {
            var corners = g.HomeChoices(f.Seat, g.Map.Clearings.Values.Where(c => c.Corner).Select(c => c.Id));
            var d = new Decision(f.Seat, "Marquise setup: place the keep in a corner clearing (a warrior goes everywhere except the diagonally opposite corner)");
            foreach (var c in corners) d.Add($"keep:{c}", $"Keep in {g.ClearingName(c)} (no warrior in {g.ClearingName(g.Map.Clearings[c].OppositeCorner!.Value)})");
            return d;
        }
        if (f.Step > 3) { g.Pop(f); return null; }
        string type = BuildingTypes[f.Step - 1];
        int keep = f.Get("keep");
        var spots = new[] { keep }.Concat(g.Map.Clearings[keep].Paths).Where(c => g.FreeSlots(c) > 0).OrderBy(c => c).ToList();
        if (spots.Count == 0) { g.Log($"No free slot for the starting {type}."); f.Step++; return null; }
        var dd = new Decision(f.Seat, $"Marquise setup: place your starting {type} in the keep's clearing or an adjacent one");
        foreach (var c in spots) dd.Add($"{type}:{c}", $"{type} in {g.ClearingName(c)} ({g.FreeSlots(c)} free slot(s))");
        return dd;
    }

    static void SetupChoose(SimGame g, Frame f, Option o)
    {
        int c = int.Parse(o.Key[(o.Key.IndexOf(':') + 1)..]);
        if (f.Step == 0)
        {
            f.Set("keep", c);
            g.PlaceToken(f.Seat, c, PieceTypes.Keep);
            int opp = g.Map.Clearings[c].OppositeCorner!.Value;
            foreach (var x in g.ClearingIds.Where(x => x != opp)) g.PlaceWarriors(f.Seat, x, 1, log: false);
            g.Log($"{g.SeatName(f.Seat)} garrisons a warrior in every clearing except {g.ClearingName(opp)}.");
        }
        else g.PlaceBuilding(f.Seat, c, BuildingTypes[f.Step - 1]);
        f.Step++;
    }

    static Decision? BirdsongNext(SimGame g, Frame f)
    {
        // one wood per sawmill; f.L lists the clearings already served (one entry per sawmill served)
        var owed = new List<int>();
        foreach (var c in g.ClearingIds)
            for (int i = f.L.Count(x => x == c); i < g.BuildingsOf(f.Seat, c, PieceTypes.Sawmill); i++) owed.Add(c);
        int wood = g.Supply(f.Seat, PieceTypes.Wood);
        if (owed.Count == 0 || wood == 0)
        {
            if (owed.Count > 0) g.Log($"{g.SeatName(f.Seat)} has no wood left in supply for {owed.Count} sawmill(s).");
            g.Pop(f); return null;
        }
        if (owed.Count <= wood)
        {
            foreach (var c in owed) { g.PlaceToken(f.Seat, c, PieceTypes.Wood); f.L.Add(c); }
            return null;
        }
        var d = new Decision(f.Seat, $"Birdsong: only {wood} wood left for {owed.Count} sawmills; choose where the next wood goes");
        foreach (var c in owed.Distinct()) d.Add($"wood:{c}", g.ClearingName(c));
        return d;
    }

    static Decision? DaylightNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        if (f.Step == 0) { f.Step = 1; g.Push(Crafting.Frame(seat)); return null; }
        if (f.Step == 1) { f.Set("actions", 3); f.Step = 2; return null; }
        if (f.Str("pending") != null) { f.SetStr("pending", null); g.ActionResolved(seat); }
        int actions = f.Get("actions");
        var d = new Decision(seat, $"Daylight: {actions} action(s) left. Choose an action") { Open = true };
        if (actions > 0)
        {
            foreach (var (c, def) in Battles.Targets(g, seat)) d.Add($"battle:{c}:{def}", Battles.TargetText(g, c, def));
            if (Moves.Legal(g, seat).Count > 0) d.Add("march", "March: take up to two moves");
            if (g.State.Turn.Counters.GetValueOrDefault("marquise.recruited") == 0 && Recruiters(g, seat).Any() && g.Seat(seat).SupplyWarriors > 0)
                d.Add("recruit", $"Recruit: place a warrior at each recruiter ({string.Join(", ", Recruiters(g, seat).Select(c => $"c{c}"))})");
            foreach (var type in BuildingTypes)
            {
                if (g.Supply(seat, type) == 0) continue;
                int k = Placed(g, seat, type);
                foreach (var c in g.ClearingIds.Where(c => CanBuild(g, seat, type, c)))
                    d.Add($"build:{type}:{c}", $"Build a {type} in {g.ClearingName(c)} (costs {Cost[k]} wood, scores {TrackVp[type][k]} VP)");
            }
            if (g.Supply(seat, PieceTypes.Wood) > 0)
                foreach (var c in g.ClearingIds.Where(c => g.BuildingsOf(seat, c, PieceTypes.Sawmill) > 0 && g.CanPlace(seat, c)))
                    foreach (var card in g.CardsMatching(seat, g.SuitOf(c)))
                        d.Add($"overwork:{c}:{card}", $"Overwork: spend {Cards.Label(card)} to place a wood at the sawmill in {g.ClearingName(c)}");
        }
        foreach (var card in g.Seat(seat).Hand.Where(x => Cards.Get(x).Suit == Suit.Bird))
            d.Add($"extra:{card}", $"Spend {Cards.Label(card)} for one more action");
        d.Add("end", "End Daylight");
        return d;
    }

    static void DaylightChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        var p = o.Key.Split(':');
        switch (p[0])
        {
            case "end": g.Pop(f); return;
            case "extra":
                g.DiscardFromHand(seat, int.Parse(p[1]), "spends a bird card for an extra action");
                f.Set("actions", f.Get("actions") + 1);
                return;
        }
        f.Set("actions", f.Get("actions") - 1);
        f.SetStr("pending", p[0]);
        switch (p[0])
        {
            case "battle": Battles.Start(g, seat, int.Parse(p[2]), int.Parse(p[1])); break;
            case "march": g.Push(new Frame("marquise.march", seat)); break;
            case "recruit":
                g.State.Turn.Counters["marquise.recruited"] = 1;
                g.Push(new Frame("marquise.recruit", seat));
                break;
            case "build": Build(g, seat, p[1], int.Parse(p[2])); break;
            case "overwork":
                g.DiscardFromHand(seat, int.Parse(p[2]), "spends for Overwork");
                g.PlaceToken(seat, int.Parse(p[1]), PieceTypes.Wood);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------- text

    public override string BoardText(SimGame g, int seat)
    {
        var lines = new List<string> { $"{Name} board:" };
        foreach (var t in BuildingTypes)
        {
            int k = Placed(g, seat, t);
            lines.Add(k >= 6
                ? $"  {t}s: all 6 on the map"
                : $"  {t}s: {6 - k} on the track; next costs {Cost[k]} wood and scores {TrackVp[t][k]} VP");
        }
        var keep = KeepClearing(g, seat);
        lines.Add($"  keep: {(keep is int kc ? g.ClearingName(kc) : "removed (no Field Hospitals; it cannot return)")}");
        lines.Add($"  wood in supply: {g.Supply(seat, PieceTypes.Wood)}/8; warriors in supply: {g.Seat(seat).SupplyWarriors}/25; evening draw: {1 + DrawBonus(g, seat)}");
        return string.Join("\n", lines);
    }

    public override string RulesText(SimGame g, int seat) =>
@"MARQUISE DE CAT (faction board)
Your pieces: 25 warriors, 6 sawmills, 6 workshops, 6 recruiters (buildings), 8 wood and the keep (tokens).
The Keep: only you may place pieces in the keep's clearing. If the keep is removed it never returns.
Field Hospitals: whenever your warriors are removed from a clearing, you may spend a card of that clearing's suit
  (birds are wild) to place those warriors at the keep instead.
BIRDSONG: place one wood at each sawmill (if wood runs short, you choose which sawmills get it).
DAYLIGHT: first craft cards with your workshops (each workshop gives its clearing's suit once per turn). Then take
  up to 3 actions, plus one more for each bird card you spend, in any order:
  - Battle: in a clearing with your warriors, against an enemy there.
  - March: up to two moves (move warriors along a path; you must rule the origin or the destination).
  - Recruit (once per turn): place a warrior at each recruiter.
  - Build: in a clearing you rule with a free slot, place the next sawmill, workshop or recruiter. Pay its wood cost
    (0,1,2,3,3,4 by how many of that type are out) from that clearing or clearings joined to it through clearings you
    rule, and score the VP uncovered: sawmills 0,1,2,3,4,5; workshops 0,2,2,3,4,5; recruiters 0,1,2,3,3,4.
  - Overwork: spend a card matching a sawmill's clearing to place a wood there.
EVENING: draw one card, plus one for each uncovered draw bonus (3rd and 5th recruiter placed), then discard down to 5.
SCORING: buildings as above, 1 VP per enemy building or token you remove, crafted items' VP. 30 VP wins.";
}
