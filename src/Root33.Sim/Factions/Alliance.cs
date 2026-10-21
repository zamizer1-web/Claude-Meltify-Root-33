using Root33.Core;

namespace Root33.Sim;

/// <summary>The Alliance's saved state: the face-down Supporters stack (card ids) and the officers.</summary>
public sealed class AllianceData
{
    public List<int> Supporters { get; set; } = new();
    public int Officers { get; set; }
}

/// <summary>
/// The Woodland Alliance (Law 8). Setup: 3 supporters. Birdsong: revolt, then spread sympathy, any number of times.
/// Daylight: craft (sympathy), mobilize, train, in any order and number. Evening: military operations (move, battle,
/// recruit, organize) up to the officers, then draw 1 + bases on the map and discard to 5. Guerrilla War, Outrage,
/// the 5-card supporter limit without bases, and base removal (supporters of its suit lost, half the officers).
/// Owner: engineer B.
/// </summary>
public sealed class Alliance : FactionBase
{
    public override string Id => "alliance";
    public override string Name => "Woodland Alliance";
    public override char SetupOrder => 'D';
    public override bool DefenderTakesHigherRoll => true;

    /// <summary>Supporters needed to place the next sympathy token, by tokens already on the map.</summary>
    public static readonly int[] SpreadCost = { 1, 1, 1, 2, 2, 2, 3, 3, 3, 3 };
    /// <summary>VP scored for placing the next sympathy token, by tokens already on the map.</summary>
    public static readonly int[] SpreadVp = { 0, 1, 1, 1, 2, 2, 3, 4, 4, 4 };
    public static readonly Suit[] BaseSuits = { Suit.Fox, Suit.Rabbit, Suit.Mouse };
    public const int SupporterLimit = 5;

    public override void InitSeat(SimGame g, int seat)
    {
        var s = g.Seat(seat);
        s.SupplyWarriors = 10;
        s.Supply[PieceTypes.Sympathy] = 10;
        foreach (var b in BaseSuits) s.Supply[BaseType(b)] = 1;
        g.Data<AllianceData>(seat);
    }

    // ---------------------------------------------------------------------------------------------- helpers

    public static AllianceData D(SimGame g, int seat) => g.Data<AllianceData>(seat);
    public static string BaseType(Suit s) => PieceTypes.Base + "." + Cards.SuitName(s);
    public static Suit BaseSuit(string type) => Enum.Parse<Suit>(type[(type.IndexOf('.') + 1)..], ignoreCase: true);
    public static bool Sympathetic(SimGame g, int seat, int c) => g.TokensOf(seat, c, PieceTypes.Sympathy) > 0;
    public static int SympathyOnMap(SimGame g, int seat) => g.ClearingIds.Count(c => Sympathetic(g, seat, c));
    public static List<int> BaseClearings(SimGame g, int seat) => g.ClearingIds.Where(c => g.BuildingsOf(seat, c, PieceTypes.Base) > 0).ToList();
    public static bool HasBases(SimGame g, int seat) => BaseClearings(g, seat).Count > 0;
    public static int MatchingSupporters(SimGame g, int seat, Suit s) => D(g, seat).Supporters.Count(c => Cards.Matches(c, s));
    /// <summary>Martial Law: another player has at least three warriors in c.</summary>
    public static bool MartialLaw(SimGame g, int seat, int c) => g.Clearing(c).Warriors.Any(kv => kv.Key != seat && kv.Value >= 3);
    public static int SpreadCostAt(SimGame g, int seat, int c) =>
        SpreadCost[Math.Min(SympathyOnMap(g, seat), SpreadCost.Length - 1)] + (MartialLaw(g, seat, c) ? 1 : 0);
    static string SuitList(IEnumerable<int> cards)
    {
        var l = cards.GroupBy(c => Cards.Get(c).Suit).OrderBy(x => x.Key).Select(x => $"{Cards.SuitName(x.Key)} x{x.Count()}").ToList();
        return l.Count == 0 ? "none" : string.Join(", ", l);
    }
    /// <summary>The seat's supporters by suit (only for the Alliance's own option texts and rules page).</summary>
    public static string SupportersText(SimGame g, int seat) => SuitList(D(g, seat).Supporters);

