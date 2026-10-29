using System;
using System.Collections.Generic;
using System.Threading;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using System.Diagnostics;
#endif

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

        // Above this many entries, a cleared instance releases its backing array
        // instead of holding on to capacity it is unlikely to need again.
        private const int ClearedCapacityTrimThreshold = 1024;

        private static int _instanceCounter;

        private readonly object _gate = new object();
        private readonly List<ConsoleMessage> _messages = new List<ConsoleMessage>();

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
        public void Create(ConsoleMessageType type, string source, string content)
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
            ConsoleMessage message = new ConsoleMessage(type, source, content, CaptureCallstack());

            lock (_gate)
            {
                _messages.Add(message);
            }

            ConsoleRegistry.NotifyMessageAdded(this);

            if (UnityConsoleForwarding.ShouldForward(type))
            {
                ForwardToUnityConsole(type, source, content);
            }

            RaiseCreated(type, message);
#else
            if (UnityConsoleForwarding.ShouldForward(type))
            {
                ForwardToUnityConsole(type, source, content);
            }

            // Player builds keep no history, so the entry is only worth
            // allocating when something is actually listening for it.
            if (HasListener(type))
            {
                RaiseCreated(type, new ConsoleMessage(type, source, content, null));
            }
#endif
        }

        /// <summary>
        /// Removes every message from this instance. Clearing an already disposed
        /// instance also drops it from the viewer, since nothing is left to
        /// inspect and a stale entry would only crowd the dropdown.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _messages.Clear();

                if (_messages.Capacity > ClearedCapacityTrimThreshold)
                {
                    _messages.Capacity = 0;
                }
            }

#if UNITY_EDITOR
            if (IsDisposed)
            {
                ConsoleRegistry.Unregister(this);
            }
            else
            {
                ConsoleRegistry.NotifyCleared(this);
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

        private void RaiseCreated(ConsoleMessageType type, ConsoleMessage message)
        {
            Invoke(MessageCreated, message);

            if (type == ConsoleMessageType.Error)
            {
                Invoke(ErrorCreated, message);
            }
        }

        // Mirrors Invoke below: the handler is copied out of the static event first, so a
        // concurrent unsubscribe cannot turn the invocation into a null call.
        private static void RaiseCreated(IConsoleInstance instance)
        {
            Action<IConsoleInstance> handler = Created;

            if (handler == null)
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

        private static void ForwardToUnityConsole(ConsoleMessageType type, string source, string content)
        {
            string formatted = $"{source}: {content}";

            switch (type)
            {
                case ConsoleMessageType.Text:
                    Debug.Log(formatted);
                    break;
                case ConsoleMessageType.Warning:
                    Debug.LogWarning(formatted);
                    break;
                case ConsoleMessageType.Error:
                    Debug.LogError(formatted);
                    break;
            }
        }

#if UNITY_EDITOR
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
                // _messages is append-only and therefore already sorted ascending
                // by Sequence, so the new messages are a contiguous suffix.
                for (int i = _messages.Count - 1; i >= 0; i--)
                {
                    if (_messages[i].Sequence <= afterSequence)
                    {
                        break;
                    }

                    buffer.Add(_messages[i]);
                }
            }
        }

        private bool HasMessages()
        {
            lock (_gate)
            {
                return _messages.Count > 0;
            }
        }

        private static CallstackFrame[] CaptureCallstack()
        {
            StackFrame[] frames = new StackTrace(true).GetFrames();
            if (frames == null)
            {
                return Array.Empty<CallstackFrame>();
            }

            List<CallstackFrame> result = new List<CallstackFrame>(frames.Length);
            foreach (StackFrame frame in frames)
            {
                System.Reflection.MethodBase method = frame.GetMethod();
                Type declaringType = method?.DeclaringType;

                // Skip only the logging methods themselves so the top frame is
                // the caller's real log site. Filtering by the package namespace
                // would wrongly drop user code that lives under it (e.g. samples).
                if (declaringType == typeof(ConsoleInstance) || declaringType == typeof(ConsoleInstanceExtensions))
                {
                    continue;
                }

                string file = frame.GetFileName();
                int line = frame.GetFileLineNumber();
                if (string.IsNullOrEmpty(file) || line <= 0)
                {
                    // Engine/native frame with no source info — can't open in an IDE.
                    continue;
                }

                result.Add(new CallstackFrame(file, line, ResolveMethodName(declaringType, method)));
            }

            return result.ToArray();
        }

        // Presents compiler-generated iterator/async frames ("<Method>d__N.MoveNext")
        // using their original source method name.
        private static string ResolveMethodName(Type declaringType, System.Reflection.MethodBase method)
        {
            if (declaringType == null)
            {
                return method?.Name;
            }

            string typeName = declaringType.Name;
            string methodName = method?.Name;

            if (typeName.Length > 0 && typeName[0] == '<')
            {
                int end = typeName.IndexOf('>');
                if (end > 1)
                {
                    methodName = typeName.Substring(1, end - 1);
                    Type outer = declaringType.DeclaringType;
                    typeName = outer != null ? outer.Name : typeName;
                }
            }

            return $"{typeName}.{methodName}";
        }
#else
        // Cheap pre-check that keeps a build from allocating a ConsoleMessage
        // nobody would receive.
        private bool HasListener(ConsoleMessageType type)
            => MessageCreated != null || (type == ConsoleMessageType.Error && ErrorCreated != null);
#endif
    }
}
