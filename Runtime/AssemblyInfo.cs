using System.Runtime.CompilerServices;

// The editor viewer lives in a separate assembly but needs access to the
// internal registry and message-collection plumbing exposed by the runtime.
[assembly: InternalsVisibleTo("reromanlee.ConsoleContainer.Editor")]