    /// <summary>Add a card to the Supporters stack; without bases the stack holds 5 and a sixth card is discarded.</summary>
    public static void AddSupporter(SimGame g, int seat, int card, string why)
    {
        var d = D(g, seat);
        if (!HasBases(g, seat) && d.Supporters.Count >= SupporterLimit)
        {
            g.ToDiscard(card);
            g.Log($"{g.SeatName(seat)}'s Supporters stack is full (5, no base on the map): {Cards.Label(card)} is discarded ({why}).");
            return;
        }
        d.Supporters.Add(card);
        g.Log($"{g.SeatName(seat)} gains a supporter ({why}); {d.Supporters.Count} supporter(s).");
        g.Log($"Supporter gained: {Cards.Label(card)}.", seat);
    }

    /// <summary>Draw the top card of the deck onto the Supporters stack (reshuffling the discard pile if needed).</summary>
    public static void DrawSupporter(SimGame g, int seat, string why)
    {
        var st = g.State;
        if (st.Deck.Count == 0)
        {
            if (st.Discard.Count == 0) { g.Log("The deck and the discard pile are empty: no supporter to draw."); return; }
            st.Deck.AddRange(st.Discard);
            st.Discard.Clear();
            g.Shuffle(st.Deck);
            g.Log("The deck ran out: the discard pile is shuffled into a new deck.");
        }
        int c = st.Deck[^1];
        st.Deck.RemoveAt(st.Deck.Count - 1);
        AddSupporter(g, seat, c, why);
    }

    /// <summary>Spend n supporters matching a suit (exact suit first, birds last). False when short.</summary>
    public static bool SpendSupporters(SimGame g, int seat, Suit s, int n, string why)
    {
        var d = D(g, seat);
        var pay = d.Supporters.Where(c => Cards.Get(c).Suit == s).Concat(d.Supporters.Where(c => Cards.Get(c).Suit == Suit.Bird && s != Suit.Bird)).Take(n).ToList();
        if (pay.Count < n) return false;
        foreach (var c in pay) { d.Supporters.Remove(c); g.ToDiscard(c); }
        g.Log($"{g.SeatName(seat)} spends {n} {Cards.SuitName(s)} supporter{(n == 1 ? "" : "s")} to {why}: {string.Join(", ", pay.Select(Cards.Label))}.");
        return true;
    }

    // ---------------------------------------------------------------------------------------------- shared-rule hooks

    public override IEnumerable<CraftPiece> CraftingPieces(SimGame g, int seat)
    {
        foreach (var c in g.ClearingIds)
            if (Sympathetic(g, seat, c)) yield return new CraftPiece($"sympathy:{c}", g.SuitOf(c));
    }

    public override int LossPriority(string type) => type == PieceTypes.Sympathy ? 0 : 1;

    public override string PieceName(string type) =>
        type.StartsWith(PieceTypes.Base + ".") ? $"{type[(type.IndexOf('.') + 1)..]} base" : type;

    /// <summary>Outrage (Law 8.2.6): another player moved warriors into a sympathetic clearing.</summary>
    public override void OnPiecesMoved(SimGame g, int self, int owner, int from, int to, int count)
    {
        if (owner == self || count <= 0 || g.Faction(owner).Id == "vagabond") return;   // the Vagabond pawn is not a warrior
        if (!Sympathetic(g, self, to)) return;
        g.Push(new Frame("alliance.outrage", owner).Set("alliance", self).Set("c", to));
    }

    public override void OnPieceRemoved(SimGame g, int self, RemovedRecord r)
    {
        if (r.Owner != self || r.Type == null) return;
        if (r.Type == PieceTypes.Sympathy && r.Remover is int rem && rem != self)
        {
            g.Push(new Frame("alliance.outrage", rem).Set("alliance", self).Set("c", r.Clearing));
            return;
        }
        if (r.Kind == PieceKind.Building && SimGame.TypeMatches(r.Type, PieceTypes.Base)) BaseRemoved(g, self, BaseSuit(r.Type));
    }

