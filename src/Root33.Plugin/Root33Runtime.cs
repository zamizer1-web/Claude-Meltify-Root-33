using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Host;
using Root33.Plugin.Adapter;
using Root33.Plugin.Patches;

namespace Root33.Plugin
{
    /// <summary>
    /// Root 33 running inside Root: one <see cref="Root33Game"/> for the whole session (it resets itself at every new game)
    /// over <see cref="RootBoard"/>, called through Root33.Host's <see cref="HookDispatcher"/> exactly as the tabletop calls it,
    /// with the mod's feed (bubbles, story, act banners) and view (the panel the overlay draws) from Root33.Host.
    ///
    /// Harmony patches call the static entry points: <see cref="Hook"/> for a moment in Root's game flow, <see cref="Ask{T}"/>
    /// for a question (battle legality, a bot's decision), and <see cref="TurnEnd"/>, <see cref="Save"/> and <see cref="Load"/>
    /// for the three hooks with bookkeeping of their own. Board actions the adapter has not mapped yet are reported to the core
    /// as unsupported, so it takes their sheet fallbacks; a core exception is logged once per hook and the game goes on.
    /// </summary>
    public sealed class Root33Runtime
    {
        /// <summary>The panel's text until the board adapter can show a game (docs/PLUGIN-PLAN.md M0).</summary>
        public const string Waiting = "Root 33 loaded · waiting for the board adapter";

        public static Root33Runtime? Current { get; private set; }

        private readonly PluginSettings _settings;
        private readonly ManualLogSource _log;

        private Root33Runtime(PluginSettings settings, ManualLogSource log)
        {
            _settings = settings;
            _log = log;
            Board = new RootBoard();
            Game = new Root33Game(Board, Board, ModuleRegistry.Default(), new Capabilities().Unsupported(RootBoard.UnmappedActions));
            Game.NextGameOptions.Spoilers = settings.Spoilers;
            Game.ProfileSpoilerMode = settings.Spoilers;
            Hooks = new HookDispatcher(Game)
            {
                FirstError = (hook, e) => _log.LogWarning(ModText.CoreErrorLine(hook, e.Message)),
                AfterHook = () => Feed.CatchUp(Game.Context, Game.Context.Round, SetupDone, line => _log.LogInfo(line)),
            };
            Feed = new ModFeed();
            Host = new RootHost(Board, Game.Context, settings);
            View = new ModView(Game.Context, Host, Feed);
            Board.BubbleShown = (seat, barkId) =>
            {
                Feed.AddBubble(seat, barkId, Game.Context.Round);
                _log.LogInfo(ModText.BubbleLine(SafeWho(seat), barkId));
            };
            PatchedHooks = typeof(Root33Runtime).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributes<RootHookAttribute>()).Select(a => a.RowId).Distinct().Count();
            HooksToPatch = HookMap.Rows.Count(r => r.NeedsPatch);
        }

        /// <summary>Plugin.Load creates the one runtime of the session.</summary>
        public static Root33Runtime Start(PluginSettings settings, ManualLogSource log) => Current = new Root33Runtime(settings, log);

        public RootBoard Board { get; }
        public Root33Game Game { get; }
        public HookDispatcher Hooks { get; }
        public ModFeed Feed { get; }
        public RootHost Host { get; }
        public ModView View { get; }
        /// <summary>The current game has finished setup (hook.setup-complete): act banners show from then on.</summary>
        public bool SetupDone { get; private set; }
        /// <summary>game_hooks rows a [RootHook] patch in this assembly raises, and rows that need one.</summary>
        public int PatchedHooks { get; }
        public int HooksToPatch { get; }

        // ------------------------------------------------------------------ entry points for the Harmony patches

        /// <summary>A moment in Root's game flow (a game_hooks row): runs the core's handler now, or right after the hook the
        /// core is running. Nothing happens before Plugin.Load.</summary>
        public static void Hook(string hookId, Action<Root33Game> call) => Current?.Raise(hookId, call);

