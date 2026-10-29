### Performance

What ConsoleContainer costs, measured on real Unity runtimes. Numbers are best-of-five averages.

### Test setup

1. **IL2CPP:** Unity 6000.0.85f1 Windows x64 release player, managed stripping High, Intel Core i7-12700K.
2. **Mono editor:** Unity 6000.0.85f1 editor with Release code optimization, same machine.
3. **Allocations:** heap growth measured with garbage collection paused, divided by the number of calls.

Expect several times higher absolute numbers on mobile and WebGL hardware. The relative shape stays the same.

### Player builds

Player builds keep no history and capture no call stacks, so a message only costs what it is used for.

| Message | Time | Allocated |
| --- | --- | --- |
| Dropped (not forwarded, no handler), one message string | 21 ns | 0 B |
| Dropped, three content arguments | 70 ns | 64 B |
| Received by a `MessageCreated` handler | 190 ns | ~98 B |
| Forwarded to `Debug.Log` | ~36 µs | ~6.8 KB |

1. **Dropped messages allocate nothing.** The logging shortcuts ask the instance whether a message type would have any effect before resolving the source or joining content.
2. **Content arguments cost their array.** `params` arrays are built by the C# compiler at the call site, before the call. Prefer the single-message overload on hot paths, or check `IsEnabled` before building expensive content.
3. **A handler costs one message object.** The `ConsoleMessage` handed to handlers is only created when something is subscribed for that message type.
4. **Forwarding costs what `Debug.Log` costs.** Nearly all of it is Unity's own log call, including its stack trace capture; adjust **Player Settings ▸ Stack Trace** to change that.

### Editor

| Message | Time |
| --- | --- |
| With a call stack, logged six calls deep | ~44 µs |
| Without a call stack | 0.2 µs |

**Call stacks are the cost.** Capturing one is Mono's stack walk, which runs at roughly 3 to 5 µs per managed frame in the editor: about 10 µs with three frames on the stack and 100 µs with twenty-three. Unity's own `StackTraceUtility`, which `Debug.Log` uses, measured 16 to 58 µs on the same stacks. Turn off **Capture call stack** for a message type in the settings when it is logged on a hot path.

### Editor memory

| Retained per message | Bytes |
| --- | --- |
| With a call stack | ~125 B plus its text |
| Without a call stack | ~124 B plus its text |

**Stacks are shared.** Each distinct stack is resolved once and shared by every message logged along the same path, so a call stack adds almost nothing to a message's retained size (382 B per message before sharing). With the default limit of 10,000 messages per instance, a busy instance holds about 1.2 MB plus its messages' text. File paths and method names are stored once for the whole session.

### Console Viewer

| Operation | Time |
| --- | --- |
| Take in a burst of 10,000 new messages from three instances | 1.7 ms |
| Apply a search over 30,000 messages | 7.6 ms |

The list is virtualized: only the rows on screen exist and are re-bound as it scrolls, so drawing cost depends on the window's height, never on the number of messages. New messages are read incrementally, from each instance's own cursor, and merged into the existing list without a rebuild. Logging threads only flip a flag; all work happens on the editor's main loop.
