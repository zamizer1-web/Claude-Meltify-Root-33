using Root33.Core;

namespace Root33.Sim;

/// <summary>Movement (Law 4.2): paths only (forests are not walkable), and the mover must rule the origin or destination.</summary>
public static class Moves
{
    /// <summary>Clearings a seat may step to from c: paths, plus rivers when the core granted "rivers-as-paths".</summary>
    public static IEnumerable<int> Neighbours(SimGame g, int seat, int c)
    {
        var m = g.Map.Clearings[c];
        var l = m.Paths.ToList();
        if (g.HasAbility(seat, "rivers-as-paths")) l.AddRange(m.RiverLinks.Where(x => !l.Contains(x)));
        return l.OrderBy(x => x);
    }

    public static bool CanMove(SimGame g, int seat, int from, int to) =>
        Neighbours(g, seat, from).Contains(to) &&
        (g.Faction(seat).IgnoresRuleToMove || g.Rules(seat, from) || g.Rules(seat, to));

    /// <summary>Warriors that may leave c (the core's guard floor keeps some behind).</summary>
    public static int Movable(SimGame g, int seat, int c) => Math.Max(0, g.Warriors(seat, c) - g.GuardFloor(seat, c));

    /// <summary>Every (from, to) a seat's warriors may take now; fromSuit narrows the origin (Eyrie Decree).</summary>
    public static List<(int From, int To)> Legal(SimGame g, int seat, Suit? fromSuit = null)
    {
        var l = new List<(int, int)>();
        foreach (var from in g.ClearingIds)
        {
            if (Movable(g, seat, from) == 0) continue;
            if (fromSuit is Suit s && s != Suit.Bird && g.SuitOf(from) != s) continue;
            foreach (var to in Neighbours(g, seat, from))
                if (CanMove(g, seat, from, to)) l.Add((from, to));
        }
        return l;
    }

    /// <summary>
    /// Push a "move" frame: one move of the seat's warriors. Args: optional (may skip), fromSuit (-1 any),
    /// fixedFrom (-1 any). When it moves, the parent frame's "moves" counter goes up by one.
    /// </summary>
    public static Frame Frame(int seat, bool optional = true, Suit? fromSuit = null, string label = "Move", int fixedFrom = -1)
    {
        var f = new Frame("move", seat).Set("optional", optional ? 1 : 0).Set("fromSuit", fromSuit is Suit s ? (int)s : -1).Set("fixedFrom", fixedFrom);
        f.SetStr("label", label);
        return f;
    }

    internal static void Register(StepRegistry r)
    {
        r.Add("move", (g, f) =>
        {
            if (f.Step == 0)
            {
                Suit? suit = f.Get("fromSuit", -1) >= 0 ? (Suit)f.Get("fromSuit") : null;
                var legal = Legal(g, f.Seat, suit).Where(p => f.Get("fixedFrom", -1) < 0 || p.From == f.Get("fixedFrom")).ToList();
                if (legal.Count == 0) { if (f.Get("optional") == 0) g.Log($"{g.SeatName(f.Seat)} has no legal move."); g.Pop(f); return null; }
                var d = new Decision(f.Seat, $"{f.Str("label") ?? "Move"}: choose where to move warriors from and to");
                foreach (var (a, b) in legal)
                    d.Add($"move:{a}-{b}", $"From {g.ClearingName(a)} ({Movable(g, f.Seat, a)} warriors) to {g.ClearingName(b)}");
                if (f.Get("optional") == 1) d.Add("skip", "Do not move");
                return d;
            }
            int from = f.Get("from"), to = f.Get("to"), max = Movable(g, f.Seat, from);
            var dc = new Decision(f.Seat, $"How many warriors move from {g.ClearingName(from)} to {g.ClearingName(to)}?");
            for (int n = max; n >= 1; n--) dc.Add($"count:{n}", $"{n} warrior{(n == 1 ? "" : "s")}");
            return dc;
        }, (g, f, o) =>
        {
            if (o.Key == "skip") { g.Pop(f); return; }
            if (o.Key.StartsWith("move:"))
            {
                var p = o.Key[5..].Split('-', '>');
                f.Set("from", int.Parse(p[0])).Set("to", int.Parse(p[1]));
                f.Step = 1;
                return;
            }
            int n = int.Parse(o.Key[6..]);
            var parent = g.Parent(f);
            parent?.Set("moves", parent.Get("moves") + 1);
            g.Pop(f);
            g.MoveWarriors(f.Seat, f.Get("from"), f.Get("to"), n);
        });
    }
}

