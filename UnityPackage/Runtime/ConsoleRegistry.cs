#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Editor-only, thread-safe registry that tracks every live
    /// <see cref="ConsoleInstance"/> so the Console Viewer window can discover
    /// and display them. It carries no message data itself — each instance owns
    /// its messages — it only bridges the runtime and the editor window.
    /// </summary>
    internal static class ConsoleRegistry
    {
        private static readonly object Gate = new object();
        private static readonly List<ConsoleInstance> Instances = new List<ConsoleInstance>();

        private static int _version;
        private static int _clearGeneration;

        /// <summary>
        /// Raised whenever instances or their messages change. May be invoked
        /// from a background thread; subscribers must marshal to the main thread
        /// before touching UI.
        /// </summary>
        internal static event Action Changed;

        /// <summary>Bumped when the instance list, or an instance's display state (e.g. disposed), changes.</summary>
        internal static int Version => Volatile.Read(ref _version);

        /// <summary>Bumped when any instance is cleared (used to trigger a full rebuild).</summary>
        internal static int ClearGeneration => Volatile.Read(ref _clearGeneration);

        internal static ConsoleInstance[] Snapshot()
        {
            lock (Gate)
            {
                return Instances.ToArray();
            }
        }

        internal static void Register(ConsoleInstance instance)
        {
            lock (Gate)
            {
                if (Instances.Contains(instance))
                {
                    return;
                }

                Instances.Add(instance);
            }

            Interlocked.Increment(ref _version);
            RaiseChanged();
        }

        /// <summary>
        /// Drops an instance from the viewer entirely. Called once an instance
        /// can no longer show anything useful — it is disposed and holds no
        /// messages — so repeated create/dispose cycles (test runs, play mode)
        /// cannot pile up stale entries that outlive their usefulness and hide
        /// freshly created instances behind identically named ones.
        /// </summary>
        internal static void Unregister(ConsoleInstance instance)
        {
            bool removed;
            lock (Gate)
            {
                removed = Instances.Remove(instance);
            }

            if (!removed)
            {
                return;
            }

            Interlocked.Increment(ref _version);
            RaiseChanged();
        }

        internal static void NotifyMessageAdded(ConsoleInstance instance)
        {
            RaiseChanged();
        }

        internal static void NotifyDisposed(ConsoleInstance instance)
        {
            // Bump the version so the viewer rebuilds its dropdown with the
            // "(disposed)" label; an instance that still holds messages stays
            // registered on purpose so its history remains inspectable.
            Interlocked.Increment(ref _version);
            RaiseChanged();
        }

        internal static void NotifyCleared(ConsoleInstance instance)
        {
            Interlocked.Increment(ref _clearGeneration);
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Changed?.Invoke();
        }
    }
}
#endif
