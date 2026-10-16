using System.Text;
using Root33.Core;

namespace Root33.Sim;

/// <summary>Text views: the board as one seat may see it, the options, and the STATUS line.</summary>
public static class Render
{
    public static string Who(SimGame g, int seat) => $"seat {seat + 1} ({ModText.SeatTitle(g, seat)})";

    public static string StatusLine(SimGame g)
    {
        if (g.State.GameOver) return "GAME OVER: " + (g.State.GameOverText ?? "the game ended") + ".";
        var d = g.Pending;
        if (d == null) return $"STATUS: round {g.State.Round} · nobody to act (engine idle)";
        string round = g.State.SetupDone ? g.State.Round.ToString() : "0 (setup)";
        return $"STATUS: round {round} · {Who(g, d.Seat)} to act · {d.Prompt}";
    }

    public static string Options(SimGame g, Decision d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Your decision: {d.Prompt}");
        for (int i = 0; i < d.Options.Count; i++) sb.AppendLine($"  {i + 1}. {d.Options[i].Text}   [{d.Options[i].Key}]");
        sb.Append($"Answer with: do --seat {d.Seat + 1} <number or key>");
        return sb.ToString();
    }

    static string Pieces(SimGame g, int c)
    {
        var cs = g.Clearing(c);
        var parts = new List<string>();
        for (int s = 0; s < g.SeatCount; s++)
        {
            var f = g.Faction(s);
            var bits = new List<string>();
            int w = cs.WarriorsOf(s);
            if (w > 0) bits.Add($"{w} warrior{(w == 1 ? "" : "s")}");
            foreach (var grp in cs.Buildings.Where(p => p.Seat == s).GroupBy(p => p.Type)) bits.Add(Count(grp.Count(), f.PieceName(grp.Key)));
            foreach (var grp in cs.Tokens.Where(p => p.Seat == s).GroupBy(p => p.Type)) bits.Add(Count(grp.Count(), f.PieceName(grp.Key)));
            if (g.Seat(s).PawnClearing == c) bits.Add("pawn");
            if (bits.Count > 0) parts.Add($"seat {s + 1}: {string.Join(", ", bits)}");
        }
        if (cs.Ruin) parts.Add("ruin");
        return parts.Count == 0 ? "empty" : string.Join(" | ", parts);
    }

    static string Count(int n, string name) => n == 1 ? name : $"{n} {name}s";

