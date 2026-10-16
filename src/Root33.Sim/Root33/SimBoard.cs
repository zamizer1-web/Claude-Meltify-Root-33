using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Sim;

/// <summary>
/// Root33.Core's IBoardView and IBoardActions over a SimGame (owner: engineer D), member by member like
/// src/Root33.Core.Tests/Support/FakeBoard.cs. Reads come from the sim state and the factions' public reads; actions go
/// through the sim's own helpers (g.PlaceWarriors, g.MoveWarriors, g.AddVp, g.Draw, g.EndGame) or into State.Mod, which
/// the shared rules honour (ExtraHits, ExtraMaxHits, IgnoreFirstHit, GuardFloors, BlockedPlacements, Abilities,
/// HomeRestrictions, StartForestRestrictions, DeclaredWinners) or only display (Banners, SeatTitles, Overlays).
/// Root 33 never sees a sim seat number as anything but a SeatId with the same 0-based value; clearings are 1-12 on both sides.
/// </summary>
public sealed class SimBoard : IBoardView, IBoardActions
{
    private readonly SimGame _g;
    public SimBoard(SimGame g) { _g = g; }

    /// <summary>Board actions the base-game tabletop cannot do (no Duchy, no Keepers): the core takes their sheet fallbacks.</summary>
    public static readonly string[] Unsupported =
    {
        Ids.BoardActions.PlaceTunnel, Ids.BoardActions.GrantFreeDig, Ids.BoardActions.MoveRelic, Ids.BoardActions.TagRelic,
    };

    /// <summary>True while the core reloads its state (hook.load): nothing is logged and no banner comes down (every CLI
    /// command reloads the save, so a reload must not look like a game event).</summary>
    public bool Quiet { get; set; }

    /// <summary>Called for every do.show-bubble (the bridge queues it per seat and logs it).</summary>
    public Action<int, string>? BubbleShown { get; set; }

    /// <summary>Called for every do.declare-winner (the bridge names the win and ends the game).</summary>
    public Action<int, string>? WinnerDeclared { get; set; }

    private static SeatId S(int seat) => new(seat);
    private static ClearingId C(int c) => new(c);
    private SimState St => _g.State;
    private bool OnMap(ClearingId c) => _g.Map.Clearings.ContainsKey(c.Value);
    private bool IsSeat(SeatId s) => s.Value >= 0 && s.Value < _g.SeatCount;

    // ================================================================= IBoardView

    public IReadOnlyList<SeatId> Seats() => _g.TurnOrder().Select(S).ToArray();
    public EngineId EngineOf(SeatId seat) => ModuleRegistry.EngineIdOf(_g.Seat(seat.Value).Engine) ?? EngineId.VagabondOther;
    public bool IsBot(SeatId seat) => IsSeat(seat) && !_g.Seat(seat.Value).Human;

    public SeatId CurrentSeat()
    {
        var s = _g.ActiveSeat;
        return S(s >= 0 ? s : St.FirstSeat);
    }

    public TurnPhase Phase() => St.SetupDone ? St.Turn.Phase : TurnPhase.Birdsong;
    public int Round() => St.Round;
    public int Vp(SeatId seat) => _g.Seat(seat.Value).Vp;

    public Suit? ActiveDominance(SeatId seat) =>
        _g.Seat(seat.Value).Dominance is int d ? Cards.Get(d).Suit : null;

    public SeatId? CoalitionPartner(SeatId seat) =>
        _g.Seat(seat.Value).CoalitionWith is int p ? S(p) : null;

    public int HandSize(SeatId seat) => _g.Seat(seat.Value).Hand.Count;
    public IReadOnlyList<Suit> HandSuits(SeatId seat) => _g.Seat(seat.Value).Hand.Select(c => Cards.Get(c).Suit).ToArray();

    /// <summary>The option keys of the decision the seat owes now (the tabletop's legal-action API), else none.</summary>
    public IReadOnlyList<string> LegalActions(SeatId seat) =>
        _g.Pending is Decision d && d.Seat == seat.Value ? d.Options.Select(o => o.Key).ToArray() : Array.Empty<string>();

