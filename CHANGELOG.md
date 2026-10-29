# ConsoleContainer 1.0.0

### What's new

1. Named console instances: `ConsoleInstance` behind a small `IConsoleInstance` interface, logged to through `CreateText`, `CreateWarning` and `CreateError` extension methods with single-message and content overloads, and a string, `Type` or object as the source.
2. Console Viewer window (Window ▸ General ▸ Console Viewer): a virtualized list of one instance or all of them merged chronologically, type filters with counts, search, a details pane with clickable call stack frames, Copy and Ctrl+C, Clear and Clear on Play, several windows side by side, remembered per-window state, and Dark and Light skin support.
3. `MessageCreated` and `ErrorCreated` events in the editor and player builds, plus the static `ConsoleInstance.Created` to cover every instance at once; throwing handlers are reported without breaking logging, and runaway re-entrant logging is stopped before it can overflow the stack.
4. `CreateException`, which shows where an exception was thrown, hands the original object to handlers through `ConsoleMessage.Exception`, and is forwarded through `Debug.LogException`.
5. Dropped messages in player builds allocate nothing, and a `ConsoleMessage` is only created when a handler is subscribed for its type.
6. Per-type forwarding to the Unity log through an optional settings asset: nothing reaches player logs without one, and in the editor messages are forwarded during test runs only, so they show in the Test Runner window while the Unity Console stays clean.
7. Bounded editor history, 10,000 messages per instance by default, read through per-instance cursors so the viewer never skips a message under concurrent logging.
8. Call stacks that start at the logging call, skip wrappers marked `[HideInCallstack]`, name lambdas and iterators after their method, can be turned off per message type, and are shared between messages logged along the same path.
9. Project Settings ▸ Console Container page that creates the settings asset where the runtime finds it and warns about ignored, editor-only or duplicate assets; `ConsoleContainerSettings.Active` accepts runtime overrides.
10. Thread-safe logging from any thread, culture-independent time text, and cached message text.
11. Console Container Demo sample, Edit Mode and Play Mode test suites, and a GameCI workflow.
12. Unity 2022.3 LTS and Unity 6 support; safe under Mono, IL2CPP, managed code stripping and WebGL.

### Important

1. The package lives in the `UnityPackage/` folder of the repository, so git URL installs must end with `?path=/UnityPackage`.

### Upgrading from internal builds

1. `MessageType` is now `ConsoleMessageType`, so it no longer clashes with `UnityEditor.MessageType`.
2. Custom `IConsoleInstance` implementations must implement the new core members; calls to `CreateText`, `CreateWarning` and `CreateError` compile unchanged.
3. The Console Viewer moved from Tools ▸ Console Viewer to Window ▸ General ▸ Console Viewer.
4. The `ConsoleMessage` constructor is now internal, and a message without content is labelled with its source alone.
5. Editor history is capped at 10,000 messages per instance, and Clear on Play is on by default.
