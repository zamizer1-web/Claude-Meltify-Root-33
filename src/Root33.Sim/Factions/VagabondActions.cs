using Root33.Core;

namespace Root33.Sim;

/// <summary>The Vagabond's frames: setup, Birdsong (refresh, slip), Daylight actions, hits, Evening. Owner: engineer C.</summary>
public sealed partial class Vagabond
{
    public override void RegisterSteps(StepRegistry r)
    {
        r.Add("vagabond.setup", SetupNext, SetupChoose);
        r.Add("vagabond.birdsong", BirdsongNext, BirdsongChoose);
        r.Add("vagabond.daylight", DaylightNext, DaylightChoose);
        r.Add("vagabond.ally-move", AllyMoveNext, AllyMoveChoose);
        r.Add("vagabond.aid", AidNext, AidChoose);
        r.Add("vagabond.quest", QuestNext, QuestChoose);
        r.Add("vagabond.day-labor", DayLaborNext, DayLaborChoose);
        r.Add("vagabond.hideout", HideoutNext, HideoutChoose);
        r.Add("vagabond.glide", GlideNext, GlideChoose);
        r.Add("vagabond.hits", HitsNext, HitsChoose);
        r.Add("vagabond.evening", EveningNext, EveningChoose);
    }

    static string S(int n) => n == 1 ? "" : "s";

    // ============================================================================================ setup (Law 9.3)

    static Decision? SetupNext(SimGame g, Frame f)
    {
        var forests = g.HomeChoices(f.Seat, SimMap.ForestDefs.Keys.OrderBy(x => x)).ToList();
        var where = forests.Count < SimMap.ForestDefs.Count ? "in one of the forests Root 33 allows" : "in any forest";
        var d = new Decision(f.Seat, $"{FactionRegistry.EngineName(g.Seat(f.Seat).Engine)} setup: place your pawn {where}");
        foreach (var x in forests) d.Add($"forest:{x}", $"Pawn in {ForestText(x)}");
        return d;
    }

    static void SetupChoose(SimGame g, Frame f, Option o)
    {
        g.Pop(f);
        int forest = int.Parse(o.Key[7..]);
        var s = g.Seat(f.Seat);
        s.PawnForest = forest;
        s.PawnClearing = null;
        g.Log($"{g.SeatName(f.Seat)} places the {Character(g, f.Seat)}'s pawn in {ForestText(forest)}. Starting items: " +
              $"{string.Join(", ", Data(g, f.Seat).Items.Select(i => ItemName(i.Type)))}. Every relationship starts Indifferent.");
        if (QuestHolder(g) == f.Seat) g.Log($"Face-up quests: {string.Join("; ", QuestsUp(g).Select(VagabondQuests.Label))}.");
    }

    // ============================================================================================ Birdsong (Law 9.4)