/// <summary>
/// Battle (Law 4.3): ambush and counter-ambush, two 0-3 dice (attacker the higher, defender the lower, each capped by
/// its warriors in the clearing), after-roll card effects, the defenseless extra hit, the core's extra hits, then hits
/// that remove warriors first and buildings/tokens chosen by their owner (1 VP each to the other side).
/// </summary>
public static class Battles
{
    /// <summary>Every (clearing, defender) the seat may battle now (the core's hook.battle-legal is asked here).</summary>
    public static List<(int Clearing, int Defender)> Targets(SimGame g, int seat, Suit? suit = null)
    {
        var l = new List<(int, int)>();
        foreach (var c in g.ClearingIds)
        {
            if (suit is Suit s && s != Suit.Bird && g.SuitOf(c) != s) continue;
            if (!g.Faction(seat).CanAttackFrom(g, seat, c)) continue;
            for (int d = 0; d < g.SeatCount; d++)
                if (d != seat && g.Seat(d).CoalitionWith != seat && g.Seat(seat).CoalitionWith != d
                    && g.Faction(d).PresentIn(g, d, c) && g.Bridge.OnBattleLegal(seat, d, c))
                    l.Add((c, d));
        }
        return l;
    }

    public static string TargetText(SimGame g, int c, int d) => $"Battle {g.SeatName(d)} in {g.ClearingName(c)}";

    /// <summary>Push a battle. When it ends, the parent frame's "battles" counter goes up by one.</summary>
    public static void Start(SimGame g, int attacker, int defender, int c) =>
        g.Push(new Frame("battle", attacker).Set("def", defender).Set("c", c));

    static IEnumerable<int> AmbushCards(SimGame g, int seat, int c) =>
        g.Seat(seat).Hand.Where(x => Cards.Get(x).Kind == CardKind.Ambush && (Cards.Get(x).Suit == Suit.Bird || Cards.Get(x).Suit == g.SuitOf(c)))
            .OrderBy(x => Cards.Get(x).Suit == Suit.Bird ? 1 : 0);

    internal static void Register(StepRegistry r)
    {
        r.Add("battle", Next, Choose);
        r.Add("hits", HitsNext, HitsChoose);
    }

