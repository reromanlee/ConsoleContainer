#if UNITY_EDITOR
using System;
using System.Collections.Generic;

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

        private static volatile int _version;
        private static volatile int _clearGeneration;

        /// <summary>
        /// Raised whenever instances or their messages change. May be invoked
        /// from a background thread; subscribers must marshal to the main thread
        /// before touching UI.
        /// </summary>
        internal static event Action Changed;

        /// <summary>Bumped when the set of registered instances changes.</summary>
        internal static int Version => _version;

        /// <summary>Bumped when any instance is cleared (used to trigger a full rebuild).</summary>
        internal static int ClearGeneration => _clearGeneration;

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
                _version++;
            }

            RaiseChanged();
        }

        internal static void Unregister(ConsoleInstance instance)
        {
            lock (Gate)
            {
                if (!Instances.Remove(instance))
                {
                    return;
                }

                _version++;
                _clearGeneration++;
            }

            RaiseChanged();
        }

        internal static void NotifyMessageAdded(ConsoleInstance instance)
        {
            RaiseChanged();
        }

        internal static void NotifyCleared(ConsoleInstance instance)
        {
            _clearGeneration++;
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Changed?.Invoke();
        }
    }
}
#endif
