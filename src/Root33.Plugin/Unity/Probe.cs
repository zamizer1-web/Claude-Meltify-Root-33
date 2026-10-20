using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Root33.Plugin.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !ROOT_MONO
using Il2CppInterop.Runtime;
#endif

namespace Root33.Plugin.Unity
{
    /// <summary>
    /// Developer probe (ProbeMode in the config; dev copy only). Logs every scene load, and on F9 writes
    /// BepInEx\root33-probe.json: each loaded scene's root GameObjects and their children (depth- and size-limited, see
    /// <see cref="ProbeSnapshot"/>) with every component's type name, and the DontDestroyOnLoad objects. Press it on the title
    /// screen, on the faction select screen and during a game against bots, so the PC session can see which of Root's objects
    /// hold the board, the pieces and the turn state.
    ///
    /// Under IL2CPP it is a class injected into the IL2CPP domain (ClassInjector.RegisterTypeInIl2Cpp in Plugin.Load), which
    /// needs the IntPtr constructor. Scenes are polled, not watched through SceneManager.sceneLoaded, so no delegate has to
    /// cross into IL2CPP.
    /// </summary>
    public sealed class Probe : MonoBehaviour
    {
#if !ROOT_MONO
        public Probe(IntPtr ptr) : base(ptr) { }
#endif

        internal static ManualLogSource? Log;
        public const string FileName = "root33-probe.json";

        private readonly KeyWatch _f9 = new KeyWatch(KeyCode.F9);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private float _nextSceneCheck;

        public void Update()
        {
            if (Time.unscaledTime >= _nextSceneCheck)
            {
                _nextSceneCheck = Time.unscaledTime + 0.5f;
                WatchScenes();
            }
            if (_f9.PressedInUpdate()) Dump();
        }

        public void OnGUI()
        {
            if (_f9.PressedInGui()) Dump();
        }

        private void WatchScenes()
        {
            try
            {
                var now = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.isLoaded) continue;
                    var key = s.name + "#" + s.handle;
                    now.Add(key);
                    if (!_seen.Contains(key))
                        Log?.LogInfo($"Root 33 probe: scene loaded: {s.name} ({s.rootCount} root objects; build index {s.buildIndex}; active scene {SceneManager.GetActiveScene().name})");
                }
                foreach (var gone in _seen)
                    if (!now.Contains(gone)) Log?.LogInfo("Root 33 probe: scene unloaded: " + gone.Substring(0, gone.LastIndexOf('#')));
                _seen.Clear();
                _seen.UnionWith(now);
            }
            catch (Exception e)
            {
                Log?.LogWarning("Root 33 probe: cannot list scenes: " + e.Message);
                _nextSceneCheck = Time.unscaledTime + 30f;
            }
        }

        private void Dump()
        {
            var path = Path.Combine(Paths.BepInExRootPath, FileName);
            try
            {
                var snap = new ProbeSnapshot(DateTime.Now, SceneManager.GetActiveScene().name);
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    names.Add(s.name);
                    AddScene(snap, s, s.name);
                }
                // Objects kept across scenes (DontDestroyOnLoad) live in a scene SceneManager does not list; ours is one of them.
                var ddol = gameObject.scene;
                if (ddol.IsValid() && !names.Contains(ddol.name)) AddScene(snap, ddol, "DontDestroyOnLoad");
                File.WriteAllText(path, snap.ToJson());
                Log?.LogInfo($"Root 33 probe: wrote {path} ({snap.NodeCount} objects in {snap.Scenes.Count} scenes)");
            }
            catch (Exception e)
            {
                Log?.LogError("Root 33 probe: could not write " + path + ": " + e);
            }
        }

        private static void AddScene(ProbeSnapshot snap, Scene scene, string name)
        {
            var ps = new ProbeScene(name, scene.path ?? "", scene.isLoaded, scene.isLoaded ? scene.rootCount : 0);
            snap.Scenes.Add(ps);
            if (!scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (snap.NodeCount >= snap.MaxNodes) { snap.Notes.Add($"stopped at {snap.MaxNodes} objects in scene {name}"); return; }
                Walk(snap, ps.Roots, root.transform, 0);
            }
        }

        private static void Walk(ProbeSnapshot snap, List<ProbeNode> into, Transform t, int depth)
        {
            if (!snap.TakeNode()) return;
            var go = t.gameObject;
            var node = new ProbeNode(go.name, go.activeSelf, t.childCount);
            into.Add(node);
            foreach (var c in Components(go)) snap.AddComponent(node, TypeName(c));
            if (depth + 1 >= snap.MaxDepth) { node.ChildrenCut = t.childCount > 0; return; }
            var shown = Math.Min(t.childCount, snap.MaxChildren);
            for (var i = 0; i < shown; i++) Walk(snap, node.Children, t.GetChild(i), depth + 1);
            if (t.childCount > shown || node.Children.Count < shown) node.ChildrenCut = true;
        }

        private static IEnumerable<Component?> Components(GameObject go)
        {
#if !ROOT_MONO
            return go.GetComponents(Il2CppType.Of<Component>());
#else
            return go.GetComponents<Component>();
#endif
        }

        /// <summary>The game's own type name (under IL2CPP the IL2CPP type, not the interop wrapper's).</summary>
        private static string TypeName(Component? c)
        {
            if (c == null) return "(missing script)";
#if !ROOT_MONO
            try { return c.GetIl2CppType().FullName; }
            catch (Exception) { return c.GetType().FullName ?? c.GetType().Name; }
#else
            return c.GetType().FullName ?? c.GetType().Name;
#endif
        }
    }
}
