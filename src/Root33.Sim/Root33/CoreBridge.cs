using System.Reflection;
using System.Text.Json;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Host;
using SharedText = Root33.Host.ModText;

namespace Root33.Sim;

/// <summary>
/// The seam between the tabletop and the real Root 33 rules in src/Root33.Core (owner: engineer D). The engine creates one
/// CoreBridge per SimGame (new game and every load) and calls one method per row of sheets/game_hooks.json at that
/// moment (method name = the row id in PascalCase: hook.battle-resolved -> OnBattleResolved). Arguments are the
/// tabletop's own (seats 0-based ints, clearings 1-12); the bridge turns them into Root33.Core types and forwards them to
/// a <see cref="Root33Game"/> built over <see cref="SimBoard"/>, exactly as the in-game plugin will from its patches.
///
/// The parts that do not depend on the tabletop are Root33.Host's, shared with the in-game plugin: the hook dispatch
/// (<see cref="HookDispatcher"/>: nested hooks the core causes are queued and delivered right after the hook that caused
/// them, and core exceptions are logged once per hook), the mod's feed (<see cref="ModFeed"/>: bubbles, story, act banners),
/// the mod's view (<see cref="ModView"/>) and the character cards (<see cref="ModCards"/>). <see cref="SimHost"/> is what
/// the tabletop supplies to them.
///
/// What the core does not save (speech-bubble cooldowns, the story log, bubbles each seat has not seen yet) the bridge
/// keeps in State.Mod.Extra["bridge"], because every CLI command reloads the game.
/// </summary>
public sealed class CoreBridge
{
    private static readonly Lazy<ModuleRegistry> Registry = new(ModuleRegistry.Default);

    private readonly SimGame _g;
    private readonly SimBoard _board;
    private readonly Root33Game _game;
    private readonly HookDispatcher _hooks;
    private readonly BridgeData _data;
    private readonly ModView _view;
    private bool _selectShown;

    public CoreBridge(SimGame g)
    {
        _g = g;
        _board = new SimBoard(g) { BubbleShown = Bubble, WinnerDeclared = Declared };
        _game = new Root33Game(_board, _board, Registry.Value, new Capabilities().Unsupported(SimBoard.Unsupported));
        _hooks = new HookDispatcher(_game)
        {
            FirstError = (hook, e) => _g.Log(SharedText.CoreErrorLine(hook, e.Message)),
            AfterHook = AfterCore,
        };
        _data = ReadData();
        Host = new SimHost(g);
        _view = new ModView(Ctx, Host, _data);
    }

    /// <summary>The core game (tests read its context: act, story log, barks, modules).</summary>
    public Root33Game Game => _game;
    public GameContext Ctx => _game.Context;
    public SimBoard Board => _board;
    /// <summary>What the tabletop supplies to Root33.Host (seats, titles, banners, names).</summary>
    public SimHost Host { get; }
    /// <summary>Exceptions the core threw (logged once per hook; the game goes on). Tests assert this stays empty.</summary>
    public List<string> Errors => _hooks.Errors;
    /// <summary>Every speech bubble kept for the views (the last 120).</summary>
    public IReadOnlyList<BubbleRecord> Bubbles => _data.Bubbles;
    /// <summary>Story events fired this game, in order (kept across reloads).</summary>
    public IReadOnlyList<StoryRecord> Story => _data.Story;

    private SimState St => _g.State;
    private static SeatId S(int seat) => new(seat);
    private static ClearingId C(int c) => new(c);
    private static ClearingId? C(int? c) => c is int v ? new ClearingId(v) : null;
    private bool IsVagabond(int seat) => _g.Seat(seat).Faction == "vagabond";
    private SpoilerMode Spoilers => St.Spoiler == "finished" ? SpoilerMode.Finished : SpoilerMode.Safe;

    // ---------------------------------------------------------------- dispatch (Root33.Host.HookDispatcher)

    private void Call(string hook, Action<Root33Game> call) => _hooks.Call(hook, call);
    private T Ask<T>(string hook, Func<Root33Game, T> ask, T fallback) => _hooks.Ask(hook, ask, fallback);

    /// <summary>After each hook: log the story events, act banners and core notes it produced.</summary>
    private void AfterCore() => _data.CatchUp(Ctx, St.Round, St.SetupDone, line => _g.Log(line));

    private void Bubble(int seat, string barkId)
    {
        _data.AddBubble(S(seat), barkId, St.Round);
        _g.Log(SharedText.BubbleLine(Render.Who(_g, seat), barkId));
    }

    private void Declared(int seat, string winId)
    {
        if (St.GameOver) return;
        _g.EndGame(St.Mod.DeclaredWinners.ToList(), WinText());
    }

