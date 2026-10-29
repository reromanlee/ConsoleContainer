using System;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// A named container of log messages that can be observed as messages are created.
    ///
    /// Implementations only provide the core members below. The <c>CreateText</c>,
    /// <c>CreateWarning</c> and <c>CreateError</c> shortcuts live in
    /// <see cref="ConsoleInstanceExtensions"/>, so they work with any implementation and new
    /// overloads never break one.
    /// </summary>
    public interface IConsoleInstance : IDisposable
    {
        /// <summary>Display name shown in the Console Viewer's instance dropdown.</summary>
        string Name { get; }

        /// <summary>True once <see cref="IDisposable.Dispose"/> has been called; further messages are ignored.</summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Raised for every created message, of any type. Raised on the logging
        /// thread — marshal to the main thread before touching Unity APIs — in
        /// both the editor and player builds.
        /// </summary>
        event Action<ConsoleMessage> MessageCreated;

        /// <summary>
        /// Raised for <see cref="ConsoleMessageType.Error"/> messages only, right after
        /// <see cref="MessageCreated"/>. Lets application code react to a failure
        /// (for example by showing a soft-crash screen with the message as the
        /// reason) without inspecting every message's type.
        /// </summary>
        event Action<ConsoleMessage> ErrorCreated;

        /// <summary>
        /// Whether a message of <paramref name="type"/> would have any effect right now. Callers use it
        /// to skip building message content that would only be thrown away.
        /// </summary>
        bool IsEnabled(ConsoleMessageType type);

        /// <summary>Creates a message from an already resolved source name and content.</summary>
        /// <param name="type">Severity of the message.</param>
        /// <param name="source">Name of whatever produced the message.</param>
        /// <param name="content">Message body.</param>
        void Create(ConsoleMessageType type, string source, string content);

        /// <summary>Removes every message this instance holds.</summary>
        void Clear();
    }
}
