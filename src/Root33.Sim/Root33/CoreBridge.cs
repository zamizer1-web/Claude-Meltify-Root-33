using System.Reflection;
using System.Text;
using System.Text.Json;
using Root33.Core;
using Root33.Core.Characters.Esquie;
using Root33.Core.Characters.Gustave;
using Root33.Core.Characters.Paintress;
using Root33.Core.Characters.Sciel;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Sim;

/// <summary>
/// The seam between the tabletop and the real Root 33 rules in src/Root33.Core (owner: engineer D). The engine creates one
/// CoreBridge per SimGame (new game and every load) and calls one method per row of sheets/game_hooks.json at that
/// moment (method name = the row id in PascalCase: hook.battle-resolved -> OnBattleResolved). Arguments are the
/// tabletop's own (seats 0-based ints, clearings 1-12); the bridge turns them into Root33.Core types and forwards them to
/// a <see cref="Root33Game"/> built over <see cref="SimBoard"/>, exactly as the in-game plugin will from its patches.
///
/// Re-entrancy: the core changes the board through IBoardActions while it runs a hook, and the sim reports those changes
/// back as hooks (a guard warrior placed, VP scored, the game ending). Such nested hooks are queued and delivered right
/// after the hook that caused them, so the core never runs inside itself.
///
/// What the core does not save (speech-bubble cooldowns, the story log, bubbles each seat has not seen yet) the bridge
/// keeps in State.Mod.Extra["bridge"], because every CLI command reloads the game.
/// </summary>
public sealed class CoreBridge
{
    private static readonly Lazy<ModuleRegistry> Registry = new(ModuleRegistry.Default);
    private const int MaxBubbles = 120;

    private readonly SimGame _g;
    private readonly SimBoard _board;
    private readonly Root33Game _game;
    private readonly BridgeData _data;
    private readonly Queue<(string Hook, Action<Root33Game> Call)> _queued = new();
    private readonly HashSet<string> _errorHooks = new();
    private int _depth;
    private int _storySeen;
    private int _warningsSeen;
    private bool _selectShown;

    public CoreBridge(SimGame g)
    {
        _g = g;
        _board = new SimBoard(g) { BubbleShown = Bubble, WinnerDeclared = Declared };
        _game = new Root33Game(_board, _board, Registry.Value, new Capabilities().Unsupported(SimBoard.Unsupported));
        _data = ReadData();
    }

    /// <summary>The core game (tests read its context: act, story log, barks, modules).</summary>
    public Root33Game Game => _game;
    public GameContext Ctx => _game.Context;
    public SimBoard Board => _board;
    /// <summary>Exceptions the core threw (logged once per hook; the game goes on). Tests assert this stays empty.</summary>
    public List<string> Errors { get; } = new();
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

    // ---------------------------------------------------------------- dispatch

    private void Call(string hook, Action<Root33Game> call)
    {
        if (_depth > 0) { _queued.Enqueue((hook, call)); return; }
        _depth++;
        try
        {
            Safe(hook, () => call(_game));
            while (_queued.Count > 0)
            {
                var (h, c) = _queued.Dequeue();
                Safe(h, () => c(_game));
            }
        }
        finally { _depth--; }
        AfterCore();
    }

    private T Ask<T>(string hook, Func<Root33Game, T> ask, T fallback)
    {
        T result = fallback;
        if (_depth > 0) { Safe(hook, () => result = ask(_game)); return result; }
        Call(hook, game => result = ask(game));
        return result;
    }

