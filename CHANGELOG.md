# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
