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
    /// Viewer window only — they never reach the Unity Console. In a player
    /// build, messages are forwarded to <see cref="Debug"/> according to
    /// <see cref="ConsoleContainerSettings"/> (and hidden entirely when no
    /// settings asset is present).
    ///
    /// Every <c>Create*</c> method is safe to call concurrently from any thread.
    /// </summary>
    public sealed class ConsoleInstance : IConsoleInstance, IDisposable
    {
        private static int _instanceCounter;

        private readonly object _gate = new object();
        private readonly List<ConsoleMessage> _messages = new List<ConsoleMessage>();

        private volatile bool _disposed;

        /// <summary>Display name shown in the viewer's instance dropdown.</summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new console instance.
        /// </summary>
        /// <param name="name">
        /// Optional display name for the viewer dropdown. When omitted, a unique
        /// "Instance N" name is generated.
        /// </param>
        public ConsoleInstance(string name = null)
        {
            Name = string.IsNullOrEmpty(name)
                ? $"Instance {Interlocked.Increment(ref _instanceCounter)}"
                : name;

#if UNITY_EDITOR
            ConsoleRegistry.Register(this);
#endif
        }

        public void CreateText(object source, params string[] messageContent)
            => Create(MessageType.Text, ResolveSource(source), messageContent);

        public void CreateText(string source, params string[] messageContent)
            => Create(MessageType.Text, ResolveSource(source), messageContent);

        public void CreateWarning(object source, params string[] messageContent)
            => Create(MessageType.Warning, ResolveSource(source), messageContent);

        public void CreateWarning(string source, params string[] messageContent)
            => Create(MessageType.Warning, ResolveSource(source), messageContent);

        public void CreateError(object source, params string[] messageContent)
            => Create(MessageType.Error, ResolveSource(source), messageContent);

        public void CreateError(string source, params string[] messageContent)
            => Create(MessageType.Error, ResolveSource(source), messageContent);

        /// <summary>Removes every message from this instance.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _messages.Clear();
            }

#if UNITY_EDITOR
            ConsoleRegistry.NotifyCleared(this);
#endif
        }

        /// <summary>Clears the instance and detaches it from the viewer registry.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_gate)
            {
                _messages.Clear();
            }

#if UNITY_EDITOR
            ConsoleRegistry.Unregister(this);
#endif
        }

        private void Create(MessageType type, string source, string[] messageContent)
        {
            if (_disposed)
            {
                return;
            }

            string content = BuildContent(messageContent);

#if UNITY_EDITOR
            ConsoleMessage message = new ConsoleMessage(type, source, content, CaptureCallstack());

            lock (_gate)
            {
                _messages.Add(message);
            }

            ConsoleRegistry.NotifyMessageAdded(this);
#else
            ForwardToUnityConsole(type, source, content);
#endif
        }

        private static string ResolveSource(object source)
            => source?.GetType().Name ?? "null";

        private static string ResolveSource(string source)
            => string.IsNullOrEmpty(source) ? "null" : source;

        private static string BuildContent(string[] messageContent)
            => messageContent == null || messageContent.Length == 0
                ? string.Empty
                : string.Join(" ", messageContent);

#if UNITY_EDITOR
        private static readonly string PackageNamespace = typeof(ConsoleInstance).Namespace;

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

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _messages.Count;
                }
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

                // Skip this package's own logging plumbing so the top frame is the
                // caller's actual log site.
                string ns = declaringType?.Namespace;
                if (ns != null && ns.StartsWith(PackageNamespace, StringComparison.Ordinal))
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

                string methodName = declaringType != null
                    ? $"{declaringType.Name}.{method.Name}"
                    : method?.Name;

                result.Add(new CallstackFrame(file, line, methodName));
            }

            return result.ToArray();
        }
#else
        private static void ForwardToUnityConsole(MessageType type, string source, string content)
        {
            ConsoleContainerSettings settings = ConsoleContainerSettings.Active;
            if (settings == null)
            {
                // No settings asset in the build => messages stay hidden.
                return;
            }

            string formatted = $"{source}: {content}";
            switch (type)
            {
                case MessageType.Text:
                    if (settings.LogTextInBuild) Debug.Log(formatted);
                    break;
                case MessageType.Warning:
                    if (settings.LogWarningsInBuild) Debug.LogWarning(formatted);
                    break;
                case MessageType.Error:
                    if (settings.LogErrorsInBuild) Debug.LogError(formatted);
                    break;
            }
        }
#endif
    }
}