    static Decision? Next(SimGame g, Frame f)
    {
        int att = f.Seat, def = f.Get("def"), c = f.Get("c");
        var b = g.State.Battle;
        switch (f.Step)
        {
            case 0:
            {
                var vps = g.State.Seats.Select(s => s.Vp).ToList();
                int top = vps.Max();
                g.State.Battle = new BattleState
                {
                    Attacker = att, Defender = def, Clearing = c,
                    VpLeaderAtStart = vps.Count(v => v == top) == 1 ? vps.IndexOf(top) : null,
                };
                g.Log($"{g.SeatName(att)} battles {g.SeatName(def)} in {g.ClearingName(c)}.");
                g.Bridge.OnBattleStart(att, def, c);
                f.Step = 1;
                return null;
            }
            case 1: // defender may ambush (Law 4.3.1)
            {
                var amb = AmbushCards(g, def, c).ToList();
                if (amb.Count == 0) { f.Step = 3; return null; }
                if (g.HasPersistent(att, "scouting-party"))
                {
                    g.Log($"{g.SeatName(att)}'s Scouting Party: ambush cards do not affect this attack.");
                    f.Step = 3; return null;
                }
                var d = new Decision(def, $"Defend in {g.ClearingName(c)}: play an ambush card?");
                foreach (var a in amb) d.Add($"ambush:{a}", $"Play {Cards.Label(a)}: deal two hits now");
                d.Add("no", "Do not ambush");
                d.DefaultKey = $"ambush:{amb[0]}";
                return d;
            }
            case 2: // attacker may foil (Law 4.3.1.I)
            {
                var amb = AmbushCards(g, att, c).ToList();
                if (amb.Count == 0) { ResolveAmbush(g, f); return null; }
                var d = new Decision(att, $"{g.SeatName(def)} ambushes you! Foil it with your own ambush card?");
                foreach (var a in amb) d.Add($"foil:{a}", $"Play {Cards.Label(a)} to cancel the ambush");
                d.Add("no", "Do not foil: take two hits");
                return d;
            }
            case 21: // after the ambush hits: stop if no attacking warriors remain
                if (g.Faction(att).AttackingPieces(g, att, c) == 0)
                {
                    g.Log("No attacking pieces remain: the battle ends.");
                    f.Step = 7; return null;
                }
                f.Step = 3; return null;
            case 3: // roll (Law 4.3.3)
            {
                int x = g.RollDie(), y = g.RollDie();
                b!.AttackerRoll = Math.Max(x, y); b.DefenderRoll = Math.Min(x, y);
                if (g.Faction(def).DefenderTakesHigherRoll) (b.AttackerRoll, b.DefenderRoll) = (b.DefenderRoll, b.AttackerRoll);
                g.Log($"Dice: {x} and {y}. {g.SeatName(att)} (attacker) takes {b.AttackerRoll}, {g.SeatName(def)} (defender) takes {b.DefenderRoll}.");
                f.Step = 4; return null;
            }
            case 4: // attacker's after-roll effects
            {
                var d = new Decision(att, "Battle effects before hits are counted");
                if (!b!.BrutalTactics && g.HasPersistent(att, "brutal-tactics"))
                    d.Add("brutal", "Brutal Tactics: deal an extra hit (the defender scores one point)");
                if (!b.AttackerArmorers && g.HasPersistent(att, "armorers"))
                    d.Add("armorers", $"Discard Armorers: ignore the {Capped(g, def, c, b.DefenderRoll)} rolled hit(s) you would take");
                if (d.Options.Count == 0) { f.Step = 5; return null; }
                d.Add("continue", "Continue to hits");
                return d;
            }
            case 5: // defender's after-roll effects (auto for the non-active defender)
            {
                var d = new Decision(def, "Battle effects as defender");
                int incoming = Capped(g, att, c, b!.AttackerRoll);
                string dflt = "continue";
                if (!b.DefenderArmorers && g.HasPersistent(def, "armorers"))
                {
                    d.Add("armorers", $"Discard Armorers: ignore the {incoming} rolled hit(s) you would take");
                    if (incoming >= 2 || (incoming >= 1 && incoming > g.Warriors(def, c))) dflt = "armorers";
                }
                if (!b.Sappers && g.HasPersistent(def, "sappers"))
                {
                    d.Add("sappers", "Discard Sappers: deal an extra hit");
                    if (dflt == "continue" && g.Faction(att).PresentIn(g, att, c)) dflt = "sappers";
                }
                if (d.Options.Count == 0) { f.Step = 6; return null; }
                d.Add("continue", "Continue to hits");
                d.DefaultKey = dflt;
                return d;
            }
            case 6: // count and deal hits (Law 4.3.5-4.3.6)
            {
                var mod = g.State.Mod;
                int attRolled = b!.DefenderArmorers ? 0 : Capped(g, att, c, b.AttackerRoll);
                int defRolled = b.AttackerArmorers ? 0 : Capped(g, def, c, b.DefenderRoll);
                int attHits = attRolled + (b.BrutalTactics ? 1 : 0) + mod.ExtraHits.GetValueOrDefault(att) + g.Faction(att).ExtraHits(g, att, true);
                int defHits = defRolled + (b.Sappers ? 1 : 0) + mod.ExtraHits.GetValueOrDefault(def) + g.Faction(def).ExtraHits(g, def, false);
                bool defenseless = g.Faction(def).IsDefenseless(g, def, c);
                if (defenseless) attHits++;
                if (mod.IgnoreFirstHit.Contains(def) && attHits > 0) attHits--;
                if (mod.IgnoreFirstHit.Contains(att) && defHits > 0) defHits--;
                b.AttackerHits = attHits; b.DefenderHits = defHits;
                g.Log($"Hits: {g.SeatName(att)} deals {attHits}{(defenseless ? " (incl. +1, defender defenseless)" : "")}, {g.SeatName(def)} deals {defHits}.");
                f.Step = 7;
                g.Push(HitsFrame(g, att, c, defHits, def));
                g.Push(HitsFrame(g, def, c, attHits, att));
                return null;
            }
            default: // resolved
            {
                var parent = g.Parent(f);
                parent?.Set("battles", parent.Get("battles") + 1);
                g.Pop(f);
                var done = g.State.Battle!;
                g.State.Battle = null;
                g.State.Mod.ExtraHits.Clear(); g.State.Mod.ExtraMaxHits.Clear(); g.State.Mod.IgnoreFirstHit.Clear();
                g.Bridge.OnBattleResolved(done);
                for (int s = 0; s < g.SeatCount; s++) g.Faction(s).OnBattleResolved(g, s, done);
                return null;
            }
        }
    }