    /// <summary>Law 8.2.4: discard the supporters of the base's suit (birds too), lose half the officers rounded up, and
    /// trim the stack to 5 when no base is left.</summary>
    static void BaseRemoved(SimGame g, int seat, Suit suit)
    {
        var d = D(g, seat);
        var lost = d.Supporters.Where(c => Cards.Matches(c, suit)).ToList();
        foreach (var c in lost) { d.Supporters.Remove(c); g.ToDiscard(c); }
        g.Log($"{g.SeatName(seat)} loses the {Cards.SuitName(suit)} base: {lost.Count} matching supporter(s) discarded{(lost.Count > 0 ? ": " + string.Join(", ", lost.Select(Cards.Label)) : "")}.");
        int off = (d.Officers + 1) / 2;
        if (off > 0)
        {
            d.Officers -= off;
            g.Seat(seat).SupplyWarriors += off;
            g.Log($"{g.SeatName(seat)} loses {off} officer(s) (half, rounded up): {d.Officers} left.");
        }
        if (!HasBases(g, seat) && d.Supporters.Count > SupporterLimit) g.Push(new Frame("alliance.trim", seat));
    }

    public override void Setup(SimGame g, int seat)
    {
        g.Log($"{g.SeatName(seat)} draws 3 cards face down onto the Supporters stack.");
        for (int i = 0; i < 3; i++) DrawSupporter(g, seat, "setup");
    }

