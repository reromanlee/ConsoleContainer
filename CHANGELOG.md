# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-08-11

### Added

- **`ConsoleInstance.Created`** — a static event raised right after any instance
  finishes constructing, so one subscriber can attach to every instance the
  application will ever create, including the ones built later by nested scopes.
  Previously a subscriber had to be wired up at each construction site, which
  meant a console added later silently went unobserved — the failure mode that
  matters most when that subscriber is a crash handler. Handlers see a fully
  built instance, run on the constructing thread, and one that throws is reported
  through `Debug.LogException` without disturbing the construction that triggered
  it. Subscriptions are static and live as long as the domain does.

## [1.1.0] - 2026-08-11

### Added

- **Message events** — `IConsoleInstance` now raises `MessageCreated` for every
  message and `ErrorCreated` for errors, so application code can react to what it
  logs (raising a soft crash screen with the error as its reason, for example).
  Both fire in player builds as well as the editor, on the thread that logged the
  message; a handler that throws is reported through `Debug.LogException` without
  disturbing the logging call, and `Dispose` drops every handler. In a build the
  `ConsoleMessage` is only allocated when something is subscribed.
- **Parameterless `ConsoleInstance` constructor** — dependency-injection
  containers can resolve `IConsoleInstance` by convention instead of failing to
  supply the optional name ([#3]).
- **Editor logging settings** — `ConsoleContainerSettings` gained an *Editor
  logging* section: a forwarding mode (`Never`, `During Test Runs`, `Always`) and
  per-type toggles. Defaulting to *During Test Runs* makes messages show up in the
  Test Runner window for the length of a run while leaving the Unity Console clean
  the rest of the time, and it works without a settings asset ([#5]). Test-run
  detection lives in a separate assembly gated on the Unity Test Framework, so
  projects without that package are unaffected.
- **Per-window view state** — each Console Viewer window remembers its splitter
  sizes, selected instance and scroll position across domain reloads and editor
  restarts, and re-attaches to a recreated instance by name ([#7]).

### Fixed

- Instances that share a name no longer collapse into a single dropdown entry
  where picking one silently showed another; duplicates are numbered
  (`Networking`, `Networking (2)`) ([#4]).
- Disposed instances no longer linger in the dropdown once they have nothing to
  show. An instance is dropped when it is disposed while empty, or cleared after
  being disposed, so repeated test runs without a domain reload stop stacking
  stale entries in front of freshly created ones ([#6]).
- The registry's version counters are now incremented atomically; concurrent
  logging could previously lose an update and leave the viewer out of date.
- Loading the settings asset no longer risks touching the main-thread-only
  `Resources` API from a background thread, and edits to the asset take effect
  without waiting for a domain reload.
- The window title, icon and minimum size are re-applied whenever the window is
  enabled, so a viewer restored from a saved layout keeps them.

## [1.0.0] - 2026-07-09

Initial release.

### Added

- **Console instances** — `ConsoleInstance` implementing `IConsoleInstance`,
  with an optional name passed to the constructor for the viewer dropdown.
- **Three message types** — `CreateText`, `CreateWarning` and `CreateError`,
  each with `object` and `string` source overloads plus `params string[]`
  content. Source resolves to the string, or the object's type name; content is
  the arguments joined by single spaces; the label is `{source}: {content}`.
- **Thread-safe logging** — every `Create*` call is safe from any thread
  (jobs, background threads, async), guarded by per-instance locking, an atomic
  global sequence number and a thread-safe editor registry.
- **Console Viewer window** (`Tools ▸ Console Viewer`):
  - Instance dropdown with an **All Instances** entry that merges every
    instance's messages in chronological order.
  - `Text` / `Warning` / `Error` rows rendered from visual tree assets, with
    per-type icons/colors, `[HH:mm:ss]` timestamps and even-row zebra striping.
  - Message selection: the details pane shows the full `{source}: {content}`
    text followed by the complete stack trace (like the Unity Console), a
    **Copy** button, and the same call stack rendered on the left as buttons
    that open the file at its line in the external IDE (top button = the log
    call site).
  - **Clear** button that clears the selected instance, or all of them when
    *All Instances* is selected.
  - Incremental rendering (append-only) with auto-scroll when pinned to the
    bottom; full rebuilds only on instance-set or clear changes.
  - Instances and their messages remain viewable after Play Mode ends (flagged
    `(disposed)` in the dropdown) until the next domain reload.
- **Editor vs. player build separation** — in the editor, messages appear only
  in the Console Viewer and never reach the Unity Console.
- **Optional build logging** — `ConsoleContainerSettings` ScriptableObject
  (Assets ▸ Create ▸ ConsoleContainer ▸ Settings) with an independent toggle per
  message type. In player builds, enabled types are forwarded as
  `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` of `{source}: {content}`;
  with no settings asset in the build, all messages stay hidden.
- **Console Container Demo sample** — an importable MonoBehaviour that logs to
  several named instances continuously, including from a background thread.

[1.0.0]: https://github.com/reromanlee/ConsoleContainer/releases/tag/v1.0.0
[1.1.0]: https://github.com/reromanlee/ConsoleContainer/releases/tag/v1.1.0
[1.2.0]: https://github.com/reromanlee/ConsoleContainer/releases/tag/v1.2.0
[#3]: https://github.com/reromanlee/ConsoleContainer/issues/3
[#4]: https://github.com/reromanlee/ConsoleContainer/issues/4
[#5]: https://github.com/reromanlee/ConsoleContainer/issues/5
[#6]: https://github.com/reromanlee/ConsoleContainer/issues/6
[#7]: https://github.com/reromanlee/ConsoleContainer/issues/7
