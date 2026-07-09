using UnityEngine;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Optional project settings that decide whether ConsoleContainer messages
    /// are forwarded to the Unity <see cref="Debug"/> log in <b>player builds</b>,
    /// with an independent toggle per message type.
    ///
    /// In the Unity Editor these settings are ignored on purpose — editor
    /// messages are shown exclusively inside the Console Viewer window and never
    /// reach the Unity Console.
    ///
    /// To take effect in a build, create one asset (Assets ▸ Create ▸
    /// ConsoleContainer ▸ Settings) and place it inside any <c>Resources</c>
    /// folder. When no asset exists, all messages are hidden in builds.
    /// </summary>
    [CreateAssetMenu(fileName = ResourcesPath, menuName = "ConsoleContainer/Settings", order = 0)]
    public sealed class ConsoleContainerSettings : ScriptableObject
    {
        /// <summary>Resources path the runtime looks up to load the active settings.</summary>
        public const string ResourcesPath = "ConsoleContainerSettings";

        [Header("Player build logging")]
        [Tooltip("Forward Text messages to Debug.Log in player builds.")]
        [SerializeField] private bool logTextInBuild = true;

        [Tooltip("Forward Warning messages to Debug.LogWarning in player builds.")]
        [SerializeField] private bool logWarningsInBuild = true;

        [Tooltip("Forward Error messages to Debug.LogError in player builds.")]
        [SerializeField] private bool logErrorsInBuild = true;

        public bool LogTextInBuild => logTextInBuild;
        public bool LogWarningsInBuild => logWarningsInBuild;
        public bool LogErrorsInBuild => logErrorsInBuild;

        private static volatile ConsoleContainerSettings _active;
        private static volatile bool _loaded;

        /// <summary>
        /// The active settings asset loaded from <c>Resources</c>, or <c>null</c>
        /// when none exists (messages are then fully hidden in builds).
        /// The lookup is cached after the first access.
        /// </summary>
        public static ConsoleContainerSettings Active
        {
            get
            {
                if (!_loaded)
                {
                    _active = Resources.Load<ConsoleContainerSettings>(ResourcesPath);
                    _loaded = true;
                }

                return _active;
            }
        }

#if !UNITY_EDITOR
        // Warm the cache on the main thread at startup so console instances that
        // log from background threads can read the settings without touching the
        // (main-thread-only) Resources API.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preload()
        {
            _ = Active;
        }
#endif
    }
}