    private string WinText() => SharedText.WinLine(Host, St.Mod.DeclaredWinners.Select(S).ToList(), St.Mod.DeclaredWinId);

    // ---------------------------------------------------------------- one method per game_hooks.json row

    /// <summary>hook.title-screen</summary>
    public void OnTitleScreen(bool firstLaunch) => Call("hook.title-screen", g => g.OnTitleScreenShown(firstLaunch));

    /// <summary>hook.character-select: the mod's select screen; every engine is owned at the tabletop.</summary>
    public void OnCharacterSelect()
    {
        if (_selectShown) return;
        _selectShown = true;
        _game.NextGameOptions.Spoilers = Spoilers;
        Call("hook.character-select", g => g.OnCharacterSelectShown((EngineId[])Enum.GetValues(typeof(EngineId)), 4));
    }

    /// <summary>hook.seat-assigned: each seat at game creation, before OnGameStart (the character select comes first).</summary>
    public void OnSeatAssigned(int seat)
    {
        OnCharacterSelect();
        var ss = _g.Seat(seat);
        Call("hook.seat-assigned", g =>
        {
            if (Data.Characters.ContainsKey(ss.Character)) g.SelectCharacter(S(seat), ss.Character);
            g.OnSeatAssigned(S(seat), _board.EngineOf(S(seat)), !ss.Human);
        });
    }

    /// <summary>hook.game-start: after every seat is assigned and the starting hands are drawn, before the map.</summary>
    public void OnGameStart() => Call("hook.game-start", g => g.OnGameStart(_board.Seats(), Difficulty.Normal, Spoilers));
    /// <summary>hook.map-loaded</summary>
    public void OnMapLoaded() => Call("hook.map-loaded", g => g.OnMapLoaded(_board.MapName()));
    /// <summary>hook.setup-home: just before a seat's setup (then its faction asks ForcedHome through g.HomeChoices).</summary>
    public void OnSetupHome(int seat) => Call("hook.setup-home", g => g.OnSetupChoice(S(seat)));
    /// <summary>hook.setup-complete: every seat has set up, before the first Birdsong.</summary>
    public void OnSetupComplete() => Call("hook.setup-complete", g => g.OnSetupComplete());
    /// <summary>hook.round-start: the first player's Birdsong begins (round 1, 2, ...).</summary>
    public void OnRoundStart(int round) => Call("hook.round-start", g => g.OnRoundStart(round));
    /// <summary>hook.birdsong-start: before any Birdsong step; dominance wins are checked right after it.</summary>
    public void OnBirdsongStart(int seat) => Call("hook.birdsong-start", g => g.OnBirdsongStart(S(seat)));
    /// <summary>hook.evening-start</summary>
    public void OnEveningStart(int seat) => Call("hook.evening-start", g => g.OnEveningStart(S(seat)));
    /// <summary>hook.evening-end: the seat's Evening (draw and discard) has resolved.</summary>
    public void OnEveningEnd(int seat) => Call("hook.evening-end", g => g.OnEveningEnd(S(seat)));

    /// <summary>hook.turn-end: right after OnEveningEnd, before the next seat's turn. The seat has now seen every bubble so far.</summary>
    public void OnTurnEnd(int seat)
    {
        Call("hook.turn-end", g => g.OnTurnEnd(S(seat)));
        _data.TurnEnded(S(seat));
    }

    /// <summary>hook.act-change (raised by the core itself from hook.turn-end).</summary>
    public void OnActChange(int act) => Call("hook.act-change", g => g.OnActChanged(act));
    /// <summary>hook.action-resolved: after each action of a seat (factions call g.ActionResolved).</summary>
    public void OnActionResolved(int seat) => Call("hook.action-resolved", g => g.OnActionResolved(S(seat)));
    /// <summary>hook.piece-moved: from/to are clearings, null for a forest or off the map.</summary>
    public void OnPieceMoved(int owner, PieceKind kind, int? from, int? to, int count) =>
        Call("hook.piece-moved", g => g.OnPieceMoved(S(owner), kind, C(from), C(to), count));
    /// <summary>hook.piece-placed: once per piece; type is a PieceTypes string or "warrior".</summary>
    public void OnPiecePlaced(int owner, PieceKind kind, string type, int clearing) =>
        Call("hook.piece-placed", g => g.OnPiecePlaced(S(owner), kind, type, C(clearing)));
    /// <summary>hook.piece-removed: once per piece; remover null when no seat removed it.</summary>
    public void OnPieceRemoved(int? remover, int owner, PieceKind kind, string? type, int clearing) =>
        Call("hook.piece-removed", g => g.OnPieceRemoved(remover is int r ? S(r) : null,
            new RemovedPiece(S(owner), kind, kind == PieceKind.Warrior ? null : type), C(clearing)));
    /// <summary>hook.battle-legal: false hides that battle (or Strike) from the options.</summary>
    public bool OnBattleLegal(int attacker, int defender, int clearing, bool strike = false) =>
        Ask("hook.battle-legal", g => g.CanStartBattle(S(attacker), S(defender), C(clearing), strike), true);
    /// <summary>hook.battle-start: before ambushes. Extra hits go in State.Mod (ExtraHits, ExtraMaxHits, IgnoreFirstHit).</summary>
    public void OnBattleStart(int attacker, int defender, int clearing) =>
        Call("hook.battle-start", g => g.OnBattleStarting(S(attacker), S(defender), C(clearing)));

