# ConsoleContainer

A lightweight logging container for Unity that keeps your debug output **out of
the Unity Console** and inside dedicated, per-context windows you control.

Instead of dumping every message into one shared console, you create named
**console instances** — one per system, feature, or subsystem — and view them in
a purpose-built editor window. Each message keeps its timestamp, source and full
call stack, and every stack frame is clickable straight into your IDE, exactly
like the built-in Unity Console.

![Console viewer window showcase](.github/readme-console-viewer.gif)

## Why use it instead of the Unity Console?

- **No clutter.** Your gameplay/tooling logs live in their own window, so the
  Unity Console stays reserved for engine warnings, exceptions and third‑party
  noise.
- **Split by context.** Give each system its own instance (`"Networking"`,
  `"AI"`, `"Save System"`, …) and debug one context at a time without filtering
  through everything else.
- **See everything, in order.** The **All Instances** view merges every
  instance's messages into a single chronological stream, so you never lose the
  ordering between systems.
- **Jump to code.** Selecting a message lists its call stack as buttons; each
  one opens the file at the exact line in your external editor.
- **Hide it in builds — or don't.** By default nothing reaches a player build.
  An optional settings asset lets you forward messages to `Debug.Log` in builds,
  with an independent toggle per message type.
- **Thread-safe.** Log from jobs, background threads or async code without
  worrying about where the call comes from.

## Installation (UPM)

**Package Manager (git URL)**

1. `Window ▸ Package Manager`
2. `+` ▸ **Add package from git URL…**
3. Enter:

   ```
   https://github.com/reromanlee/ConsoleContainer.git
   ```

**Or edit `Packages/manifest.json` directly**

```json
{
  "dependencies": {
    "com.reromanlee.consolecontainer": "https://github.com/reromanlee/ConsoleContainer.git"
  }
}
```

**Or install locally** by cloning the repository into your project's `Packages/`
folder.

> Requires **Unity 6000.0 (Unity 6)** or newer.

## Quick start

```csharp
using reromanlee.ConsoleContainer;

public class NetworkService
{
    // Name the instance so it shows up in the viewer's dropdown.
    private readonly IConsoleInstance console = new ConsoleInstance("Networking");

    public void Connect(string host)
    {
        console.CreateText(this, "Connecting to", host);
        console.CreateWarning(this, "Latency high:", "180ms");
        console.CreateError("Socket", "Connection refused by", host);
    }
}
```

Open the window from **`Tools ▸ Console Viewer`**.

### Try the sample

In the Package Manager, select ConsoleContainer and import the **Console
Container Demo** sample. Drop `ConsoleContainerDemo` on a GameObject, press
Play, open the viewer, and switch the dropdown between the running instances
(one of which logs from a background thread).

## Logging API

`ConsoleInstance` implements `IConsoleInstance`:

```csharp
void CreateText   (object source, params string[] messageContent);
void CreateText   (string source, params string[] messageContent);
void CreateWarning(object source, params string[] messageContent);
void CreateWarning(string source, params string[] messageContent);
void CreateError  (object source, params string[] messageContent);
void CreateError  (string source, params string[] messageContent);
```

Every message is composed the same way — think of it like `console.log` in the
browser, where the first argument names the source:

- **Time** — rendered as `[HH:mm:ss]`.
- **Source** — the `string` you pass, or `source.GetType().Name` for the
  `object` overload.
- **Content** — every `messageContent` element joined with a single space.
- **Label** — the full line shown in the list and details pane:

  ```
  {source}: {content}
  ```

So `console.CreateText(this, "Loaded", "42", "assets")` from a `SaveSystem`
produces:

```
[14:03:11]  SaveSystem: Loaded 42 assets
```

### Other members

```csharp
new ConsoleInstance();              // auto-named "Instance N"
new ConsoleInstance("My Context");  // named for the dropdown

instance.Name;    // the display name
instance.Clear(); // remove this instance's messages
instance.Dispose();// clear and detach from the viewer
```

## The Console Viewer window

| Feature | Behaviour |
| --- | --- |
| **Instance dropdown** | Pick a single instance, or **All Instances** to see every message merged in chronological order. |
| **Zebra striping** | Alternating rows are subtly highlighted for readability; selection and hover always take priority. |
| **Selection** | Click a message to show its full `{source}: {content}` text in the details pane. |
| **Copy** | Copies the selected message to the system clipboard. |
| **Call stack** | Each frame becomes a button — top button is the log call site, going down the chain — that opens the file at its line in your IDE. |
| **Clear** | Clears the currently selected instance, or **all** of them when *All Instances* is selected. |

## Editor vs. player builds

**In the Unity Editor**, messages go **only** to the Console Viewer window. They
never touch the Unity Console — no `Debug.Log`, no doubled-up output — as long as
you log through a `ConsoleInstance` (calling `Debug.Log` or throwing exceptions
yourself still behaves normally).

**In a player build**, there is no viewer, so messages are optionally forwarded
to Unity's log based on an optional settings asset:

1. `Assets ▸ Create ▸ ConsoleContainer ▸ Settings`.
2. Move the created `ConsoleContainerSettings` asset into any **`Resources`**
   folder so it ships with the build.
3. Toggle logging **per message type**:

   | Message type | Build output |
   | --- | --- |
   | Text | `Debug.Log("{source}: {content}")` |
   | Warning | `Debug.LogWarning("{source}: {content}")` |
   | Error | `Debug.LogError("{source}: {content}")` |

If **no** settings asset exists in the build, all ConsoleContainer messages stay
hidden. This lets you keep verbose instrumentation in your code and decide, per
project, exactly what (if anything) surfaces in shipped logs.

## Performance

- **Editor-only cost.** Message storage and call-stack capture happen only under
  `UNITY_EDITOR`; player builds do nothing beyond the optional `Debug` forward.
- **Incremental rendering.** The viewer appends only *new* rows each editor
  frame using a globally monotonic sequence number — it does not rebuild the
  whole list on every message. A full rebuild happens only when instances change
  or a clear occurs.
- **Off-thread friendly.** Logging never blocks on the UI; the window marshals
  all rendering to the editor's main-thread update loop, so background threads
  just append under a short lock.
- **Chronological merge is free.** Because sequence numbers are assigned
  atomically at creation, the *All Instances* view stays ordered without sorting
  the entire history.

Best suited to typical debugging volumes. For sustained, extremely high-rate
logging, prefer a dedicated instance you can `Clear()` periodically.

## License

MIT — see [LICENSE.md](LICENSE.md).