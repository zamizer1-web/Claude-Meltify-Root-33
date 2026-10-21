using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Root33.Host;
using Root33.Plugin.Adapter;
#if !ROOT_MONO
using BepInEx.Unity.IL2CPP;
#endif
#if !NOUNITY
using Root33.Plugin.Unity;
#if !ROOT_MONO
using Il2CppInterop.Runtime.Injection;
#endif
#endif

namespace Root33.Plugin
{
    /// <summary>The plugin's identity, the same for BepInEx 6 and 5.</summary>
    public static class PluginInfo
    {
        public const string Guid = "gg.root33.mod";
        public const string Name = "Root 33";
        public const string Version = "0.1.0";
    }

#if !ROOT_MONO
    /// <summary>
    /// Root 33's entry under BepInEx 6 IL2CPP (the build the PC found in the dev copy). Load: the start-up line in the log
    /// (BepInEx\LogOutput.log), the config, the Root 33 runtime, every Harmony patch in Patches/, then the Unity components
    /// (registered in IL2CPP first): the F8 panel (Unity/Overlay.cs) and, in probe mode, the scene probe (Unity/Probe.cs).
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Root33Plugin : BasePlugin
    {
        private Harmony? _harmony;

        public override void Load()
        {
            var settings = PluginStart.Run(Log, Config, out _harmony);
#if !NOUNITY
            PluginStart.Component(Log, "panel (F8)", () =>
            {
                ClassInjector.RegisterTypeInIl2Cpp<Overlay>();
                AddComponent<Overlay>();
            });
            if (settings.ProbeMode.Value)
                PluginStart.Component(Log, "probe (F9)", () =>
                {
                    ClassInjector.RegisterTypeInIl2Cpp<Probe>();
                    AddComponent<Probe>();
                });
#endif
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return true;
        }
    }
#else
    /// <summary>
    /// The alternative entry for a Mono build of Root under BepInEx 5 (build with -p:RootMono=true, which defines ROOT_MONO and
    /// references BepInEx 5 and Root_Data\Managed instead). The same start-up; the Unity components are ordinary
    /// MonoBehaviours, added to BepInEx's own manager object. Kept in step with the IL2CPP entry above in case the PC finds
    /// a Mono build after all.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Root33Plugin : BaseUnityPlugin
    {
        private Harmony? _harmony;

        private void Awake()
        {
            var settings = PluginStart.Run(Logger, Config, out _harmony);
#if !NOUNITY
            PluginStart.Component(Logger, "panel (F8)", () => gameObject.AddComponent<Overlay>());
            if (settings.ProbeMode.Value) PluginStart.Component(Logger, "probe (F9)", () => gameObject.AddComponent<Probe>());
#endif
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
#endif

    /// <summary>What both entries do at start, before their Unity components.</summary>
    internal static class PluginStart
    {
        public static PluginSettings Run(ManualLogSource log, BepInEx.Configuration.ConfigFile config, out Harmony harmony)
        {
            log.LogInfo($"Root 33 loaded (core {SheetsInfo.CoreVersion}, sheets {SheetsInfo.RowCount} rows)");
            var settings = new PluginSettings(config);
            log.LogInfo($"Root 33 settings: spoiler mode {settings.Spoiler.Value}, E33 mode {(settings.E33Mode.Value ? "on" : "off")}, " +
                        $"probe mode {(settings.ProbeMode.Value ? "on" : "off")} (BepInEx\\config\\{PluginInfo.Guid}.cfg)");
            var runtime = Root33Runtime.Start(settings, log);
            Patches.PatchGuard.Log = log;
#if !NOUNITY
            Probe.Log = log;
            Overlay.Log = log;
#endif
            harmony = new Harmony(PluginInfo.Guid);
            harmony.PatchAll(typeof(PluginStart).Assembly);
            log.LogInfo($"Root 33: {harmony.GetPatchedMethods().Count()} Root method(s) patched; game_hooks rows patched " +
                        $"{runtime.PatchedHooks}/{runtime.HooksToPatch}; board adapter {RootBoard.MappedReads}/{RootBoard.ReadRows} reads, " +
                        $"{RootBoard.MappedActions}/{RootBoard.ActionRows} actions mapped");
            return settings;
        }

        /// <summary>Adds one Unity component; a failure is logged and the rest of the plugin goes on.</summary>
        public static void Component(ManualLogSource log, string what, Action add)
        {
            try
            {
                add();
                log.LogInfo($"Root 33: {what} ready");
            }
            catch (Exception e)
            {
                log.LogError($"Root 33: the {what} could not start: {e}");
            }
        }
    }
}
