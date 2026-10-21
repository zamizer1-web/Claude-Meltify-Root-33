using System;
using System.Collections.Concurrent;
using BepInEx.Logging;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// Every patch body runs inside <see cref="Run"/>: an exception from the mod is logged once per patch and never reaches
    /// Root, so a broken hook cannot stop a game. Plugin.Load sets the log.
    /// </summary>
    internal static class PatchGuard
    {
        internal static ManualLogSource? Log;
        private static readonly ConcurrentDictionary<string, bool> Reported = new ConcurrentDictionary<string, bool>();

        public static void Run(string where, Action body)
        {
            try { body(); }
            catch (Exception e) { Report(where, e); }
        }

        public static T Run<T>(string where, Func<T> body, T fallback)
        {
            try { return body(); }
            catch (Exception e)
            {
                Report(where, e);
                return fallback;
            }
        }

        private static void Report(string where, Exception e)
        {
            if (Reported.TryAdd(where, true)) Log?.LogWarning($"Root 33 patch {where} failed (reported once): {e}");
        }

        /// <summary>Logs a line once per key (first sightings while mapping Root, kept short).</summary>
        public static void Once(string key, string line)
        {
            if (Reported.TryAdd("once:" + key, true)) Log?.LogInfo(line);
        }
    }
}
