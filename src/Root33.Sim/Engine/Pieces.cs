using Root33.Core;

namespace Root33.Sim;

/// <summary>Shared piece helpers: rule, placement, removal (1 VP per enemy building or token), movement of warriors.</summary>
public sealed partial class SimGame
{
    public int Warriors(int seat, int c) => Clearing(c).WarriorsOf(seat);
    public int BuildingsOf(int seat, int c, string? type = null) =>
        Clearing(c).Buildings.Count(p => p.Seat == seat && (type == null || TypeMatches(p.Type, type)));
    public int TokensOf(int seat, int c, string? type = null) =>
        Clearing(c).Tokens.Count(p => p.Seat == seat && (type == null || TypeMatches(p.Type, type)));
    /// <summary>"base" matches "base.fox".</summary>
    public static bool TypeMatches(string actual, string wanted) => actual == wanted || actual.StartsWith(wanted + ".");
    public IEnumerable<int> ClearingsWith(int seat, Func<ClearingState, bool> test) => State.Clearings.Values.Where(test).Select(c => c.Id);
    public int FreeSlots(int c) => Map.Clearings[c].Slots - Clearing(c).Buildings.Count - (Clearing(c).Ruin ? 1 : 0);
    public int Supply(int seat, string type) => Seat(seat).Supply.TryGetValue(type, out var n) ? n : 0;

    /// <summary>Law 2.5: most warriors plus buildings rules; ties rule nobody, unless exactly one tied seat wins rule ties (Eyrie).</summary>
    public int? Ruler(int c)
    {
        var cs = Clearing(c);
        var presence = new Dictionary<int, int>();
        foreach (var kv in cs.Warriors) if (kv.Value > 0) presence[kv.Key] = kv.Value;
        foreach (var b in cs.Buildings) presence[b.Seat] = presence.GetValueOrDefault(b.Seat) + 1;
        if (presence.Count == 0) return null;
        int max = presence.Values.Max();
        var top = presence.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
        if (top.Count == 1) return top[0];
        var lords = top.Where(s => Faction(s).WinsRuleTies).ToList();
        return lords.Count == 1 ? lords[0] : null;
    }

    public bool Rules(int seat, int c) => Ruler(c) == seat;
    public IEnumerable<int> RuledBy(int seat) => ClearingIds.Where(c => Rules(seat, c));

    /// <summary>Whether a seat may place a piece in c: no other faction blocks it (the Keep).</summary>
    public bool CanPlace(int seat, int c)
    {
        for (int o = 0; o < SeatCount; o++)
            if (o != seat && Faction(o).BlocksPlacement(this, o, seat, c)) return false;
        return true;
    }

    /// <summary>Place up to n warriors from supply (Law 1.5.4: as many as possible). Returns how many.</summary>
    public int PlaceWarriors(int seat, int c, int n, bool log = true)
    {
        if (!CanPlace(seat, c)) return 0;
        var s = Seat(seat);
        int k = Math.Min(n, s.SupplyWarriors);
        if (k <= 0) return 0;
        s.SupplyWarriors -= k;
        var cs = Clearing(c);
        cs.Warriors[seat] = cs.WarriorsOf(seat) + k;
        if (log) Log($"{SeatName(seat)} places {k} warrior{(k == 1 ? "" : "s")} in {ClearingName(c)}.");
        for (int i = 0; i < k; i++) NotifyPlaced(seat, PieceKind.Warrior, "warrior", c);
        return k;
    }

    public bool PlaceBuilding(int seat, int c, string type, bool log = true)
    {
        if (!CanPlace(seat, c) || FreeSlots(c) <= 0 || Supply(seat, type) <= 0) return false;
        Seat(seat).Supply[type]--;
        Clearing(c).Buildings.Add(new Piece(seat, type));
        if (log) Log($"{SeatName(seat)} places a {Faction(seat).PieceName(type)} in {ClearingName(c)}.");
        NotifyPlaced(seat, PieceKind.Building, type, c);
        return true;
    }

    public bool PlaceToken(int seat, int c, string type, bool log = true)
    {
        if (!CanPlace(seat, c) || Supply(seat, type) <= 0) return false;
        Seat(seat).Supply[type]--;
        Clearing(c).Tokens.Add(new Piece(seat, type));
        if (log) Log($"{SeatName(seat)} places a {Faction(seat).PieceName(type)} token in {ClearingName(c)}.");
        NotifyPlaced(seat, PieceKind.Token, type, c);
        return true;
    }

