### Features

A detailed look at what ConsoleContainer does and the guarantees behind it. For setup and code, see [USAGE.md](USAGE.md).

### Console instances

A `ConsoleInstance` is a named container of messages, usually one per system: `"Networking"`, `"AI"`, `"Save System"`. Instances are independent, so a system can name its instance after itself without coordinating with anything else; two instances may even share a name, and the viewer numbers them (`Networking`, `Networking (2)`) so each stays selectable.

1. `new ConsoleInstance("Name")` names the instance; `new ConsoleInstance()` generates a unique `Instance N` name, so dependency-injection containers can construct it by convention.
2. Code depends on `IConsoleInstance`, a deliberately small interface: `Name`, `IsDisposed`, the two message events, `IsEnabled`, `Create`, `Clear` and `Dispose`. The logging shortcuts are extension methods, so they work with any implementation and new ones never break an existing one.
3. `Dispose` stops logging and drops every event handler. A disposed instance that still holds messages stays in the viewer, marked `(disposed)`, until it is cleared; one with nothing to show leaves at once.

### Message composition

Every message has a type, a timestamp, a source, content and, in the editor, a call stack.

| Part | Rule |
| --- | --- |
| **Type** | `Text`, `Warning` or `Error` (`ConsoleMessageType`). |
| **Source** | A `string` as is; a `Type` by its own name, for static classes (`typeof(SaveSystem)`); any other object by its type's name. Generic types read as `List<Int32>`. Empty or null becomes `null`. |
| **Content** | Content arguments joined with single spaces. |
| **Label** | `{source}: {content}`, or the source alone when there is no content. Shown in the viewer and used for forwarded logs. |
| **TimeText** | `[HH:mm:ss]`, identical on every machine regardless of culture. |
| **Sequence** | A global creation order, so messages from different instances merge chronologically. |

Source names are cached per type, and `Label` and `TimeText` are built once per message, so repeated reads allocate nothing.

### Exception logging

`CreateException(source, exception)` logs an `Error` that reads `{source}: {ExceptionType}: {Message}`.

1. Its call stack is where the exception was **thrown**, starting at the innermost inner exception, rather than where it was logged. An exception that was never thrown falls back to the logging site.
2. `ConsoleMessage.Exception` hands the original object to handlers, so crash reporters receive a real exception.
3. When forwarded, it goes through `Debug.LogException`, so tools hooked into the Unity log see an exception rather than a string.
4. The viewer lists inner exceptions as `Caused by …` lines.

### Events

1. **`MessageCreated`** is raised for every message; **`ErrorCreated`** for errors only, right after it.
2. **`ConsoleInstance.Created`** is static and raised for every instance once it is fully built, so one subscriber covers instances created anywhere, including ones built long afterwards.
3. Events fire **in player builds too**, independent of forwarding: a message that never reaches the Unity log still reaches handlers.
4. Handlers run **on the logging thread**. Marshal to the main thread before touching the Unity API.
5. A **throwing handler** is reported through `Debug.LogException`; the remaining handlers still run and the message is still kept.
6. A handler that **logs from inside a handler** is fine, but nesting is capped at 8 levels per thread. Endless recursion, such as an `ErrorCreated` handler logging an error into the same instance, stops with a single warning instead of a stack overflow that would take the whole process down.

### The Console Viewer

Open it from **Window ▸ General ▸ Console Viewer**.

| Feature | Behaviour |
| --- | --- |
| **Instance menu** | One instance, or **All Instances** merged in chronological order, where each row names its instance on the right, exactly as the menu lists it. Recreated instances are re-attached by name. |
| **Virtualized list** | Only rows on screen exist, so cost depends on the window size, not on how many messages there are. |
| **Type filters** | Toggle Text, Warning and Error, each showing its count (search-aware, capped at `999+`). |
| **Search** | Case-insensitive match over source and content, applied to new messages as they arrive. |
| **Details pane** | Full message, exception causes and a readable stack trace, with selectable text. Very long text is cut for display; Copy keeps all of it. |
| **Call stack buttons** | One per frame, top first; each opens the file at the line in your IDE. Double-click or Enter on a row opens the top frame. |
| **Copy** | The **Copy** button or Ctrl+C in the list copies the message with its full call stack. |
| **Clear / Clear on Play** | Clear the selected instance, or all of them. Clear on Play (on by default, shared by every window) clears everything when play mode starts. |
| **Several windows** | **New Console Viewer** in the tab's menu opens another, so two instances can be watched side by side. |
| **Remembered state** | Each window keeps its instance, filters, search, splitter sizes and scroll position across domain reloads and editor restarts. |
| **Both skins** | Built from Unity's own toolbar and list controls, so it follows the Dark and Light editor themes. |

### Call stacks

1. Captured in the editor only, and only when the message type's **Capture call stack** setting is on (it is by default).
2. The top frame is your logging call: the package's own frames are skipped, and so is any method marked with Unity's `[HideInCallstack]`, so a project's own logging wrapper drops out too.
3. Lambdas, iterators and async methods are named after the method they were written in, not the compiler-generated type.
4. Each distinct stack is resolved once and shared by later messages logged along the same path, which keeps repeated logging cheap and retained memory low.

### Editor history

1. Each instance keeps up to **10,000** messages by default (`Max Messages Per Instance`; 0 keeps everything), dropping the oldest first.
2. Readers resume from per-instance cursors, so the viewer never skips a message, however logging threads interleave.
3. Player builds keep no history at all.

### Forwarding to the Unity log

| Context | Default | Configurable |
| --- | --- | --- |
| **Editor** | Forwarded only **during test runs**, so messages show in the Test Runner window while the Unity Console stays clean the rest of the time. | `Never`, `During Test Runs` or `Always`, plus a toggle per message type. |
| **Player builds** | **Nothing** is forwarded without a settings asset. | A toggle per message type. |

Forwarded messages use `Debug.Log`, `Debug.LogWarning` and `Debug.LogError` with the message label; exception messages use `Debug.LogException`. The package's frames carry `[HideInCallstack]`, so with **Strip logging callstack** enabled in the Unity Console, double-clicking a forwarded entry opens your call site.

### Settings

1. **Project Settings ▸ Console Container** creates the settings asset exactly where the runtime looks for it (`Assets/Resources/ConsoleContainerSettings.asset`) and edits it inline.
2. The page warns about assets the runtime would ignore (wrong name, wrong folder), leave out of builds (a `Resources` folder inside an `Editor` folder), or pick between arbitrarily (more than one).
3. `ConsoleContainerSettings.Active` accepts a settings object created at runtime, overriding the asset without touching it, for QA menus and tests.
4. Settings are safe to read from any thread: the asset is resolved on the main thread and cached at startup.

### Robustness

1. Every member of `ConsoleInstance` is safe to call from any thread, including jobs and async code.
2. Logging never throws because of a handler, a destroyed settings override, or a missing settings asset.
3. A viewer whose layout assets are missing shows a readable error instead of a blank, broken window.
4. There is no platform-specific code: the runtime relies only on standard .NET threading primitives and Unity's `Debug` and `Resources` APIs, and was verified in an IL2CPP release player with managed code stripping set to High.
