using System;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Defines a contract for creating text messages, warning
    /// messages or error messages inside a container instance, and for
    /// observing the messages it produces.
    /// </summary>
    public interface IConsoleInstance
    {
        /// <summary>
        /// Raised for every created message, of any type. Raised on the logging
        /// thread — marshal to the main thread before touching Unity APIs — in
        /// both the editor and player builds.
        /// </summary>
        public event Action<ConsoleMessage> MessageCreated;

        /// <summary>
        /// Raised for <see cref="MessageType.Error"/> messages only, right after
        /// <see cref="MessageCreated"/>. Lets application code react to a failure
        /// (for example by showing a soft-crash screen with the message as the
        /// reason) without inspecting every message's type.
        /// </summary>
        public event Action<ConsoleMessage> ErrorCreated;

        public void CreateText(object source, params string[] messageContent);
        public void CreateText(string source, params string[] messageContent);

        public void CreateWarning(object source, params string[] messageContent);
        public void CreateWarning(string source, params string[] messageContent);

        public void CreateError(object source, params string[] messageContent);
        public void CreateError(string source, params string[] messageContent);
    }
}
