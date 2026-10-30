#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Growable ring buffer of messages with an optional size limit: once full, each new message
    /// replaces the oldest one.
    ///
    /// Every message ever added owns a position in one ever-increasing cursor space, so a reader
    /// that remembers where it stopped resumes exactly there — it never misses or repeats an
    /// entry, however many were added or dropped in between, and clearing never rewinds it.
    ///
    /// Not thread-safe; the owning instance serializes access.
    /// </summary>
    internal sealed class MessageBuffer
    {
        private const int MinimumCapacity = 16;

        // Above this many slots, a cleared buffer releases its storage instead of holding on to
        // capacity it is unlikely to need again.
        private const int ClearedCapacityTrimThreshold = 1024;

        // Largest array the runtime allows; an unlimited buffer stops growing here and starts
        // replacing its oldest entries like a limited one.
        private const int MaximumCapacity = 0x7FFFFFC7;

        private ConsoleMessage[] items = Array.Empty<ConsoleMessage>();
        private int head;
        private int count;
        private long endPosition;

        /// <summary>Number of messages currently held.</summary>
        internal int Count => count;

        /// <summary>Cursor of the oldest message still held.</summary>
        internal long StartPosition => endPosition - count;

        /// <summary>Cursor just past the newest message; where a reader that is up to date resumes.</summary>
        internal long EndPosition => endPosition;

        /// <summary>The held message at <paramref name="index"/>, oldest first.</summary>
        internal ConsoleMessage this[int index] => items[Wrap(head + index)];

        /// <summary>Appends a message, dropping the oldest ones first to stay within the limit.</summary>
        /// <param name="message">Message to append.</param>
        /// <param name="limit">Most messages to hold; zero or less means unlimited.</param>
        internal void Add(ConsoleMessage message, int limit)
        {
            int effectiveLimit = limit > 0 ? limit : MaximumCapacity;

            // A loop rather than a single removal, so lowering the limit takes effect at once.
            while (count >= effectiveLimit)
            {
                RemoveOldest();
            }

            if (count == items.Length)
            {
                Grow(effectiveLimit);
            }

            message.Position = endPosition;
            items[Wrap(head + count)] = message;
            count++;
            endPosition++;
        }

        /// <summary>
        /// Appends every message from <paramref name="cursor"/> onwards to
        /// <paramref name="destination"/> — or from the oldest still held, when the ones in between
        /// were dropped — and returns the cursor to resume from next time.
        /// </summary>
        internal long CopySince(long cursor, List<ConsoleMessage> destination)
        {
            long start = StartPosition;
            if (cursor < start)
            {
                cursor = start;
            }

            for (long position = cursor; position < endPosition; position++)
            {
                destination.Add(items[Wrap(head + (int)(position - start))]);
            }

            return endPosition;
        }

        /// <summary>Drops every message. Positions keep counting, so existing cursors stay valid.</summary>
        internal void Clear()
        {
            if (items.Length > ClearedCapacityTrimThreshold)
            {
                items = Array.Empty<ConsoleMessage>();
            }
            else
            {
                Array.Clear(items, 0, items.Length);
            }

            head = 0;
            count = 0;
        }

        private void RemoveOldest()
        {
            items[head] = null;
            head = Wrap(head + 1);
            count--;
        }

        private void Grow(int limit)
        {
            long doubled = Math.Max(MinimumCapacity, (long)items.Length * 2);
            ConsoleMessage[] grown = new ConsoleMessage[(int)Math.Min(doubled, limit)];

            for (int i = 0; i < count; i++)
            {
                grown[i] = items[Wrap(head + i)];
            }

            items = grown;
            head = 0;
        }

        // head and any offset are each below the capacity, so one subtraction always wraps.
        private int Wrap(int index) => index >= items.Length ? index - items.Length : index;
    }
}
#endif
