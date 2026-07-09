using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    internal class ConsoleViewer : EditorWindow
    {
        private const string WindowName = "Console Viewer";
        private const string AllInstancesLabel = "All Instances";
        private const string EvenRowClass = "message-container-even";
        private const string SelectedRowClass = "message-container-selected";
        private const string CallstackButtonClass = "callstack-button";

        // How close to the bottom (in pixels) the scroll must be to keep following
        // new messages. Scrolling up further than this detaches the view; scrolling
        // back within it re-attaches. The slack absorbs the per-append layout lag.
        private const float StickToBottomSlack = 30f;

        [SerializeField] private Texture2D windowIconDark;
        [SerializeField] private Texture2D windowIconLight;

        [SerializeField] private VisualTreeAsset consoleViewerAsset;
        [SerializeField] private VisualTreeAsset messageTextAsset;
        [SerializeField] private VisualTreeAsset messageWarningAsset;
        [SerializeField] private VisualTreeAsset messageErrorAsset;

        private DropdownField instanceDropdown;
        private VisualElement clearButton;
        private VisualElement copyButton;
        private VisualElement contentContainer;
        private ScrollView contentScrollView;
        private Label selectedMessageLabel;
        private VisualElement callstackContainer;

        private ConsoleMessage selectedMessage;
        private VisualElement selectedRow;

        // Dropdown index 0 is always the chronological "All Instances" view; the
        // remaining indices map to currentInstances[selectedViewIndex - 1].
        private ConsoleInstance[] currentInstances = Array.Empty<ConsoleInstance>();
        private int selectedViewIndex;

        private long lastRenderedSequence;
        private int renderedRowCount;
        private int lastRegistryVersion = -1;
        private int lastClearGeneration;

        private readonly List<ConsoleMessage> scratch = new List<ConsoleMessage>();
        private volatile bool dirty;
        private bool suppressDropdownCallback;

        [MenuItem("Tools/Console Viewer")]
        public static void ShowWindow()
        {
            ConsoleViewer window = GetWindow<ConsoleViewer>();
            Texture2D icon = EditorGUIUtility.isProSkin ? window.windowIconLight : window.windowIconDark;
            window.titleContent = new GUIContent(WindowName, icon);
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();

            VisualElement root = consoleViewerAsset.CloneTree();
            root.style.flexGrow = 1;
            rootVisualElement.Add(root);

            instanceDropdown = root.Q<DropdownField>("instance-dropdown");
            clearButton = root.Q<VisualElement>("clear-button");
            copyButton = root.Q<VisualElement>("copy-button");
            contentContainer = root.Q<VisualElement>("content-container");
            contentScrollView = contentContainer.GetFirstAncestorOfType<ScrollView>();
            selectedMessageLabel = root.Q<Label>("selected-message-label");
            callstackContainer = root.Q<VisualElement>("selected-message-callstack-container");

            clearButton.RegisterCallback<ClickEvent>(OnClearClicked);
            copyButton.RegisterCallback<ClickEvent>(OnCopyClicked);
            instanceDropdown.RegisterValueChangedCallback(OnDropdownChanged);

            ResetDetails();

            ConsoleRegistry.Changed += OnRegistryChanged;
            EditorApplication.update += OnEditorUpdate;

            lastClearGeneration = ConsoleRegistry.ClearGeneration;
            RebuildInstances();
            RebuildMessages();
        }

        private void OnDisable()
        {
            ConsoleRegistry.Changed -= OnRegistryChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        // Raised from arbitrary threads — only flip a flag and let the main-thread
        // editor loop do the UI work.
        private void OnRegistryChanged() => dirty = true;

        private void OnEditorUpdate()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;

            bool fullRebuild = false;

            if (ConsoleRegistry.Version != lastRegistryVersion)
            {
                RebuildInstances();
                fullRebuild = true;
            }

            if (ConsoleRegistry.ClearGeneration != lastClearGeneration)
            {
                lastClearGeneration = ConsoleRegistry.ClearGeneration;
                fullRebuild = true;
            }

            if (fullRebuild)
            {
                RebuildMessages();
            }
            else
            {
                AppendNewMessages();
            }
        }

        private void RebuildInstances()
        {
            ConsoleInstance previous = selectedViewIndex > 0 && selectedViewIndex - 1 < currentInstances.Length
                ? currentInstances[selectedViewIndex - 1]
                : null;

            currentInstances = ConsoleRegistry.Snapshot();
            lastRegistryVersion = ConsoleRegistry.Version;

            List<string> choices = new List<string>(currentInstances.Length + 1) { AllInstancesLabel };
            int restoredIndex = 0;
            for (int i = 0; i < currentInstances.Length; i++)
            {
                choices.Add(DisplayName(currentInstances[i]));
                if (currentInstances[i] == previous)
                {
                    restoredIndex = i + 1;
                }
            }

            selectedViewIndex = restoredIndex;

            suppressDropdownCallback = true;
            instanceDropdown.choices = choices;
            instanceDropdown.SetValueWithoutNotify(choices[selectedViewIndex]);
            suppressDropdownCallback = false;
        }

        private void OnDropdownChanged(ChangeEvent<string> evt)
        {
            if (suppressDropdownCallback)
            {
                return;
            }

            selectedViewIndex = Mathf.Max(0, instanceDropdown.index);
            RebuildMessages();
        }

        private void OnClearClicked(ClickEvent evt)
        {
            if (selectedViewIndex == 0)
            {
                foreach (ConsoleInstance instance in currentInstances)
                {
                    instance.Clear();
                }
            }
            else if (selectedViewIndex - 1 < currentInstances.Length)
            {
                currentInstances[selectedViewIndex - 1].Clear();
            }
        }

        private void RebuildMessages()
        {
            contentContainer.Clear();
            lastRenderedSequence = 0;
            renderedRowCount = 0;
            ResetDetails();
            AppendNewMessages();
            // A fresh view (instance switch, clear, window open) starts at the newest.
            ScrollToBottomDeferred();
        }

        private void AppendNewMessages()
        {
            scratch.Clear();

            if (selectedViewIndex == 0)
            {
                foreach (ConsoleInstance instance in currentInstances)
                {
                    instance.CollectMessagesAfter(lastRenderedSequence, scratch);
                }
            }
            else if (selectedViewIndex - 1 < currentInstances.Length)
            {
                currentInstances[selectedViewIndex - 1].CollectMessagesAfter(lastRenderedSequence, scratch);
            }

            if (scratch.Count == 0)
            {
                return;
            }

            // Merge across instances into one chronological stream. Because the
            // global sequence only grows, this batch always appends at the end.
            scratch.Sort(CompareBySequence);

            // Measure before appending, while the scroller still reflects a settled layout.
            bool followBottom = IsNearBottom();

            foreach (ConsoleMessage message in scratch)
            {
                VisualElement row = CreateRow(message);
                if ((renderedRowCount & 1) == 0)
                {
                    row.AddToClassList(EvenRowClass);
                }

                contentContainer.Add(row);
                renderedRowCount++;
                lastRenderedSequence = message.Sequence;
            }

            if (followBottom)
            {
                ScrollToBottomDeferred();
            }
        }

        private VisualElement CreateRow(ConsoleMessage message)
        {
            VisualTreeAsset asset = message.Type switch
            {
                MessageType.Warning => messageWarningAsset,
                MessageType.Error => messageErrorAsset,
                _ => messageTextAsset
            };

            VisualElement row = asset.Instantiate().Q<VisualElement>("message-container");
            row.Q<Label>("message-time").text = message.TimeText;
            row.Q<Label>("message-label").text = ToSingleLine(message.Label);
            row.RegisterCallback<ClickEvent>(_ => Select(message, row));
            return row;
        }

        private void Select(ConsoleMessage message, VisualElement row)
        {
            if (selectedRow != null)
            {
                selectedRow.RemoveFromClassList(SelectedRowClass);
            }

            selectedMessage = message;
            selectedRow = row;
            row.AddToClassList(SelectedRowClass);

            selectedMessageLabel.text = BuildDetails(message);
            BuildCallstack(message);
        }

        // The details label shows the full message followed by a readable stack
        // trace, mirroring the Unity Console's detail pane.
        private static string BuildDetails(ConsoleMessage message)
        {
            if (message.Callstack.Count == 0)
            {
                return message.Label;
            }

            StringBuilder builder = new StringBuilder(message.Label);
            builder.Append('\n');

            foreach (CallstackFrame frame in message.Callstack)
            {
                builder.Append('\n');
                if (!string.IsNullOrEmpty(frame.MethodName))
                {
                    builder.Append(frame.MethodName).Append(' ');
                }

                builder.Append("(at ").Append(ProjectRelativePath(frame.FilePath))
                    .Append(':').Append(frame.Line).Append(')');
            }

            return builder.ToString();
        }

        private static string ProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return absolutePath;
            }

            string normalized = absolutePath.Replace('\\', '/');

            // Application.dataPath ends with "/Assets"; strip back to the project
            // root so paths read like "Assets/…" or "Packages/…" as in Unity.
            string dataPath = Application.dataPath;
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);

            return normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(projectRoot.Length)
                : normalized;
        }

        private static string DisplayName(ConsoleInstance instance)
            => instance.IsDisposed ? $"{instance.Name} (disposed)" : instance.Name;

        private void BuildCallstack(ConsoleMessage message)
        {
            callstackContainer.Clear();

            foreach (CallstackFrame frame in message.Callstack)
            {
                CallstackFrame captured = frame;
                Button button = new Button(() => OpenFrame(captured))
                {
                    text = frame.Label,
                    tooltip = frame.MethodName
                };
                button.AddToClassList(CallstackButtonClass);
                callstackContainer.Add(button);
            }
        }

        private static void OpenFrame(CallstackFrame frame)
        {
            if (string.IsNullOrEmpty(frame.FilePath))
            {
                return;
            }

            InternalEditorUtility.OpenFileAtLineExternal(frame.FilePath, frame.Line, 0);
        }

        private void OnCopyClicked(ClickEvent evt)
        {
            if (selectedMessage != null)
            {
                EditorGUIUtility.systemCopyBuffer = selectedMessage.Label;
            }
        }

        private void ResetDetails()
        {
            selectedMessage = null;
            selectedRow = null;
            callstackContainer.Clear();
            selectedMessageLabel.text = string.Empty;
        }

        private static int CompareBySequence(ConsoleMessage a, ConsoleMessage b)
            => a.Sequence.CompareTo(b.Sequence);

        private static string ToSingleLine(string text)
            => string.IsNullOrEmpty(text) ? text : text.Replace('\r', ' ').Replace('\n', ' ');

        // True while the view should follow new messages: no scrollable content
        // yet, or scrolled to within StickToBottomSlack of the bottom. Evaluated
        // before rows are appended, while the scroller reflects a settled layout.
        private bool IsNearBottom()
        {
            if (contentScrollView == null)
            {
                return true;
            }

            Scroller scroller = contentScrollView.verticalScroller;
            return scroller.highValue <= 0f || scroller.value >= scroller.highValue - StickToBottomSlack;
        }

        private void ScrollToBottomDeferred()
        {
            if (contentScrollView == null)
            {
                return;
            }

            // Scheduled items can run before the freshly added rows get a layout, in
            // which case the scroller range still describes the old content. Snap on
            // two consecutive ticks: the first covers the case where layout already
            // ran, the second sees the settled range.
            ScrollView scrollView = contentScrollView;
            scrollView.schedule.Execute(() =>
            {
                SnapToBottom(scrollView);
                scrollView.schedule.Execute(() => SnapToBottom(scrollView));
            });
        }

        private static void SnapToBottom(ScrollView scrollView)
        {
            Scroller scroller = scrollView.verticalScroller;
            // Never below zero: with always-visible scrollers and content shorter
            // than the viewport, highValue is negative and snapping to it would push
            // the content downward.
            scroller.value = Mathf.Max(0f, scroller.highValue);
        }
    }
}