    static int Capped(SimGame g, int seat, int c, int roll) =>
        Math.Min(roll, g.Faction(seat).MaxRolledHits(g, seat, c) + g.State.Mod.ExtraMaxHits.GetValueOrDefault(seat));

    static void ResolveAmbush(SimGame g, Frame f)
    {
        int att = f.Seat, def = f.Get("def"), c = f.Get("c");
        g.Log($"The ambush deals two hits to {g.SeatName(att)}.");
        f.Step = 21;
        g.Push(HitsFrame(g, att, c, 2, def));
    }

    static void Choose(SimGame g, Frame f, Option o)
    {
        int att = f.Seat, def = f.Get("def"), c = f.Get("c");
        var b = g.State.Battle!;
        switch (f.Step)
        {
            case 1:
                if (o.Key == "no") { f.Step = 3; return; }
                int card = int.Parse(o.Key[7..]);
                b.AmbushCard = card;
                g.DiscardFromHand(def, card, "plays an ambush");
                f.Step = 2;
                return;
            case 2:
                if (o.Key == "no") { ResolveAmbush(g, f); return; }
                g.DiscardFromHand(att, int.Parse(o.Key[5..]), "foils the ambush with");
                b.Foiled = true;
                f.Step = 3;
                return;
            case 4:
                if (o.Key == "brutal")
                {
                    b.BrutalTactics = true;
                    g.Log($"{g.SeatName(att)} uses Brutal Tactics: one extra hit.");
                    g.AddVp(def, 1, "Brutal Tactics was used against them");
                }
                else if (o.Key == "armorers")
                {
                    b.AttackerArmorers = true;
                    g.DiscardFromPlay(att, g.PersistentCard(att, "armorers")!.Value, "discards Armorers to ignore rolled hits");
                }
                else f.Step = 5;
                return;
            case 5:
                if (o.Key == "armorers")
                {
                    b.DefenderArmorers = true;
                    g.DiscardFromPlay(def, g.PersistentCard(def, "armorers")!.Value, "discards Armorers to ignore rolled hits");
                }
                else if (o.Key == "sappers")
                {
                    b.Sappers = true;
                    g.DiscardFromPlay(def, g.PersistentCard(def, "sappers")!.Value, "discards Sappers for an extra hit");
                }
                else f.Step = 6;
                return;
        }
    }

    /// <summary>A frame that makes seat take n hits in c (the faction's own TakeHitsKind if it has one).</summary>
    public static Frame HitsFrame(SimGame g, int seat, int c, int n, int? remover)
    {
        var f = new Frame(g.Faction(seat).TakeHitsKind ?? "hits", seat).Set("c", c).Set("n", n).Set("remover", remover ?? -1);
        return f;
    }

    static Decision? HitsNext(SimGame g, Frame f)
    {
        int seat = f.Seat, c = f.Get("c"), n = f.Get("n");
        int? remover = f.Get("remover", -1) < 0 ? null : f.Get("remover");
        if (n <= 0) { g.Pop(f); return null; }
        int w = g.Warriors(seat, c);
        if (w > 0)
        {
            int k = Math.Min(n, w);
            f.Set("n", n - k);
            g.RemoveWarriors(seat, c, k, remover, "hits");
            return null;
        }
        var cs = g.Clearing(c);
        var pieces = cs.Buildings.Where(p => p.Seat == seat).Select(p => ("building", p.Type))
            .Concat(cs.Tokens.Where(p => p.Seat == seat).Select(p => ("token", p.Type))).ToList();
        if (pieces.Count == 0) { g.Pop(f); return null; }
        var kinds = pieces.Distinct().ToList();
        if (pieces.Count <= n || kinds.Count == 1)
        {
            var (kind, type) = pieces[0];
            f.Set("n", n - 1);
            if (kind == "building") g.RemoveBuilding(seat, c, type, remover, "hit");
            else g.RemoveToken(seat, c, type, remover, "hit");
            return null;
        }
        var fac = g.Faction(seat);
        var d = new Decision(seat, $"You take {n} more hit(s) in {g.ClearingName(c)}: choose a piece to lose");
        foreach (var (kind, type) in kinds.OrderBy(k => fac.LossPriority(k.Item2)))
            d.Add($"lose:{kind}:{type}", $"Lose a {fac.PieceName(type)} {kind}");
        d.DefaultKey = d.Options[0].Key;
        return d;
    }