    private void Safe(string hook, Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            Errors.Add($"{hook}: {e.GetType().Name}: {e.Message}");
            if (_errorHooks.Add(hook)) _g.Log($"(Root 33 core error in {hook}: {e.Message}; the game goes on without it.)");
        }
    }

    /// <summary>After each hook: log the story events, act banners and core notes it produced.</summary>
    private void AfterCore()
    {
        if (!Ctx.E33Mode) return;
        var log = Ctx.StoryLog;
        for (; _storySeen < log.Count; _storySeen++)
        {
            var e = log[_storySeen];
            if (St.SetupDone && Ctx.Act > _data.LastAct) ActBanner();
            _data.Story.Add(new StoryRecord { EventId = e.EventId, Act = e.Act, Round = St.Round });
            _g.Log($"Root 33 story ({ModText.ActTitle(e.Act)}): {ModText.StoryText(e.EventId)}");
        }
        if (St.SetupDone && Ctx.Act > _data.LastAct) ActBanner();
        for (; _warningsSeen < Ctx.Warnings.Count; _warningsSeen++) _g.Log("(Root 33 note: " + Ctx.Warnings[_warningsSeen] + ")");
    }

    private void ActBanner()
    {
        _data.LastAct = Ctx.Act;
        _g.Log($"=== Root 33 · {ModText.ActTitle(Ctx.Act)} === {ModText.ActLine(Ctx.Act)}");
    }

    private void Bubble(int seat, string barkId)
    {
        var n = ++_data.BubbleCount;
        _data.Bubbles.Add(new BubbleRecord { N = n, Seat = seat, BarkId = barkId, Round = St.Round });
        if (_data.Bubbles.Count > MaxBubbles) _data.Bubbles.RemoveAt(0);
        _g.Log($"{Render.Who(_g, seat)} says: \"{ModText.BarkText(barkId)}\"");
    }

    private void Declared(int seat, string winId)
    {
        if (St.GameOver) return;
        _g.EndGame(St.Mod.DeclaredWinners.ToList(), WinText());
    }

    private string WinText()
    {
        var winners = St.Mod.DeclaredWinners;
        if (winners.Count == 0) return "";
        int seat = winners[0];
        var who = Render.Who(_g, seat);
        var partners = winners.Skip(1).Select(s => Render.Who(_g, s)).ToList();
        return $"{who} wins with {ModText.WinTitle(St.Mod.DeclaredWinId)}, the Root 33 themed win of {ModText.SeatTitle(_g, seat)}" +
               (partners.Count > 0 ? $" (with {string.Join(" and ", partners)} in coalition)" : "");
    }

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
        _data.BubbleMark[seat] = _data.BubbleCount;
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
        Safe("hook.save", () => text = _game.OnSave());
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
        _depth++;
        try { Safe("hook.load", () => _game.OnLoad(saved)); }
        finally { _depth--; _board.Quiet = false; }
        _queued.Clear();
        if (!Ctx.E33Mode) return;
        // The CLI reloads on every command, so a reload must not change the game: the core ran its load-time re-checks
        // above (on the very board it saved), and now gets back exactly the state it saved, bot plans included.
        Safe("hook.load", () => Ctx.State.Load(ModState.FromText(saved)));
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
        _storySeen = Ctx.StoryLog.Count;
        _warningsSeen = Ctx.Warnings.Count;
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
    /// its card (name, pitch, how it wins, which Root faction it plays).</summary>
    public static string SelectCards(string spoiler)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ROOT 33 · choose your characters (spoiler mode: {spoiler}). Each character plays a base-game Root faction.");
        foreach (var id in ModText.BaseCharacters)
        {
            if (!Data.Characters.TryGetValue(id, out var c)) continue;
            if (spoiler != "finished" && !c.SafeModeSelectable) continue;
            sb.AppendLine();
            sb.AppendLine($"[{id}] {ModText.CharacterName(id, spoiler)} · plays the {FactionRegistry.EngineName(c.Engine)}");
            sb.AppendLine("  " + c.SummarySafe);
            if (Data.WinConditions.TryGetValue(c.WinCondition, out var w))
            {
                var kind = w.Kind == "quest" ? "quest win" : "countdown win";
                sb.AppendLine($"  How they win: {w.Title} ({kind}). {ModText.Plain(w.PlayerText)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Every character can also win the Root way: 30 VP, or a dominance card.");
        sb.Append("Seat 2-4 of them, at most two Vagabonds: new --seats \"gustave:human,paintress:human,sciel:human,esquie:human\"");
        return sb.ToString();
    }

    /// <summary>The seat's character card: who it is, its themed win, and its Root 33 rule changes in plain words.</summary>
    public string CharacterCard(int seat)
    {
        var id = _g.Seat(seat).Character;
        if (!Data.Characters.TryGetValue(id, out var c)) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"ROOT 33 CHARACTER: {ModText.SeatTitle(_g, seat)}, playing the {FactionRegistry.EngineName(c.Engine)}.");
        sb.AppendLine("  " + c.SummarySafe);
        if (Data.WinConditions.TryGetValue(c.WinCondition, out var w))
        {
            sb.AppendLine($"THEMED WIN · {w.Title}: {ModText.Plain(w.PlayerText)}");
            var counter = ModText.Plain(w.Counterplay);
            if (counter.Length > 0) sb.AppendLine($"  How the table stops it: {counter}");
        }
        sb.AppendLine("You can also win the Root way: 30 VP, or a dominance card.");
        var tweaks = Data.RuleTweaks.Values.Where(t => t.Character == id && RuleOn(t.Id)).ToList();
        if (tweaks.Count > 0)
        {
            sb.AppendLine("ROOT 33 RULE CHANGES:");
            foreach (var t in tweaks) sb.AppendLine($"  - {t.Name}: {ModText.Plain(t.Rule)}");
        }
        return sb.ToString().TrimEnd();
    }

    private bool RuleOn(string tweakId)
    {
        try { return Ctx.E33Mode ? Ctx.RuleOn(tweakId) : Data.RuleTweaks[tweakId].Version != "v1.1"; }
        catch { return Data.RuleTweaks[tweakId].Version != "v1.1"; }
    }

    // ---------------------------------------------------------------- the mod's view

    /// <summary>What the mod shows this seat: act banner, countdown banners, the public Goal Tracker with each character's
    /// tracker or gauge, the story so far and the speech bubbles since its last turn. Only public information.</summary>
    public string ModView(int seat)
    {
        if (!Ctx.E33Mode) return "";
        var sb = new StringBuilder();
        try { WriteModView(sb, seat); }
        catch (Exception e) { sb.AppendLine($"(Root 33 view unavailable: {e.Message})"); }
        return sb.ToString().TrimEnd();
    }

    private void WriteModView(StringBuilder sb, int seat)
    {
        sb.AppendLine($"ROOT 33 · {ModText.ActTitle(Ctx.Act)} · Monolith Number {Ctx.MonolithNumber} (33 minus the top score of players not on a dominance card)");
        sb.AppendLine("  " + ModText.ActLine(Ctx.Act));
        foreach (var kv in St.Mod.Banners.OrderBy(k => k.Key))
            sb.AppendLine("  " + ModText.BannerAnnounced(_g, kv.Key, kv.Value));

        sb.AppendLine("GOAL TRACKER (public)");
        foreach (var s in _g.TurnOrder())
        {
            var ch = Ctx.CharacterOf(S(s));
            if (ch == null) continue;
            sb.AppendLine($"  {Render.Who(_g, s)} · {GoalLine(s, ch)}");
            var detail = Tracker(s, ch);
            if (detail.Length > 0) sb.AppendLine("      " + detail);
        }

        if (_data.Story.Count > 0)
        {
            sb.AppendLine("STORY SO FAR");
            foreach (var e in _data.Story.TakeLast(6)) sb.AppendLine($"  {ModText.ActTitle(e.Act)}: {ModText.StoryText(e.EventId)}");
        }

        var mark = _data.BubbleMark.TryGetValue(seat, out var m) ? m : 0;
        var bubbles = _data.Bubbles.Where(b => b.N > mark).ToList();
        if (bubbles.Count > 0)
        {
            sb.AppendLine("SPEECH BUBBLES SINCE YOUR LAST TURN");
            foreach (var b in bubbles) sb.AppendLine($"  r{b.Round} {Render.Who(_g, b.Seat)}: \"{ModText.BarkText(b.BarkId)}\"");
        }
    }

    /// <summary>One Goal Tracker row: the win's title and how far the seat is (gates, then the themed test's missing steps).</summary>
    private string GoalLine(int seat, string ch)
    {
        var win = Ctx.WinOf(S(seat));
        if (win == null || !Data.WinConditions.TryGetValue(win.WinId, out var row)) return "no themed win";
        var parts = new List<string>();
        if (Ctx.Act < row.ActMin) parts.Add($"locked until Act {ModText.Roman(row.ActMin)}");
        if (row.VpMin > 0 && _g.Seat(seat).Vp < row.VpMin) parts.Add($"needs {row.VpMin}+ VP");
        if (row.OffWithDominance && _g.Seat(seat).Dominance != null) parts.Add("off while a dominance card is active");
        if (row.OffInCoalition && _g.Seat(seat).CoalitionWith != null) parts.Add("off while in a coalition");
        if (win is IGoalProgress p)
        {
            var missing = p.UnitsMissing(Ctx, S(seat));
            parts.Add(missing switch { 0 => "themed test met", 1 => "ONE STEP AWAY", _ => $"{missing} steps missing" });
        }
        if (St.Mod.Banners.ContainsKey(seat)) parts.Add("COUNTDOWN ANNOUNCED");
        return $"{row.Title} ({(row.Kind == "quest" ? "quest" : "countdown")}): {string.Join(", ", parts)}";
    }

    private string Where(ClearingId c) => _g.ClearingName(c.Value);
    private string Where(int c) => _g.ClearingName(c);
    private static string Suits(IEnumerable<Suit> suits)
    {
        var l = suits.Where(s => s != Suit.Bird).OrderBy(s => s).Select(s => s.ToString().ToLowerInvariant()).ToList();
        return l.Count == 0 ? "none" : string.Join(", ", l);
    }

    /// <summary>The character's own public tracker or gauge (overlays.json: ui.gustave.road/arm, ui.paintress.portrait-tracker,
    /// ui.sciel.gauges, ui.verso.rank, ui.maelle.family/stance, ui.esquie.rock-ring/rocks).</summary>
    private string Tracker(int seat, string ch)
    {
        var st = Ctx.State;
        var me = S(seat);
        var monolith = Ctx.Role(Ids.MapRoles.Monolith);
        var lumiere = Ctx.Role(Ids.MapRoles.Lumiere);
        switch (ch)
        {
            case "gustave":
            {
                var chain = GustaveChain.Chain(_board, st, me, lumiere);
                var beach = GustaveChain.ChainBeachhead(_board, me, chain, monolith);
                var flags = st.GustaveFlags.OrderBy(c => c.Value).Select(c => "c" + c.Value + (_board.Ruler(c) == me ? "" : " (faded)"));
                return $"Arm {st.GustaveArm}/{Tuning.GustaveArmMax}{(st.GustaveOverchargeArmed ? ", Overcharge armed" : "")} · " +
                       $"flags: {(st.GustaveFlags.Count == 0 ? "none" : string.Join(", ", flags))} · " +
                       $"Chain from Lumière: {(chain.Count == 0 ? "none" : string.Join(", ", chain.OrderBy(c => c.Value).Select(c => "c" + c.Value)))} " +
                       $"covering {Suits(GustaveChain.ChainSuits(_board, chain))} · Beachhead: {(beach is ClearingId b ? Where(b) : "not reached")}";
            }
            case "paintress":
            {
                var portraits = Spoilers == SpoilerMode.Finished
                    ? PaintressPortraits.LabelledPortraits(_board, st, me, monolith).Select(kv => $"{kv.Key} at {Where(kv.Value)}")
                    : PaintressPortraits.LivingPortraits(_board, st, me, monolith).Select(c => Where(c));
                var missing = PaintressPortraits.MissingSuits(_board, st, me, monolith);
                return $"Portraits {3 - missing.Count}/3: {string.Join("; ", portraits.DefaultIfEmpty("none"))} · missing: {Suits(missing)} · " +
                       $"holds the Monolith with an easel: {(PaintressPortraits.HoldsMonolith(_board, me, monolith) ? "yes" : "no")}";
            }
            case "sciel":
            {
                var sanct = ScielBoard.Sanctuaries(_board, me);
                return $"Sun: {sanct.Count}/5 Sanctuaries ({Suits(ScielBoard.SuitsCovered(_board, sanct))}) · Moon: {ScielBoard.Aquafarms(_board, me)} Aquafarm(s) on the map";
            }
            case "verso":
            {
                var rank = "DCBAS"[Math.Clamp(st.VersoRank, 0, 4)];
                var inMono = monolith.HasValue && _g.Seat(seat).PawnClearing == monolith.Value.Value;
                return $"Perfection rank {rank} · pawn in the Monolith: {(inMono ? "yes" : "no")}";
            }
            case "maelle":
            {
                var rels = _g.TurnOrder().Where(o => o != seat && !IsVagabond(o))
                    .Select(o => $"{Render.Who(_g, o)} {_board.RelationshipOf(me, S(o))}{(st.MaelleFamily != null && st.MaelleFamily.Contains(S(o)) ? " (her chosen family)" : "")}");
                var stance = string.IsNullOrEmpty(st.MaelleStance) ? "" : $" · stance {st.MaelleStance}";
                return $"family: {string.Join("; ", rels.DefaultIfEmpty("no non-Vagabond faction"))}{stance}";
            }
            case "esquie":
            {
                var names = new[] { "Florrie", "Dorrie", "Soarrie", "Urrie" };
                var rocks = st.EsquieRocks ?? Array.Empty<RockState>();
                int home = rocks.Count(r => r != null && r.Status == RockStatus.Held);
                var parts = new List<string>();
                for (int i = 0; i < rocks.Length && i < names.Length; i++) parts.Add($"{names[i]} {RockText(rocks[i], i)}");
                return $"rocks home {home}/4 · {string.Join("; ", parts)}";
            }
            default:
                return "";
        }
    }

    private string RockText(RockState? r, int i)
    {
        if (r == null) return "unknown";
        var at = r.Clearing is ClearingId c ? Where(c) : "?";
        return r.Status switch
        {
            RockStatus.NotSpawned => i switch { 1 or 2 => "appears in Act II", 3 => "appears in Act III", _ => "not on the map" },
            RockStatus.Hidden => $"hidden in the ruin of {at}",
            RockStatus.Guarded => $"at {at}, guarded by {r.GuardsLeft} {(r.HostSeat is SeatId h ? Render.Who(_g, h.Value) : "host")} warrior{(r.GuardsLeft == 1 ? "" : "s")}",
            RockStatus.Loose => $"loose at {at} (Esquie picks it up after any of his actions there)",
            RockStatus.HeldByRuler => $"held by whoever rules {at}",
            RockStatus.WithVerso => "on Verso's card",
            RockStatus.Held => "home",
            _ => r.Status,
        };
    }

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

/// <summary>What the bridge keeps across reloads (State.Mod.Extra["bridge"]).</summary>
public sealed class BridgeData
{
    public List<BubbleRecord> Bubbles { get; set; } = new();
    public int BubbleCount { get; set; }
    /// <summary>For each seat, the last bubble number it has seen (set at the end of its turn).</summary>
    public Dictionary<int, int> BubbleMark { get; set; } = new();
    public List<StoryRecord> Story { get; set; } = new();
    /// <summary>The last act whose banner was logged.</summary>
    public int LastAct { get; set; }
    public BarkMemory? Barks { get; set; }
}

public sealed class BubbleRecord
{
    public int N { get; set; }
    public int Seat { get; set; }
    public string BarkId { get; set; } = "";
    public int Round { get; set; }
}

public sealed class StoryRecord
{
    public string EventId { get; set; } = "";
    public int Act { get; set; }
    public int Round { get; set; }
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