    public override void BeginPhase(SimGame g, int seat, TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.Birdsong: g.Push(new Frame("alliance.birdsong", seat)); break;
            case TurnPhase.Daylight: g.Push(new Frame("alliance.daylight", seat)); break;
            case TurnPhase.Evening: g.Push(new Frame("alliance.evening", seat)); break;
        }
    }

    // ---------------------------------------------------------------------------------------------- legal actions

    /// <summary>Revolt targets: sympathetic clearings without a base, whose base is still on the board, with 2 matching supporters.</summary>
    public static List<int> RevoltTargets(SimGame g, int seat) => g.ClearingIds.Where(c =>
    {
        var suit = g.SuitOf(c);
        var m = g.Map.Clearings[c];
        return Sympathetic(g, seat, c) && g.BuildingsOf(seat, c, PieceTypes.Base) == 0 && g.Supply(seat, BaseType(suit)) > 0
               && MatchingSupporters(g, seat, suit) >= 2
               && m.Slots - (g.Clearing(c).Ruin ? 1 : 0) - g.BuildingsOf(seat, c) > 0;     // a slot once enemy buildings are gone
    }).ToList();

    /// <summary>Spread targets: unsympathetic clearings next to a sympathetic one (any clearing if none), affordable now.</summary>
    public static List<int> SpreadTargets(SimGame g, int seat)
    {
        if (g.Supply(seat, PieceTypes.Sympathy) == 0) return new List<int>();
        bool any = SympathyOnMap(g, seat) > 0;
        return g.ClearingIds.Where(c => !Sympathetic(g, seat, c) && g.CanPlace(seat, c)
                                         && (!any || g.Map.Clearings[c].Paths.Any(x => Sympathetic(g, seat, x)))
                                         && MatchingSupporters(g, seat, g.SuitOf(c)) >= SpreadCostAt(g, seat, c)).ToList();
    }

    static void Revolt(SimGame g, int seat, int c)
    {
        var suit = g.SuitOf(c);
        SpendSupporters(g, seat, suit, 2, $"revolt in {g.ClearingName(c)}");
        g.Log($"{g.SeatName(seat)} REVOLTS in {g.ClearingName(c)}: every enemy piece there is removed.");
        for (int o = 0; o < g.SeatCount; o++)
        {
            if (o == seat) continue;
            g.RemoveWarriors(o, c, g.Warriors(o, c), seat, "revolt");
            foreach (var p in g.Clearing(c).Buildings.Where(p => p.Seat == o).ToList()) g.RemoveBuilding(o, c, p.Type, seat, "revolt");
            foreach (var p in g.Clearing(c).Tokens.Where(p => p.Seat == o).ToList()) g.RemoveToken(o, c, p.Type, seat, "revolt");
            if (g.Seat(o).PawnClearing == c && g.Faction(o).TakeHitsKind != null)
            {
                // Full Removal (Law 9.2.2.I): the Vagabond damages three items.
                g.Log($"{g.SeatName(o)}'s pawn cannot be removed: the Vagabond damages three items.");
                g.Push(Battles.HitsFrame(g, o, c, 3, seat));
            }
        }
        g.PlaceBuilding(seat, c, BaseType(suit));
        int n = g.ClearingIds.Count(x => g.SuitOf(x) == suit && Sympathetic(g, seat, x));
        int placed = g.PlaceWarriors(seat, c, n);
        if (placed < n) g.Log($"{g.SeatName(seat)} had only {placed} of {n} warriors to place.");
        Train(g, seat, "revolt");
    }

    static void Train(SimGame g, int seat, string why)
    {
        var s = g.Seat(seat);
        if (s.SupplyWarriors == 0) { g.Log($"{g.SeatName(seat)} has no warrior left in supply to become an officer."); return; }
        s.SupplyWarriors--;
        D(g, seat).Officers++;
        g.Log($"{g.SeatName(seat)} places a warrior in the Officers box ({why}): {D(g, seat).Officers} officer(s).");
    }

    static void Spread(SimGame g, int seat, int c)
    {
        int cost = SpreadCostAt(g, seat, c), k = SympathyOnMap(g, seat);
        SpendSupporters(g, seat, g.SuitOf(c), cost, $"spread sympathy to {g.ClearingName(c)}{(MartialLaw(g, seat, c) ? " (martial law: +1)" : "")}");
        g.PlaceToken(seat, c, PieceTypes.Sympathy);
        int vp = SpreadVp[Math.Min(k, SpreadVp.Length - 1)];
        if (vp > 0) g.AddVp(seat, vp, "sympathy track");
    }

    // ---------------------------------------------------------------------------------------------- steps

    public override void RegisterSteps(StepRegistry r)
    {
        r.Add("alliance.birdsong", BirdsongNext, (g, f, o) =>
        {
            if (o.Key == "done") { f.Step = 1; return; }
            if (o.Key == "end") { g.Pop(f); return; }
            int c = int.Parse(o.Key[(o.Key.IndexOf(':') + 1)..]);
            if (o.Key.StartsWith("revolt:")) Revolt(g, f.Seat, c); else Spread(g, f.Seat, c);
            g.ActionResolved(f.Seat);
        });
        r.Add("alliance.daylight", DaylightNext, DaylightChoose);
        r.Add("alliance.evening", EveningNext, EveningChoose);
        r.Add("alliance.outrage", OutrageNext, (g, f, o) =>
        {
            g.Pop(f);
            int ally = f.Get("alliance"), card = int.Parse(o.Key[5..]);
            g.Seat(f.Seat).Hand.Remove(card);
            g.Log($"Outrage: {g.SeatName(f.Seat)} gives a card to {g.SeatName(ally)}'s Supporters stack.");
            g.Log($"You gave {Cards.Label(card)} to the Alliance (Outrage).", f.Seat);
            AddSupporter(g, ally, card, "Outrage");
        });
        r.Add("alliance.trim", (g, f) =>
        {
            var d = D(g, f.Seat);
            if (HasBases(g, f.Seat) || d.Supporters.Count <= SupporterLimit) { g.Pop(f); return null; }
            var dec = new Decision(f.Seat, $"No base is left: discard supporters down to 5 ({d.Supporters.Count} now)");
            foreach (var c in d.Supporters.OrderBy(c => Cards.Get(c).Suit == Suit.Bird ? 1 : 0)) dec.Add($"discard:{c}", $"Discard supporter {Cards.Label(c)}");
            dec.DefaultKey = dec.Options[0].Key;
            return dec;
        }, (g, f, o) =>
        {
            int c = int.Parse(o.Key[8..]);
            D(g, f.Seat).Supporters.Remove(c);
            g.ToDiscard(c);
            g.Log($"{g.SeatName(f.Seat)} discards a supporter: {Cards.Label(c)}.");
        });
    }

    // Birdsong: step 0 revolts, step 1 spread sympathy

    static Decision? BirdsongNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        string sup = $"(your supporters: {SupportersText(g, seat)})";
        if (f.Step == 0)
        {
            var targets = RevoltTargets(g, seat);
            if (targets.Count == 0) { f.Step = 1; return null; }
            var d = new Decision(seat, "Birdsong, Revolt: revolt in a sympathetic clearing (any number of times), or go on to Spread Sympathy");
            foreach (var c in targets)
            {
                var suit = g.SuitOf(c);
                int n = g.ClearingIds.Count(x => g.SuitOf(x) == suit && Sympathetic(g, seat, x));
                d.Add($"revolt:{c}", $"Revolt in {g.ClearingName(c)}: spend 2 {Cards.SuitName(suit)} supporters, remove every enemy piece there, place the {Cards.SuitName(suit)} base, {n} warrior(s) and an officer {sup}");
            }
            d.Add("done", "Stop revolting: go on to Spread Sympathy");
            return d;
        }
        var spread = SpreadTargets(g, seat);
        if (spread.Count == 0) { g.Pop(f); return null; }
        int k = SympathyOnMap(g, seat);
        var ds = new Decision(seat, "Birdsong, Spread Sympathy: place a sympathy token (any number of times), or end Birdsong");
        foreach (var c in spread)
            ds.Add($"spread:{c}", $"Spread sympathy to {g.ClearingName(c)}: spend {SpreadCostAt(g, seat, c)} {Cards.SuitName(g.SuitOf(c))} supporter(s){(MartialLaw(g, seat, c) ? " (incl. +1 martial law)" : "")}, score {SpreadVp[Math.Min(k, SpreadVp.Length - 1)]} VP {sup}");
        ds.Add("end", "End Birdsong");
        return ds;
    }

    // Daylight: craft, mobilize, train, any order and number

    public static IEnumerable<int> TrainCards(SimGame g, int seat)
    {
        if (g.Seat(seat).SupplyWarriors == 0) return Enumerable.Empty<int>();
        var suits = BaseClearings(g, seat).Select(g.SuitOf).Distinct().ToList();
        return g.Seat(seat).Hand.Where(c => suits.Any(s => Cards.Matches(c, s)));
    }

    static Decision? DaylightNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        var d = new Decision(seat, "Daylight: craft, mobilize or train, in any order and number") { Open = true };
        foreach (var c in Crafting.Craftable(g, seat)) d.Add($"craft:{c}", $"Craft {Cards.DescribeToCraft(c)}");
        bool full = !HasBases(g, seat) && D(g, seat).Supporters.Count >= SupporterLimit;
        foreach (var c in g.Seat(seat).Hand)
            d.Add($"mobilize:{c}", $"Mobilize: add {Cards.Label(c)} to the Supporters stack{(full ? " (the stack is full: it would be discarded)" : "")}");
        foreach (var c in TrainCards(g, seat)) d.Add($"train:{c}", $"Train: spend {Cards.Label(c)} to make a warrior an officer");
        d.Add("end", $"End Daylight (supporters: {SupportersText(g, seat)}; officers: {D(g, seat).Officers})");
        return d;
    }

    static void DaylightChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        if (o.Key == "end") { g.Pop(f); return; }
        var p = o.Key.Split(':');
        int card = int.Parse(p[1]);
        switch (p[0])
        {
            case "craft": Crafting.Craft(g, seat, card); break;
            case "mobilize":
                g.Seat(seat).Hand.Remove(card);
                g.Log($"{g.SeatName(seat)} mobilizes: a card from hand goes to the Supporters stack.");
                AddSupporter(g, seat, card, "Mobilize");
                break;
            case "train":
                g.DiscardFromHand(seat, card, "spends to train an officer");
                Train(g, seat, "Train");
                break;
        }
        g.ActionResolved(seat);
    }

    /// <summary>Added to the Military Operations prompt when no Organize option is offered.</summary>
    public const string OrganizeHint = "(Organize needs one of your warriors in a clearing without your sympathy)";

    // Evening: step 0 military operations (I["used"]), step 1 draw and discard

    static Decision? EveningNext(SimGame g, Frame f)
    {
        int seat = f.Seat;
        if (f.Str("pending") is string pending)
        {
            f.SetStr("pending", null);
            // A Move operation is spent only if a move happened: opening Move and choosing "Do not move" costs nothing.
            if (pending != "move" || f.Get("moves") > f.Get("movesBefore"))
            {
                if (pending == "move") f.Set("used", f.Get("used") + 1);
                g.ActionResolved(seat);
            }
            else g.Log($"{g.SeatName(seat)} does not move; the operation is not spent.");
        }
        if (f.Step == 0)
        {
            int left = D(g, seat).Officers - f.Get("used");
            if (left <= 0) { f.Step = 1; return null; }
            var d = new Decision(seat, $"Evening, Military Operations: {left} of {D(g, seat).Officers} operation(s) left (one per officer)");
            if (Moves.Legal(g, seat).Count > 0) d.Add("move", "Move: take one move");
            foreach (var (c, def) in Battles.Targets(g, seat)) d.Add($"battle:{c}:{def}", Battles.TargetText(g, c, def));
            if (g.Seat(seat).SupplyWarriors > 0)
                foreach (var c in BaseClearings(g, seat).Where(c => g.CanPlace(seat, c)))
                    d.Add($"recruit:{c}", $"Recruit: place a warrior at the base in {g.ClearingName(c)}");
            if (g.Supply(seat, PieceTypes.Sympathy) > 0)
            {
                int vp = SpreadVp[Math.Min(SympathyOnMap(g, seat), SpreadVp.Length - 1)];
                foreach (var c in g.ClearingIds.Where(c => g.Warriors(seat, c) > 0 && !Sympathetic(g, seat, c) && g.CanPlace(seat, c)))
                    d.Add($"organize:{c}", $"Organize: remove a warrior from {g.ClearingName(c)} to place sympathy there (score {vp} VP)");
            }
            if (!d.Options.Any(o => o.Key.StartsWith("organize:")))
                d.Prompt += g.Supply(seat, PieceTypes.Sympathy) > 0
                    ? " " + OrganizeHint
                    : " (Organize: every sympathy token is already on the map)";
            d.Add("end", "End military operations");
            return d;
        }
        if (f.Step == 1)
        {
            int bonus = BaseClearings(g, seat).Count;
            g.Log($"{g.SeatName(seat)} draws 1 card plus {bonus} bonus (one per base on the map).");
            g.Draw(seat, 1 + bonus);
            f.Step = 2;
            g.Push(new Frame("discard", seat).Set("limit", SimGame.HandLimit));
            return null;
        }
        g.Pop(f);
        return null;
    }

    static void EveningChoose(SimGame g, Frame f, Option o)
    {
        int seat = f.Seat;
        if (o.Key == "end") { f.Step = 1; return; }
        var p = o.Key.Split(':');
        if (p[0] == "move") f.Set("movesBefore", f.Get("moves"));   // counted in EveningNext once the move happened
        else f.Set("used", f.Get("used") + 1);
        f.SetStr("pending", p[0]);
        switch (p[0])
        {
            case "move": g.Push(Moves.Frame(seat, optional: true, label: "Military operation, move")); break;
            case "battle": Battles.Start(g, seat, int.Parse(p[2]), int.Parse(p[1])); break;
            case "recruit": g.PlaceWarriors(seat, int.Parse(p[1]), 1); break;
            case "organize":
            {
                int c = int.Parse(p[1]), k = SympathyOnMap(g, seat);
                g.RemoveWarriors(seat, c, 1, seat, "organize");
                if (g.PlaceToken(seat, c, PieceTypes.Sympathy))
                {
                    int vp = SpreadVp[Math.Min(k, SpreadVp.Length - 1)];
                    if (vp > 0) g.AddVp(seat, vp, "sympathy track (organize)");
                }
                break;
            }
        }
    }

    // Outrage: the payer (f.Seat) gives a matching card, or shows its hand and the Alliance draws a supporter.

    static Decision? OutrageNext(SimGame g, Frame f)
    {
        int payer = f.Seat, ally = f.Get("alliance"), c = f.Get("c");
        var suit = g.SuitOf(c);
        var cards = g.CardsMatching(payer, suit).ToList();
        if (cards.Count == 0)
        {
            g.Pop(f);
            g.Log($"Outrage in {g.ClearingName(c)}: {g.SeatName(payer)} has no {Cards.SuitName(suit)} card (nor bird), shows their hand to {g.SeatName(ally)}, and the Alliance draws a supporter from the deck.");
            g.Log($"{g.SeatName(payer)}'s hand (Outrage): {(g.Seat(payer).Hand.Count == 0 ? "empty" : string.Join(", ", g.Seat(payer).Hand.Select(Cards.Label)))}.", ally);
            DrawSupporter(g, ally, "Outrage");
            return null;
        }
        var d = new Decision(payer, $"Outrage in {g.ClearingName(c)}: give a {Cards.SuitName(suit)} card (birds count) to {g.SeatName(ally)}'s Supporters stack");
        foreach (var x in cards.OrderBy(x => Cards.Get(x).Suit == Suit.Bird ? 1 : 0)) d.Add($"give:{x}", $"Give {Cards.Label(x)}");
        d.DefaultKey = d.Options[0].Key;   // a non-active payer gives a card of the suit before a bird
        return d;
    }

    // ---------------------------------------------------------------------------------------------- reads and text

    public override IReadOnlyList<Suit> Supporters(SimGame g, int seat) => D(g, seat).Supporters.Select(c => Cards.Get(c).Suit).ToList();
    public override int Officers(SimGame g, int seat) => D(g, seat).Officers;
    /// <summary>The supporter cards: held by the faction outside the hand.</summary>
    public static IEnumerable<int> SupporterCards(SimGame g, int seat) => D(g, seat).Supporters;

    public override string BoardText(SimGame g, int seat)
    {
        var d = D(g, seat);
        int k = SympathyOnMap(g, seat);
        var bases = BaseSuits.Select(s => BaseClearings(g, seat).Where(c => g.Clearing(c).Buildings.Any(p => p.Seat == seat && p.Type == BaseType(s)))
            .Select(c => $"{Cards.SuitName(s)} base in c{c}").FirstOrDefault() ?? $"{Cards.SuitName(s)} base on the board").ToList();
        var lines = new List<string> { $"{Name} board:" };
        lines.Add($"  supporters: {d.Supporters.Count} card(s), face down{(HasBases(g, seat) ? " (no limit: a base is on the map)" : " (limit 5: no base on the map)")}; officers: {d.Officers}");
        lines.Add($"  bases: {string.Join(", ", bases)}");
        lines.Add(k >= 10
            ? "  sympathy: all 10 tokens on the map"
            : $"  sympathy: {k} on the map, {g.Supply(seat, PieceTypes.Sympathy)} on the track; the next costs {SpreadCost[k]} matching supporter(s) (+1 under martial law) and scores {SpreadVp[k]} VP");
        lines.Add($"  warriors in supply: {g.Seat(seat).SupplyWarriors}/10; evening draw: {1 + BaseClearings(g, seat).Count}");
        return string.Join("\n", lines);
    }

    public override string PrivateText(SimGame g, int seat) =>
        $"YOUR SUPPORTERS (face down, only you see them; {D(g, seat).Supporters.Count}): {SupportersText(g, seat)}";

    public override string RulesText(SimGame g, int seat) =>
