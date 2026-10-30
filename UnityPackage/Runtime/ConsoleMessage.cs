using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// An immutable log entry produced by an <see cref="IConsoleInstance"/>.
    /// Instances are safe to read from any thread once constructed.
    /// </summary>
    public sealed class ConsoleMessage
    {
        private const string TimeFormat = "[HH:mm:ss]";
        private const string LabelSeparator = ": ";

        private static long _globalSequence;

        // Derived text is built on first use and kept, so rows being re-bound or handlers
        // formatting the same message allocate nothing more. Racing threads can only ever
        // store equal strings, so no lock is needed.
        private string label;
        private string timeText;

        /// <summary>
        /// Globally monotonic creation order. Because it is assigned atomically
        /// the moment a message is built, sorting several instances' messages by
        /// <see cref="Sequence"/> yields a stable chronological ("all instances")
        /// ordering without relying on wall-clock resolution.
        /// </summary>
        public long Sequence { get; }

        /// <summary>Severity of the message.</summary>
        public ConsoleMessageType Type { get; }

        /// <summary>Local time at which the message was created.</summary>
        public DateTime Timestamp { get; }

        /// <summary>The resolved source name (a string, or an object's type name).</summary>
        public string Source { get; }

        /// <summary>The message body (all content arguments joined by single spaces).</summary>
        public string Content { get; }

        /// <summary>
        /// Source-resolvable call stack, innermost caller first — the exception's own stack when
        /// <see cref="Exception"/> is set. Empty in player builds.
        /// </summary>
        public IReadOnlyList<CallstackFrame> Callstack { get; }

        /// <summary>
        /// The exception this message reports, or null. Handed to handlers as is, so crash reporters
        /// receive the original object; in the editor it stays referenced while the message is kept.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>The instance that created the message.</summary>
        internal ConsoleInstance Owner { get; }

        /// <summary>Cursor of the message within its owner's history; assigned when it is stored.</summary>
        internal long Position { get; set; }

        internal ConsoleMessage(ConsoleInstance owner, ConsoleMessageType type, string source, string content, Exception exception, IReadOnlyList<CallstackFrame> callstack)
        {
            Owner = owner;
            Timestamp = DateTime.Now;
            Sequence = Interlocked.Increment(ref _globalSequence);
            Type = type;
            Source = source ?? string.Empty;
            Content = content ?? string.Empty;
            Exception = exception;
            Callstack = callstack ?? Array.Empty<CallstackFrame>();
        }

        /// <summary>Timestamp as "[HH:mm:ss]", formatted identically on every machine regardless of culture.</summary>
        public string TimeText => timeText ??= Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// "{source}: {content}", or just the source when there is no content. The same text is shown
        /// in the viewer and written to the Unity log when a message is forwarded.
        /// </summary>
        public string Label => label ??= ComposeLabel(Source, Content);

        internal static string ComposeLabel(string source, string content)
            => string.IsNullOrEmpty(content) ? source : string.Concat(source, LabelSeparator, content);
    }
}