    private void NotifyPlaced(int owner, PieceKind kind, string type, int c)
    {
        Bridge.OnPiecePlaced(owner, kind, type, c);
        for (int s = 0; s < SeatCount; s++) Faction(s).OnPiecePlaced(this, s, owner, kind, type, c);
    }

    /// <summary>Remove up to n warriors of owner from c to its supply. Field Hospitals and other reactions are pushed as frames.</summary>
    public int RemoveWarriors(int owner, int c, int n, int? remover, string why)
    {
        var cs = Clearing(c);
        int k = Math.Min(n, cs.WarriorsOf(owner));
        if (k <= 0) return 0;
        cs.Warriors[owner] -= k;
        if (cs.Warriors[owner] == 0) cs.Warriors.Remove(owner);
        Seat(owner).SupplyWarriors += k;
        Log($"{k} {SeatName(owner)} warrior{(k == 1 ? " is" : "s are")} removed from {ClearingName(c)} ({why}).");
        for (int i = 0; i < k; i++) NotifyRemoved(new RemovedRecord { Owner = owner, Kind = PieceKind.Warrior, Type = null, Remover = remover, Clearing = c });
        for (int s = 0; s < SeatCount; s++) Faction(s).OnWarriorsRemoved(this, s, owner, c, k, remover);
        return k;
    }

    /// <summary>Remove one building (to its owner's supply). The remover scores 1 VP when it is an enemy's (Law 3.2.1).</summary>
    public bool RemoveBuilding(int owner, int c, string type, int? remover, string why) => RemovePiece(owner, c, type, remover, why, building: true);

    /// <summary>Remove one token. The keep never returns to supply (Law 6.2.2). The remover scores 1 VP when it is an enemy's.</summary>
    public bool RemoveToken(int owner, int c, string type, int? remover, string why) => RemovePiece(owner, c, type, remover, why, building: false);

    private bool RemovePiece(int owner, int c, string type, int? remover, string why, bool building)
    {
        var list = building ? Clearing(c).Buildings : Clearing(c).Tokens;
        var p = list.FirstOrDefault(x => x.Seat == owner && TypeMatches(x.Type, type));
        if (p == null) return false;
        list.Remove(p);
        if (p.Type != PieceTypes.Keep) Seat(owner).Supply[p.Type] = Supply(owner, p.Type) + 1;
        Log($"{SeatName(owner)}'s {Faction(owner).PieceName(p.Type)} is removed from {ClearingName(c)} ({why}).");
        if (remover is int r && r != owner) AddVp(r, 1, $"removed an enemy {(building ? "building" : "token")}");
        NotifyRemoved(new RemovedRecord { Owner = owner, Kind = building ? PieceKind.Building : PieceKind.Token, Type = p.Type, Remover = remover, Clearing = c });
        return true;
    }

    private void NotifyRemoved(RemovedRecord r)
    {
        State.Battle?.Removed.Add(r);
        Bridge.OnPieceRemoved(r.Remover, r.Owner, r.Kind, r.Type, r.Clearing);
        for (int s = 0; s < SeatCount; s++) Faction(s).OnPieceRemoved(this, s, r);
    }

    /// <summary>Move warriors without checking legality (the caller checked it with Moves.CanMove).</summary>
    public void MoveWarriors(int seat, int from, int to, int n)
    {
        var a = Clearing(from);
        n = Math.Min(n, a.WarriorsOf(seat));
        if (n <= 0) return;
        a.Warriors[seat] -= n;
        if (a.Warriors[seat] == 0) a.Warriors.Remove(seat);
        var b = Clearing(to);
        b.Warriors[seat] = b.WarriorsOf(seat) + n;
        Log($"{SeatName(seat)} moves {n} warrior{(n == 1 ? "" : "s")} from {ClearingName(from)} to {ClearingName(to)}.");
        NotifyMoved(seat, PieceKind.Warrior, from, to, n);
    }

    /// <summary>Report a move of pieces done by a faction's own code (a Vagabond pawn): hooks and notifications.</summary>
    public void NotifyMoved(int owner, PieceKind kind, int? from, int? to, int count)
    {
        Bridge.OnPieceMoved(owner, kind, from, to, count);
        if (from is int f && to is int t)
            for (int s = 0; s < SeatCount; s++) Faction(s).OnPiecesMoved(this, s, owner, f, t, count);
    }
}