    static void HitsChoose(SimGame g, Frame f, Option o)
    {
        var p = o.Key.Split(':');
        int? remover = f.Get("remover", -1) < 0 ? null : f.Get("remover");
        f.Set("n", f.Get("n") - 1);
        if (p[1] == "building") g.RemoveBuilding(f.Seat, f.Get("c"), p[2], remover, "hit");
        else g.RemoveToken(f.Seat, f.Get("c"), p[2], remover, "hit");
    }
}

/// <summary>Crafting (Law 4.1): activate crafting pieces matching the cost; items from the supply; persistent effects; favors.</summary>
public static class Crafting
{
    public static List<CraftPiece> FreePieces(SimGame g, int seat) =>
        g.Faction(seat).CraftingPieces(g, seat).Where(p => !g.State.Turn.UsedCraftPieces.Contains(p.Key)).ToList();

    /// <summary>Pick pieces paying a cost: exact suits first, then any suit; null when it cannot be paid.</summary>
    public static List<CraftPiece>? Pay(CardDef card, List<CraftPiece> pieces)
    {
        var left = pieces.ToList();
        var used = new List<CraftPiece>();
        foreach (var s in card.Cost)
        {
            var p = left.FirstOrDefault(x => x.Suit == s);
            if (p == null) return null;
            left.Remove(p); used.Add(p);
        }
        if (left.Count < card.AnyCost) return null;
        used.AddRange(left.Take(card.AnyCost));
        return used;
    }

    public static string? WhyNot(SimGame g, int seat, int cardId)
    {
        var card = Cards.Get(cardId);
        if (!card.Craftable) return "cannot be crafted";
        if (card.Kind == CardKind.Persistent && g.Seat(seat).PlayArea.Any(x => Cards.Get(x).Key == card.Key)) return "you already have it";
        if (card.Item is ItemType it && !g.State.ItemSupply.Contains(it)) return $"no {it} left in the item supply";
        if (Pay(card, FreePieces(g, seat)) == null) return "not enough crafting pieces of its suits";
        return null;
    }

    public static IEnumerable<int> Craftable(SimGame g, int seat) => g.Seat(seat).Hand.Where(c => WhyNot(g, seat, c) == null).ToList();

    /// <summary>Craft a card from hand: pay, then resolve (item and VP, persistent effect, or favor).</summary>
    public static void Craft(SimGame g, int seat, int cardId)
    {
        var card = Cards.Get(cardId);
        var why = WhyNot(g, seat, cardId);
        if (why != null) throw new InvalidOperationException($"cannot craft {card.Name}: {why}");
        var pay = Pay(card, FreePieces(g, seat))!;
        g.Faction(seat).SpendCraftingPieces(g, seat, pay);
        g.Seat(seat).Hand.Remove(cardId);
        g.Log($"{g.SeatName(seat)} crafts {Cards.Label(cardId)} (activating {string.Join(", ", pay.Select(p => Cards.SuitName(p.Suit)))}).");
        switch (card.Kind)
        {
            case CardKind.Item:
                var item = card.Item!.Value;
                g.State.ItemSupply.Remove(item);
                g.Faction(seat).GainCraftedItem(g, seat, item);
                g.ToDiscard(cardId);
                g.AddVp(seat, g.Faction(seat).CraftVp(g, seat, card), $"crafted {item}");
                break;
            case CardKind.Persistent:
                g.Seat(seat).PlayArea.Add(cardId);
                break;
            case CardKind.Immediate:
                var suit = card.Suit;
                foreach (var c in g.ClearingIds.Where(c => g.SuitOf(c) == suit))
                {
                    for (int o = 0; o < g.SeatCount; o++)
                    {
                        if (o == seat) continue;
                        g.RemoveWarriors(o, c, g.Warriors(o, c), seat, card.Name);
                        foreach (var p in g.Clearing(c).Buildings.Where(p => p.Seat == o).ToList()) g.RemoveBuilding(o, c, p.Type, seat, card.Name);
                        foreach (var p in g.Clearing(c).Tokens.Where(p => p.Seat == o).ToList()) g.RemoveToken(o, c, p.Type, seat, card.Name);
                        Vagabond.FullRemoval(g, o, c, seat); // Law 9.2.2.I: a Vagabond caught in a favor damages 3 items
                    }
                }
                g.ToDiscard(cardId);
                break;
        }
    }

