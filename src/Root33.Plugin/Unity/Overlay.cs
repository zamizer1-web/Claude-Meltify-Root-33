using System;
using BepInEx.Logging;
using UnityEngine;

namespace Root33.Plugin.Unity
{
    /// <summary>
    /// The mod's panel, toggled with F8: Root33.Host's text for the human seat (act banner and Monolith Number, countdown
    /// banners, the public Goal Tracker, story so far, speech bubbles since that seat's last turn), from
    /// <see cref="Root33Runtime.PanelText"/>. Until the board adapter is mapped it reads "Root 33 loaded · waiting for the
    /// board adapter" with the adapter's progress and the next read to map.
    ///
    /// Drawn with plain IMGUI (GUI.Box and GUI.Label with a Rect and a string): GUILayout is avoided because IL2CPP games often
    /// strip it. The text is rebuilt twice a second, not on every OnGUI call. Under IL2CPP the class is injected
    /// (ClassInjector.RegisterTypeInIl2Cpp in Plugin.Load), which needs the IntPtr constructor.
    /// </summary>
    public sealed class Overlay : MonoBehaviour
    {
#if !ROOT_MONO
        public Overlay(IntPtr ptr) : base(ptr) { }
#endif

        internal static ManualLogSource? Log;
        private const string Keys = "F8: hide this panel · F7: word list / game · F9: probe file (probe mode)";
        private const float Margin = 10f, Padding = 8f, LineHeight = 17f, CharsPerLine = 105f, MaxWidth = 780f;

        private readonly KeyWatch _f8 = new KeyWatch(KeyCode.F8);
        private readonly KeyWatch _f7 = new KeyWatch(KeyCode.F7);
        private bool _glossary;
        private bool _shown = true;
        private string _text = Root33Runtime.Waiting;
        private int _lines = 2;
        private float _nextRefresh;
        private bool _errorLogged;

        public void Update()
        {
            Root33Runtime.PumpMainThread(Log);
            if (_f8.PressedInUpdate()) _shown = !_shown;
            if (_f7.PressedInUpdate()) ToggleGlossary();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        public void OnGUI()
        {
            if (_f8.PressedInGui()) _shown = !_shown;
            if (_f7.PressedInGui()) ToggleGlossary();
            if (!_shown) return;
            var width = Mathf.Min(MaxWidth, Screen.width - 2 * Margin);
            var height = Mathf.Min(Screen.height - 2 * Margin, 2 * Padding + LineHeight * _lines);
            var box = new Rect(Margin, Margin, width, height);
            GUI.Box(box, "");
            GUI.Label(new Rect(box.x + Padding, box.y + Padding, box.width - 2 * Padding, box.height - 2 * Padding), _text);
        }

        /// <summary>F7: the panel shows the mod's word list instead of the game, and back (it shows itself if hidden).</summary>
        private void ToggleGlossary()
        {
            _glossary = !_glossary;
            _shown = true;
            Refresh();
        }

        private void Refresh()
        {
            string text;
            try
            {
                var rt = Root33Runtime.Current;
                text = rt == null ? Root33Runtime.Waiting : _glossary ? rt.GlossaryText() : rt.PanelText();
            }
            catch (Exception e)
            {
                text = Root33Runtime.Waiting + "\n(panel error: " + e.Message + ")";
                if (!_errorLogged) { _errorLogged = true; Log?.LogWarning("Root 33 panel: " + e); }
            }
            _text = text + "\n" + Keys;
            var lines = 0;
            foreach (var line in _text.Split('\n')) lines += 1 + (int)(line.Length / CharsPerLine);
            _lines = lines;
        }
    }
}