$@"WOODLAND ALLIANCE (faction board)
Your supporters now (face down, only you see them): {SupportersText(g, seat)}; officers: {D(g, seat).Officers}.
Your pieces: 10 warriors, 3 bases (fox, rabbit, mouse; buildings), 10 sympathy tokens.
Supporters: cards on your Supporters stack, spent only for their suit (birds match any suit); they do not count
  toward your hand. Without a base on the map the stack holds at most 5 (a sixth card is discarded).
Guerrilla War: as defender in battle you deal hits equal to the higher roll, and the attacker the lower.
Outrage: whenever another player removes a sympathy token or moves warriors into a sympathetic clearing, they must
  add a matching card (birds count) from their hand to your Supporters stack; with none, they show you their hand
  and you draw a card from the deck onto the stack.
Losing a base: discard every supporter of its suit (birds too) and half your officers (rounded up); with no base
  left, discard supporters down to 5.
BIRDSONG: 1. Revolt, any number of times: in a sympathetic clearing without a base whose base is on your board,
  spend 2 matching supporters; remove every enemy piece there (1 VP per building or token), place the base and one
  warrior per sympathetic clearing of that suit, and one warrior becomes an officer.
  2. Spread Sympathy, any number of times: place sympathy in an unsympathetic clearing next to a sympathetic one
  (any clearing when you have none), spending matching supporters: 1,1,1,2,2,2,3,3,3,3 by tokens already out, +1
  (martial law) when another player has 3+ warriors there. Score the VP uncovered: 0,1,1,1,2,2,3,4,4,4.
DAYLIGHT, in any order and number: Craft (each sympathy token gives its clearing's suit once per turn); Mobilize
  (a card from hand onto the Supporters stack); Train (spend a card matching a base's clearing: a warrior becomes an
  officer).
EVENING: Military Operations, one per officer: move, battle, recruit (a warrior at a base), or organize (remove one
  of your warriors from an unsympathetic clearing to place sympathy there and score it). Then draw one card plus
  one per base on the map, and discard down to 5.
SCORING: the sympathy track, 1 VP per enemy building or token removed, crafted items' VP. 30 VP wins.";
}
