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
    /// own frame array, never for another copy of the same strings.
    /// </summary>
    internal static class CallstackCapture
    {
        private const char GeneratedNameStart = '<';
        private const char GeneratedNameEnd = '>';
        private const string StateMachineMethod = "MoveNext";

        private static readonly ConcurrentDictionary<MethodBase, MethodDetails> Methods =
            new ConcurrentDictionary<MethodBase, MethodDetails>();

        private static readonly ConcurrentDictionary<string, string> FilePaths =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        // Cached delegate: a method group passed to GetOrAdd would allocate on every call.
        private static readonly Func<MethodBase, MethodDetails> Describe = DescribeMethod;

        [ThreadStatic]
        private static List<CallstackFrame> _scratch;

        /// <summary>Captures the calling thread's stack, without the logging methods themselves.</summary>
        internal static CallstackFrame[] FromCurrentThread() => Convert(new StackTrace(1, true).GetFrames());

        private static CallstackFrame[] Convert(StackFrame[] frames)
        {
            if (frames == null || frames.Length == 0)
            {
                return Array.Empty<CallstackFrame>();
            }

            List<CallstackFrame> resolved = _scratch ??= new List<CallstackFrame>(frames.Length);
            resolved.Clear();

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

            CallstackFrame[] result = resolved.Count == 0 ? Array.Empty<CallstackFrame>() : resolved.ToArray();
            resolved.Clear();
            return result;
        }

        private static MethodDetails DescribeMethod(MethodBase method)
        {
            Type declaringType = method.DeclaringType;

            // Only the logging path itself is skipped, so the top frame is the caller's log site.
            // Filtering by namespace would wrongly drop user code that lives under it (samples).
            bool isHidden = declaringType == typeof(ConsoleInstance)
                            || declaringType == typeof(ConsoleInstanceExtensions)
                            || declaringType == typeof(CallstackCapture);

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
