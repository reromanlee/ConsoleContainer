using System;
using UnityEditor;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// The editor-wide "Clear on Play" preference. When on, every instance is cleared as play mode
    /// starts, so a project that skips the domain reload on entering play mode behaves like one
    /// that doesn't, instead of piling up history across sessions. Stored per user, and shared by
    /// every Console Viewer window since it clears data they all show.
    /// </summary>
    [InitializeOnLoad]
    internal static class ClearOnPlay
    {
        private const string PreferenceKey = "reromanlee.ConsoleContainer.ClearOnPlay";
        private const bool DefaultEnabled = true;

        static ClearOnPlay()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Raised after the preference changes, so open windows can update their toggles.</summary>
        internal static event Action Changed;

        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(PreferenceKey, DefaultEnabled);
            set
            {
                if (value == Enabled)
                {
                    return;
                }

                EditorPrefs.SetBool(PreferenceKey, value);
                Changed?.Invoke();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && Enabled)
            {
                ConsoleRegistry.ClearAll();
            }
        }
    }
}
