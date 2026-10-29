#if UNITY_EDITOR
namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Single place that answers how much detail the editor keeps for each message, read from the
    /// active <see cref="ConsoleContainerSettings"/> or the package defaults when there is none.
    /// Safe to call from any thread.
    /// </summary>
    internal static class EditorHistoryPolicy
    {
        /// <summary>Whether a message of <paramref name="type"/> records the call stack that logged it.</summary>
        internal static bool ShouldCaptureCallstack(ConsoleMessageType type)
        {
            ConsoleContainerSettings settings = ConsoleContainerSettings.Active;
            if (settings == null)
            {
                return ConsoleContainerSettings.DefaultCaptureCallstack;
            }

            switch (type)
            {
                case ConsoleMessageType.Text:
                    return settings.CaptureTextCallstack;
                case ConsoleMessageType.Warning:
                    return settings.CaptureWarningCallstack;
                case ConsoleMessageType.Error:
                    return settings.CaptureErrorCallstack;
                default:
                    return false;
            }
        }
    }
}
#endif