    /// <summary>Everything the seat may see: board, every faction's public state, its own hand, the mod's view, its options.</summary>
    public static string View(SimGame g, int seat)
    {
        var sb = new StringBuilder();
        var st = g.State;
        sb.AppendLine($"=== Root 33 tabletop · Autumn map (the Continent) · seed {st.Seed} · spoiler {st.Spoiler} · you are {Who(g, seat)}, {FactionRegistry.EngineName(g.Seat(seat).Engine)} ===");
        if (st.SetupDone) sb.AppendLine($"Round {st.Round} · {Who(g, st.Turn.Seat)}'s turn · {st.Turn.Phase}");
        else sb.AppendLine("Setup in progress.");
        sb.AppendLine();
        sb.AppendLine("CLEARINGS (number, name, suit, slots: pieces · ruler)");
        foreach (var c in g.ClearingIds)
        {
            var m = g.Map.Clearings[c];
            var ruler = g.Ruler(c);
            var extra = (m.Corner ? " corner" : "") + (m.OnRiver ? " river" : "");
            sb.AppendLine($"  {c,2} {m.Name} ({Cards.SuitName(m.Suit)}{extra}, {m.Slots} slot{(m.Slots == 1 ? "" : "s")}, {g.FreeSlots(c)} free) - paths to {string.Join(",", m.Paths)}");
            sb.AppendLine($"       {Pieces(g, c)} · ruled by {(ruler is int r ? Who(g, r) : "nobody")}");
        }
        var forestPawns = Enumerable.Range(0, g.SeatCount).Where(s => g.Seat(s).PawnForest != null).ToList();
        foreach (var s in forestPawns) sb.AppendLine($"  {Who(g, s)}'s pawn is in forest {g.Seat(s).PawnForest} (touching {string.Join(",", SimMap.ForestDefs[g.Seat(s).PawnForest!.Value])})");
        sb.AppendLine();
        sb.AppendLine("FACTIONS");
        for (int s = 0; s < g.SeatCount; s++)
        {
            var ss = g.Seat(s);
            var dom = ss.Dominance is int dc ? $" · ACTIVE {Cards.Get(dc).Name} (no VP scoring)" : "";
            var coal = ss.CoalitionWith is int cw ? $" · in coalition with seat {cw + 1}" : "";
            sb.AppendLine($"  {Who(g, s)} {FactionRegistry.EngineName(ss.Engine)}{(ss.Human ? "" : " [bot]")}: {ss.Vp} VP{dom}{coal} · {ss.Hand.Count} card{(ss.Hand.Count == 1 ? "" : "s")} in hand");
            if (ss.CraftedItems.Count > 0) sb.AppendLine($"    crafted items: {string.Join(", ", ss.CraftedItems)}");
            if (ss.PlayArea.Count > 0) sb.AppendLine($"    play area: {string.Join(", ", ss.PlayArea.Select(Cards.Label))}");
            foreach (var line in g.Faction(s).BoardText(g, s).Split('\n')) sb.AppendLine("    " + line);
        }
        sb.AppendLine();
        sb.AppendLine($"Item supply: {(st.ItemSupply.Count == 0 ? "empty" : string.Join(", ", st.ItemSupply.GroupBy(i => i).Select(x => $"{x.Key} x{x.Count()}")))}");
        sb.AppendLine($"Ruins (items face down): {string.Join(", ", st.Clearings.Values.Where(c => c.Ruin).Select(c => $"c{c.Id}"))}");
        sb.AppendLine($"Deck: {st.Deck.Count} cards · discard pile: {st.Discard.Count} (top: {(st.Discard.Count > 0 ? Cards.Label(st.Discard[^1]) : "none")})");
        if (st.AvailableDominance.Count > 0) sb.AppendLine($"Available dominance cards: {string.Join(", ", st.AvailableDominance.Select(Cards.Label))}");
        sb.AppendLine();
        sb.AppendLine($"YOUR HAND ({g.Seat(seat).Hand.Count}):");
        foreach (var c in g.Seat(seat).Hand) sb.AppendLine("  " + Cards.Describe(c));
        var own = g.Faction(seat).PrivateText(g, seat);
        if (!string.IsNullOrWhiteSpace(own)) sb.AppendLine(own.TrimEnd());
        var mod = g.Bridge.ModView(seat);
        if (!string.IsNullOrWhiteSpace(mod)) { sb.AppendLine(); sb.AppendLine(mod.TrimEnd()); }
        if (g.Pending is Decision d && d.Seat == seat) { sb.AppendLine(); sb.AppendLine(Options(g, d)); }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Bot seats: the core's director first (hook.bot-decision), else a seeded random legal option.</summary>
public static class Bots
{
    static readonly string[] Passive = { "end", "skip", "no", "done", "continue", "cancel", "decline" };

    public static string Pick(SimGame g, Decision d)
    {
        var fromCore = g.Bridge.OnBotDecision(d.Seat, d);
        if (fromCore != null && d.Find(fromCore) != null) return fromCore;
        // random legal, with passing options three times less likely so bots play the game out
        var weights = d.Options.Select(o => Passive.Contains(o.Key) ? 1 : (o.Key.StartsWith("card:") ? 1 : 3)).ToList();
        int r = g.BotRandom(weights.Sum());
        for (int i = 0; i < d.Options.Count; i++) { r -= weights[i]; if (r < 0) return d.Options[i].Key; }
        return d.Options[^1].Key;
    }

    /// <summary>Let bot seats decide until a human seat must decide or the game ends. Returns the number of bot decisions.</summary>
    public static int Run(SimGame g, int maxDecisions = 1_000_000, int roundCap = int.MaxValue)
    {
        int n = 0;
        while (g.Pending is Decision d && !g.Seat(d.Seat).Human && n < maxDecisions && g.State.Round <= roundCap)
        {
            g.Choose(d.Seat, Pick(g, d));
            n++;
        }
        return n;
    }
}
