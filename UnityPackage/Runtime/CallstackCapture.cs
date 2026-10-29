#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Editor-only capture of source-resolvable call stacks for the Console Viewer.
    ///
    /// Messages logged from the same places repeat the same frames endlessly, so per-method
    /// details and file paths are resolved once and shared: a retained message only pays for its
    /// own frame array, never for another copy of the same strings. The attribute lookup that
    /// decides whether a frame is hidden runs once per method for the same reason.
    /// </summary>
    internal static class CallstackCapture
    {
        private const char GeneratedNameStart = '<';
        private const char GeneratedNameEnd = '>';
        private const string StateMachineMethod = "MoveNext";

        // Bounds the inner-exception walk, which also guards against a pathological cycle.
        private const int MaximumExceptionDepth = 16;

        private static readonly ConcurrentDictionary<MethodBase, MethodDetails> Methods =
            new ConcurrentDictionary<MethodBase, MethodDetails>();

        private static readonly ConcurrentDictionary<string, string> FilePaths =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        // Cached delegate: a method group passed to GetOrAdd would allocate on every call.
        private static readonly Func<MethodBase, MethodDetails> Describe = DescribeMethod;

        [ThreadStatic]
        private static List<CallstackFrame> _scratch;

        /// <summary>Captures the calling thread's stack, without the logging methods themselves.</summary>
        internal static CallstackFrame[] FromCurrentThread()
        {
            List<CallstackFrame> resolved = BeginCapture();
            Append(new StackTrace(1, true).GetFrames(), resolved);
            return EndCapture(resolved);
        }

        /// <summary>
        /// Captures where <paramref name="exception"/> was thrown, innermost exception first, so the
        /// top frame is the original throw site — the order .NET itself prints them in. An exception
        /// that was never thrown carries no stack, and falls back to the logging site instead.
        /// </summary>
        internal static CallstackFrame[] FromException(Exception exception)
        {
            List<CallstackFrame> resolved = BeginCapture();

            Exception[] chain = new Exception[MaximumExceptionDepth];
            int depth = 0;
            for (Exception current = exception; current != null && depth < chain.Length; current = current.InnerException)
            {
                chain[depth++] = current;
            }

            for (int i = depth - 1; i >= 0; i--)
            {
                Append(new StackTrace(chain[i], true).GetFrames(), resolved);
            }

            if (resolved.Count == 0)
            {
                Append(new StackTrace(1, true).GetFrames(), resolved);
            }

            return EndCapture(resolved);
        }

        private static List<CallstackFrame> BeginCapture()
        {
            List<CallstackFrame> resolved = _scratch ??= new List<CallstackFrame>(32);
            resolved.Clear();
            return resolved;
        }

        private static CallstackFrame[] EndCapture(List<CallstackFrame> resolved)
        {
            CallstackFrame[] result = resolved.Count == 0 ? Array.Empty<CallstackFrame>() : resolved.ToArray();
            resolved.Clear();
            return result;
        }

        private static void Append(StackFrame[] frames, List<CallstackFrame> resolved)
        {
            if (frames == null)
            {
                return;
            }

            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                if (method == null)
                {
                    continue;
                }

                MethodDetails details = Methods.GetOrAdd(method, Describe);
                if (details.IsHidden)
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

                resolved.Add(new CallstackFrame(FilePaths.GetOrAdd(file, file), line, details.DisplayName));
            }
        }

        private static MethodDetails DescribeMethod(MethodBase method)
        {
            Type declaringType = method.DeclaringType;

            // The logging path itself is skipped, so the top frame is the caller's log site, and so is
            // anything marked [HideInCallstack] — the same attribute the Unity Console honours — which
            // lets a project's own logging wrappers drop out too. Filtering by namespace instead would
            // wrongly drop user code that lives under it (samples).
            bool isHidden = declaringType == typeof(ConsoleInstance)
                            || declaringType == typeof(ConsoleInstanceExtensions)
                            || declaringType == typeof(CallstackCapture)
                            || method.IsDefined(typeof(UnityEngine.HideInCallstackAttribute), false);

            return new MethodDetails(ResolveMethodName(declaringType, method), isHidden);
        }

        // Compiler-generated frames read as the method they came from: an iterator or async state
        // machine ("<Load>d__4.MoveNext") and a lambda ("<>c.<Start>b__0_0") both name their
        // source method on the nearest type a developer actually wrote.
        private static string ResolveMethodName(Type declaringType, MethodBase method)
        {
            string methodName = method.Name;
            if (declaringType == null)
            {
                return methodName;
            }

            if (TryExtractSourceName(methodName, out string lambdaOwner))
            {
                methodName = lambdaOwner;
            }

            Type type = declaringType;
            while (type != null && type.Name.Length > 0 && type.Name[0] == GeneratedNameStart)
            {
                if (method.Name == StateMachineMethod && TryExtractSourceName(type.Name, out string stateMachineOwner))
                {
                    methodName = stateMachineOwner;
                }

                type = type.DeclaringType;
            }

            return string.Concat(SourceNames.Of(type ?? declaringType), ".", methodName);
        }

        private static bool TryExtractSourceName(string generatedName, out string sourceName)
        {
            sourceName = null;
            if (generatedName.Length < 3 || generatedName[0] != GeneratedNameStart)
            {
                return false;
            }

            // "<>c" display classes carry no name between the brackets.
            int end = generatedName.IndexOf(GeneratedNameEnd);
            if (end <= 1)
            {
                return false;
            }

            sourceName = generatedName.Substring(1, end - 1);
            return true;
        }

        private readonly struct MethodDetails
        {
            public readonly string DisplayName;
            public readonly bool IsHidden;

            public MethodDetails(string displayName, bool isHidden)
            {
                DisplayName = displayName;
                IsHidden = isHidden;
            }
        }
    }
}
#endif
