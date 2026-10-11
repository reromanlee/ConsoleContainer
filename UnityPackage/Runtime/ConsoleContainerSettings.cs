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
    /// To take effect in a build, create one asset (Assets ▸ Create ▸
    /// ConsoleContainer ▸ Settings) and place it inside any <c>Resources</c>
    /// folder. When no asset exists, all messages are hidden in builds and the
    /// editor defaults below apply.
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

        public bool LogTextInBuild => logTextInBuild;
        public bool LogWarningsInBuild => logWarningsInBuild;
        public bool LogErrorsInBuild => logErrorsInBuild;

        /// <summary>When editor messages are mirrored into the Unity console.</summary>
        public EditorForwardingMode EditorForwarding => editorForwarding;

        public bool LogTextInEditor => logTextInEditor;
        public bool LogWarningsInEditor => logWarningsInEditor;
        public bool LogErrorsInEditor => logErrorsInEditor;

        private static volatile ConsoleContainerSettings _active;
        private static volatile bool _loaded;
        private static volatile int _mainThreadIdentifier;

        /// <summary>
        /// The active settings asset loaded from <c>Resources</c>, or <c>null</c>
        /// when none exists (package defaults then apply). The lookup is cached
        /// after the first access.
        ///
        /// <see cref="Resources"/> is a main-thread-only API, so the lookup only
        /// ever runs on the main thread; anywhere else this returns the last
        /// resolved value rather than risking an exception. Because the cache is
        /// warmed at startup, that only matters for a background thread logging
        /// before the very first load.
        /// </summary>
        public static ConsoleContainerSettings Active
        {
            get
            {
                if (!_loaded && Environment.CurrentManagedThreadId == _mainThreadIdentifier)
                {
                    Load();
                }

                // Off the main thread, or before the very first load, this is the
                // last resolved value — never a forbidden Resources call.
                return _active;
            }
        }

        private static void Load()
        {
            _active = Resources.Load<ConsoleContainerSettings>(ResourcesPath);
            _loaded = true;
        }

        // Identifying the main thread is what lets Active refuse to touch the
        // Resources API from anywhere else, so it happens at the earliest
        // initialization step available.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThread()
        {
            _mainThreadIdentifier = Environment.CurrentManagedThreadId;
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
            CaptureMainThread();

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
