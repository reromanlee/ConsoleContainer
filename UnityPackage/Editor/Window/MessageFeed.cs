using System;
using System.Collections.Generic;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// The list of messages one viewer window shows, kept up to date incrementally.
    ///
    /// Each instance in scope is read from its own cursor, so no message is ever skipped however
    /// logging threads interleave. New messages are merged into one chronological stream, filtered
    /// by the search text and the enabled types, and counted per type. Messages an instance has
    /// since dropped from its history are pruned in batches, so the feed never holds on to them.
    ///
    /// Main thread only.
    /// </summary>
    internal sealed class MessageFeed
    {
        private const int TypeCount = 3;

        // Pruning walks the whole feed, so evictions are collected until they are worth it: at
        // least this many, and at least a sixteenth of the feed. Bounds how many dropped messages
        // can linger, without paying a full pass on every frame of sustained logging.
        private const int MinimumPruneBatch = 256;
        private const int PruneBatchDivisor = 16;

        private static readonly Comparison<ConsoleMessage> BySequence = (left, right) => left.Sequence.CompareTo(right.Sequence);

        // Messages in scope that match the search, of every type, in sequence order.
        private readonly List<ConsoleMessage> matched = new List<ConsoleMessage>();

        // The subset of matched whose type is enabled; unused while every type is.
        private readonly List<ConsoleMessage> filtered = new List<ConsoleMessage>();

        private readonly List<ConsoleMessage> batch = new List<ConsoleMessage>();
        private readonly Dictionary<ConsoleInstance, int> sourceIndices = new Dictionary<ConsoleInstance, int>();
        private readonly int[] counts = new int[TypeCount];
        private readonly bool[] enabledTypes = { true, true, true };

        private ConsoleInstance[] sources = Array.Empty<ConsoleInstance>();
        private long[] cursors = Array.Empty<long>();
        private long[] startPositions = Array.Empty<long>();
        private string search = string.Empty;
        private long pendingEvictions;

        /// <summary>
        /// The messages to show, in chronological order. The list instance changes when the type
        /// filter switches between "everything" and a subset, so re-read it after a reload.
        /// </summary>
        internal List<ConsoleMessage> Visible => AllTypesEnabled ? matched : filtered;

        /// <summary>The current search text; empty when nothing is filtered by text.</summary>
        internal string Search => search;

        private bool AllTypesEnabled => enabledTypes[0] && enabledTypes[1] && enabledTypes[2];

        /// <summary>How many messages of <paramref name="type"/> are in scope and match the search, shown or not.</summary>
        internal int GetCount(ConsoleMessageType type) => counts[(int)type];

        internal bool IsTypeEnabled(ConsoleMessageType type) => enabledTypes[(int)type];

        /// <summary>Switches to a new set of instances and reloads everything they hold.</summary>
        internal void SetSources(ConsoleInstance[] scope)
        {
            sources = scope ?? Array.Empty<ConsoleInstance>();
            cursors = new long[sources.Length];
            startPositions = new long[sources.Length];

            sourceIndices.Clear();
            for (int i = 0; i < sources.Length; i++)
            {
                sourceIndices[sources[i]] = i;
            }

            Reload();
        }

        /// <summary>Applies a case-insensitive search over source and content; returns false when unchanged.</summary>
        internal bool SetSearch(string text)
        {
            text ??= string.Empty;
            if (string.Equals(text, search, StringComparison.Ordinal))
            {
                return false;
            }

            search = text;
            Reload();
            return true;
        }

        /// <summary>Shows or hides one message type; returns false when unchanged.</summary>
        internal bool SetTypeEnabled(ConsoleMessageType type, bool enabled)
        {
            if (enabledTypes[(int)type] == enabled)
            {
                return false;
            }

            enabledTypes[(int)type] = enabled;
            RebuildFiltered();
            return true;
        }

        /// <summary>Drops everything and reads every source again from its oldest held message.</summary>
        internal void Reload()
        {
            matched.Clear();
            filtered.Clear();
            Array.Clear(counts, 0, counts.Length);
            Array.Clear(cursors, 0, cursors.Length);
            Array.Clear(startPositions, 0, startPositions.Length);
            pendingEvictions = 0;

            Update();
        }

        /// <summary>Pulls in what was logged since the last call and reports how the visible list changed.</summary>
        internal FeedChange Update()
        {
            batch.Clear();

            for (int i = 0; i < sources.Length; i++)
            {
                cursors[i] = sources[i].CollectSince(cursors[i], batch, out long startPosition);

                if (startPosition > startPositions[i])
                {
                    pendingEvictions += startPosition - startPositions[i];
                    startPositions[i] = startPosition;
                }
            }

            FeedChange change = FeedChange.None;

            if (batch.Count > 0)
            {
                // Each source is already in order; sorting merges them into one stream.
                batch.Sort(BySequence);

                foreach (ConsoleMessage message in batch)
                {
                    change |= Add(message);
                }

                batch.Clear();
            }

            if (pendingEvictions >= Math.Max(MinimumPruneBatch, matched.Count / PruneBatchDivisor))
            {
                change |= Prune();
            }

            return change;
        }

        /// <summary>Index of <paramref name="message"/> in <see cref="Visible"/>, or -1.</summary>
        internal int IndexOf(ConsoleMessage message)
        {
            if (message == null)
            {
                return -1;
            }

            List<ConsoleMessage> list = Visible;
            int index = LowerBound(list, message.Sequence);
            return index < list.Count && ReferenceEquals(list[index], message) ? index : -1;
        }

        private FeedChange Add(ConsoleMessage message)
        {
            if (!Matches(message))
            {
                return FeedChange.None;
            }

            counts[(int)message.Type]++;
            bool appended = Insert(matched, message);

            if (!AllTypesEnabled)
            {
                if (!enabledTypes[(int)message.Type])
                {
                    return FeedChange.None;
                }

                appended = Insert(filtered, message);
            }

            return appended ? FeedChange.Appended : FeedChange.Reordered;
        }

        private bool Matches(ConsoleMessage message)
        {
            return search.Length == 0
                   || message.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                   || message.Content.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildFiltered()
        {
            filtered.Clear();
            if (AllTypesEnabled)
            {
                return;
            }

            foreach (ConsoleMessage message in matched)
            {
                if (enabledTypes[(int)message.Type])
                {
                    filtered.Add(message);
                }
            }
        }

        private FeedChange Prune()
        {
            pendingEvictions = 0;

            int removed = Compact(matched, countRemovals: true);
            if (!AllTypesEnabled)
            {
                removed += Compact(filtered, countRemovals: false);
            }

            return removed > 0 ? FeedChange.Reordered : FeedChange.None;
        }

        private int Compact(List<ConsoleMessage> list, bool countRemovals)
        {
            int kept = 0;
            for (int i = 0; i < list.Count; i++)
            {
                ConsoleMessage message = list[i];
                if (IsEvicted(message))
                {
                    if (countRemovals)
                    {
                        counts[(int)message.Type]--;
                    }

                    continue;
                }

                list[kept++] = message;
            }

            int removed = list.Count - kept;
            list.RemoveRange(kept, removed);
            return removed;
        }

        private bool IsEvicted(ConsoleMessage message)
            => sourceIndices.TryGetValue(message.Owner, out int index) && message.Position < startPositions[index];

        // Messages almost always arrive in order and append; one that a concurrent thread logged
        // just before the last batch is slotted into place instead. Returns whether it appended.
        private static bool Insert(List<ConsoleMessage> list, ConsoleMessage message)
        {
            int count = list.Count;
            if (count == 0 || list[count - 1].Sequence < message.Sequence)
            {
                list.Add(message);
                return true;
            }

            list.Insert(LowerBound(list, message.Sequence), message);
            return false;
        }

        // First index whose sequence is not below the given one.
        private static int LowerBound(List<ConsoleMessage> list, long sequence)
        {
            int low = 0;
            int high = list.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (list[middle].Sequence < sequence)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }

    /// <summary>How the visible list changed during a <see cref="MessageFeed.Update"/>.</summary>
    [Flags]
    internal enum FeedChange
    {
        None = 0,

        /// <summary>Messages were added at the end; existing indices are unchanged.</summary>
        Appended = 1,

        /// <summary>Messages were inserted or removed elsewhere; existing indices may have shifted.</summary>
        Reordered = 2
    }
}