    /// <summary>hook.battle-resolved: b.Removed lists every piece removed in the battle.</summary>
    public void OnBattleResolved(BattleState b)
    {
        var r = new BattleResult
        {
            Clearing = C(b.Clearing), Attacker = S(b.Attacker), Defender = S(b.Defender),
            VpLeaderAtStart = b.VpLeaderAtStart is int l ? S(l) : null,
        };
        foreach (var p in b.Removed) r.Removed.Add(new RemovedPiece(S(p.Owner), p.Kind, p.Kind == PieceKind.Warrior ? null : p.Type));
        foreach (var kv in b.DamagedBefore) r.DamagedItemsBefore[S(kv.Key)] = kv.Value;
        foreach (var kv in b.DamagedAfter) r.DamagedItemsAfter[S(kv.Key)] = kv.Value;
        Call("hook.battle-resolved", g => g.OnBattleResolved(r));
    }

    /// <summary>hook.explore-resolved (called by the Vagabond faction).</summary>
    public void OnExploreResolved(int vagabond, int clearing) => Call("hook.explore-resolved", g => g.OnExploreResolved(S(vagabond), C(clearing)));
    /// <summary>hook.aid-resolved (called by the Vagabond faction).</summary>
    public void OnAidResolved(int vagabond, int receiver, int clearing) =>
        Call("hook.aid-resolved", g => g.OnAidResolved(S(vagabond), S(receiver), C(clearing)));
    /// <summary>hook.hostility-change: false keeps the faction from turning Hostile (called by the Vagabond faction).</summary>
    public bool OnHostilityChange(int vagabond, int faction, int clearing) =>
        Ask("hook.hostility-change", g => g.AllowHostile(S(vagabond), S(faction), C(clearing)), true);
    /// <summary>hook.relationship-changed (called by the Vagabond faction).</summary>
    public void OnRelationshipChanged(int vagabond, int faction, Relationship before, Relationship after) =>
        Call("hook.relationship-changed", g => g.OnRelationshipChanged(S(vagabond), S(faction), before, after));
    /// <summary>hook.item-changed (called by the Vagabond faction).</summary>
    public void OnItemChanged(int vagabond, ItemState? before, ItemState? after) =>
        Call("hook.item-changed", g => g.OnItemChanged(S(vagabond), before, after));
    /// <summary>hook.warlord-removed (no Hundreds in the base game).</summary>
    public void OnWarlordRemoved(int seat, int clearing) => Call("hook.warlord-removed", g => g.OnWarlordRemoved(S(seat), C(clearing)));
    /// <summary>hook.relic-recovered (no Keepers in the base game).</summary>
    public void OnRelicRecovered(int seat, int relicId, string relicType) =>
        Call("hook.relic-recovered", g => g.OnRelicRecovered(S(seat), relicId, relicType));
    /// <summary>hook.relic-moved (no Keepers in the base game).</summary>
    public void OnRelicMoved(int relicId) => Call("hook.relic-moved", g => g.OnRelicMoved(relicId));
    /// <summary>hook.vp-changed</summary>
    public void OnVpChanged(int seat, int before, int after) => Call("hook.vp-changed", g => g.OnVpChanged(S(seat), before, after));
    /// <summary>hook.turmoil (called by the Eyrie faction).</summary>
    public void OnTurmoil(int eyrie) => Call("hook.turmoil", g => g.OnTurmoil(S(eyrie)));
    /// <summary>hook.convert (no Lizard Cult in the base game).</summary>
    public void OnConvert(int lizards, int victim, int clearing) => Call("hook.convert", g => g.OnConvert(S(lizards), S(victim), C(clearing)));
    /// <summary>hook.ui-toggle (Gustave's Overcharge toggle: "toggle.gustave.overcharge").</summary>
    public void OnUiToggle(int seat, string toggleId, bool on) => Call("hook.ui-toggle", g => g.OnToggle(S(seat), toggleId, on));