    /// <summary>The one line a seat reads when its crafting step finds nothing it can craft (private: it speaks of the hand).</summary>
    public static string NothingToCraft(SimGame g, int seat)
    {
        var free = FreePieces(g, seat).GroupBy(p => p.Suit).OrderBy(x => x.Key).Select(x => $"{x.Count()} {Cards.SuitName(x.Key)}").ToList();
        var pieces = free.Count == 0 ? "no free crafting pieces" : "free crafting pieces: " + string.Join(", ", free);
        var hand = g.Seat(seat).Hand.Count == 0 ? "your hand is empty" : "nothing in your hand can be crafted now";
        return $"Crafting: {hand} ({pieces}), so crafting is skipped this turn.";
    }

    /// <summary>A "craft" frame: craft cards one at a time until done. once=1 crafts at most one card (an action).</summary>
    public static Frame Frame(int seat, bool once = false) => new Frame("craft", seat).Set("once", once ? 1 : 0);

    internal static void Register(StepRegistry r)
    {
        r.Add("craft", (g, f) =>
        {
            var list = Craftable(g, f.Seat).ToList();
            if (list.Count == 0)
            {
                // A crafting step (not the Vagabond's one-card action) with nothing craftable says so instead of passing silently.
                if (f.Get("once") == 0 && f.Get("crafted") == 0) g.Log(NothingToCraft(g, f.Seat), f.Seat);
                g.Pop(f);
                return null;
            }
            var d = new Decision(f.Seat, "Craft: choose a card to craft") { Open = true };
            foreach (var c in list) d.Add($"craft:{c}", $"Craft {Cards.DescribeToCraft(c)}");
            d.Add("done", f.Get("once") == 1 ? "Do not craft" : "Stop crafting");
            return d;
        }, (g, f, o) =>
        {
            if (o.Key == "done") { g.Pop(f); return; }
            Craft(g, f.Seat, int.Parse(o.Key[6..]));
            f.Set("crafted", f.Get("crafted") + 1);
            if (f.Get("once") == 1) g.Pop(f);
        });
    }
}

