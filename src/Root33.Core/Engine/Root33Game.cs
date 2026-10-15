using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// The core's entry point: the plugin calls these <see cref="IGameHooks"/> members from its Harmony patches, and every
    /// hook goes to the shared systems (roles, countdowns, act clock, story events, save/load) and to the character modules
    /// of the seated characters, in <see cref="ICharacterModule.Order"/>. Nothing happens in a plain Root game
    /// (state.global.e33-mode false): only a game created from the mod's character select is an E33 game.
    ///
    /// Ordering the sheets fix (game_hooks.json):
    ///   hook.birdsong-start: countdown confirm for that seat first, then pending VP, then modules, then the bot plans its turn.
    ///   hook.evening-start:  pending draws, then modules.
    ///   hook.evening-end:    modules (Gustave's flags, then Esquie's hand-off and early Urrie, then the rest), then the seat's
    ///                        themed-win test (countdown announce or quest win).
    ///   hook.turn-end:       modules (per-turn resets, Monolith Barrier), then the act clock (at most one step); a new act
    ///                        raises hook.act-change: act banner, that act's story events, then modules' OnActChanged.
    ///   hook.load:           state restored, global onLoad rules, modules' OnStateLoaded, then countdowns re-checked.
    /// </summary>
    public sealed class Root33Game : IGameHooks
    {
        private readonly ModuleRegistry _registry;
        private readonly List<ICharacterModule> _modules = new List<ICharacterModule>();
        private readonly Dictionary<SeatId, string> _selected = new Dictionary<SeatId, string>();
        private readonly Dictionary<SeatId, string> _pendingSeats = new Dictionary<SeatId, string>();
        private bool _pendingE33;
        private bool _roundStarted;
        private int _actEventsFiredUpTo;

        public Root33Game(IBoardView board, IBoardActions actions, ModuleRegistry registry = null, Capabilities capabilities = null)
        {
            Context = new GameContext(board, actions, capabilities);
            _registry = registry ?? ModuleRegistry.Default();
            Context.WinLookup = s => ModuleOf(s)?.Win;
        }

        public GameContext Context { get; }
        private IBoardView Board => Context.Board;
        private IBoardActions Actions => Context.Actions;
        private ModState State => Context.State;

        // ------------------------------------------------------------ player profile (outside any one game)

        /// <summary>Settings for the next game (sys.settings). Spoiler mode and difficulty are copied at hook.game-start.</summary>
        public GameOptions NextGameOptions { get; } = new GameOptions();
        /// <summary>The first-launch answer (opt.spoiler-mode), or null before it was asked.</summary>
        public SpoilerMode? ProfileSpoilerMode { get; set; }
        /// <summary>Campaign progress kept in the player's profile (state.global.campaign-game / campaign-journal).</summary>
        public int ProfileCampaignGame { get; set; }
        public bool ProfileCampaignJournal { get; set; }
        /// <summary>Engines the player owns, from hook.character-select.</summary>
        public IReadOnlyList<EngineId> OwnedEngines { get; private set; } = new EngineId[0];

        /// <summary>The modules bound for the current game, in dispatch order.</summary>
        public IReadOnlyList<ICharacterModule> Modules => _modules;

        public ICharacterModule ModuleOf(SeatId seat) => _modules.FirstOrDefault(m => m.Seat == seat);
        public ICharacterModule ModuleOf(string characterId) => _modules.FirstOrDefault(m => m.CharacterId == characterId);

        /// <summary>The mod's character select: the character a seat will play (before hook.seat-assigned).</summary>
        public void SelectCharacter(SeatId seat, string characterId)
        {
            if (!Data.Characters.ContainsKey(characterId)) throw new ArgumentException("no characters row " + characterId);
            _selected[seat] = characterId;
        }

        /// <summary>Characters the player may pick: engine owned, and (in safe mode) safeModeSelectable.</summary>
        public IReadOnlyList<string> SelectableCharacters(SpoilerMode mode) =>
            Data.Characters.Values
                .Where(c => ModuleRegistry.EngineIdOf(c.Engine) is EngineId e && OwnedEngines.Contains(e))
                .Where(c => mode == SpoilerMode.Finished || c.SafeModeSelectable)
                .OrderBy(c => c.BuildOrder).Select(c => c.Id).ToList();

        private bool Active => State.GlobalE33Mode && !Context.GameOver;

        private void Each(Action<ICharacterModule> handler)
        {
            foreach (var m in _modules.ToArray()) handler(m);
        }

        private void BindModules()
        {
            _modules.Clear();
            foreach (var kv in State.GlobalSeatCharacters.OrderBy(kv => kv.Key.Value))
            {
                var m = _registry.Create(kv.Value);
                if (m == null) continue;
                m.Bind(Context, kv.Key);
                _modules.Add(m);
            }
            _modules.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seat.Value.CompareTo(b.Seat.Value));
        }

        // ------------------------------------------------------------ menus and game creation

        public void OnTitleScreenShown(bool firstLaunch)
        {
            Actions.ShowOverlay(Ids.Overlays.GlobalTitle, null, null, null);
            if (firstLaunch || !ProfileSpoilerMode.HasValue) Actions.ShowOverlay(Ids.Overlays.GlobalSpoilerPrompt, null, null, null);
        }

        /// <summary>The first-launch spoiler answer; applies from the next game.</summary>
        public void AnswerSpoilerPrompt(SpoilerMode mode)
        {
            ProfileSpoilerMode = mode;
            NextGameOptions.Spoilers = mode;
            Actions.HideOverlay(Ids.Overlays.GlobalSpoilerPrompt, null, null, null);
        }

        public void OnCharacterSelectShown(IReadOnlyList<EngineId> ownedEngines, int maxSeats)
        {
            _pendingE33 = true;
            _selected.Clear();
            _pendingSeats.Clear();
            OwnedEngines = ownedEngines.ToArray();
            Actions.HideClockworkBots();
            Actions.ShowOverlay(Ids.Overlays.GlobalCharacterSelect, null, null, null);
        }

        public void OnSeatAssigned(SeatId seat, EngineId engine, bool isBot)
        {
            if (!_pendingE33) return;
            string ch;
            if (!_selected.TryGetValue(seat, out ch) || ModuleRegistry.EngineIdOf(Data.Characters[ch].Engine) != engine)
                ch = ModuleRegistry.CharacterForEngine(engine);
            if (ch == null) _pendingSeats.Remove(seat);
            else _pendingSeats[seat] = ch;
        }

        public void OnGameStart(IReadOnlyList<SeatId> turnOrder, Difficulty difficulty, SpoilerMode spoilers)
        {
            var e33 = _pendingE33;
            _pendingE33 = false;
            ResetGame();
            Context.TurnOrder = turnOrder.ToArray();
            State.GlobalE33Mode = e33;
            if (!e33) return;
            State.GlobalDifficulty = difficulty;
            State.GlobalSpoilerMode = spoilers;
            State.GlobalCampaignGame = NextGameOptions.Campaign ? Math.Max(1, ProfileCampaignGame) : 0;
            State.GlobalCampaignJournal = State.GlobalCampaignGame > 0 && ProfileCampaignJournal;
            Context.Options = new GameOptions
            {
                Spoilers = spoilers, Difficulty = difficulty, FaithfulGustave = NextGameOptions.FaithfulGustave,
                Campaign = NextGameOptions.Campaign, MonolithBarrier = NextGameOptions.MonolithBarrier,
            };
            foreach (var kv in _pendingSeats) State.GlobalSeatCharacters[kv.Key] = kv.Value;
            _pendingSeats.Clear();
            BindModules();
            Each(m => m.OnGameStart());
            ApplySeatTitles();
        }

        /// <summary>do.set-seat-title for every seated character: its spoiler-tiered name, or Faithful Gustave's retitle.</summary>
        private void ApplySeatTitles()
        {
            foreach (var kv in State.GlobalSeatCharacters.OrderBy(kv => kv.Key.Value))
            {
                var title = SpoilerText.SeatTitle(Context, kv.Key);
                if (title != null) Actions.SetSeatTitle(kv.Key, title);
            }
        }

        /// <summary>Seats playing a character, in turn order.</summary>
        private List<SeatId> SeatedInTurnOrder()
        {
            var order = Context.TurnOrder.Count > 0 ? Context.TurnOrder : Board.Seats();
            var list = order.Where(s => Context.CharacterOf(s) != null).ToList();
            foreach (var s in State.GlobalSeatCharacters.Keys.OrderBy(s => s.Value)) if (!list.Contains(s)) list.Add(s);
            return list;
        }

        private void ResetGame()
        {
            State.Reset();
            Context.Countdown.Reset();
            Context.ActClock = GameContext.NewActClock();
            Context.GameOver = false;
            Context.DeclaredWins.Clear();
            Context.StoryLog.Clear();
            Context.Warnings.Clear();
            Context.Barks.Reset();
            _modules.Clear();
            _roundStarted = false;
            _actEventsFiredUpTo = 0;
        }

        // ------------------------------------------------------------ map and setup

        public void OnMapLoaded(string mapName)
        {
            if (!Active) return;
            if (mapName == AutumnMap.MapName || !Context.IsSeated(CharacterIds.Gustave)) AssignRoles(null);
            Each(m => m.OnMapLoaded(mapName));
        }

        private void AssignRoles(ClearingId? gustaveKeepCorner)
        {
            State.SetRoles(RoleMap.ForMap(Board, gustaveKeepCorner));
            Each(m => m.OnRolesAssigned());
        }

        /// <summary>The corner holding Gustave's keep, if he is seated and it is on the map.</summary>
        private ClearingId? GustaveKeepCorner()
        {
            var g = Context.SeatOf(CharacterIds.Gustave);
            if (!g.HasValue) return null;
            foreach (var c in Board.Corners().OrderBy(c => c.Value))
                if (Board.TokensOfType(g.Value, c, PieceTypes.Keep) + Board.BuildingsOfType(g.Value, c, PieceTypes.Keep) > 0) return c;
            return null;
        }

        public void OnSetupChoice(SeatId seat)
        {
            if (!Active) return;
            Each(m => m.OnSetupChoice(seat));
        }

        public void OnSetupComplete()
        {
            if (!Active) return;
            if (!Context.RolesAssigned) AssignRoles(GustaveKeepCorner());
            if (State.GlobalCampaignGame > 0 && State.GlobalCampaignJournal)
            {
                foreach (var s in Board.Seats().Where(s => !Board.IsBot(s))) Context.DrawCards(s, Tuning.CampaignJournalCards);
                State.GlobalCampaignJournal = false;
                ProfileCampaignJournal = false;
            }
            Actions.ShowOverlay(Ids.Overlays.GlobalActBanner, null, null, null);
            FireActEvents(1);
            _actEventsFiredUpTo = 1;
            Each(m => m.OnSetupComplete());
            if (Context.GameOver) return;
            // trigger.game-start: each seated character may say one opening line, the human's character first.
            var seated = SeatedInTurnOrder();
            Context.Barks.SayEach(seated.Where(s => !Board.IsBot(s)).Concat(seated.Where(s => Board.IsBot(s))), Ids.BarkTriggers.GameStart);
        }

        // ------------------------------------------------------------ turn flow

        public void OnRoundStart(int round)
        {
            if (!Active) return;
            var r = Board.Round() > 0 ? Board.Round() : round > 0 ? round : _roundStarted ? State.GlobalRound + 1 : 1;
            State.GlobalRound = Math.Max(1, r);
            _roundStarted = true;
            Each(m => m.OnRoundStart(State.GlobalRound));
            if (Context.GameOver) return;
            // trigger.canvas-truth (Act III only), else trigger.idle: one seated character may speak.
            if (Context.Barks.SayOne(SeatedInTurnOrder(), Ids.BarkTriggers.CanvasTruth) == null)
                Context.Barks.SayOne(SeatedInTurnOrder(), Ids.BarkTriggers.Idle);
        }

        public void OnBirdsongStart(SeatId seat)
        {
            if (!Active) return;
            var module = ModuleOf(seat);
            var mark = Context.Barks.Mark;
            var outcome = WinRules.StartOfBirdsong(Context, seat, module?.Win);
            if (Context.GameOver) return;
            if (outcome == CountdownOutcome.Cleared) Context.Barks.Say(seat, Ids.BarkTriggers.CountdownStopped);
            if (State.GlobalPendingVp.TryGetValue(seat, out var vp))
            {
                State.GlobalPendingVp.Remove(seat);
                if (vp > 0) Actions.ScoreVp(seat, vp);
            }
            Each(m => m.OnBirdsongStart(seat));
            if (Context.GameOver) return;
            // trigger.goal-move belongs to each bot: only its director knows whether it has a goal move to name, so its
            // module says it from OnBirdsongStart (Ctx.Barks.Say(seat, Ids.BarkTriggers.GoalMove)).
            if (module?.Bot != null && Board.IsBot(seat)) module.Bot.PlanTurn(new BotView(Context, seat), State);
            // trigger.turn-start: only when nothing more specific was said in this Birdsong.
            if (!Context.Barks.SpokeSince(seat, mark)) Context.Barks.Say(seat, Ids.BarkTriggers.TurnStart);
        }

        public void OnEveningStart(SeatId seat)
        {
            if (!Active) return;
            if (State.GlobalPendingDraws.TryGetValue(seat, out var n))
            {
                State.GlobalPendingDraws.Remove(seat);
                if (n > 0) Actions.DrawCards(seat, n);
            }
            Each(m => m.OnEveningStart(seat));
        }

        public void OnEveningEnd(SeatId seat)
        {
            if (!Active) return;
            var bannerBefore = State.GlobalCountdowns.Contains(seat);
            Each(m => m.OnEveningEnd(seat));
            if (Context.GameOver) return;
            WinRules.EndOfTurn(Context, seat, ModuleOf(seat)?.Win);
            if (Context.GameOver) return;
            if (!bannerBefore && State.GlobalCountdowns.Contains(seat)) Context.Barks.Say(seat, Ids.BarkTriggers.CountdownAnnounced);
            // trigger.rival-countdown: the spoiler layer, voiced by each bot it would send against this seat.
            foreach (var s in SeatedInTurnOrder())
                if (s != seat && Board.IsBot(s) && SpoilerLayer.Threatens(new BotView(Context, s), seat))
                    Context.Barks.Say(s, Ids.BarkTriggers.RivalCountdown);
        }

        public void OnTurnEnd(SeatId seat)
        {
            if (!Active) return;
            Context.Barks.TurnEnded(seat);
            // tweak.white-haired-man.monolith-barrier: the Barrier comes down once both Axons are ruled at the end of a turn.
            Characters.WhiteHairedMan.WhiteHairedManTweaks.BarrierTurnEnd(Context);
            Each(m => m.OnTurnEnd(seat));
            if (Context.GameOver) return;
            if (Context.ActClock.Step(Context.HighestVp(), Context.CompletedRoundsAfterTurnOf(seat)))
            {
                State.GlobalAct = Context.ActClock.Act;
                OnActChanged(State.GlobalAct);
            }
        }

        /// <summary>hook.act-change, raised by <see cref="OnTurnEnd"/>. Each act's events fire once; a call for an act that
        /// already fired (or an older one) does nothing.</summary>
        public void OnActChanged(int act)
        {
            if (!Active || act <= _actEventsFiredUpTo) return;
            if (State.GlobalAct < act)
            {
                Context.ActClock.Restore(act);
                State.GlobalAct = Context.ActClock.Act;
            }
            _actEventsFiredUpTo = act;
            Actions.ShowOverlay(Ids.Overlays.GlobalActBanner, null, null, null);
            FireActEvents(act);
            Each(m => m.OnActChanged(act));
            if (Context.GameOver) return;
            ApplySeatTitles();
            var trigger = act == 2 ? Ids.BarkTriggers.Act2Start : act == 3 ? Ids.BarkTriggers.Act3Start : null;
            if (trigger != null) Context.Barks.SayEach(SeatedInTurnOrder(), trigger);
        }

        /// <summary>The act's story events in acts.json order, each only when its spoiler tier allows and its owner (the
        /// spawn's owner, for spawn and tag events) is seated. Owned events go to the owner's module; the rest to every module.</summary>
        private void FireActEvents(int act)
        {
            var row = Data.Acts.Values.FirstOrDefault(a => a.Number == act);
            if (row == null) return;
            foreach (var id in row.Events)
            {
                if (!Data.StoryEvents.TryGetValue(id, out var e)) continue;
                if (!StoryEvents.TierAllows(e, Context.Spoilers)) continue;
                var owner = StoryEvents.OwnerOf(e);
                if (owner != null && !Context.IsSeated(owner)) continue;
                Context.StoryLog.Add(new StoryEventRecord(id, act, owner));
                SharedStoryEffects(e);
                if (owner != null) ModuleOf(owner)?.OnStoryEvent(e);
                else Each(m => m.OnStoryEvent(e));
            }
        }

        /// <summary>What a story event shows that no module owns: every vignette plays in ui.global.vignette (the plugin reads
        /// which one from <see cref="GameContext.StoryLog"/>); the Landing puts the Monolith Number on the Monolith; the Fall
        /// marks Lumière as the final battlefield. None of them changes VP, charge, rank or relationships.</summary>
        private void SharedStoryEffects(StoryEventDef e)
        {
            if (e.Kind == "vignette") Actions.ShowOverlay(Ids.Overlays.GlobalVignette, null, null, null);
            if (e.Id == Ids.StoryEvents.Act1Landing)
                Actions.ShowOverlay(Ids.Overlays.GlobalMonolithNumber, null, Context.Role(Ids.MapRoles.Monolith), null);
            if (e.Id == Ids.StoryEvents.Act3Fall)
                Actions.ShowOverlay(Ids.Overlays.GlobalFinalBattlefield, null, Context.Role(Ids.MapRoles.Lumiere), null);
        }

        // ------------------------------------------------------------ actions, pieces, battles

        public void OnActionResolved(SeatId seat) { if (Active) Each(m => m.OnActionResolved(seat)); }

        public void OnPieceMoved(SeatId owner, PieceKind kind, ClearingId? from, ClearingId? to, int count)
        {
            if (Active) Each(m => m.OnPieceMoved(owner, kind, from, to, count));
        }

        public void OnPiecePlaced(SeatId owner, PieceKind kind, string type, ClearingId c)
        {
            if (!Active) return;
            if (!Context.RolesAssigned && type == PieceTypes.Keep && Context.SeatOf(CharacterIds.Gustave) == owner) AssignRoles(c);
            Each(m => m.OnPiecePlaced(owner, kind, type, c));
        }

        public void OnPieceRemoved(SeatId? remover, RemovedPiece piece, ClearingId c)
        {
            if (Active) Each(m => m.OnPieceRemoved(remover, piece, c));
        }

        public bool CanStartBattle(SeatId attacker, SeatId defender, ClearingId c, bool strike)
        {
            if (!Active) return true;
            // tweak.white-haired-man.monolith-barrier: a table rule (opt.monolith-barrier), whether or not he is seated.
            var ok = Characters.WhiteHairedMan.WhiteHairedManTweaks.BarrierAllows(Context, c, strike);
            foreach (var m in _modules.ToArray()) ok &= m.CanStartBattle(attacker, defender, c, strike);
            return ok;
        }

        public void OnBattleStarting(SeatId attacker, SeatId defender, ClearingId c)
        {
            if (!Active) return;
            Each(m => m.OnBattleStarting(attacker, defender, c));
            if (!Context.GameOver) Context.Barks.Say(defender, Ids.BarkTriggers.Attacked);
        }

        public void OnBattleResolved(BattleResult r)
        {
            if (!Active) return;
            Each(m => m.OnBattleResolved(r));
            if (!Context.GameOver && r.Removed.Any(p => p.Owner != r.Attacker)) Context.Barks.Say(r.Attacker, Ids.BarkTriggers.BattleWon);
        }

        public void OnExploreResolved(SeatId vagabond, ClearingId c) { if (Active) Each(m => m.OnExploreResolved(vagabond, c)); }

        public void OnAidResolved(SeatId vagabond, SeatId receiver, ClearingId c)
        {
            if (Active) Each(m => m.OnAidResolved(vagabond, receiver, c));
        }

        public bool AllowHostile(SeatId vagabond, SeatId faction, ClearingId battleClearing)
        {
            if (!Active) return true;
            var ok = true;
            foreach (var m in _modules.ToArray()) ok &= m.AllowHostile(vagabond, faction, battleClearing);
            return ok;
        }

        public void OnRelationshipChanged(SeatId vagabond, SeatId faction, Relationship before, Relationship after)
        {
            if (Active) Each(m => m.OnRelationshipChanged(vagabond, faction, before, after));
        }

        public void OnItemChanged(SeatId vagabond, ItemState? before, ItemState? after)
        {
            if (Active) Each(m => m.OnItemChanged(vagabond, before, after));
        }

        public void OnWarlordRemoved(SeatId hundreds, ClearingId c) { if (Active) Each(m => m.OnWarlordRemoved(hundreds, c)); }

        public void OnRelicRecovered(SeatId keepers, int relicId, string relicType)
        {
            if (Active) Each(m => m.OnRelicRecovered(keepers, relicId, relicType));
        }

        public void OnRelicMoved(int relicId) { if (Active) Each(m => m.OnRelicMoved(relicId)); }

        public void OnVpChanged(SeatId seat, int before, int after)
        {
            if (!Active) return;
            var monolith = Context.Role(Ids.MapRoles.Monolith);
            Actions.ShowOverlay(Ids.Overlays.GlobalMonolithNumber, null, monolith, null);
            Actions.ShowOverlay(Ids.Overlays.GlobalGoalTracker, null, null, null);
            Each(m => m.OnVpChanged(seat, before, after));
        }

        public void OnTurmoil(SeatId eyrie) { if (Active) Each(m => m.OnTurmoil(eyrie)); }

        public void OnConvert(SeatId lizards, SeatId victim, ClearingId c) { if (Active) Each(m => m.OnConvert(lizards, victim, c)); }

        public void OnToggle(SeatId seat, string toggleId, bool on) { if (Active) Each(m => m.OnToggle(seat, toggleId, on)); }

        public string? OnBotDecision(SeatId seat, IReadOnlyList<string> legalActions)
        {
            if (!Active || !Board.IsBot(seat)) return null;
            var bot = ModuleOf(seat)?.Bot;
            if (bot == null) return null;
            var pick = bot.ChooseAction(new BotView(Context, seat), legalActions);
            return pick != null && legalActions.Contains(pick) ? pick : null;
        }

        public void OnGameOver(IReadOnlyList<SeatId> winners)
        {
            if (!State.GlobalE33Mode) return;
            Each(m => m.OnGameOver(winners));
            // trigger.win for every winner; trigger.loss for one seated character who did not win.
            Context.Barks.SayEach(SeatedInTurnOrder().Where(winners.Contains), Ids.BarkTriggers.Win);
            if (winners.Count > 0) Context.Barks.SayOne(SeatedInTurnOrder().Where(s => !winners.Contains(s)), Ids.BarkTriggers.Loss);
            Context.GameOver = true;
            if (State.GlobalCampaignGame > 0)
            {
                var humanWon = winners.Any(w => !Board.IsBot(w));
                ProfileCampaignJournal = !humanWon;
                ProfileCampaignGame = State.GlobalCampaignGame >= Tuning.CampaignGames ? 0 : State.GlobalCampaignGame + 1;
            }
        }

        // ------------------------------------------------------------ save and load

        public string OnSave() => State.GlobalE33Mode ? State.ToText() : "";

        public void OnLoad(string saved)
        {
            var map = ModState.FromText(saved);
            ResetGame();
            Context.TurnOrder = Board.Seats().ToArray();
            if (map.Count == 0) return; // state.global.e33-mode: a save without mod state loads as plain Root

            var loaded = State.Load(map);
            State.GlobalE33Mode = true;
            Context.Options = new GameOptions
            {
                Spoilers = State.GlobalSpoilerMode, Difficulty = State.GlobalDifficulty, FaithfulGustave = NextGameOptions.FaithfulGustave,
                Campaign = State.GlobalCampaignGame > 0, MonolithBarrier = NextGameOptions.MonolithBarrier,
            };

            // state.global.round: Root's own counter wins when it has one.
            if (Board.Round() > 0) State.GlobalRound = Board.Round();
            _roundStarted = true;

            // state.global.act: restored (never backwards); recomputed by the turn-end test when the save has none.
            if (loaded.Contains(Ids.State.GlobalAct)) Context.ActClock.Restore(State.GlobalAct);
            else Context.ActClock.Update(Context.HighestVp(), State.GlobalRound - 1);
            State.GlobalAct = Context.ActClock.Act;
            _actEventsFiredUpTo = State.GlobalAct;

            // state.global.seat-characters: a seat whose engine no longer matches plays plain Root.
            var seats = new HashSet<SeatId>(Board.Seats());
            foreach (var kv in State.GlobalSeatCharacters.ToList())
            {
                var expected = Data.Characters.TryGetValue(kv.Value, out var row) ? ModuleRegistry.EngineIdOf(row.Engine) : null;
                if (seats.Contains(kv.Key) && expected.HasValue && expected.Value == Board.EngineOf(kv.Key)) continue;
                State.GlobalSeatCharacters.Remove(kv.Key);
                Context.Warnings.Add(kv.Key + " no longer plays the engine of " + kv.Value + "; it plays plain Root.");
            }

            // state.global.roles: restored as is; only a save without them recomputes by the map_roles rules.
            if (!loaded.Contains(Ids.State.GlobalRoles)) State.SetRoles(RoleMap.ForMap(Board, GustaveKeepCorner()));

            BindModules();
            Each(m => m.OnStateLoaded(loaded));
            ApplySeatTitles();

            // state.global.countdowns: keep (and redraw) a banner only if its confirm test passes on the loaded board.
            var kept = new HashSet<SeatId>();
            foreach (var seat in State.GlobalCountdowns.OrderBy(s => s.Value).ToList())
            {
                var win = ModuleOf(seat)?.Win;
                if (win != null && WinRules.ConfirmPasses(Context, seat, win))
                {
                    kept.Add(seat);
                    Context.Countdown.EndOfTurn(seat, win.WinId, true);
                    Actions.ShowBanner(seat, win.WinId);
                }
                else Actions.ClearBanner(seat);
            }
            State.GlobalCountdowns = kept;
        }
    }

    /// <summary>characters.json ids the framework itself refers to.</summary>
    public static class CharacterIds
    {
        public const string Gustave = "gustave";
        public const string Esquie = "esquie";
        public const string Paintress = "paintress";
        public const string Sciel = "sciel";
        public const string Verso = "verso";
        public const string Maelle = "maelle";
        public const string Lune = "lune";
        public const string Monoco = "monoco";
        public const string Curator = "curator";
        public const string Clea = "clea";
        public const string WhiteHairedMan = "white-haired-man";
    }

    /// <summary>Story event rules shared by every act (sys.acts, story_events.json).</summary>
    public static class StoryEvents
    {
        /// <summary>tier safe: always; act2: either mode (it only fires from Act II on); finished: finished mode only.</summary>
        public static bool TierAllows(StoryEventDef e, SpoilerMode mode) => e.Tier != "finished" || mode == SpoilerMode.Finished;

        private static readonly System.Text.RegularExpressions.Regex SeatedClause = new System.Text.RegularExpressions.Regex(
            @"\b(?:if|only when)\s+(.+?)\s+is seated", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>The character that owns an event: its spawn's owner (spawns.json); for an event with no spawn, the
        /// character its 'what' names in an "If X is seated" / "only when X is seated" clause (story_events notes: "Every event
        /// whose 'what' names a character fires only when that character is seated"); else null (a table-wide event).</summary>
        public static string OwnerOf(StoryEventDef e)
        {
            if (!string.IsNullOrEmpty(e.Spawn) && Data.Spawns.TryGetValue(e.Spawn, out var s)) return s.Owner;
            foreach (System.Text.RegularExpressions.Match m in SeatedClause.Matches(e.What ?? ""))
            {
                var name = m.Groups[1].Value.Trim();
                var row = Data.Characters.Values.FirstOrDefault(c =>
                    string.Equals(c.SafeName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.FinishedName, name, StringComparison.OrdinalIgnoreCase));
                if (row != null) return row.Id;
            }
            return null;
        }

        /// <summary>Whether an event has fired in this game, worked out from the act (so it holds after a load): its act has
        /// opened, its tier allows it in this game's spoiler mode, and its owner (if any) is seated.</summary>
        public static bool HasFired(GameContext ctx, string eventId)
        {
            if (!Data.StoryEvents.TryGetValue(eventId, out var e) || !Data.Acts.TryGetValue(e.Act, out var act)) return false;
            if (ctx.Act < act.Number || !TierAllows(e, ctx.Spoilers)) return false;
            var owner = OwnerOf(e);
            return owner == null || ctx.IsSeated(owner);
        }
    }
}