    /// <summary>hook.bot-decision: an option key for a bot seat (a do.submit-action the core made earlier, or its bot
    /// director's pick among the legal keys), or null to let the random-legal policy choose.</summary>
    public string? OnBotDecision(int seat, Decision d)
    {
        if (St.Mod.Extra.Remove("submit:" + seat, out var submitted) && d.Find(submitted) != null) return submitted;
        var keys = d.Options.Select(o => o.Key).ToList();
        return Ask<string?>("hook.bot-decision", g => g.OnBotDecision(S(seat), keys), null);
    }

    /// <summary>hook.game-over</summary>
    public void OnGameOver(IReadOnlyList<int> winners) => Call("hook.game-over", g => g.OnGameOver(winners.Select(S).ToList()));

    /// <summary>hook.save: the core's state text, stored in State.Mod.CoreState (the bridge's own record goes in Extra).</summary>
    public string OnSave()
    {
        _data.Barks = BarkMemory.Take(Ctx.Barks);
        St.Mod.Extra["bridge"] = JsonSerializer.Serialize(_data, SimJson.Options);
        string text = "";
        _hooks.Safe("hook.save", () => text = _game.OnSave());
        Canonical();
        return text;
    }

    /// <summary>
    /// The CLI reloads the save on every command, so a reloaded game must play on exactly like the one kept in memory. A .NET
    /// Dictionary refills the slot of a removed key, so after a removal its order is no longer the order the same
    /// dictionary has when read back from JSON (and rules that walk it, such as ruler ties, could then differ). At each save
    /// the dictionaries that lose keys during play (a clearing's warriors, banners, Extra) are rebuilt in key order.
    /// </summary>
    private void Canonical()
    {
        foreach (var cs in St.Clearings.Values)
            cs.Warriors = cs.Warriors.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        St.Mod.Banners = St.Mod.Banners.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        St.Mod.Extra = St.Mod.Extra.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>hook.load: called right after a save is read, with State.Mod.CoreState. A reload is not a game event: it
    /// logs nothing, keeps every countdown banner for its owner's next Birdsong, and restores the bubble cooldowns.</summary>
    public void OnLoad(string saved)
    {
        _selectShown = true;
        var banners = St.Mod.Banners.ToList();
        _board.Quiet = true;
        try { _hooks.RunAlone("hook.load", g => g.OnLoad(saved)); }
        finally { _board.Quiet = false; }
        if (!Ctx.E33Mode) return;
        // The CLI reloads on every command, so a reload must not change the game: the core ran its load-time re-checks
        // above (on the very board it saved), and now gets back exactly the state it saved, bot plans included.
        _hooks.Safe("hook.load", () => Ctx.State.Load(ModState.FromText(saved)));
        var announced = new Dictionary<SeatId, string>();
        foreach (var s in Ctx.State.GlobalCountdowns)
        {
            var winId = banners.FirstOrDefault(kv => kv.Key == s.Value).Value ?? Ctx.WinOf(s)?.WinId;
            if (winId == null) continue;
            announced[s] = winId;
            St.Mod.Banners[s.Value] = winId;
        }
        Ctx.Countdown.Restore(announced);
        _data.Barks?.Restore(Ctx.Barks);
        _data.SkipSeen(Ctx);
    }

    // ---------------------------------------------------------------- tabletop reads

    /// <summary>The home choices Root 33 forces on a seat (clearings for a faction home, forests for a Vagabond), or null when free.</summary>
    public IReadOnlyList<int>? ForcedHome(int seat)
    {
        if (IsVagabond(seat))
            return St.Mod.StartForestRestrictions.TryGetValue(seat, out var f) && f.Count > 0 ? f : null;
        return St.Mod.HomeRestrictions.TryGetValue(seat, out var h) && h.Count > 0 ? h : null;
    }

    /// <summary>True when the core declared a themed win (winners in State.Mod.DeclaredWinners); text says who and how.</summary>
    public bool IsGameOver(out string text)
    {
        text = "";
        if (St.Mod.DeclaredWinners.Count == 0) return false;
        text = WinText();
        return true;
    }

    /// <summary>The character select screen as text (works without a game): each base-game character as a player reads
    /// its card (name, pitch, how it wins, which Root faction it plays), then how to seat them here.</summary>
    public static string SelectCards(string spoiler) =>
        ModCards.SelectCards(SimHost.SpoilersOf(spoiler), ModText.BaseCharacters) +
        "Seat 2-4 of them, at most two Vagabonds: new --seats \"gustave:human,paintress:human,sciel:human,esquie:human\"";

    /// <summary>The seat's character card: who it is, its themed win, and its Root 33 rule changes in plain words.</summary>
    public string CharacterCard(int seat) => ModCards.CharacterCard(Ctx, Host, S(seat));

    // ---------------------------------------------------------------- the mod's view (Root33.Host.ModView)

    /// <summary>What the mod shows this seat: act banner, countdown banners, the public Goal Tracker with each character's
    /// tracker or gauge, the story so far and the speech bubbles since its last turn. Only public information.</summary>
    public string ModView(int seat) => _view.Text(S(seat));

    /// <summary>The mod's public view for a spectator (the live watch page): like ModView, with the last 12 speech bubbles.</summary>
    public string SpectatorModView() => _view.Text(null);

    // ---------------------------------------------------------------- the bridge's saved record

    private BridgeData ReadData()
    {
        if (St.Mod.Extra.TryGetValue("bridge", out var json) && !string.IsNullOrEmpty(json))
        {
            try { return JsonSerializer.Deserialize<BridgeData>(json, SimJson.Options) ?? new BridgeData(); }
            catch (JsonException) { }
        }
        return new BridgeData();
    }
}

/// <summary>What the bridge keeps across reloads (State.Mod.Extra["bridge"]): the mod's feed (Root33.Host.ModFeed: bubbles,
/// each seat's 'seen up to' mark, story, last act banner) and the bark director's cooldowns.</summary>
public sealed class BridgeData : ModFeed
{
    public BarkMemory? Barks { get; set; }
}

/// <summary>
/// The speech-bubble director's cooldown memory. The core keeps it out of ModState (a loaded game starts with every
/// cooldown cleared); the tabletop reloads on every CLI command, so it carries the memory over by reading and restoring
/// the director's private fields. If the core renames them, the memory is simply not carried over.
/// </summary>
public sealed class BarkMemory
{
    public Dictionary<int, int> Turns { get; set; } = new();
    public List<TriggerMark> ByTrigger { get; set; } = new();
    public Dictionary<int, int> LastAny { get; set; } = new();
    public Dictionary<string, int> LineUsed { get; set; } = new();
    public int Said { get; set; }