/// <summary>Shared steps: card effects with timing, hand limit, dominance, open-point options.</summary>
public sealed partial class SimGame
{
    private static void RegisterSharedSteps(StepRegistry r)
    {
        Moves.Register(r);
        Battles.Register(r);
        Crafting.Register(r);

        // Discard down to a hand limit (Evening). Arg "limit" (default 5).
        r.Add("discard", (g, f) =>
        {
            var hand = g.Seat(f.Seat).Hand;
            int limit = f.Get("limit", HandLimit);
            if (hand.Count <= limit) { g.Pop(f); return null; }
            var d = new Decision(f.Seat, $"You hold {hand.Count} cards: discard down to {limit} (choose one)");
            foreach (var c in hand) d.Add($"discard:{c}", $"Discard {Cards.Describe(c)}");
            return d;
        }, (g, f, o) => g.DiscardFromHand(f.Seat, int.Parse(o.Key[8..]), "discards"));

        // Better Burrow Bank: at start of Birdsong, you and another player draw a card.
        r.Add("card.burrow-bank", (g, f) =>
        {
            var d = new Decision(f.Seat, "Better Burrow Bank: you and another player of your choice draw a card");
            foreach (var s in g.TurnOrder(f.Seat).Skip(1)) d.Add($"seat:{s}", $"{g.SeatName(s)} draws with you");
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            g.Log($"{g.SeatName(f.Seat)} uses Better Burrow Bank.");
            g.Draw(f.Seat, 1);
            g.Draw(int.Parse(o.Key[5..]), 1);
        });

        // In Birdsong: Royal Claim and Stand and Deliver! (offered once, at the end of the faction's Birdsong steps).
        r.Add("card.birdsong", (g, f) =>
        {
            var d = new Decision(f.Seat, "Birdsong card effects (or continue to Daylight)");
            var claim = g.PersistentCard(f.Seat, "royal-claim");
            if (claim != null)
                d.Add("claim", $"Discard Royal Claim: score {g.RuledBy(f.Seat).Count()} VP (one per clearing you rule)");
            var sd = g.PersistentCard(f.Seat, "stand-and-deliver");
            if (sd != null && !g.State.Turn.UsedOnce.Contains("stand-and-deliver"))
                foreach (var s in g.TurnOrder(f.Seat).Skip(1).Where(s => g.Seat(s).Hand.Count > 0))
                    d.Add($"steal:{s}", $"Stand and Deliver!: take a random card from {g.SeatName(s)} (they score 1 VP)");
            if (d.Options.Count == 0) { g.Pop(f); return null; }
            d.Add("continue", "Continue to Daylight");
            return d;
        }, (g, f, o) =>
        {
            if (o.Key == "continue") { g.Pop(f); return; }
            if (o.Key == "claim")
            {
                int n = g.RuledBy(f.Seat).Count();
                g.DiscardFromPlay(f.Seat, g.PersistentCard(f.Seat, "royal-claim")!.Value, "discards Royal Claim");
                g.AddVp(f.Seat, n, "Royal Claim");
                return;
            }
            int victim = int.Parse(o.Key[6..]);
            g.State.Turn.UsedOnce.Add("stand-and-deliver");
            var hand = g.Seat(victim).Hand;
            int card = hand[g.Random(hand.Count)];
            hand.Remove(card);
            g.Seat(f.Seat).Hand.Add(card);
            g.Log($"{g.SeatName(f.Seat)} takes a random card from {g.SeatName(victim)} (Stand and Deliver!).");
            g.Log($"You took {Cards.Label(card)}.", f.Seat);
            g.Log($"{Cards.Label(card)} was taken from you.", victim);
            g.AddVp(victim, 1, "Stand and Deliver! was used on them");
        });

        // Command Warren: at start of Daylight, you may initiate a battle.
        r.Add("card.command-warren", (g, f) =>
        {
            var t = Battles.Targets(g, f.Seat);
            if (t.Count == 0) { g.Pop(f); return null; }
            var d = new Decision(f.Seat, "Command Warren: you may initiate a battle");
            foreach (var (c, def) in t) d.Add($"battle:{c}:{def}", Battles.TargetText(g, c, def));
            d.Add("skip", "Do not battle");
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            if (o.Key == "skip") return;
            var p = o.Key.Split(':');
            Battles.Start(g, f.Seat, int.Parse(p[2]), int.Parse(p[1]));
        });

        // Cobbler: at start of Evening, you may take a move.
        r.Add("card.cobbler", (g, f) =>
        {
            g.Pop(f);
            g.Push(Moves.Frame(f.Seat, optional: true, label: "Cobbler"));
            return null;
        });

        // Take an available dominance card: choose the card to spend.
        r.Add("card.take-dominance", (g, f) =>
        {
            int dom = f.Get("card");
            var suit = Cards.Get(dom).Suit;
            var pay = g.Seat(f.Seat).Hand.Where(c => suit == Suit.Bird ? Cards.Get(c).Suit == Suit.Bird : Cards.Matches(c, suit)).ToList();
            var d = new Decision(f.Seat, $"Take {Cards.Label(dom)}: choose a matching card to spend");
            foreach (var c in pay) d.Add($"spend:{c}", $"Spend {Cards.Label(c)}");
            d.Add("cancel", "Cancel");
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            if (o.Key == "cancel") return;
            int dom = f.Get("card");
            g.DiscardFromHand(f.Seat, int.Parse(o.Key[6..]), "spends");
            g.State.AvailableDominance.Remove(dom);
            g.Seat(f.Seat).Hand.Add(dom);
            g.Log($"{g.SeatName(f.Seat)} takes the available {Cards.Label(dom)}.");
        });

        // Tax Collector: remove one of your warriors from the map to draw a card.
        r.Add("card.tax-collector", (g, f) =>
        {
            var d = new Decision(f.Seat, "Tax Collector: remove one of your warriors from which clearing?");
            foreach (var c in g.ClearingIds.Where(c => g.Warriors(f.Seat, c) > 0)) d.Add($"c:{c}", g.ClearingName(c));
            d.Add("cancel", "Cancel");
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            if (o.Key == "cancel") return;
            g.State.Turn.UsedOnce.Add("tax-collector");
            g.RemoveWarriors(f.Seat, int.Parse(o.Key[2..]), 1, f.Seat, "Tax Collector");
            g.Draw(f.Seat, 1);
        });

        // Codebreakers: look at another player's hand.
        r.Add("card.codebreakers", (g, f) =>
        {
            var d = new Decision(f.Seat, "Codebreakers: look at whose hand?");
            foreach (var s in g.TurnOrder(f.Seat).Skip(1)) d.Add($"seat:{s}", g.SeatName(s));
            return d;
        }, (g, f, o) =>
        {
            g.Pop(f);
            int s = int.Parse(o.Key[5..]);
            g.State.Turn.UsedOnce.Add("codebreakers");
            g.Log($"{g.SeatName(f.Seat)} looks at {g.SeatName(s)}'s hand (Codebreakers).");
            g.Log($"{g.SeatName(s)}'s hand: {string.Join(", ", g.Seat(s).Hand.Select(Cards.Label))}.", f.Seat);
        });
    }