    public string MapName() => AutumnMap.MapName;
    public IReadOnlyList<ClearingId> Clearings() => _g.ClearingIds.OrderBy(c => c).Select(C).ToArray();
    public IReadOnlyList<Suit> SuitsOf(ClearingId c) => new[] { _g.Map.Clearings[c.Value].Suit };
    public IReadOnlyList<ClearingId> PathNeighbours(ClearingId c) => _g.Map.Clearings[c.Value].Paths.OrderBy(x => x).Select(C).ToArray();
    public IReadOnlyList<ClearingId> RiverNeighbours(ClearingId c) => _g.Map.Clearings[c.Value].RiverLinks.OrderBy(x => x).Select(C).ToArray();
    public bool OnRiver(ClearingId c) => _g.Map.Clearings[c.Value].OnRiver;
    public IReadOnlyList<ClearingId> Corners() => _g.Map.Clearings.Values.Where(x => x.Corner).Select(x => C(x.Id)).ToArray();
    public ClearingId? OppositeCorner(ClearingId corner) =>
        OnMap(corner) && _g.Map.Clearings[corner.Value].OppositeCorner is int o ? C(o) : null;
    public bool HasRuin(ClearingId c) => _g.Clearing(c.Value).Ruin;
    public int TotalSlots(ClearingId c) => _g.Map.Clearings[c.Value].Slots;
    public int FreeSlots(ClearingId c) => Math.Max(0, _g.FreeSlots(c.Value));
    public IReadOnlyList<int> ForestsAround(ClearingId c) => _g.Map.ForestsAround(c.Value).OrderBy(f => f).ToArray();
    public IReadOnlyList<ClearingId> ForestClearings(int forest) =>
        SimMap.ForestDefs.TryGetValue(forest, out var l) ? l.Select(C).ToArray() : Array.Empty<ClearingId>();

    public SeatId? Ruler(ClearingId c) => _g.Ruler(c.Value) is int r ? S(r) : null;
    public int Warriors(SeatId seat, ClearingId c) => _g.Warriors(seat.Value, c.Value);
    public int Buildings(SeatId seat, ClearingId c) => _g.BuildingsOf(seat.Value, c.Value);
    public int BuildingsOfType(SeatId seat, ClearingId c, string type) => _g.BuildingsOf(seat.Value, c.Value, type);
    public int Tokens(SeatId seat, ClearingId c) => _g.TokensOf(seat.Value, c.Value);
    public int TokensOfType(SeatId seat, ClearingId c, string type) => _g.TokensOf(seat.Value, c.Value, type);
    public ClearingId? PawnClearing(SeatId seat) => _g.Seat(seat.Value).PawnClearing is int c ? C(c) : null;
    public int? PawnForest(SeatId seat) => _g.Seat(seat.Value).PawnForest;
    public int SupplyWarriors(SeatId seat) => _g.Seat(seat.Value).SupplyWarriors;
    public int SupplyPieces(SeatId seat, string type) => _g.Supply(seat.Value, type);
    public IReadOnlyList<ItemState> Items(SeatId seat) => _g.Faction(seat.Value).Items(_g, seat.Value);
    public Relationship RelationshipOf(SeatId vagabond, SeatId faction) => _g.Faction(vagabond.Value).RelationshipWith(_g, vagabond.Value, faction.Value);
    public IReadOnlyList<ItemType> CraftedItems(SeatId seat) => _g.Seat(seat.Value).CraftedItems.ToArray();

    public string EyrieLeader(SeatId eyrie)
    {
        var l = _g.Faction(eyrie.Value).EyrieLeader(_g, eyrie.Value);
        return string.IsNullOrEmpty(l) ? "builder" : l;
    }

    public IReadOnlyList<Suit> DecreeColumn(SeatId eyrie, string column) => _g.Faction(eyrie.Value).DecreeColumn(_g, eyrie.Value, column);
    public IReadOnlyList<Suit> Supporters(SeatId alliance) => _g.Faction(alliance.Value).Supporters(_g, alliance.Value);
    public int Officers(SeatId alliance) => _g.Faction(alliance.Value).Officers(_g, alliance.Value);

