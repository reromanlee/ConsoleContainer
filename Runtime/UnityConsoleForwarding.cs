namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Single place that answers whether a message should <b>also</b> be written
    /// to the Unity console on top of being kept by its
    /// <see cref="ConsoleInstance"/>.
    ///
    /// The answer differs by platform: player builds forward only what the
    /// settings asset opts in to, while the editor forwards according to
    /// <see cref="EditorForwardingMode"/> — by default only while a test run is
    /// active, which is what makes messages visible in the Test Runner window.
    /// </summary>
    internal static class UnityConsoleForwarding
    {
#if UNITY_EDITOR
        private static volatile bool _testRunActive;

        /// <summary>
        /// True while the Unity Test Framework reports a running test. Driven by
        /// the editor-side test runner bridge; stays false when the Test
        /// Framework package is not installed.
        /// </summary>
        internal static bool IsTestRunActive
        {
            get => _testRunActive;
            set => _testRunActive = value;
        }
#endif

        /// <summary>
        /// Decides whether <paramref name="type"/> reaches the Unity console.
        /// Safe to call from any thread: when the settings asset has not been
        /// cached yet on the main thread, the package defaults apply.
        /// </summary>
        internal static bool ShouldForward(MessageType type)
        {
            ConsoleContainerSettings settings = ConsoleContainerSettings.Active;

#if UNITY_EDITOR
            EditorForwardingMode mode = settings != null
                ? settings.EditorForwarding
                : ConsoleContainerSettings.DefaultEditorForwarding;

            if (mode == EditorForwardingMode.Never)
            {
                return false;
            }

            if (mode == EditorForwardingMode.DuringTestRuns && !_testRunActive)
            {
                return false;
            }

            if (settings == null)
            {
                return ConsoleContainerSettings.DefaultLogInEditor;
            }

            switch (type)
            {
                case MessageType.Text:
                    return settings.LogTextInEditor;
                case MessageType.Warning:
                    return settings.LogWarningsInEditor;
                case MessageType.Error:
                    return settings.LogErrorsInEditor;
                default:
                    return false;
            }
#else
            // No settings asset in the build => messages stay hidden.
            if (settings == null)
            {
                return false;
            }

            switch (type)
            {
                case MessageType.Text:
                    return settings.LogTextInBuild;
                case MessageType.Warning:
                    return settings.LogWarningsInBuild;
                case MessageType.Error:
                    return settings.LogErrorsInBuild;
                default:
                    return false;
            }
#endif
        }
    }
}
