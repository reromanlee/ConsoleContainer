using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// A thread-safe container of log messages.
    ///
    /// In the Unity Editor, messages are stored and surfaced through the Console
    /// Viewer window. In a player build there is no viewer, so they are only
    /// forwarded to <see cref="Debug"/> according to
    /// <see cref="ConsoleContainerSettings"/> (and hidden entirely when no
    /// settings asset is present). Either way, <see cref="MessageCreated"/> and
    /// <see cref="ErrorCreated"/> let application code react to messages —
    /// turning a logged error into a soft crash screen, for example — and the
    /// static <see cref="Created"/> event reaches every instance at once, so a
    /// single subscriber can cover instances it never constructed.
    ///
    /// Every member is safe to call concurrently from any thread. Log through the
    /// <see cref="ConsoleInstanceExtensions"/> shortcuts (<c>CreateText</c>,
    /// <c>CreateWarning</c>, <c>CreateError</c>).
    /// </summary>
    public sealed class ConsoleInstance : IConsoleInstance
    {
        private const string MissingSource = "null";

        // How deeply handlers may log from inside other handlers on one thread before events stop
        // being raised. Generous for legitimate chains, far below any stack limit.
        private const int MaximumDispatchDepth = 8;

        private static int _instanceCounter;
        private static int _recursionReported;

        [ThreadStatic]
        private static int _dispatchDepth;

#if UNITY_EDITOR
        private readonly object _gate = new object();
        private readonly MessageBuffer _history = new MessageBuffer();
#endif

        private int _disposed;

        /// <summary>Display name shown in the viewer's instance dropdown.</summary>
        public string Name { get; }

        /// <summary>
        /// True once <see cref="Dispose"/> has been called. Disposed instances
        /// ignore further logging but keep any messages they already hold so they
        /// remain inspectable in the viewer (flagged "(disposed)") until they are
        /// cleared or the domain reloads.
        /// </summary>
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>
        /// Raised right after any instance finishes constructing, on the thread that constructed
        /// it. Lets one subscriber attach to every instance the application will ever create —
        /// including the ones built later by nested scopes — instead of each construction site
        /// having to remember to wire that subscriber up itself.
        ///
        /// The instance is fully built when this runs, so subscribing to
        /// <see cref="ErrorCreated"/> or <see cref="MessageCreated"/> from a handler is safe. A
        /// handler that throws is reported through <see cref="Debug.LogException(Exception)"/>
        /// without breaking the construction that triggered it.
        ///
        /// Subscriptions are static and live as long as the domain does: a static subscriber needs
        /// no unsubscribe, while one that is not static must detach or it keeps its target alive.
        ///
        /// <example>
        /// <code>
        /// ConsoleInstance.Created += instance => instance.ErrorCreated += SoftCrash.Show;
        /// </code>
        /// </example>
        /// </summary>
        public static event Action<IConsoleInstance> Created;

        /// <summary>
        /// Raised for every message this instance creates, of any type.
        ///
        /// Raised on the thread that logged the message — which may be a
        /// background thread — so a handler that touches the Unity API or UI must
        /// marshal to the main thread itself. Handlers run in both the editor and
        /// player builds, and an exception thrown by one is reported through
        /// <see cref="Debug.LogException(Exception)"/> without interrupting the
        /// logging call. All handlers are dropped on <see cref="Dispose"/>.
        ///
        /// <example>
        /// <code>
        /// console.MessageCreated += message => Telemetry.Record(message.Label);
        /// </code>
        /// </example>
        /// </summary>
        public event Action<ConsoleMessage> MessageCreated;

        /// <summary>
        /// Raised only for <see cref="ConsoleMessageType.Error"/> messages, right after
        /// <see cref="MessageCreated"/>. Convenience for the common case of
        /// reacting to failures without filtering by type; the same threading and
        /// exception rules as <see cref="MessageCreated"/> apply.
        ///
        /// <example>
        /// <code>
        /// console.ErrorCreated += message => SoftCrash.Show(message.Label);
        /// </code>
        /// </example>
        /// </summary>
        public event Action<ConsoleMessage> ErrorCreated;

        /// <summary>
        /// Creates a console instance with a generated "Instance N" name.
        /// Exists so dependency-injection containers, which cannot supply the
        /// optional name, can construct the type by convention.
        /// </summary>
        public ConsoleInstance() : this(null)
        {
        }

        /// <summary>
        /// Creates a new console instance.
        /// </summary>
        /// <param name="name">
        /// Display name for the viewer dropdown. When null or empty, a unique
        /// "Instance N" name is generated.
        /// </param>
        public ConsoleInstance(string name)
        {
            Name = string.IsNullOrEmpty(name)
                ? $"Instance {Interlocked.Increment(ref _instanceCounter)}"
                : name;

#if UNITY_EDITOR
            ConsoleRegistry.Register(this);
#endif

            // Raised last, so no subscriber can observe a half-built instance.
            RaiseCreated(this);
        }

        /// <summary>
        /// Whether a message of <paramref name="type"/> would have any effect right now; false once
        /// disposed. The editor keeps every message for the viewer, while a player build only acts on
        /// one that is forwarded to the Unity log or has a subscriber to receive it.
        /// </summary>
        public bool IsEnabled(ConsoleMessageType type)
        {
            if (IsDisposed)
            {
                return false;
            }

#if UNITY_EDITOR
            return true;
#else
            return HasListener(type) || UnityConsoleForwarding.ShouldForward(type);
#endif
        }

        /// <summary>
        /// Creates a message from an already resolved source name and content. Prefer the
        /// <see cref="ConsoleInstanceExtensions"/> shortcuts, which resolve the source and skip the
        /// work entirely when the message type is disabled.
        /// </summary>
        /// <param name="type">Severity of the message.</param>
        /// <param name="source">Name of whatever produced the message; "null" when empty.</param>
        /// <param name="content">Message body.</param>
        /// <param name="exception">
        /// Exception the message reports, if any. Its stack trace replaces the logging site's, and a
        /// forwarded error goes through <see cref="Debug.LogException(Exception)"/> so crash reporters
        /// hooked into the Unity log see a real exception.
        /// </param>
        [HideInCallstack]
        public void Create(ConsoleMessageType type, string source, string content, Exception exception = null)
        {
            if (IsDisposed)
            {
                return;
            }

            source = string.IsNullOrEmpty(source) ? MissingSource : source;
            content ??= string.Empty;

#if UNITY_EDITOR
            // The editor keeps every message for the viewer, so the entry is
            // always built.
            CallstackFrame[] callstack = exception != null
                ? CallstackCapture.FromException(exception)
                : EditorHistoryPolicy.ShouldCaptureCallstack(type)
                    ? CallstackCapture.FromCurrentThread()
                    : Array.Empty<CallstackFrame>();

            int limit = EditorHistoryPolicy.MessageLimit;
            ConsoleMessage message;

            // Built under the lock so sequence numbers are handed out in the order messages enter
            // the history; threads logging concurrently could otherwise store them out of order.
            lock (_gate)
            {
                message = new ConsoleMessage(this, type, source, content, exception, callstack);
                _history.Add(message, limit);
            }

            ConsoleRegistry.NotifyMessageAdded();

            if (UnityConsoleForwarding.ShouldForward(type))
            {
                ForwardToUnityConsole(type, message.Label, exception);
            }

            RaiseCreated(type, message);
#else
            if (UnityConsoleForwarding.ShouldForward(type))
            {
                ForwardToUnityConsole(type, ConsoleMessage.ComposeLabel(source, content), exception);
            }

            // Player builds keep no history, so the entry is only worth
            // allocating when something is actually listening for it.
            if (HasListener(type))
            {
                RaiseCreated(type, new ConsoleMessage(this, type, source, content, exception, null));
            }
#endif
        }

        /// <summary>
        /// Removes every message from this instance. Clearing an already disposed
        /// instance also drops it from the viewer, since nothing is left to
        /// inspect and a stale entry would only crowd the dropdown. Player builds
        /// keep no history, so there it does nothing.
        /// </summary>
        public void Clear()
        {
#if UNITY_EDITOR
            lock (_gate)
            {
                _history.Clear();
            }

            // Always reported as a clear, so every viewer drops what it copied even when the
            // instance is about to leave the registry.
            ConsoleRegistry.NotifyCleared(this);

            if (IsDisposed)
            {
                ConsoleRegistry.Unregister(this);
            }
#endif
        }

        /// <summary>
        /// Marks the instance as disposed so it ignores further logging and drops
        /// its event handlers.
        ///
        /// An instance that still holds messages keeps its place in the viewer
        /// (flagged "(disposed)") so its history stays inspectable after play mode
        /// ends; one with nothing to show is removed outright, which keeps
        /// repeated create/dispose cycles — test runs especially — from piling up
        /// empty entries. Everything is released on the next domain reload.
        /// </summary>
        public void Dispose()
        {
            // Exchange rather than a plain check so concurrent disposals cannot
            // both run the teardown below.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // Handlers (and whatever they capture) must not outlive the instance.
            MessageCreated = null;
            ErrorCreated = null;

#if UNITY_EDITOR
            if (HasMessages())
            {
                ConsoleRegistry.NotifyDisposed(this);
            }
            else
            {
                ConsoleRegistry.Unregister(this);
            }
#endif
        }

        // Handlers are copied out of the events first, so a concurrent unsubscribe cannot turn an
        // invocation into a null call.
        private void RaiseCreated(ConsoleMessageType type, ConsoleMessage message)
        {
            Action<ConsoleMessage> messageHandler = MessageCreated;
            Action<ConsoleMessage> errorHandler = type == ConsoleMessageType.Error ? ErrorCreated : null;

            if ((messageHandler == null && errorHandler == null) || !TryEnterDispatch())
            {
                return;
            }

            try
            {
                Invoke(messageHandler, message);
                Invoke(errorHandler, message);
            }
            finally
            {
                _dispatchDepth--;
            }
        }

        private static void RaiseCreated(IConsoleInstance instance)
        {
            Action<IConsoleInstance> handler = Created;

            if (handler == null || !TryEnterDispatch())
            {
                return;
            }

            try
            {
                handler(instance);
            }
            catch (Exception exception)
            {
                // A faulty subscriber must never break the construction that triggered it.
                Debug.LogException(exception);
            }
            finally
            {
                _dispatchDepth--;
            }
        }

        // A handler that logs into what it observes (or constructs an instance from Created) would
        // recurse until the stack overflows, which no catch can stop and which takes the whole
        // process down. Nesting is therefore capped per thread: deeper messages are still kept and
        // forwarded, only their events are skipped.
        private static bool TryEnterDispatch()
        {
            if (_dispatchDepth >= MaximumDispatchDepth)
            {
                ReportRecursion();
                return false;
            }

            _dispatchDepth++;
            return true;
        }

        private static void ReportRecursion()
        {
            if (Interlocked.Exchange(ref _recursionReported, 1) != 0)
            {
                return;
            }

            Debug.LogWarning(
                $"ConsoleContainer: event handlers kept logging from inside other handlers more than {MaximumDispatchDepth} " +
                "levels deep. Events for the deeper messages were skipped to prevent a stack overflow; the messages " +
                "themselves are still kept. This is reported once per session.");
        }

        // The handler is copied into a parameter before it is called, so a
        // concurrent unsubscribe cannot turn the invocation into a null call.
        private static void Invoke(Action<ConsoleMessage> handler, ConsoleMessage message)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(message);
            }
            catch (Exception exception)
            {
                // A faulty subscriber must never break the logging call that
                // triggered it. Report it through Unity's exception channel
                // instead, the way an unhandled event handler is normally
                // surfaced.
                Debug.LogException(exception);
            }
        }

        [HideInCallstack]
        private static void ForwardToUnityConsole(ConsoleMessageType type, string label, Exception exception)
        {
            if (exception != null && type == ConsoleMessageType.Error)
            {
                Debug.LogException(exception);
                return;
            }

            switch (type)
            {
                case ConsoleMessageType.Text:
                    Debug.Log(label);
                    break;
                case ConsoleMessageType.Warning:
                    Debug.LogWarning(label);
                    break;
                case ConsoleMessageType.Error:
                    Debug.LogError(label);
                    break;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Copies every message added since <paramref name="cursor"/> into
        /// <paramref name="destination"/>, oldest first, and returns the cursor to resume from.
        /// <paramref name="startPosition"/> receives the cursor of the oldest message still held,
        /// so a reader can tell which of its copies the history has dropped since.
        /// </summary>
        internal long CollectSince(long cursor, List<ConsoleMessage> destination, out long startPosition)
        {
            lock (_gate)
            {
                startPosition = _history.StartPosition;
                return _history.CopySince(cursor, destination);
            }
        }

        /// <summary>
        /// Copies messages created after <paramref name="afterSequence"/> into
        /// <paramref name="buffer"/>. Consumed by the viewer for incremental
        /// appends; the caller sorts the merged result by
        /// <see cref="ConsoleMessage.Sequence"/>.
        /// </summary>
        internal void CollectMessagesAfter(long afterSequence, List<ConsoleMessage> buffer)
        {
            lock (_gate)
            {
                // History is kept oldest first and therefore sorted ascending
                // by Sequence, so the new messages are a contiguous suffix.
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    ConsoleMessage message = _history[i];
                    if (message.Sequence <= afterSequence)
                    {
                        break;
                    }

                    buffer.Add(message);
                }
            }
        }

        private bool HasMessages()
        {
            lock (_gate)
            {
                return _history.Count > 0;
            }
        }
#else
        // Cheap pre-check that keeps a build from allocating a ConsoleMessage
        // nobody would receive.
        private bool HasListener(ConsoleMessageType type)
            => MessageCreated != null || (type == ConsoleMessageType.Error && ErrorCreated != null);
#endif
    }
}
