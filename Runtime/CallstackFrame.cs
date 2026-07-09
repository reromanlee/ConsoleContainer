using System.IO;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// A single, source-resolvable entry of a message's call stack.
    /// The editor viewer uses it to open the originating line in an IDE,
    /// mirroring how the built-in Unity Console links stack frames to code.
    /// </summary>
    public readonly struct CallstackFrame
    {
        /// <summary>Absolute path of the source file this frame points to.</summary>
        public string FilePath { get; }

        /// <summary>1-based line number inside <see cref="FilePath"/>.</summary>
        public int Line { get; }

        /// <summary>Human-readable "Type.Method" that owns this frame.</summary>
        public string MethodName { get; }

        public CallstackFrame(string filePath, int line, string methodName)
        {
            FilePath = filePath;
            Line = line;
            MethodName = methodName;
        }

        /// <summary>Compact label such as "ClassCaller.cs:15" for the callstack buttons.</summary>
        public string Label =>
            string.IsNullOrEmpty(FilePath)
                ? $"<unknown>:{Line}"
                : $"{Path.GetFileName(FilePath)}:{Line}";
    }
}
