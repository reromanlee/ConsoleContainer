#if UNITY_EDITOR
using System;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Single place that answers how much history the editor keeps — how many messages per instance,
    /// and whether each records its call stack — read from the active
    /// <see cref="ConsoleContainerSettings"/>, or the package defaults when there is none. Safe to call
    /// from any thread.
    /// </summary>
    internal static class EditorHistoryPolicy
    {
        /// <summary>Most messages each instance keeps before dropping the oldest; zero means unlimited.</summary>
        internal static int MessageLimit
        {
            get
            {
                ConsoleContainerSettings settings = ConsoleContainerSettings.Active;
                return settings != null
                    ? Math.Max(0, settings.MaxMessagesPerInstance)
                    : ConsoleContainerSettings.DefaultMaxMessagesPerInstance;
            }
        }

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
