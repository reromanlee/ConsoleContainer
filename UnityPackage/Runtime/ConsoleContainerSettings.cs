using System;
using UnityEngine;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Optional project settings that decide whether ConsoleContainer messages
    /// are forwarded to the Unity <see cref="Debug"/> log, with an independent
    /// toggle per message type for <b>player builds</b> and for the <b>editor</b>.
    ///
    /// Editor forwarding is what makes messages visible in the Test Runner
    /// window (see <see cref="EditorForwardingMode"/>); it is limited to test
    /// runs by default so the Unity Console stays clean during normal work.
    ///
    /// To take effect in a build, create one asset from
    /// <c>Project Settings ▸ Console Container</c> (or Assets ▸ Create ▸
    /// ConsoleContainer ▸ Settings, placed directly inside any <c>Resources</c>
    /// folder). When no asset exists, all messages are hidden in builds and the
    /// editor defaults below apply. <see cref="Active"/> also accepts a settings
    /// object created at runtime, which overrides the asset.
    /// </summary>
    [CreateAssetMenu(fileName = ResourcesPath, menuName = "ConsoleContainer/Settings", order = 0)]
    public sealed class ConsoleContainerSettings : ScriptableObject
    {
        /// <summary>Resources path the runtime looks up to load the active settings.</summary>
        public const string ResourcesPath = "ConsoleContainerSettings";

        /// <summary>Editor forwarding used when no settings asset exists.</summary>
        internal const EditorForwardingMode DefaultEditorForwarding = EditorForwardingMode.DuringTestRuns;

        /// <summary>Per-type editor forwarding used when no settings asset exists.</summary>
        internal const bool DefaultLogInEditor = true;

        /// <summary>Per-type editor call stack capture used when no settings asset exists.</summary>
        internal const bool DefaultCaptureCallstack = true;

        /// <summary>Most messages each instance keeps in the editor when no settings asset exists.</summary>
        internal const int DefaultMaxMessagesPerInstance = 10000;

        [Header("Player build logging")]
        [Tooltip("Forward Text messages to Debug.Log in player builds.")]
        [SerializeField] private bool logTextInBuild = true;

        [Tooltip("Forward Warning messages to Debug.LogWarning in player builds.")]
        [SerializeField] private bool logWarningsInBuild = true;

        [Tooltip("Forward Error messages to Debug.LogError in player builds.")]
        [SerializeField] private bool logErrorsInBuild = true;

        [Header("Editor logging")]
        [Tooltip("When editor messages are also forwarded to the Unity console. " +
                 "Forwarding during test runs is what makes them visible in the Test Runner window.")]
        [SerializeField] private EditorForwardingMode editorForwarding = DefaultEditorForwarding;

        [Tooltip("Forward Text messages to Debug.Log while editor forwarding is active.")]
        [SerializeField] private bool logTextInEditor = DefaultLogInEditor;

        [Tooltip("Forward Warning messages to Debug.LogWarning while editor forwarding is active.")]
        [SerializeField] private bool logWarningsInEditor = DefaultLogInEditor;

        [Tooltip("Forward Error messages to Debug.LogError while editor forwarding is active. " +
                 "Note that an unexpected Debug.LogError fails the running test unless it is " +
                 "declared with LogAssert.Expect.")]
        [SerializeField] private bool logErrorsInEditor = DefaultLogInEditor;

        [Header("Editor history")]
        [Tooltip("Most messages each instance keeps in the editor; once reached, the oldest are dropped " +
                 "first. Bounds memory in long sessions. 0 keeps everything.")]
        [Min(0)]
        [SerializeField] private int maxMessagesPerInstance = DefaultMaxMessagesPerInstance;

        [Tooltip("Capture a clickable call stack for Text messages in the editor. Stack capture is by far " +
                 "the most expensive part of a log call; turn it off for messages logged on hot paths.")]
        [SerializeField] private bool captureTextCallstack = DefaultCaptureCallstack;

        [Tooltip("Capture a clickable call stack for Warning messages in the editor.")]
        [SerializeField] private bool captureWarningCallstack = DefaultCaptureCallstack;

        [Tooltip("Capture a clickable call stack for Error messages in the editor. " +
                 "Messages that report an exception always show where it was thrown.")]
        [SerializeField] private bool captureErrorCallstack = DefaultCaptureCallstack;

        /// <summary>Whether Text messages are forwarded to <c>Debug.Log</c> in player builds.</summary>
        public bool LogTextInBuild
        {
            get => logTextInBuild;
            set => logTextInBuild = value;
        }

        /// <summary>Whether Warning messages are forwarded to <c>Debug.LogWarning</c> in player builds.</summary>
        public bool LogWarningsInBuild
        {
            get => logWarningsInBuild;
            set => logWarningsInBuild = value;
        }

        /// <summary>Whether Error messages are forwarded to <c>Debug.LogError</c> in player builds.</summary>
        public bool LogErrorsInBuild
        {
            get => logErrorsInBuild;
            set => logErrorsInBuild = value;
        }

        /// <summary>When editor messages are mirrored into the Unity console.</summary>
        public EditorForwardingMode EditorForwarding
        {
            get => editorForwarding;
            set => editorForwarding = value;
        }

        /// <summary>Whether Text messages are forwarded while editor forwarding is active.</summary>
        public bool LogTextInEditor
        {
            get => logTextInEditor;
            set => logTextInEditor = value;
        }

        /// <summary>Whether Warning messages are forwarded while editor forwarding is active.</summary>
        public bool LogWarningsInEditor
        {
            get => logWarningsInEditor;
            set => logWarningsInEditor = value;
        }

        /// <summary>Whether Error messages are forwarded while editor forwarding is active.</summary>
        public bool LogErrorsInEditor
        {
            get => logErrorsInEditor;
            set => logErrorsInEditor = value;
        }

        /// <summary>Most messages each instance keeps in the editor before dropping the oldest; 0 keeps everything.</summary>
        public int MaxMessagesPerInstance
        {
            get => maxMessagesPerInstance;
            set => maxMessagesPerInstance = Math.Max(0, value);
        }

        /// <summary>Whether Text messages capture a call stack in the editor.</summary>
        public bool CaptureTextCallstack
        {
            get => captureTextCallstack;
            set => captureTextCallstack = value;
        }

        /// <summary>Whether Warning messages capture a call stack in the editor.</summary>
        public bool CaptureWarningCallstack
        {
            get => captureWarningCallstack;
            set => captureWarningCallstack = value;
        }

        /// <summary>Whether Error messages capture a call stack in the editor.</summary>
        public bool CaptureErrorCallstack
        {
            get => captureErrorCallstack;
            set => captureErrorCallstack = value;
        }

        private static volatile ConsoleContainerSettings _asset;
        private static volatile ConsoleContainerSettings _override;
        private static volatile bool _loaded;
        private static volatile int _mainThreadIdentifier;

        /// <summary>
        /// The settings in effect: a runtime override when one is installed, otherwise the asset
        /// loaded from <c>Resources</c>, or <c>null</c> when neither exists (package defaults then
        /// apply).
        ///
        /// Assign a settings object created at runtime (<c>ScriptableObject.CreateInstance</c>) to
        /// change behaviour from code — a QA menu turning build logging on, a test silencing
        /// forwarding — without touching the project asset, whose properties would persist to disk
        /// if changed in the editor. Assign <c>null</c> to return to the asset. An override lasts
        /// until it is replaced, destroyed, or play mode starts again.
        ///
        /// Safe to read from any thread. <see cref="Resources"/> is a main-thread-only API, so the
        /// asset lookup only ever runs there; anywhere else this returns the last resolved asset —
        /// warmed at startup — rather than risking an exception.
        /// </summary>
        public static ConsoleContainerSettings Active
        {
            get
            {
                // Unity's null check, so a destroyed override falls back to the asset.
                ConsoleContainerSettings runtimeOverride = _override;
                if (runtimeOverride != null)
                {
                    return runtimeOverride;
                }

                if (!_loaded && Environment.CurrentManagedThreadId == _mainThreadIdentifier)
                {
                    Load();
                }

                // Off the main thread, or before the very first load, this is the
                // last resolved value — never a forbidden Resources call.
                return _asset;
            }
            set => _override = value;
        }

        private static void Load()
        {
            _asset = Resources.Load<ConsoleContainerSettings>(ResourcesPath);
            _loaded = true;
        }

        // Identifying the main thread is what lets Active refuse to touch the
        // Resources API from anywhere else, so it happens at the earliest
        // initialization step available. An override left from a previous play
        // session (when entering play mode skips the domain reload) is dropped too.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThread()
        {
            _mainThreadIdentifier = Environment.CurrentManagedThreadId;
            _override = null;
        }

        // Warm the cache on the main thread at startup so console instances that
        // log from background threads can read the settings without touching the
        // (main-thread-only) Resources API.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreloadOnPlay()
        {
            Load();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void PreloadInEditor()
        {
            _mainThreadIdentifier = Environment.CurrentManagedThreadId;

            // The asset database is not guaranteed to answer this early in a
            // domain reload, so resolve the asset once the editor is idle. Any
            // main-thread access before that still loads lazily.
            UnityEditor.EditorApplication.delayCall += Load;
        }

        // Keeps the cached asset in step with edits made while the editor runs:
        // OnEnable covers creating or (re)loading the asset, OnValidate covers
        // toggling a value in the inspector. Without this, changes would only
        // take effect after the next domain reload.
        private void OnEnable() => Invalidate();

        private void OnValidate() => Invalidate();

        // Only the "resolved" flag is dropped: the previously loaded asset stays
        // in place so a background thread logging in the meantime keeps using the
        // project's settings instead of falling back to the package defaults.
        private static void Invalidate()
        {
            _loaded = false;
        }
#endif
    }
}
