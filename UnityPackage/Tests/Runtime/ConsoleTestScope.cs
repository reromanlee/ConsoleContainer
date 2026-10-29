using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace reromanlee.ConsoleContainer.Tests
{
    /// <summary>
    /// Isolates a test from the project and from other tests: installs a runtime settings override
    /// that forwards nothing to the Unity log (a test run would otherwise forward every message,
    /// and a forwarded error fails the test), and disposes and clears every instance it created so
    /// none lingers in an open Console Viewer.
    /// </summary>
    public sealed class ConsoleTestScope : IDisposable
    {
        private readonly List<ConsoleInstance> instances = new List<ConsoleInstance>();

        public ConsoleTestScope()
        {
            Settings = ScriptableObject.CreateInstance<ConsoleContainerSettings>();
            Settings.EditorForwarding = EditorForwardingMode.Never;
            ConsoleContainerSettings.Active = Settings;
        }

        /// <summary>The override in effect for the test; adjust it freely.</summary>
        public ConsoleContainerSettings Settings { get; }

        public ConsoleInstance Create(string name = "Test")
        {
            ConsoleInstance instance = new ConsoleInstance(name);
            instances.Add(instance);
            return instance;
        }

        public void Dispose()
        {
            foreach (ConsoleInstance instance in instances)
            {
                instance.Dispose();
                instance.Clear();
            }

            instances.Clear();
            ConsoleContainerSettings.Active = null;
            Object.DestroyImmediate(Settings);
        }
    }
}