    // No Keepers, Duchy, Corvids, Lizards or Hundreds in the base game: their reads are empty.
    public IReadOnlyList<int> RelicsInForest(int forest) => Array.Empty<int>();
    public string? RelicType(int relicId) => null;
    public int? RelicValue(int relicId) => null;
    public bool RelicFaceUp(int relicId) => false;
    public int? RelicForest(int relicId) => null;
    public ClearingId? RelicClearing(int relicId) => null;
    public IReadOnlyList<int> RecoveredRelics(SeatId keepers) => Array.Empty<int>();
    public IReadOnlyList<Suit> RetinueColumn(SeatId keepers, string column) => Array.Empty<Suit>();
    public int BurrowWarriors(SeatId duchy) => 0;
    public IReadOnlyList<string> SwayedMinisters(SeatId duchy) => Array.Empty<string>();
    public string? FaceUpPlot(SeatId corvids, ClearingId c) => null;
    public string? HiddenPlotType(SeatId corvids, ClearingId c) => null;
    public Suit? OutcastSuit(SeatId lizards) => null;
    public bool OutcastHated(SeatId lizards) => false;
    public int Acolytes(SeatId lizards) => 0;
    public string Mood(SeatId hundreds) => "";
    public int HoardTrack(SeatId hundreds, string track) => 0;

    // ================================================================= IBoardActions

    private void Log(string text) { if (!Quiet) _g.Log("Root 33: " + text); }
    private string Who(int seat) => Render.Who(_g, seat);

    public int PlaceWarriors(SeatId seat, ClearingId c, int count)
    {
        if (!IsSeat(seat) || !OnMap(c) || count <= 0) return 0;
        return _g.PlaceWarriors(seat.Value, c.Value, count);
    }

    public bool MoveWarriors(SeatId seat, ClearingId from, ClearingId to, int count)
    {
        if (!IsSeat(seat) || !OnMap(from) || !OnMap(to) || count <= 0 || _g.Warriors(seat.Value, from.Value) < count) return false;
        _g.MoveWarriors(seat.Value, from.Value, to.Value, count);
        return true;
    }

    public bool PlaceTunnel(SeatId duchy, ClearingId c, bool extraPiece) => false;
    public bool GrantFreeDig(SeatId duchy, ClearingId c) => false;
    public bool MoveRelicToForest(int relicId, int forest) => false;
    public bool TagRelic(int relicId) => false;

    public void DeclareWinner(SeatId seat, string winConditionId)
    {
        if (!IsSeat(seat) || St.GameOver) return;
        St.Mod.DeclaredWinners = _g.WithPartners(seat.Value);
        St.Mod.DeclaredWinId = winConditionId;
        WinnerDeclared?.Invoke(seat.Value, winConditionId);
    }

    public void ScoreVp(SeatId seat, int vp)
    {
        if (IsSeat(seat) && vp != 0) _g.AddVp(seat.Value, vp, "a Root 33 rule");
    }

    public void DrawCards(SeatId seat, int count)
    {
        if (!IsSeat(seat) || count <= 0) return;
        Log($"{Who(seat.Value)} draws {count} extra card{(count == 1 ? "" : "s")}.");
        _g.Draw(seat.Value, count);
    }

    public void AddHits(SeatId seat, int hits)
    {
        var m = St.Mod;
        m.ExtraHits[seat.Value] = m.ExtraHits.GetValueOrDefault(seat.Value) + hits;
        Log($"{Who(seat.Value)} deals {hits} extra hit{(hits == 1 ? "" : "s")} in this battle.");
    }

    public void AddMaxHits(SeatId vagabond, int extra)
    {
        var m = St.Mod;
        m.ExtraMaxHits[vagabond.Value] = m.ExtraMaxHits.GetValueOrDefault(vagabond.Value) + extra;
        Log($"{Who(vagabond.Value)} may roll {extra} more hit{(extra == 1 ? "" : "s")} than usual in this battle.");
    }

    public void IgnoreFirstHit(SeatId seat)
    {
        if (!St.Mod.IgnoreFirstHit.Contains(seat.Value)) St.Mod.IgnoreFirstHit.Add(seat.Value);
        Log($"{Who(seat.Value)}'s warriors ignore the first hit they take in this battle.");
    }

