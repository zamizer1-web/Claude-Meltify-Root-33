using BepInEx.Configuration;
using Root33.Core;

namespace Root33.Plugin
{
    /// <summary>
    /// The plugin's BepInEx config (BepInEx\config\gg.root33.mod.cfg, written on first start). The same API in BepInEx 5 and 6.
    /// Until the mod's own title-screen prompt and character select exist (M5), these are how the creator picks them.
    /// </summary>
    public sealed class PluginSettings
    {
        public const string Safe = "safe";
        public const string Finished = "finished";

        public PluginSettings(BepInEx.Configuration.ConfigFile config)
        {
            Spoiler = config.Bind("Root 33", "SpoilerMode", Safe, new ConfigDescription(
                "safe: names and story stay spoiler-free. finished: for players who have finished Clair Obscur: Expedition 33 " +
                "(finished names, the finished-only lines and characters). Applies from the next game.",
                new AcceptableValueList<string>(Safe, Finished)));
            E33Mode = config.Bind("Root 33", "E33Mode", true,
                "On: new games are Root 33 games (characters, acts, themed wins, speech bubbles). Off: plain Root; the mod only watches.");
            ProbeMode = config.Bind("Developer", "ProbeMode", true,
                "Dev copy only, off in the release: logs every scene load and writes BepInEx\\root33-probe.json (the scene's objects " +
                "and their components) when F9 is pressed.");
        }

        public ConfigEntry<string> Spoiler { get; }
        public ConfigEntry<bool> E33Mode { get; }
        public ConfigEntry<bool> ProbeMode { get; }

        /// <summary>The spoiler mode as the core reads it.</summary>
        public SpoilerMode Spoilers => Spoiler.Value == Finished ? SpoilerMode.Finished : SpoilerMode.Safe;
    }
}