        /// <summary>A question to the core (hook.battle-legal, hook.hostility-change, hook.bot-decision): its answer, or
        /// <paramref name="fallback"/> before Plugin.Load or when the core throws.</summary>
        public static T Ask<T>(string hookId, Func<Root33Game, T> ask, T fallback) =>
            Current == null ? fallback : Current.Hooks.Ask(hookId, ask, fallback);

        /// <summary>hook.turn-end: the core's handler, then the seat has seen every bubble so far.</summary>
        public static void TurnEnd(SeatId seat)
        {
            var rt = Current;
            if (rt == null) return;
            rt.Raise(Ids.GameHooks.TurnEnd, g => g.OnTurnEnd(seat));
            rt.Feed.TurnEnded(seat);
        }

        /// <summary>hook.save: the core's state as text, to store beside Root's save under the game's id ("" in plain Root).</summary>
        public static string Save()
        {
            var rt = Current;
            if (rt == null) return "";
            var text = "";
            rt.Hooks.Safe(Ids.GameHooks.Save, () => text = rt.Game.OnSave());
            return text;
        }

        /// <summary>hook.load: restores the core's state from the text stored beside Root's save. Reloading is not a game event:
        /// hooks the core raises meanwhile are dropped and nothing it logged while reloading is shown again.</summary>
        public static void Load(string saved)
        {
            var rt = Current;
            if (rt == null) return;
            rt.Feed.Reset();
            rt.Board.Display.Clear();
            rt.Hooks.RunAlone(Ids.GameHooks.Load, g => g.OnLoad(saved));
            rt.SetupDone = rt.Game.Context.E33Mode;
            rt.Feed.SkipSeen(rt.Game.Context);
            rt.Feed.LastAct = rt.Game.Context.Act;
        }

        private void Raise(string hookId, Action<Root33Game> call)
        {
            if (hookId == Ids.GameHooks.CharacterSelect)
            {
                if (!_settings.E33Mode.Value) return;   // plain Root: no E33 game is set up, so the core stays idle
                Game.NextGameOptions.Spoilers = _settings.Spoilers;
            }
            else if (hookId == Ids.GameHooks.GameStart)
            {
                Feed.Reset();
                Board.Display.Clear();
                SetupDone = false;
            }
            else if (hookId == Ids.GameHooks.SetupComplete) SetupDone = true;   // the Act I banner shows with its story
            Hooks.Call(hookId, call);
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>
        /// What the overlay shows: the waiting text and the adapter's progress until a Root 33 game can be shown, then the mod's
        /// panel for the human seat (Root33.Host's ModView, the same as the tabletop's). When a read is not mapped yet the panel
        /// says which one to map next.
        /// </summary>
        public string PanelText()
        {
            var progress = $"board adapter: {RootBoard.MappedReads}/{RootBoard.ReadRows} reads, {RootBoard.MappedActions}/{RootBoard.ActionRows} actions mapped" +
                           $" · hooks: {PatchedHooks}/{HooksToPatch} patched · core {SheetsInfo.CoreVersion}";
            if (!Game.Context.E33Mode)
            {
                var head = RootBoard.MappedReads == 0 ? Waiting
                    : _settings.E33Mode.Value ? "Root 33 loaded · start a new game to play Root 33"
                    : "Root 33 loaded · E33 mode is off (plain Root)";
                return head + "\n" + progress;
            }
            var panel = View.Panel(ViewerSeat());
            if (panel.Failure is NotMappedException nm)
                return $"{Waiting}\n{progress}\nnext to map: {nm.RowId} (engineSymbol guess: {nm.EngineSymbol})";
            return panel.ToText();
        }

        /// <summary>The seat the panel speaks to: the first human seat in turn order, or null (a spectator's view).</summary>
        private SeatId? ViewerSeat()
        {
            try { return Host.TurnOrder().Where(s => !Board.IsBot(s)).Select(s => (SeatId?)s).FirstOrDefault(); }
            catch (NotMappedException) { return null; }
        }

        private string SafeWho(SeatId seat)
        {
            try { return Host.Who(seat); }
            catch (NotMappedException) { return ModText.SeatLabel(seat, "?"); }
        }
    }
}
