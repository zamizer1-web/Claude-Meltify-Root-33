using System;
using UnityEngine;

namespace Root33.Plugin.Unity
{
    /// <summary>
    /// One function key (F8, F9) read two ways: Unity's legacy Input in Update, and IMGUI's key events in OnGUI. A game built
    /// with only the new Input System throws from the legacy Input class (and IL2CPP builds may strip it); the IMGUI events
    /// still arrive. A press is counted once per frame, whichever way saw it.
    /// </summary>
    internal sealed class KeyWatch
    {
        private readonly KeyCode _key;
        private bool _legacyInputBroken;
        private int _lastFrame = -1;

        public KeyWatch(KeyCode key) { _key = key; }

        /// <summary>Call from Update.</summary>
        public bool PressedInUpdate()
        {
            if (_legacyInputBroken) return false;
            try { return Input.GetKeyDown(_key) && Once(); }
            catch (Exception)
            {
                _legacyInputBroken = true;   // from now on OnGUI's key events only
                return false;
            }
        }

        /// <summary>Call from OnGUI.</summary>
        public bool PressedInGui()
        {
            var e = Event.current;
            return e != null && e.type == EventType.KeyDown && e.keyCode == _key && Once();
        }

        private bool Once()
        {
            var frame = Time.frameCount;
            if (frame == _lastFrame) return false;
            _lastFrame = frame;
            return true;
        }
    }
}
