### Usage

Everything beyond the README quick start. The importable **Console Container Demo** sample (Package Manager ▸ ConsoleContainer ▸ Samples) shows these patterns running.

### Creating instances

Give each system its own instance and name it after the system, so it shows up under that name in the viewer. Depend on `IConsoleInstance`, and dispose the instance when its owner goes away:

```csharp
using reromanlee.ConsoleContainer;

public sealed class NetworkService : IDisposable
{
    private readonly IConsoleInstance console = new ConsoleInstance("Networking");

    public void Dispose() => console.Dispose();
}
```

`new ConsoleInstance()` generates a unique `Instance N` name, so dependency-injection containers that construct by convention can resolve it. Register a named one through the container's instance API when the name matters:

```csharp
builder.Register<IConsoleInstance, ConsoleInstance>(Lifetime.Singleton);           // VContainer, by convention
builder.RegisterInstance<IConsoleInstance>(new ConsoleInstance("Networking"));     // named
```

### Logging

```csharp
console.CreateText(this, "Connected");                         // one message string
console.CreateWarning(this, "Latency high:", latency + "ms");  // content joined with spaces
console.CreateError("Socket", "Connection refused by", host);  // a string source
console.CreateText(typeof(SaveSystem), "Loaded");              // a static class
console.CreateException(this, exception);                      // an exception
```

| Source you pass | Shown as |
| --- | --- |
| `"Socket"` | `Socket` |
| `this` in a `NetworkService` | `NetworkService` |
| `typeof(SaveSystem)` | `SaveSystem` |
| a `Dictionary<string, int>` | `Dictionary<String, Int32>` |
| `null` or `""` | `null` |

Every message is labelled `{source}: {content}`, or just the source when there is no content, so `console.CreateText(this, "Loaded", "42", "assets")` in a `SaveSystem` shows `[14:03:11] SaveSystem: Loaded 42 assets`.

**On hot paths**, prefer the single-message overload: the `params` overload builds its array at the call site even when the message ends up dropped. For content that is expensive to build, ask first:

```csharp
if (console.IsEnabled(ConsoleMessageType.Text))
{
    console.CreateText(this, BuildExpensiveReport());
}
```

`IsEnabled` is always true in the editor, which keeps every message, and in a player build is only true when the type is forwarded or something is subscribed.

### Logging exceptions

```csharp
try
{
    socket.Connect(host);
}
catch (SocketException exception)
{
    console.CreateException(this, exception);
}
```

The message reads `NetworkService: SocketException: Connection refused`, its call stack is where the exception was thrown, and handlers receive the original object through `message.Exception`. Forwarded, it goes through `Debug.LogException`.

### Reacting to messages

```csharp
console.ErrorCreated += message => SoftCrashScreen.Show(message.Label);
console.MessageCreated += message => Telemetry.Record(message.Type, message.Label);
```

1. Handlers run in player builds too, even when nothing is forwarded to the Unity log.
2. Handlers run **on the thread that logged**. Marshal to the main thread before touching the Unity API, for example through a `SynchronizationContext` captured on the main thread.
3. A throwing handler is reported through `Debug.LogException` and never breaks the logging call.
4. `Dispose` drops every handler, so a disposed instance cannot keep the objects they captured alive.

The `ConsoleMessage` a handler receives carries `Type`, `Timestamp`, `Source`, `Content`, `Label`, `TimeText`, `Sequence`, `Exception` and, in the editor, `Callstack`.

### Covering every instance at once

Subscribing per instance means remembering to do it wherever one is built. `ConsoleInstance.Created` is static and reaches every instance the application will ever create, including ones built much later:

```csharp
// Install once, before anything logs.
ConsoleInstance.Created += instance => instance.ErrorCreated += message => CrashReporter.Report(instance.Name, message);
```

The instance is fully built when handlers run, on the constructing thread. Subscriptions live as long as the domain does: a static handler needs no unsubscribe, one that is not must detach or it keeps its target alive. When entering play mode skips the domain reload, unsubscribe before subscribing, so a surviving handler is not registered twice.

### The Console Viewer

Open **Window ▸ General ▸ Console Viewer**. Pick an instance or **All Instances** from the toolbar menu, filter by type with the count toggles, and type in the search field to match source or content.

1. Select a row to see the full message and its call stack; each frame button opens the file at its line, and double-click or Enter opens the top one.
2. **Copy**, or Ctrl+C in the list, copies the message with its full call stack. The details text is also selectable.
3. **Clear** clears the selected instance, or all of them under All Instances. **Clear on Play** clears everything when play mode starts; it is shared by every window.
4. **New Console Viewer** in the window tab's menu opens another window with its own view.

### Writing a logging wrapper

Mark wrapper methods with Unity's `[HideInCallstack]`, so their frames are skipped and the top frame stays the real call site, in the viewer and, with **Strip logging callstack** enabled, in the Unity Console:

```csharp
public static class Log
{
    private static readonly IConsoleInstance Gameplay = new ConsoleInstance("Gameplay");

    [HideInCallstack]
    public static void Info(object source, string message) => Gameplay.CreateText(source, message);
}
```

### Settings

Open **Project Settings ▸ Console Container** and choose **Create Settings Asset**, which places it at `Assets/Resources/ConsoleContainerSettings.asset`, where the runtime looks for it. Without an asset, the defaults in the table apply.

| Setting | Default | Meaning |
| --- | --- | --- |
| **Log Text / Warnings / Errors In Build** | On | Forward each type to `Debug.Log`, `LogWarning`, `LogError` in player builds, once an asset exists. |
| **Editor Forwarding** | During Test Runs | When editor messages are also forwarded: `Never`, `During Test Runs` or `Always`. |
| **Log Text / Warnings / Errors In Editor** | On | Which types are forwarded while editor forwarding is active. |
| **Max Messages Per Instance** | 10,000 | Editor history per instance; the oldest are dropped first. 0 keeps everything. |
| **Capture Text / Warning / Error Callstack** | On | Record a clickable call stack per type in the editor. |

With no asset at all, nothing reaches the log in player builds. The settings page warns about assets the runtime would ignore or leave out of builds.

### Test runs

The Test Runner window only lists entries that pass through Unity's log, so by default messages are forwarded for the length of a test run, and only then. A forwarded error is a `Debug.LogError`, which fails a test unless it is declared with `LogAssert.Expect`, exactly as `Debug`-based code does; turn **Log Errors In Editor** off, or install an override (below), to see errors without failing on them. Test-run detection uses the Unity Test Framework; without it, forwarding during test runs simply never activates.

### Overriding settings at runtime

Install a settings object created in code to change behaviour without touching the project asset: a QA menu that turns build logging on, or a test that silences forwarding. Assign `null` to return to the asset.

```csharp
ConsoleContainerSettings verbose = ScriptableObject.CreateInstance<ConsoleContainerSettings>();
verbose.LogTextInBuild = true;
ConsoleContainerSettings.Active = verbose;

// Later:
ConsoleContainerSettings.Active = null;
```

An override lasts until it is replaced, destroyed, or play mode starts again. Avoid changing the project asset's own properties from code in the editor, since the change persists to disk.

### Implementing `IConsoleInstance`

Mocks and adapters implement only the core members: `Name`, `IsDisposed`, `MessageCreated`, `ErrorCreated`, `IsEnabled`, `Create`, `Clear` and `Dispose`. Every shortcut (`CreateText`, `CreateWarning`, `CreateError`, `CreateException`) is an extension method that resolves the source, builds the content and calls `Create`, so it works with any implementation.
