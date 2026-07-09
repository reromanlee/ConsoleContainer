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
        /// True once <see cref="Dispose"/> has been called. Disposed instances
        /// ignore further logging but keep their messages so they remain
        /// inspectable in the viewer (flagged "(disposed)") until the next
        /// domain reload.
        /// </summary>
        public bool IsDisposed => _disposed;

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

        /// <summary>
        /// Marks the instance as disposed so it ignores further logging. Its
        /// messages and its place in the viewer are intentionally kept (and
        /// flagged "(disposed)") so they stay inspectable after play mode ends;
        /// everything is released on the next domain reload.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

#if UNITY_EDITOR
            ConsoleRegistry.NotifyDisposed(this);
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

                // Skip only this class's own logging methods so the top frame is
                // the caller's real log site. Filtering by the package namespace
                // would wrongly drop user code that lives under it (e.g. samples).
                if (declaringType == typeof(ConsoleInstance))
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
