using System;
using System.Collections.Generic;
using Root33.Core.Engine;

namespace Root33.Host
{
    /// <summary>
    /// How a host calls the core's hooks (IGameHooks on a <see cref="Root33Game"/>), the same in the tabletop and in Root:
    ///
    /// Re-entrancy: the core changes the board through IBoardActions while it runs a hook, and the host reports those changes
    /// back as hooks (a guard warrior placed, VP scored, the game ending). Such nested hooks are queued and delivered right
    /// after the hook that caused them, so the core never runs inside itself. A nested question (<see cref="Ask{T}"/>: may
    /// this battle start?) is answered at once.
    ///
    /// Errors: an exception from the core is caught and recorded in <see cref="Errors"/>; the first one of each hook is
    /// reported through <see cref="FirstError"/>, and the game goes on without that rule.
    /// </summary>
    public sealed class HookDispatcher
    {
        private readonly Root33Game _game;
        private readonly Queue<(string Hook, Action<Root33Game> Call)> _queued = new Queue<(string, Action<Root33Game>)>();
        private readonly HashSet<string> _errorHooks = new HashSet<string>();
        private int _depth;

        public HookDispatcher(Root33Game game) { _game = game ?? throw new ArgumentNullException(nameof(game)); }

        public Root33Game Game => _game;

        /// <summary>Every exception the core threw, as "hook.id: Type: message" (tests assert this stays empty).</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Called the first time the core throws in a hook (the tabletop logs it; the plugin writes the BepInEx log).
        /// <see cref="ModText.CoreErrorLine"/> is the standard text.</summary>
        public Action<string, Exception>? FirstError { get; set; }

        /// <summary>Called after every outermost hook and the hooks it queued (hosts pick up story events, act banners and
        /// notes there, with <see cref="ModFeed.CatchUp"/>).</summary>
        public Action? AfterHook { get; set; }

        /// <summary>True while a hook runs (a hook raised now is queued).</summary>
        public bool Running => _depth > 0;

        /// <summary>Runs a hook now, or queues it when the core is already running one.</summary>
        public void Call(string hookId, Action<Root33Game> call)
        {
            if (_depth > 0) { _queued.Enqueue((hookId, call)); return; }
            _depth++;
            try
            {
                Safe(hookId, () => call(_game));
                while (_queued.Count > 0)
                {
                    var (h, c) = _queued.Dequeue();
                    Safe(h, () => c(_game));
                }
            }
            finally { _depth--; }
            AfterHook?.Invoke();
        }

        /// <summary>A hook with an answer (hook.battle-legal, hook.hostility-change, hook.bot-decision): <paramref name="fallback"/>
        /// when the core throws. Answered at once even inside another hook.</summary>
        public T Ask<T>(string hookId, Func<Root33Game, T> ask, T fallback)
        {
            var result = fallback;
            if (_depth > 0) { Safe(hookId, () => result = ask(_game)); return result; }
            Call(hookId, game => result = ask(game));
            return result;
        }

        /// <summary>Runs one hook on its own (hook.load): hooks it raises are dropped, and <see cref="AfterHook"/> is not called,
        /// because reloading is not a game event.</summary>
        public void RunAlone(string hookId, Action<Root33Game> call)
        {
            _depth++;
            try { Safe(hookId, () => call(_game)); }
            finally { _depth--; }
            _queued.Clear();
        }

        /// <summary>Runs <paramref name="action"/>, recording an exception under <paramref name="hookId"/> instead of throwing.</summary>
        public void Safe(string hookId, Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                Errors.Add($"{hookId}: {e.GetType().Name}: {e.Message}");
                if (_errorHooks.Add(hookId)) FirstError?.Invoke(hookId, e);
            }
        }
    }
}
