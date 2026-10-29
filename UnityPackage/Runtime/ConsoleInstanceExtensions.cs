using System;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Logging shortcuts for any <see cref="IConsoleInstance"/>.
    ///
    /// The <c>source</c> names whatever produced the message: a <see cref="string"/> is used as is,
    /// and any other object contributes its type name. Content arguments are joined with single
    /// spaces. Nothing is built when the instance reports the message type as disabled.
    /// </summary>
    public static class ConsoleInstanceExtensions
    {
        private const string ContentSeparator = " ";

        public static void CreateText(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Text, source, message);

        public static void CreateText(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Text, source, messageContent);

        public static void CreateWarning(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Warning, source, message);

        public static void CreateWarning(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Warning, source, messageContent);

        public static void CreateError(this IConsoleInstance console, object source, string message)
            => CreateSingle(console, ConsoleMessageType.Error, source, message);

        public static void CreateError(this IConsoleInstance console, object source, params string[] messageContent)
            => CreateJoined(console, ConsoleMessageType.Error, source, messageContent);

        private static void CreateSingle(IConsoleInstance console, ConsoleMessageType type, object source, string message)
        {
            if (console == null)
            {
                throw new ArgumentNullException(nameof(console));
            }

            if (console.IsEnabled(type))
            {
                console.Create(type, ResolveSource(source), message ?? string.Empty);
            }
        }

        private static void CreateJoined(IConsoleInstance console, ConsoleMessageType type, object source, string[] messageContent)
        {
            if (console == null)
            {
                throw new ArgumentNullException(nameof(console));
            }

            if (console.IsEnabled(type))
            {
                console.Create(type, ResolveSource(source), Join(messageContent));
            }
        }

        private static string ResolveSource(object source)
            => source is string text ? text : source?.GetType().Name;

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
