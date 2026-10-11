namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Decides when ConsoleContainer messages are <b>also</b> forwarded to the
    /// Unity console while running inside the editor.
    ///
    /// Forwarding exists because some editor tooling — most notably the Test
    /// Runner window — only shows entries that pass through Unity's log handler.
    /// Messages always reach the Console Viewer regardless of this setting.
    /// </summary>
    public enum EditorForwardingMode
    {
        /// <summary>
        /// Never forward. Messages live exclusively in the Console Viewer, which
        /// keeps the Unity Console clean but hides them from the Test Runner.
        /// </summary>
        Never = 0,

        /// <summary>
        /// Forward only while a test run is in progress, so results show up in
        /// the Test Runner window and the Unity Console stays clean the rest of
        /// the time. Requires the Unity Test Framework package.
        /// </summary>
        DuringTestRuns = 1,

        /// <summary>
        /// Always forward, mirroring every message into the Unity Console on top
        /// of the Console Viewer.
        /// </summary>
        Always = 2
    }
}