    /// <summary>Card options usable "in Daylight" at an open point of the active seat's turn, plus the faction's own.</summary>
    private void AddOpenOptions(Decision d)
    {
        int seat = d.Seat;
        if (!State.SetupDone || seat != State.Turn.Seat) return;
        var s = Seat(seat);
        if (State.Turn.Phase == TurnPhase.Daylight)
        {
            if (s.Dominance == null && s.Vp >= 10 && Faction(seat).UsesDominanceNormally)
                foreach (var c in s.Hand.Where(c => Cards.Get(c).Kind == CardKind.Dominance))
                    d.Add($"card:dominance:{c}", $"Activate {Cards.Label(c)} (you stop scoring VP; {DominanceGoal(c)})");
            foreach (var c in State.AvailableDominance)
            {
                var suit = Cards.Get(c).Suit;
                if (s.Hand.Any(h => suit == Suit.Bird ? Cards.Get(h).Suit == Suit.Bird : Cards.Matches(h, suit)))
                    d.Add($"card:take-dominance:{c}", $"Take the available {Cards.Label(c)} by spending a matching card");
            }
            if (HasPersistent(seat, "tax-collector") && !State.Turn.UsedOnce.Contains("tax-collector") && ClearingIds.Any(c => Warriors(seat, c) > 0))
                d.Add("card:tax-collector", "Tax Collector: remove one of your warriors to draw a card");
            if (HasPersistent(seat, "codebreakers") && !State.Turn.UsedOnce.Contains("codebreakers"))
                d.Add("card:codebreakers", "Codebreakers: look at another player's hand");
        }
        foreach (var o in Faction(seat).OpenOptions(this, seat)) d.Options.Add(o);
    }

    private void ChooseCardOption(int seat, Option o)
    {
        var p = o.Key.Split(':');
        switch (p[1])
        {
            case "dominance":
            {
                int c = int.Parse(p[2]);
                Seat(seat).Hand.Remove(c);
                Seat(seat).PlayArea.Add(c);
                Seat(seat).Dominance = c;
                Log($"{SeatName(seat)} activates {Cards.Label(c)}: their score marker leaves the track; they win if they {DominanceGoal(c)} at the start of their Birdsong.");
                break;
            }
            case "take-dominance": Push(new Frame("card.take-dominance", seat).Set("card", int.Parse(p[2]))); break;
            case "tax-collector": Push(new Frame("card.tax-collector", seat)); break;
            case "codebreakers": Push(new Frame("card.codebreakers", seat)); break;
            default: throw new InvalidOperationException("unknown card option " + o.Key);
        }
    }

    public string DominanceGoal(int card)
    {
        var suit = Cards.Get(card).Suit;
        return suit == Suit.Bird ? "rule two clearings in opposite corners" : $"rule three {Cards.SuitName(suit)} clearings";
    }

    /// <summary>Law 3.3.1: an activated dominance card wins at the start of its owner's Birdsong.</summary>
    public void CheckDominanceWin(int seat)
    {
        if (Seat(seat).Dominance is not int card) return;
        var suit = Cards.Get(card).Suit;
        bool won;
        if (suit == Suit.Bird)
            won = Map.Clearings.Values.Where(c => c.Corner && c.OppositeCorner is int o && o > c.Id)
                .Any(c => Rules(seat, c.Id) && Rules(seat, c.OppositeCorner!.Value));
        else
            won = ClearingIds.Count(c => SuitOf(c) == suit && Rules(seat, c)) >= 3;
        if (won) EndGame(WithPartners(seat), $"{SeatName(seat)} as the {Faction(seat).Name} wins by {Cards.Get(card).Name}: they {DominanceGoal(card)}");
    }
}
