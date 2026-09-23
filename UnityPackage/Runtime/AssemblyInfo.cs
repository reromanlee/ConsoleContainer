using System.Runtime.CompilerServices;

// The editor viewer lives in a separate assembly but needs access to the
// internal registry and message-collection plumbing exposed by the runtime.
[assembly: InternalsVisibleTo("reromanlee.ConsoleContainer.Editor")]

// The test runner bridge is its own assembly (it only compiles when the Unity
// Test Framework is installed) and drives the internal forwarding flag.
[assembly: InternalsVisibleTo("reromanlee.ConsoleContainer.TestRunnerBridge")]