    static Decision? BirdsongNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        var data = Data(g, seat);
        switch (f.Step)
        {
            case 0:
            {
                int tea = OnTrack(g, seat, ItemType.Tea);
                f.Set("budget", 3 + 2 * tea).Set("start", 3 + 2 * tea);
                f.Step = 1;
                return null;
            }
            case 1:
            {
                int budget = f.Get("budget");
                var exh = data.Items.Where(i => i.Exhausted && !i.Damaged).ToList();
                if (budget > 0 && exh.Count > budget)
                {
                    var d = new Decision(seat, $"Refresh: flip {budget} more exhausted item{S(budget)} face up (3 plus 2 per tea on its track); choose one");
                    foreach (var k in exh.Select(Key).Distinct().OrderBy(k => k)) d.Add($"refresh:{k}", $"Flip a {ItemText(ByKey(g, seat, k)!)} face up");
                    return d;
                }
                // everything left fits: flip every exhausted undamaged item, then damaged face-down ones with what is left
                foreach (var it in exh.Concat(data.Items.Where(i => i.Exhausted && i.Damaged).ToList()).Take(budget).ToList())
                    Flip(g, f, it);
                var flipped = f.Str("flipped");
                g.Log($"{g.SeatName(seat)} refreshes (up to {f.Get("start")}): " + (flipped == null ? "nothing was exhausted." : $"flips {flipped} face up."));
                f.Step = 2;
                return null;
            }
            case 2:
            {
                var s = g.Seat(seat);
                var d = new Decision(seat, $"Slip: you may move to an adjacent clearing or forest without exhausting boots (you are in {Where(g, seat)})");
                if (s.PawnClearing is int c)
                {
                    foreach (var n in Moves.Neighbours(g, seat, c)) d.Add($"slip:c:{n}", $"Slip to {g.ClearingName(n)}");
                    foreach (var fo in g.Map.ForestsAround(c).OrderBy(x => x)) d.Add($"slip:f:{fo}", $"Slip into {ForestText(fo)}");
                }
                else if (s.PawnForest is int fo)
                {
                    foreach (var n in SimMap.ForestDefs[fo].OrderBy(x => x)) d.Add($"slip:c:{n}", $"Slip to {g.ClearingName(n)}");
                    foreach (var af in AdjacentForests(g, fo)) d.Add($"slip:f:{af}", $"Slip into {ForestText(af)}");
                }
                d.Add("stay", "Do not slip");
                return d;
            }
            default:
                g.Pop(f);
                return null;
        }
    }

    static void Flip(SimGame g, Frame f, VbItem it)
    {
        ChangeItem(g, f.Seat, it, x => x.Exhausted = false);
        f.Set("budget", f.Get("budget") - 1);
        var name = ItemName(it.Type) + (it.Damaged ? " (damaged)" : "");
        f.SetStr("flipped", f.Str("flipped") == null ? name : f.Str("flipped") + ", " + name);
    }

    static void BirdsongChoose(SimGame g, Frame f, Option o)
    {
        var p = o.Key.Split(':');
        if (p[0] == "refresh") { Flip(g, f, ByKey(g, f.Seat, p[1])!); return; }
        f.Step = 3;
        if (p[0] == "stay") return;
        int n = int.Parse(p[2]);
        if (p[1] == "c") MovePawn(g, f.Seat, n, null, "slips");
        else MovePawn(g, f.Seat, null, n, "slips");
    }

    // ============================================================================================ Daylight (Law 9.5)

    public static int MoveCost(SimGame g, int seat, int to) =>
        1 + (HostileWarriorsIn(g, seat, to) && !g.HasAbility(seat, "no-hostile-boot") ? 1 : 0);

    /// <summary>Clearings the pawn may move into with a Move action: adjacent clearings, or from a forest the clearings it touches.</summary>
    public static IEnumerable<int> MoveDestinations(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        if (s.PawnClearing is int c) return Moves.Neighbours(g, seat, c);
        if (s.PawnForest is int fo) return SimMap.ForestDefs[fo].OrderBy(x => x);
        return Enumerable.Empty<int>();
    }

    public static bool CanQuest(SimGame g, int seat, int q)
    {
        var def = VagabondQuests.Get(q);
        return def.A == def.B ? Ready(g, seat, def.A) >= 2 : Ready(g, seat, def.A) >= 1 && Ready(g, seat, def.B) >= 1;
    }

    static bool Enemies(SimGame g, int a, int b) => a != b && g.Seat(a).CoalitionWith != b && g.Seat(b).CoalitionWith != a;

    static string RelNote(SimGame g, int seat, int o) => HasMarker(g, seat, o) ? $" [{RelName(Rel(g, seat, o))}]" : "";

    static string AidNote(SimGame g, int seat, int o)
    {
        if (!HasMarker(g, seat, o)) return "";
        var rel = Rel(g, seat, o);
        if (rel == Relationship.Hostile) return " [Hostile: items only]";
        if (rel == Relationship.Allied) return $" [Allied: +{AlliedAidVp} VP]";
        int step = (int)rel, done = g.State.Turn.Counters.GetValueOrDefault($"vagabond.aid.{seat}.{o}");
        return $" [{RelName(rel)}: {AidCost[step] - done} more Aid this turn reaches {(Relationship)(step + 1)}, +{StepVp[step]} VP]";
    }

    static Decision? DaylightNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        if (f.Str("pending") != null) { f.SetStr("pending", null); g.ActionResolved(seat); }
        if (f.Get("hideout") == 1)
        {
            g.Log($"{g.SeatName(seat)} hides out: Daylight ends at once.");
            g.Pop(f);
            return null;
        }
        var d = new Decision(seat, $"Daylight: choose an action or end Daylight ({ReadySummary(g, seat)}){SwordHint(g, seat)}") { Open = true };
        AddActions(g, seat, d);
        d.Add("end", "End Daylight");
        return d;
    }

    /// <summary>Added to the Daylight prompt when the pawn stands where it could battle someone but has no sword to do it.</summary>
    public const string NoSwordHint = " (battle needs an undamaged sword)";

    static string SwordHint(SimGame g, int seat)
    {
        if (Ready(g, seat, ItemType.Sword) > 0 || g.Seat(seat).PawnClearing is not int c) return "";
        bool target = Enumerable.Range(0, g.SeatCount).Any(o => Enemies(g, seat, o) && g.Faction(o).PresentIn(g, o, c) && g.Bridge.OnBattleLegal(seat, o, c));
        if (!target) return "";
        return Undamaged(g, seat, ItemType.Sword) == 0 ? NoSwordHint : " (battle needs a face-up sword: yours are exhausted)";
    }

    static void AddActions(SimGame g, int seat, Decision d)
    {
        var s = g.Seat(seat);
        var data = Data(g, seat);
        int boots = Ready(g, seat, ItemType.Boots), torches = Ready(g, seat, ItemType.Torch);

        foreach (var to in MoveDestinations(g, seat))
        {
            int cost = MoveCost(g, seat, to);
            if (cost <= boots)
                d.Add($"move:{to}", $"Move to {g.ClearingName(to)} (exhaust {cost} boot{S(cost)}{(cost > 1 ? ": Hostile warriors there" : "")})");
        }
        if (g.HasAbility(seat, "glide") && torches > 0 && !g.State.Turn.UsedOnce.Contains("vagabond.glide"))
            d.Add("glide", "Glide (Root 33): exhaust a torch to move your pawn alone to any clearing (once per turn)");

        if (s.PawnClearing is int h)
        {
            var suit = Cards.SuitName(g.SuitOf(h));
            foreach (var (c, def) in Battles.Targets(g, seat))
            {
                d.Add($"battle:{c}:{def}", $"Battle {g.SeatName(def)} here{RelNote(g, seat, def)} (exhaust a sword; at most {Undamaged(g, seat, ItemType.Sword)} rolled hits)");
                foreach (var a in AlliedSeats(g, seat).Where(a => a != def && g.Warriors(a, c) > 0))
                    d.Add($"battle:{c}:{def}:{a}", $"Battle {g.SeatName(def)} here with Allied {g.SeatName(a)}'s {g.Warriors(a, c)} warrior(s) as your own (exhaust a sword)");
            }
            var cs = g.Clearing(h);
            if (cs.Ruin && cs.RuinItem != null && torches > 0)
                d.Add("explore", $"Explore the ruin in {g.ClearingName(h)} (exhaust a torch): take its hidden item and score 1 VP");
            if (data.Items.Any(i => !i.Damaged && !i.Exhausted) && g.CardsMatching(seat, g.SuitOf(h)).Any())
                foreach (var o in Enumerable.Range(0, g.SeatCount).Where(o => o != seat && g.Faction(o).PresentIn(g, o, h)))
                    d.Add($"aid:{o}", $"Aid {g.SeatName(o)} (exhaust any item, give a {suit} card){AidNote(g, seat, o)}");
            foreach (var q in QuestsUp(g).Where(q => VagabondQuests.Get(q).Suit == g.SuitOf(h) && CanQuest(g, seat, q)))
            {
                int vp = data.Completed.Count(x => VagabondQuests.Get(x).Suit == g.SuitOf(h)) + 1;
                d.Add($"quest:{q}", $"Quest {VagabondQuests.Label(q)}: exhaust them, then score {vp} VP or draw 2 cards");
            }
            if (Ready(g, seat, ItemType.Crossbow) > 0)
                foreach (var o in Enumerable.Range(0, g.SeatCount).Where(o => Enemies(g, seat, o)))
                {
                    if (g.Warriors(o, h) > 0) { d.Add($"strike:{o}:warrior", $"Strike (exhaust a crossbow): remove a {g.SeatName(o)} warrior here{RelNote(g, seat, o)}"); continue; }
                    foreach (var t in cs.Buildings.Where(p => p.Seat == o).Select(p => p.Type).Distinct())
                        d.Add($"strike:{o}:building:{t}", $"Strike (exhaust a crossbow): remove {g.SeatName(o)}'s {g.Faction(o).PieceName(t)} here (+1 VP)");
                    foreach (var t in cs.Tokens.Where(p => p.Seat == o).Select(p => p.Type).Distinct())
                        d.Add($"strike:{o}:token:{t}", $"Strike (exhaust a crossbow): remove {g.SeatName(o)}'s {g.Faction(o).PieceName(t)} token here (+1 VP)");
                }
        }
        if (Ready(g, seat, ItemType.Hammer) > 0)
            foreach (var k in data.Items.Where(i => i.Damaged).Select(Key).Distinct().OrderBy(k => k))
                d.Add($"repair:{k}", $"Repair a {ItemText(ByKey(g, seat, k)!)} (exhaust a hammer)");
        if (s.PawnClearing is int hc && Crafting.Craftable(g, seat).Any())
            d.Add("craft", $"Craft a card with your hammers ({Ready(g, seat, ItemType.Hammer)} face up, all {Cards.SuitName(g.SuitOf(hc))})");
        if (torches > 0)
        {
            switch (Character(g, seat))
            {
                case "Thief":
                    if (s.PawnClearing is int tc)
                        foreach (var o in Enumerable.Range(0, g.SeatCount).Where(o => o != seat && g.Seat(o).Hand.Count > 0 && g.Faction(o).PresentIn(g, o, tc)))
                            d.Add($"steal:{o}", $"Steal (exhaust a torch): take a random card from {g.SeatName(o)} ({g.Seat(o).Hand.Count} in hand)");
                    break;
                case "Tinker":
                    if (s.PawnClearing is int lc && g.State.Discard.Any(c => Cards.Matches(c, g.SuitOf(lc))))
                        d.Add("day-labor", $"Day Labor (exhaust a torch): take a {Cards.SuitName(g.SuitOf(lc))} or bird card from the discard pile");
                    break;
                case "Ranger":
                    d.Add("hideout", $"Hideout (exhaust a torch): repair up to 3 damaged items ({DamagedCount(g, seat)} damaged), then Daylight ends at once");
                    break;
            }
        }
    }

    static void DaylightChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        var p = o.Key.Split(':');
        if (p[0] == "end") { g.Pop(f); return; }
        f.SetStr("pending", p[0]);
        int here = g.Seat(seat).PawnClearing ?? 0;
        switch (p[0])
        {
            case "move":
            {
                int to = int.Parse(p[1]), cost = MoveCost(g, seat, to);
                for (int i = 0; i < cost; i++) Exhaust(g, seat, ItemType.Boots);
                int? from = g.Seat(seat).PawnClearing;
                MovePawn(g, seat, to, null, $"moves (exhausting {cost} boot{S(cost)})");
                if (from is int fr && AlliedSeats(g, seat).Any(a => Moves.Movable(g, a, fr) > 0))
                    g.Push(new Frame("vagabond.ally-move", seat).Set("from", fr).Set("to", to));
                break;
            }
            case "glide":
                g.Push(new Frame("vagabond.glide", seat));
                break;
            case "battle":
            {
                int c = int.Parse(p[1]), def = int.Parse(p[2]);
                Exhaust(g, seat, ItemType.Sword);
                var data = Data(g, seat);
                data.BattleAlly = p.Length > 3 ? int.Parse(p[3]) : null;
                data.AllyHits = 0;
                data.ItemHits = 0;
                g.Log($"{g.SeatName(seat)} exhausts a sword to battle." +
                      (data.BattleAlly is int a ? $" Allied {g.SeatName(a)}'s {g.Warriors(a, c)} warrior(s) fight as the Vagabond's own." : ""));
                Battles.Start(g, seat, def, c);
                break;
            }
            case "explore": Explore(g, seat, here); break;
            case "aid": g.Push(new Frame("vagabond.aid", seat).Set("target", int.Parse(p[1])).Set("c", here)); break;
            case "quest":
            {
                int q = int.Parse(p[1]);
                var def = VagabondQuests.Get(q);
                Exhaust(g, seat, def.A);
                Exhaust(g, seat, def.B);
                QuestsUp(g).Remove(q);
                Data(g, seat).Completed.Add(q);
                g.Log($"{g.SeatName(seat)} completes the quest {VagabondQuests.Label(q)} in {g.ClearingName(here)}, exhausting a {ItemName(def.A)} and a {ItemName(def.B)}.");
                g.Push(new Frame("vagabond.quest", seat).Set("q", q));
                break;
            }
            case "strike":
            {
                int t = int.Parse(p[1]);
                Exhaust(g, seat, ItemType.Crossbow);
                g.Log($"{g.SeatName(seat)} exhausts a crossbow to Strike {g.SeatName(t)} in {g.ClearingName(here)}.");
                if (p[2] == "warrior") g.RemoveWarriors(t, here, 1, seat, "Strike");
                else if (p[2] == "building") g.RemoveBuilding(t, here, p[3], seat, "Strike");
                else g.RemoveToken(t, here, p[3], seat, "Strike");
                break;
            }
            case "repair":
            {
                Exhaust(g, seat, ItemType.Hammer);
                var it = ByKey(g, seat, p[1])!;
                Repair(g, seat, it);
                g.Log($"{g.SeatName(seat)} exhausts a hammer to repair a {ItemName(it.Type)} ({(it.Exhausted ? "it stays face down" : "face up")}).");
                break;
            }
            case "craft": g.Push(Crafting.Frame(seat, once: true)); break;
            case "steal":
            {
                int t = int.Parse(p[1]);
                Exhaust(g, seat, ItemType.Torch);
                var hand = g.Seat(t).Hand;
                int card = hand[g.Random(hand.Count)];
                hand.Remove(card);
                g.Seat(seat).Hand.Add(card);
                g.Log($"{g.SeatName(seat)} exhausts a torch to Steal a random card from {g.SeatName(t)}.");
                g.Log($"You stole {Cards.Label(card)}.", seat);
                g.Log($"{Cards.Label(card)} was stolen from you.", t);
                break;
            }
            case "day-labor": g.Push(new Frame("vagabond.day-labor", seat).Set("c", here)); break;
            case "hideout":
                Exhaust(g, seat, ItemType.Torch);
                g.Log($"{g.SeatName(seat)} exhausts a torch for Hideout.");
                f.Set("hideout", 1);
                g.Push(new Frame("vagabond.hideout", seat).Set("left", 3));
                break;
            default: throw new InvalidOperationException("unknown Vagabond action " + o.Key);
        }
    }

    /// <summary>Explore (Law 9.5.3): exhaust a torch, take the ruin's item face up, score 1 VP; the emptied ruin is removed.</summary>
    static void Explore(SimGame g, int seat, int c)
    {
        var cs = g.Clearing(c);
        var item = cs.RuinItem!.Value;
        Exhaust(g, seat, ItemType.Torch);
        cs.RuinItem = null;
        cs.Ruin = false;
        g.Log($"{g.SeatName(seat)} exhausts a torch to explore the ruin in {g.ClearingName(c)}; the emptied ruin is removed.");
        GainItem(g, seat, item, "finds in the ruin");
        g.AddVp(seat, 1, "explored a ruin");
        g.Bridge.OnExploreResolved(seat, c);
    }

    // ---------------------------------------------------------------------------------------------- moving Allied warriors

    static Decision? AllyMoveNext(SimGame g, Frame f)
    {
        int from = f.Get("from"), to = f.Get("to");
        var d = new Decision(f.Seat, $"Your pawn moved to {g.ClearingName(to)}: bring warriors of one Allied faction from {g.ClearingName(from)} with you?");
        foreach (var a in AlliedSeats(g, f.Seat))
            for (int n = Moves.Movable(g, a, from); n >= 1; n--)
                d.Add($"ally:{a}:{n}", $"Bring {n} of Allied {g.SeatName(a)}'s warriors");
        d.Add("alone", "Move alone");
        return d;
    }

    static void AllyMoveChoose(SimGame g, Frame f, Option o)
    {
        g.Pop(f);
        if (o.Key == "alone") return;
        var p = o.Key.Split(':');
        g.MoveWarriors(int.Parse(p[1]), f.Get("from"), f.Get("to"), int.Parse(p[2]));
    }

    // ---------------------------------------------------------------------------------------------- Glide (Root 33 ability)

    static Decision? GlideNext(SimGame g, Frame f)
    {
        var d = new Decision(f.Seat, "Glide: exhaust a torch and move your pawn alone to which clearing?");
        foreach (var c in g.ClearingIds.Where(c => c != g.Seat(f.Seat).PawnClearing)) d.Add($"to:{c}", g.ClearingName(c));
        return d;
    }

    static void GlideChoose(SimGame g, Frame f, Option o)
    {
        g.Pop(f);
        Exhaust(g, f.Seat, ItemType.Torch);
        g.State.Turn.UsedOnce.Add("vagabond.glide");
        MovePawn(g, f.Seat, int.Parse(o.Key[3..]), null, "glides (exhausting a torch)");
    }

    // ---------------------------------------------------------------------------------------------- Aid (Law 9.5.4)

    static Decision? AidNext(SimGame g, Frame f)
    {
        int seat = f.Seat, target = f.Get("target"), c = f.Get("c");
        switch (f.Step)
        {
            case 0:
            {
                var d = new Decision(seat, $"Aid {g.SeatName(target)}: choose the {Cards.SuitName(g.SuitOf(c))} card to give (birds are wild)");
                foreach (var x in g.CardsMatching(seat, g.SuitOf(c))) d.Add($"give:{x}", $"Give {Cards.Label(x)}");
                return d;
            }
            case 1:
            {
                var d = new Decision(seat, $"Aid {g.SeatName(target)}: choose the item to exhaust");
                foreach (var k in Data(g, seat).Items.Where(i => !i.Damaged && !i.Exhausted).Select(Key).Distinct().OrderBy(k => k))
                    d.Add($"exhaust:{k}", $"Exhaust a {ItemText(ByKey(g, seat, k)!)}");
                return d;
            }
            case 2:
            {
                var items = IsVagabond(g, target) ? new List<ItemType>() : g.Seat(target).CraftedItems.Distinct().OrderBy(x => x).ToList();
                if (items.Count == 0) { f.Step = 3; return null; }
                var d = new Decision(seat, $"Aid: you may take one item from {g.SeatName(target)}'s Crafted Items");
                foreach (var t in items) d.Add($"take:{ItemName(t)}", $"Take their {ItemName(t)}");
                d.Add("none", "Take nothing");
                return d;
            }
            default:
                g.Pop(f);
                AidRelationship(g, seat, target);
                g.Bridge.OnAidResolved(seat, target, c);
                return null;
        }
    }

    static void AidChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat, target = f.Get("target");
        var p = o.Key.Split(':');
        switch (p[0])
        {
            case "give":
                f.Set("card", int.Parse(p[1]));
                f.Step = 1;
                return;
            case "exhaust":
            {
                var it = ByKey(g, seat, p[1])!;
                ChangeItem(g, seat, it, x => x.Exhausted = true);
                int card = f.Get("card");
                g.Seat(seat).Hand.Remove(card);
                g.Seat(target).Hand.Add(card);
                g.Log($"{g.SeatName(seat)} exhausts a {ItemName(it.Type)} to Aid {g.SeatName(target)}, giving them {Cards.Label(card)}.");
                f.Step = 2;
                return;
            }
            case "take":
            {
                var t = Enum.Parse<ItemType>(p[1], ignoreCase: true);
                g.Seat(target).CraftedItems.Remove(t);
                GainItem(g, seat, t, $"takes from {g.SeatName(target)}'s Crafted Items");
                f.Step = 3;
                return;
            }
            default:
                f.Step = 3;
                return;
        }
    }

    /// <summary>Improving relationships (Law 9.2.9.I-II): Aids this turn toward the next space, or 2 VP per Aid once Allied.</summary>
    static void AidRelationship(SimGame g, int seat, int target)
    {
        if (!HasMarker(g, seat, target)) return;
        var rel = Rel(g, seat, target);
        if (rel == Relationship.Hostile) { g.Log($"({g.SeatName(target)} is Hostile: Aid does not improve the relationship.)"); return; }
        if (rel == Relationship.Allied) { g.AddVp(seat, AlliedAidVp, $"aided Allied {g.SeatName(target)}"); return; }
        string key = $"vagabond.aid.{seat}.{target}";
        int step = (int)rel, need = AidCost[step];
        int n = g.State.Turn.Counters.GetValueOrDefault(key) + 1;
        var next = (Relationship)(step + 1);
        if (n < need)
        {
            g.State.Turn.Counters[key] = n;
            g.Log($"(Aid {n} of {need} this turn toward {next} with {g.SeatName(target)}.)");
            return;
        }
        g.State.Turn.Counters[key] = 0;
        SetRel(g, seat, target, next, $"aided {need} time{S(need)} this turn");
        g.AddVp(seat, StepVp[step], $"relationship with {g.SeatName(target)} improved to {next}");
    }

    // ---------------------------------------------------------------------------------------------- Quest (Law 9.5.5)

    static Decision? QuestNext(SimGame g, Frame f)
    {
        var def = VagabondQuests.Get(f.Get("q"));
        int vp = Data(g, f.Seat).Completed.Count(x => VagabondQuests.Get(x).Suit == def.Suit);
        var d = new Decision(f.Seat, $"Quest {def.Name} completed: choose your reward");
        d.Add("reward:vp", $"Score {vp} VP (one per {Cards.SuitName(def.Suit)} quest you completed, this one included)");
        d.Add("reward:draw", "Draw two cards");
        return d;
    }

    static void QuestChoose(SimGame g, Frame f, Option o)
    {
        g.Pop(f);
        int seat = f.Seat;
        var def = VagabondQuests.Get(f.Get("q"));
        if (o.Key == "reward:vp")
            g.AddVp(seat, Data(g, seat).Completed.Count(x => VagabondQuests.Get(x).Suit == def.Suit), $"quest {def.Name}");
        else g.Draw(seat, 2);
        var deck = QuestDeck(g);
        if (deck.Count == 0) { g.Log("The quest deck is empty: no new quest."); return; }
        int nq = deck[^1];
        deck.RemoveAt(deck.Count - 1);
        QuestsUp(g).Add(nq);
        g.Log($"New face-up quest: {VagabondQuests.Label(nq)}.");
    }

    // ---------------------------------------------------------------------------------------------- Tinker's Day Labor

    static Decision? DayLaborNext(SimGame g, Frame f)
    {
        var suit = g.SuitOf(f.Get("c"));
        var cards = g.State.Discard.Where(c => Cards.Matches(c, suit)).Distinct().ToList();
        if (cards.Count == 0) { g.Pop(f); return null; }
        var d = new Decision(f.Seat, $"Day Labor: exhaust a torch and take which {Cards.SuitName(suit)} or bird card from the discard pile?");
        foreach (var c in cards) d.Add($"take:{c}", $"Take {Cards.Describe(c)}");
        return d;
    }

    static void DayLaborChoose(SimGame g, Frame f, Option o)
    {
        g.Pop(f);
        int card = int.Parse(o.Key[5..]);
        Exhaust(g, f.Seat, ItemType.Torch);
        g.State.Discard.Remove(card);
        g.Seat(f.Seat).Hand.Add(card);
        g.Log($"{g.SeatName(f.Seat)} exhausts a torch for Day Labor and takes {Cards.Label(card)} from the discard pile.");
    }

    // ---------------------------------------------------------------------------------------------- Ranger's Hideout

    static Decision? HideoutNext(SimGame g, Frame f)
    {
        int left = f.Get("left");
        var dmg = Data(g, f.Seat).Items.Where(i => i.Damaged).ToList();
        if (left <= 0 || dmg.Count == 0) { g.Pop(f); return null; }
        if (dmg.Count <= left)
        {
            foreach (var it in dmg) Repair(g, f.Seat, it);
            g.Log($"{g.SeatName(f.Seat)} repairs {string.Join(", ", dmg.Select(i => ItemName(i.Type)))} (Hideout).");
            g.Pop(f);
            return null;
        }
        var d = new Decision(f.Seat, $"Hideout: repair {left} more damaged item{S(left)}; choose one");
        foreach (var k in dmg.Select(Key).Distinct().OrderBy(k => k)) d.Add($"repair:{k}", $"Repair a {ItemText(ByKey(g, f.Seat, k)!)}");
        return d;
    }

    static void HideoutChoose(SimGame g, Frame f, Option o)
    {
        var it = ByKey(g, f.Seat, o.Key[7..])!;
        Repair(g, f.Seat, it);
        f.Set("left", f.Get("left") - 1);
        g.Log($"{g.SeatName(f.Seat)} repairs a {ItemName(it.Type)} (Hideout).");
    }

    // ============================================================================================ taking hits (Law 9.2.7)

    /// <summary>Default order a non-active Vagabond damages items in: face-down first, then crossbow ... sword last.</summary>
    public static int DamagePriority(VbItem i) => (i.Exhausted ? 0 : 10) + i.Type switch
    {
        ItemType.Crossbow => 0, ItemType.Hammer => 1, ItemType.Coins => 2, ItemType.Tea => 3, ItemType.Torch => 4,
        ItemType.Bag => 5, ItemType.Boots => 6, _ => 7,
    };

    static int? AllyInBattle(SimGame g, Frame f)
    {
        int c = f.Get("c");
        return f.Get("full") == 0 && g.State.Battle is BattleState b && b.Attacker == f.Seat && b.Clearing == c
               && Data(g, f.Seat).BattleAlly is int a && g.Warriors(a, c) > 0 ? a : null;
    }

    static Decision? HitsNext(SimGame g, Frame f)
    {
        int seat = f.Seat, c = f.Get("c"), n = f.Get("n");
        NoteBattle(g, seat);
        if (n <= 0) { g.Pop(f); return null; }
        var keys = Data(g, seat).Items.Where(i => !i.Damaged).OrderBy(DamagePriority).Select(Key).Distinct().ToList();
        var ally = AllyInBattle(g, f);
        if (keys.Count == 0 && ally == null)
        {
            g.Log($"{g.SeatName(seat)} has no undamaged item left: {n} hit{S(n)} ignored.");
            g.Pop(f);
            return null;
        }
        var d = new Decision(seat, $"You take {n} hit{S(n)} in {g.ClearingName(c)}: damage an undamaged item for each" + (ally != null ? " (or remove an Allied warrior)" : ""));
        foreach (var k in keys) d.Add($"damage:{k}", $"Damage a {ItemText(ByKey(g, seat, k)!)}");
        if (ally is int a) d.Add($"ally:{a}", $"Remove one of Allied {g.SeatName(a)}'s warriors ({g.Warriors(a, c)} here)");
        d.DefaultKey = d.Options[0].Key;
        return d;
    }

    static void HitsChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat, c = f.Get("c");
        int? remover = f.Get("remover", -1) < 0 ? null : f.Get("remover");
        f.Set("n", f.Get("n") - 1);
        var p = o.Key.Split(':');
        var data = Data(g, seat);
        if (p[0] == "ally")
        {
            data.AllyHits++;
            g.RemoveWarriors(int.Parse(p[1]), c, 1, remover, "a hit taken for the Allied Vagabond");
            return;
        }
        var it = ByKey(g, seat, p[1])!;
        ChangeItem(g, seat, it, x => x.Damaged = true);
        data.ItemHits++;
        g.Log($"{g.SeatName(seat)} damages a {ItemName(it.Type)}.");
        NoteBattle(g, seat);
    }

    // ============================================================================================ Evening (Law 9.6)

    /// <summary>Keys of items in the Satchel or Damaged box (not on a track): what the item limit removes.</summary>
    static IEnumerable<string> RemovableKeys(SimGame g, int seat) =>
        Data(g, seat).Items.Where(i => !(TrackTypes.Contains(i.Type) && !i.Damaged && !i.Exhausted && Ready(g, seat, i.Type) <= 3))
            .Select(Key).Distinct().OrderBy(k => k);

    static Decision? EveningNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        var data = Data(g, seat);
        switch (f.Step)
        {
            case 0:
            {
                if (g.Seat(seat).PawnForest != null)
                {
                    var dmg = data.Items.Where(i => i.Damaged).ToList();
                    foreach (var it in dmg) ChangeItem(g, seat, it, x => { x.Damaged = false; x.Exhausted = false; });
                    g.Log(dmg.Count > 0
                        ? $"{g.SeatName(seat)} rests in the forest: repairs {string.Join(", ", dmg.Select(i => ItemName(i.Type)))} and flips them face up."
                        : $"{g.SeatName(seat)} rests in the forest (nothing to repair).");
                }
                int coins = OnTrack(g, seat, ItemType.Coins);
                if (coins > 0) g.Log($"{g.SeatName(seat)} draws 1 card plus {coins} for the coins on its track.");
                g.Draw(seat, 1 + coins);
                f.Step = 1;
                g.Push(new Frame("discard", seat).Set("limit", SimGame.HandLimit));
                return null;
            }
            case 1:
            {
                int counted = CountedItems(g, seat), limit = ItemLimit(g, seat);
                if (counted <= limit) { g.Pop(f); return null; }
                var d = new Decision(seat, $"Item limit: {counted} items in your Satchel and Damaged box, limit {limit} (6 plus 2 per bag on its track); choose an item to remove from the game");
                foreach (var k in RemovableKeys(g, seat)) d.Add($"drop:{k}", $"Remove a {ItemText(ByKey(g, seat, k)!)} for good");
                return d;
            }
            default:
                g.Pop(f);
                return null;
        }
    }

    static void EveningChoose(SimGame g, Frame f, Option o) =>
        RemoveItem(g, f.Seat, ByKey(g, f.Seat, o.Key[5..])!, "over the item limit");
}