    public sealed class TriggerMark { public int Seat { get; set; } public string Trigger { get; set; } = ""; public int Turn { get; set; } }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T? Field<T>(BarkDirector d, string name) where T : class => typeof(BarkDirector).GetField(name, Private)?.GetValue(d) as T;

    public static BarkMemory Take(BarkDirector d)
    {
        var m = new BarkMemory { Said = d.Said.Count };
        if (Field<Dictionary<SeatId, int>>(d, "_turns") is { } t) foreach (var kv in t) m.Turns[kv.Key.Value] = kv.Value;
        if (Field<Dictionary<SeatId, int>>(d, "_lastAny") is { } a) foreach (var kv in a) m.LastAny[kv.Key.Value] = kv.Value;
        if (Field<Dictionary<string, int>>(d, "_lineUsed") is { } u) foreach (var kv in u) m.LineUsed[kv.Key] = kv.Value;
        if (Field<Dictionary<(SeatId, string), int>>(d, "_lastByTrigger") is { } b)
            foreach (var kv in b.OrderBy(kv => kv.Key.Item1.Value).ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal))
                m.ByTrigger.Add(new TriggerMark { Seat = kv.Key.Item1.Value, Trigger = kv.Key.Item2, Turn = kv.Value });
        m.Turns = m.Turns.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        m.LastAny = m.LastAny.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        m.LineUsed = m.LineUsed.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
        return m;
    }

    public void Restore(BarkDirector d)
    {
        if (Field<Dictionary<SeatId, int>>(d, "_turns") is { } t) foreach (var kv in Turns) t[new SeatId(kv.Key)] = kv.Value;
        if (Field<Dictionary<SeatId, int>>(d, "_lastAny") is { } a) foreach (var kv in LastAny) a[new SeatId(kv.Key)] = kv.Value;
        if (Field<Dictionary<string, int>>(d, "_lineUsed") is { } u) foreach (var kv in LineUsed) u[kv.Key] = kv.Value;
        if (Field<Dictionary<(SeatId, string), int>>(d, "_lastByTrigger") is { } b)
            foreach (var x in ByTrigger) b[(new SeatId(x.Seat), x.Trigger)] = x.Turn;
        // keep later line-use positions after the restored ones (placeholders never match a seat)
        while (d.Said.Count < Said) d.Said.Add(new BarkRecord(new SeatId(-1), "", ""));
    }
}
