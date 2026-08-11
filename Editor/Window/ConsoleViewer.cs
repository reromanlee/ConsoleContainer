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

        // View state below is serialized per window: EditorWindow fields survive
        // domain reloads and are stored in the editor layout, so every window
        // instance keeps its own sizes and reopens the way it was left.
        [SerializeField] private float detailsPaneSize;
        [SerializeField] private float callstackPaneSize;
        [SerializeField] private float scrollOffset;
        [SerializeField] private string selectedInstanceName = string.Empty;

        private DropdownField instanceDropdown;
        private VisualElement clearButton;
        private VisualElement copyButton;
        private VisualElement contentContainer;
        private ScrollView contentScrollView;
        private Label selectedMessageLabel;
        private VisualElement callstackContainer;

        private TwoPaneSplitView mainSplitView;
        private TwoPaneSplitView detailsSplitView;
        private VisualElement detailsPane;

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
        private readonly HashSet<string> usedInstanceLabels = new HashSet<string>();
        private volatile bool dirty;
        private bool suppressDropdownCallback;
        private bool restoreScrollPending;

        [MenuItem("Tools/Console Viewer")]
        public static void ShowWindow()
        {
            GetWindow<ConsoleViewer>().Show();
        }

        private void OnEnable()
        {
            ApplyWindowChrome();

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

            mainSplitView = root.Q<TwoPaneSplitView>("main-split-view");
            detailsSplitView = root.Q<TwoPaneSplitView>("details-split-view");
            detailsPane = root.Q<VisualElement>("details-pane");

            // Applied before the first layout pass, so the panes come up at their
            // remembered sizes instead of visibly snapping into place.
            RestorePaneSizes();

            clearButton.RegisterCallback<ClickEvent>(OnClearClicked);
            copyButton.RegisterCallback<ClickEvent>(OnCopyClicked);
            instanceDropdown.RegisterValueChangedCallback(OnDropdownChanged);

            RegisterViewStateTracking();

            ResetDetails();

            ConsoleRegistry.Changed += OnRegistryChanged;
            EditorApplication.update += OnEditorUpdate;

            lastClearGeneration = ConsoleRegistry.ClearGeneration;
            restoreScrollPending = true;
            RebuildInstances();
            RebuildMessages();
        }

        private void OnDisable()
        {
            // Belt and braces: the tracking callbacks keep the serialized fields
            // current, and this catches whatever changed in the same frame the
            // window went away.
            CaptureViewState();

            UnregisterViewStateTracking();

            ConsoleRegistry.Changed -= OnRegistryChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        // Title, icon and minimum size are applied on every enable rather than
        // only when the menu item opens the window, so a window restored from a
        // saved layout (or reloaded after a theme change) still looks right.
        private void ApplyWindowChrome()
        {
            Texture2D icon = EditorGUIUtility.isProSkin ? windowIconLight : windowIconDark;
            titleContent = new GUIContent(WindowName, icon);
            minSize = new Vector2(600, 400);
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

            usedInstanceLabels.Clear();
            usedInstanceLabels.Add(AllInstancesLabel);

            List<string> choices = new List<string>(currentInstances.Length + 1) { AllInstancesLabel };
            int restoredIndex = 0;
            for (int i = 0; i < currentInstances.Length; i++)
            {
                choices.Add(BuildDisplayName(currentInstances[i]));
                if (currentInstances[i] == previous)
                {
                    restoredIndex = i + 1;
                }
            }

            // The selected instance can disappear between rebuilds — a domain
            // reload drops every instance, and a cleared disposed one is removed —
            // so fall back to the remembered name. Re-running the same code then
            // re-attaches the view to the freshly created instance instead of
            // silently dropping the user back to "All Instances".
            if (restoredIndex == 0 && !string.IsNullOrEmpty(selectedInstanceName))
            {
                restoredIndex = FindInstanceIndexByName(selectedInstanceName);
            }

            selectedViewIndex = restoredIndex;

            // selectedInstanceName is deliberately not cleared when nothing
            // matched: keeping it is what lets the view re-attach later. Only an
            // explicit pick in the dropdown rewrites it.
            if (restoredIndex > 0)
            {
                selectedInstanceName = currentInstances[restoredIndex - 1].Name;
            }

            suppressDropdownCallback = true;
            instanceDropdown.choices = choices;
            instanceDropdown.SetValueWithoutNotify(choices[selectedViewIndex]);
            suppressDropdownCallback = false;
        }

        private int FindInstanceIndexByName(string name)
        {
            for (int i = 0; i < currentInstances.Length; i++)
            {
                if (string.Equals(currentInstances[i].Name, name, StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private void OnDropdownChanged(ChangeEvent<string> evt)
        {
            if (suppressDropdownCallback)
            {
                return;
            }

            selectedViewIndex = Mathf.Max(0, instanceDropdown.index);
            selectedInstanceName = selectedViewIndex > 0 && selectedViewIndex - 1 < currentInstances.Length
                ? currentInstances[selectedViewIndex - 1].Name
                : string.Empty;

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

            if (restoreScrollPending)
            {
                // First build after the window came back: land where the user left
                // off rather than jumping to the newest message.
                restoreScrollPending = false;
                RestoreScrollDeferred();
            }
            else
            {
                // A fresh view (instance switch, clear) starts at the newest.
                ScrollToBottomDeferred();
            }
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

        /// <summary>
        /// Builds the dropdown entry for an instance, keeping it unique within the
        /// current list. Instance names are free-form and duplicates are common
        /// (a system that recreates its instance, or two features picking the same
        /// label), but a dropdown identifies its selection by text: without a
        /// suffix, picking the second "Networking" would silently show the first.
        /// </summary>
        private string BuildDisplayName(ConsoleInstance instance)
        {
            string label = DecorateDisplayName(instance.Name, instance.IsDisposed);

            for (int ordinal = 2; !usedInstanceLabels.Add(label); ordinal++)
            {
                label = DecorateDisplayName($"{instance.Name} ({ordinal})", instance.IsDisposed);
            }

            return label;
        }

        private static string DecorateDisplayName(string name, bool isDisposed)
            => isDisposed ? $"{name} (disposed)" : name;

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
                // Copy the full details (message + stack trace) so it can be pasted
                // straight into a chat/issue for investigation.
                EditorGUIUtility.systemCopyBuffer = BuildDetails(selectedMessage);
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

        #region View state

        private void RegisterViewStateTracking()
        {
            if (detailsPane != null)
            {
                detailsPane.RegisterCallback<GeometryChangedEvent>(OnDetailsPaneGeometryChanged);
            }

            if (callstackContainer != null)
            {
                callstackContainer.RegisterCallback<GeometryChangedEvent>(OnCallstackPaneGeometryChanged);
            }

            if (contentScrollView != null)
            {
                contentScrollView.verticalScroller.valueChanged += OnScrollOffsetChanged;
            }
        }

        private void UnregisterViewStateTracking()
        {
            if (detailsPane != null)
            {
                detailsPane.UnregisterCallback<GeometryChangedEvent>(OnDetailsPaneGeometryChanged);
            }

            if (callstackContainer != null)
            {
                callstackContainer.UnregisterCallback<GeometryChangedEvent>(OnCallstackPaneGeometryChanged);
            }

            if (contentScrollView != null)
            {
                contentScrollView.verticalScroller.valueChanged -= OnScrollOffsetChanged;
            }
        }

        private void RestorePaneSizes()
        {
            // Zero means "never resized", in which case the dimensions authored in
            // the UXML are already in place.
            if (mainSplitView != null && detailsPaneSize > 0f)
            {
                mainSplitView.fixedPaneInitialDimension = detailsPaneSize;
            }

            if (detailsSplitView != null && callstackPaneSize > 0f)
            {
                detailsSplitView.fixedPaneInitialDimension = callstackPaneSize;
            }
        }

        // Both panes are the "fixed" side of their split view, so their resolved
        // size is exactly what the dragline was left at.
        private void OnDetailsPaneGeometryChanged(GeometryChangedEvent evt)
        {
            float height = detailsPane.resolvedStyle.height;
            if (height > 0f)
            {
                detailsPaneSize = height;
            }
        }

        private void OnCallstackPaneGeometryChanged(GeometryChangedEvent evt)
        {
            float width = callstackContainer.resolvedStyle.width;
            if (width > 0f)
            {
                callstackPaneSize = width;
            }
        }

        private void OnScrollOffsetChanged(float value)
        {
            scrollOffset = value;
        }

        private void CaptureViewState()
        {
            if (detailsPane != null && detailsPane.resolvedStyle.height > 0f)
            {
                detailsPaneSize = detailsPane.resolvedStyle.height;
            }

            if (callstackContainer != null && callstackContainer.resolvedStyle.width > 0f)
            {
                callstackPaneSize = callstackContainer.resolvedStyle.width;
            }

            if (contentScrollView != null)
            {
                scrollOffset = contentScrollView.verticalScroller.value;
            }
        }

        #endregion

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
            ScheduleScroll(scrollView => SnapToBottom(scrollView));
        }

        private void RestoreScrollDeferred()
        {
            // Captured now: the pending snap-to-bottom of the initial append would
            // otherwise overwrite the remembered offset before this runs.
            float offset = scrollOffset;
            ScheduleScroll(scrollView => ApplyScrollOffset(scrollView, offset));
        }

        private void ScheduleScroll(Action<ScrollView> action)
        {
            if (contentScrollView == null)
            {
                return;
            }

            // Scheduled items can run before the freshly added rows get a layout, in
            // which case the scroller range still describes the old content. Act on
            // two consecutive ticks: the first covers the case where layout already
            // ran, the second sees the settled range.
            ScrollView scrollView = contentScrollView;
            scrollView.schedule.Execute(() =>
            {
                action(scrollView);
                scrollView.schedule.Execute(() => action(scrollView));
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

        private static void ApplyScrollOffset(ScrollView scrollView, float offset)
        {
            Scroller scroller = scrollView.verticalScroller;
            scroller.value = Mathf.Clamp(offset, 0f, Mathf.Max(0f, scroller.highValue));
        }
    }
}
