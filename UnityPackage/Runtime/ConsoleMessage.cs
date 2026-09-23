using System;
using System.Collections.Generic;
using System.Threading;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// An immutable log entry produced by an <see cref="IConsoleInstance"/>.
    /// Instances are safe to read from any thread once constructed.
    /// </summary>
    public sealed class ConsoleMessage
    {
        private static long _globalSequence;

        /// <summary>
        /// Globally monotonic creation order. Because it is assigned atomically
        /// the moment a message is built, sorting several instances' messages by
        /// <see cref="Sequence"/> yields a stable chronological ("all instances")
        /// ordering without relying on wall-clock resolution.
        /// </summary>
        public long Sequence { get; }

        public MessageType Type { get; }

        public DateTime Timestamp { get; }

        /// <summary>The resolved source name (a string, or an object's type name).</summary>
        public string Source { get; }

        /// <summary>The message body (all content arguments joined by single spaces).</summary>
        public string Content { get; }

        /// <summary>Source-resolvable call stack, innermost caller first. Empty in player builds.</summary>
        public IReadOnlyList<CallstackFrame> Callstack { get; }

        public ConsoleMessage(MessageType type, string source, string content, IReadOnlyList<CallstackFrame> callstack)
        {
            Timestamp = DateTime.Now;
            Sequence = Interlocked.Increment(ref _globalSequence);
            Type = type;
            Source = source ?? string.Empty;
            Content = content ?? string.Empty;
            Callstack = callstack ?? Array.Empty<CallstackFrame>();
        }

        /// <summary>Timestamp rendered as "[HH:mm:ss]" for the <c>message-time</c> label.</summary>
        public string TimeText => Timestamp.ToString("[HH:mm:ss]");

        /// <summary>Full "{source}: {content}" text used by both list rows and the details pane.</summary>
        public string Label => $"{Source}: {Content}";
    }
}
