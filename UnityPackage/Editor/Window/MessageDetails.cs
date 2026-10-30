using System;
using System.Text;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// Builds the text the viewer shows for a message: the one-line form used by list rows, and the
    /// full details — message, exception causes and a readable stack trace — used by the details
    /// pane and the Copy action.
    /// </summary>
    internal static class MessageDetails
    {
        /// <summary>
        /// Characters of message text the details pane renders. A single UI Toolkit label stops
        /// drawing at roughly twelve thousand glyphs, so longer text is cut with a notice; Copy
        /// always includes all of it.
        /// </summary>
        internal const int DisplayTextLimit = 6000;

        /// <summary>Stack frames the details pane lists as text; every frame still gets a button.</summary>
        internal const int DisplayFrameLimit = 64;

        // A row shows one line; anything past this is never visible, so it is not rendered.
        private const int RowTextLimit = 300;

        // Bounds the inner-exception walk, which also guards against a pathological cycle.
        private const int MaximumCauseDepth = 16;

        private const string Ellipsis = "…";

        private static readonly char[] LineBreaks = { '\r', '\n' };

        private static string _projectRoot;

        /// <summary>The message's first line, cut to what a row can show.</summary>
        internal static string RowText(ConsoleMessage message)
        {
            string label = message.Label;
            int lineEnd = label.IndexOfAny(LineBreaks);
            int length = Math.Min(lineEnd >= 0 ? lineEnd : label.Length, RowTextLimit);

            return length == label.Length ? label : string.Concat(label.Substring(0, length), Ellipsis);
        }

        /// <summary>Full details shown in the pane, cut to what one label can render.</summary>
        internal static string ForDisplay(ConsoleMessage message) => Build(message, DisplayTextLimit, DisplayFrameLimit);

        /// <summary>Full, untruncated details for the clipboard.</summary>
        internal static string ForClipboard(ConsoleMessage message) => Build(message, int.MaxValue, int.MaxValue);

        private static string Build(ConsoleMessage message, int textLimit, int frameLimit)
        {
            StringBuilder builder = new StringBuilder();

            string label = message.Label;
            if (label.Length > textLimit)
            {
                builder.Append(label, 0, textLimit)
                    .Append('\n').Append(Ellipsis).Append(" [")
                    .Append(label.Length - textLimit)
                    .Append(" more characters; Copy includes the full text]");
            }
            else
            {
                builder.Append(label);
            }

            AppendCauses(builder, message.Exception);

            if (message.Callstack.Count == 0)
            {
                return builder.ToString();
            }

            builder.Append('\n');

            int shown = Math.Min(message.Callstack.Count, frameLimit);
            for (int i = 0; i < shown; i++)
            {
                CallstackFrame frame = message.Callstack[i];
                builder.Append('\n');

                if (!string.IsNullOrEmpty(frame.MethodName))
                {
                    builder.Append(frame.MethodName).Append(' ');
                }

                builder.Append("(at ").Append(ProjectRelativePath(frame.FilePath))
                    .Append(':').Append(frame.Line).Append(')');
            }

            if (shown < message.Callstack.Count)
            {
                builder.Append('\n').Append(Ellipsis).Append(' ')
                    .Append(message.Callstack.Count - shown).Append(" more frames");
            }

            return builder.ToString();
        }

        // The label already names the reported exception; each inner exception adds the reason
        // behind it, the way .NET's own exception text reads.
        private static void AppendCauses(StringBuilder builder, Exception exception)
        {
            Exception cause = exception?.InnerException;
            for (int depth = 0; cause != null && depth < MaximumCauseDepth; depth++, cause = cause.InnerException)
            {
                builder.Append("\nCaused by ").Append(cause.GetType().Name).Append(": ").Append(cause.Message);
            }
        }

        /// <summary>Shortens an absolute path to the "Assets/…" or "Packages/…" form Unity uses.</summary>
        internal static string ProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return absolutePath;
            }

            string normalized = absolutePath.Replace('\\', '/');
            string projectRoot = ProjectRoot;

            return normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(projectRoot.Length)
                : normalized;
        }

        // Application.dataPath ends with "/Assets"; the project root is what precedes it.
        private static string ProjectRoot
        {
            get
            {
                if (_projectRoot == null)
                {
                    string dataPath = Application.dataPath;
                    _projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
                }

                return _projectRoot;
            }
        }
    }
}
