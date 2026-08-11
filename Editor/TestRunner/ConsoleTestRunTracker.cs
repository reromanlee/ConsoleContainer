using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// Bridges the Unity Test Framework to
    /// <see cref="UnityConsoleForwarding.IsTestRunActive"/> so console messages
    /// can be mirrored into the Unity log — and therefore into the Test Runner
    /// window — for as long as a test run lasts.
    ///
    /// This lives in its own assembly, constrained to
    /// <c>CONSOLE_CONTAINER_TEST_FRAMEWORK</c>, so the package still compiles in
    /// projects that removed the Test Framework package: without it the whole
    /// assembly is skipped and the flag simply stays false.
    /// </summary>
    internal static class ConsoleTestRunTracker
    {
        // Survives the domain reloads a play mode test run triggers; cleared when
        // the editor session ends.
        private const string ActiveStateKey = "reromanlee.ConsoleContainer.TestRunActive";

        private static TestRunnerApi testRunnerApi;
        private static TestRunCallbacks callbacks;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Both the flag and the callback registration are lost on a domain
            // reload, so a run that spans one (any play mode run) has to restore
            // them here or it would stop forwarding halfway through.
            UnityConsoleForwarding.IsTestRunActive = SessionState.GetBool(ActiveStateKey, false);

            callbacks = new TestRunCallbacks();
            testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            testRunnerApi.RegisterCallbacks(callbacks);

            AssemblyReloadEvents.beforeAssemblyReload += Release;
        }

        private static void Release()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Release;

            if (testRunnerApi != null)
            {
                testRunnerApi.UnregisterCallbacks(callbacks);
                Object.DestroyImmediate(testRunnerApi);
                testRunnerApi = null;
            }

            callbacks = null;
        }

        private static void SetTestRunActive(bool active)
        {
            SessionState.SetBool(ActiveStateKey, active);
            UnityConsoleForwarding.IsTestRunActive = active;
        }

        /// <summary>
        /// Only the run-level callbacks matter here — per-test callbacks would
        /// toggle forwarding off between tests, hiding anything logged by setup,
        /// teardown or background work in between.
        /// </summary>
        private sealed class TestRunCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                SetTestRunActive(true);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                SetTestRunActive(false);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
