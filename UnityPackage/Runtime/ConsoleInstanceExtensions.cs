using System;
using UnityEngine;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Logging shortcuts for any <see cref="IConsoleInstance"/>.
    ///
    /// The <c>source</c> names whatever produced the message: a <see cref="string"/> is used as is,
    /// a <see cref="Type"/> contributes its own name (handy in static classes), and any other object
    /// contributes its type's name. Content arguments are joined with single spaces. Nothing is
    /// built when the instance reports the message type as disabled.
    /// </summary>
    public static class ConsoleInstanceExtensions
    {
        private const string ContentSeparator = " ";

        [HideInCallstack]
        public static void CreateText(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Text, source, message);

        [HideInCallstack]
        public static void CreateText(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Text, source, messageContent);

        [HideInCallstack]
        public static void CreateWarning(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Warning, source, message);

        [HideInCallstack]
        public static void CreateWarning(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Warning, source, messageContent);

        [HideInCallstack]
        public static void CreateError(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Error, source, message);

        [HideInCallstack]
        public static void CreateError(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Error, source, messageContent);

        [HideInCallstack]
        private static void CreateSingle(IConsoleInstance console, ConsoleMessageType type, object source, string message)
        {
            if (console == null)
            {
                throw new ArgumentNullException(nameof(console));
            }

            if (console.IsEnabled(type))
            {
                console.Create(type, SourceNames.Resolve(source), message ?? string.Empty);
            }
        }

        [HideInCallstack]
        private static void CreateJoined(IConsoleInstance console, ConsoleMessageType type, object source, string[] messageContent)
        {
            if (console == null)
            {
                throw new ArgumentNullException(nameof(console));
            }

            if (console.IsEnabled(type))
            {
                console.Create(type, SourceNames.Resolve(source), Join(messageContent));
            }
        }

        private static string Join(string[] messageContent)
        {
            if (messageContent == null || messageContent.Length == 0)
            {
                return string.Empty;
            }

            return messageContent.Length == 1
                ? messageContent[0] ?? string.Empty
                : string.Join(ContentSeparator, messageContent);
        }
    }
}
