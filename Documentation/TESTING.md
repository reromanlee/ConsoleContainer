### Testing

The package ships its test suite in `UnityPackage/Tests`. It compiles only in projects that opt in, so it costs nothing otherwise.

### What is covered

1. **Runtime** (`Tests/Runtime`, Play Mode and players): message composition and source naming, culture-independent time text, sequence ordering, exception logging, `MessageCreated`, `ErrorCreated` and `Created` events, throwing handlers, re-entrant handlers and constructors, disposal, logging from many threads, runtime settings overrides, and zero-allocation paths (dropped messages, cached source names, cached derived text).
2. **Editor** (`Tests/Editor`, Edit Mode): the history ring buffer and its cursors, retention limits, call stack capture (log site, `[HideInCallstack]` wrappers, lambdas, iterators, exceptions and their inner exceptions, per-type toggles, call sites sharing one method), registry lifetime, forwarding modes and per-type toggles, the viewer's message feed (chronological merging, type filters, counts, search, pruning of dropped history, and concurrent logging that never loses a message), details text and truncation, the settings page's asset checks, and the Console Viewer window itself.

For 1.0.0, both suites passed in the editor on Unity 2022.3.62f3 and Unity 6000.0.85f1. Performance was measured in an IL2CPP Windows release player with managed stripping set to High, see [PERFORMANCE.md](PERFORMANCE.md).

### Running the tests locally

Add the package to `testables` in your project's `Packages/manifest.json`, then open **Window ▸ General ▸ Test Runner**:

```json
{
  "dependencies": {
    "com.reromanlee.consolecontainer": "https://github.com/reromanlee/ConsoleContainer.git?path=/UnityPackage"
  },
  "testables": [
    "com.reromanlee.consolecontainer"
  ]
}
```

Run Edit Mode and Play Mode tests in the editor. The tests install a runtime settings override that forwards nothing, so they behave the same whatever settings asset the project has.

### Continuous integration

`.github/workflows/tests.yml` runs both suites with [GameCI](https://game.ci) on Unity 2022.3 and Unity 6 for every push to `main` and every pull request. It needs the `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` repository secrets; without them the job passes with a notice instead of failing, so forks stay green.