    public void SetGuardFloor(SeatId host, ClearingId c, int floor)
    {
        var cur = St.Mod.GuardFloors.FirstOrDefault(x => x.Seat == host.Value && x.Clearing == c.Value);
        if (floor <= 0) { if (cur != null) St.Mod.GuardFloors.Remove(cur); }
        else if (cur != null) cur.Floor = floor;
        else St.Mod.GuardFloors.Add(new GuardFloor { Seat = host.Value, Clearing = c.Value, Floor = floor });
    }

    public void BlockPlacement(SeatId seat, ClearingId c, bool blocked)
    {
        var has = St.Mod.BlockedPlacements.Any(x => x.Seat == seat.Value && x.Clearing == c.Value);
        if (!blocked) St.Mod.BlockedPlacements.RemoveAll(x => x.Seat == seat.Value && x.Clearing == c.Value);
        else if (!has) St.Mod.BlockedPlacements.Add(new SeatClearing { Seat = seat.Value, Clearing = c.Value });
    }

    public void RestrictHomeChoice(SeatId seat, IReadOnlyList<ClearingId> allowed) =>
        St.Mod.HomeRestrictions[seat.Value] = allowed.Select(c => c.Value).ToList();

    public void RestrictStartForest(SeatId vagabond, IReadOnlyList<int> forests) =>
        St.Mod.StartForestRestrictions[vagabond.Value] = forests.ToList();

    public void GrantAbility(SeatId seat, string abilityKey)
    {
        if (!St.Mod.Abilities.TryGetValue(seat.Value, out var l)) St.Mod.Abilities[seat.Value] = l = new List<string>();
        if (l.Contains(abilityKey)) return;
        l.Add(abilityKey);
        Log($"{Who(seat.Value)} gains a new power: {ModText.AbilityText(abilityKey)}.");
    }

    public void ShowBanner(SeatId seat, string winConditionId)
    {
        var had = St.Mod.Banners.TryGetValue(seat.Value, out var old) && old == winConditionId;
        St.Mod.Banners[seat.Value] = winConditionId;
        if (!had) Log(ModText.BannerAnnounced(_g, seat.Value, winConditionId));
    }

    public void ClearBanner(SeatId seat)
    {
        if (Quiet) return; // a reload keeps the banner; the owner's next Birdsong decides it
        if (St.Mod.Banners.Remove(seat.Value, out var winId) && !St.GameOver)
            Log($"the countdown banner of {Who(seat.Value)} ({ModText.WinTitle(winId)}) comes down.");
    }

    public void ShowBubble(SeatId seat, string barkId)
    {
        if (!Quiet) BubbleShown?.Invoke(seat.Value, barkId);
    }

    public void ShowOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest)
    {
        if (Quiet) return; // display only: a reload redraws, it does not change what is on the table
        var key = OverlayKey(overlayId, seat, c, forest);
        if (!St.Mod.Overlays.Contains(key)) St.Mod.Overlays.Add(key);
    }

    public void HideOverlay(string overlayId, SeatId? seat, ClearingId? c, int? forest)
    {
        if (!Quiet) St.Mod.Overlays.Remove(OverlayKey(overlayId, seat, c, forest));
    }

    public static string OverlayKey(string overlayId, SeatId? seat, ClearingId? c, int? forest) =>
        overlayId + "@" + (seat.HasValue ? seat.Value.Value.ToString() : "") + "/" + (c.HasValue ? c.Value.Value.ToString() : "") +
        "/" + (forest.HasValue ? "f" + forest.Value : "");

    public void SetSeatTitle(SeatId seat, string title)
    {
        if (!IsSeat(seat)) return;
        var had = St.Mod.SeatTitles.TryGetValue(seat.Value, out var old);
        St.Mod.SeatTitles[seat.Value] = title;
        if (had && old != title) Log($"seat {seat.Value + 1} is now called {title}.");
    }

    public void HideClockworkBots() { }

    /// <summary>do.submit-action: the key is remembered and the bot seat takes it at its next decision if still legal.</summary>
    public bool SubmitAction(SeatId seat, string actionKey)
    {
        if (!IsBot(seat)) return false;
        St.Mod.Extra["submit:" + seat.Value] = actionKey;
        return LegalActions(seat).Contains(actionKey);
    }

    /// <summary>do.native-ai-step: the tabletop's own bot policy (seeded random legal) decides; nothing to do.</summary>
    public void UseNativeAi(SeatId seat) { }
}
