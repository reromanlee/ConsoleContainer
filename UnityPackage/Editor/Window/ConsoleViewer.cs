using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// Editor window listing the messages of one console instance, or of every instance merged in
    /// chronological order, with a details pane for the selected message and its clickable call
    /// stack. The list is virtualized, so its cost depends on the rows on screen rather than on
    /// how many messages exist. Several windows can be open at once, each with its own view.
    /// </summary>
    internal sealed class ConsoleViewer : EditorWindow, IHasCustomMenu
    {
        private const string WindowName = "Console Viewer";
        private const string AllInstancesLabel = "All Instances";
        private const string LightThemeClass = "console-viewer--light";
        private const string CallstackButtonClass = "callstack-button";
        private const string LoadErrorClass = "console-viewer__load-error";
        private const string TypeIconClass = "console-viewer__type-icon";
        private const string CountOverflowText = "999+";
        private const int MaximumShownCount = 999;
        private const string CopyCommand = "Copy";
        private const float RowHeight = 18f;

        // How close to the bottom (in pixels) the list must be to keep following new messages.
        // Scrolling up further detaches the view; scrolling back within it re-attaches.
        private const float StickToBottomSlack = RowHeight * 2f;

        // Used when the script's default references are missing, so a viewer restored from an old
        // layout or a moved package still finds what it is built from.
        private const string LayoutGuid = "fbd09c8d7cd32064898d4ed7d266f022";
        private const string DarkIconGuid = "14f03de8a7c5d9543aee0043237d84fa";
        private const string LightIconGuid = "d3ad80c4b750d3748bd70c14e8fddba9";

        [SerializeField] private Texture2D windowIconDark;
        [SerializeField] private Texture2D windowIconLight;
        [SerializeField] private VisualTreeAsset consoleViewerAsset;

        // View state below is serialized per window: EditorWindow fields survive domain reloads
        // and are stored in the editor layout, so every window reopens the way it was left.
        [SerializeField] private float detailsPaneSize;
        [SerializeField] private float callstackPaneSize;
        [SerializeField] private float scrollOffset;
        [SerializeField] private string selectedInstanceName = string.Empty;
        [SerializeField] private string searchText = string.Empty;
        [SerializeField] private bool showText = true;
        [SerializeField] private bool showWarnings = true;
        [SerializeField] private bool showErrors = true;

        private readonly MessageFeed feed = new MessageFeed();
        private readonly HashSet<string> usedInstanceLabels = new HashSet<string>();
        private readonly List<string> instanceLabels = new List<string>();
        private readonly int[] selectionBuffer = new int[1];
        private readonly ToolbarToggle[] typeToggles = new ToolbarToggle[3];
        private readonly int[] shownCounts = { -1, -1, -1 };

        private ToolbarMenu instanceMenu;
        private ToolbarToggle clearOnPlayToggle;
        private ListView messageList;
        private ScrollView messageScrollView;
        private TwoPaneSplitView mainSplitView;
        private TwoPaneSplitView detailsSplitView;
        private VisualElement detailsPane;
        private VisualElement callstackPane;
        private Label detailsLabel;
        private Label detailsInstanceLabel;

        // Index 0 of the instance menu is always the chronological "All Instances" view; the rest
        // map to currentInstances[selectedViewIndex - 1].
        private ConsoleInstance[] currentInstances = Array.Empty<ConsoleInstance>();
        private ConsoleInstance[] feedScope = Array.Empty<ConsoleInstance>();
        private int selectedViewIndex;

        private ConsoleMessage selectedMessage;
        private ScrollTarget pendingScroll;
        private float pendingScrollOffset;
        private Action applyPendingScrollFirst;
        private Action applyPendingScrollSecond;
        private int lastRegistryVersion = -1;
        private int lastClearGeneration;
        private bool isBuilt;
        private bool restoreScrollPending;

        // Set from any logging thread; the editor loop does the actual work on the main thread.
        private volatile bool dirty;

        [MenuItem("Tools/Console Viewer")]
        public static void ShowWindow()
        {
            GetWindow<ConsoleViewer>().Show();
        }

        // The tab's context menu opens another viewer, so two instances can be watched side by
        // side; each window keeps its own selection, filters and layout.
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("New Console Viewer"), false, OpenNewWindow);
        }

        private static void OpenNewWindow()
        {
            CreateWindow<ConsoleViewer>().Show();
        }

        private ConsoleInstance SelectedInstance =>
            selectedViewIndex > 0 && selectedViewIndex - 1 < currentInstances.Length
                ? currentInstances[selectedViewIndex - 1]
                : null;

        private void OnEnable()
        {
            ApplyWindowChrome();

            rootVisualElement.Clear();
            isBuilt = BuildLayout();
            if (!isBuilt)
            {
                return;
            }

            ConsoleRegistry.Changed += OnRegistryChanged;
            ClearOnPlay.Changed += OnClearOnPlayChanged;
            EditorApplication.update += OnEditorUpdate;

            lastClearGeneration = ConsoleRegistry.ClearGeneration;
            restoreScrollPending = true;
            RebuildInstances(forceReload: true);
        }

        private void OnDisable()
        {
            if (!isBuilt)
            {
                return;
            }

            // The tracking callbacks keep the serialized fields current; this catches whatever
            // changed in the same frame the window went away.
            CaptureViewState();
            UnregisterViewStateTracking();

            ConsoleRegistry.Changed -= OnRegistryChanged;
            ClearOnPlay.Changed -= OnClearOnPlayChanged;
            EditorApplication.update -= OnEditorUpdate;
            isBuilt = false;
        }

        // Title, icon and minimum size are applied on every enable rather than only when the menu
        // opens the window, so a window restored from a saved layout still looks right.
        private void ApplyWindowChrome()
        {
            Texture2D icon = EditorGUIUtility.isProSkin
                ? windowIconLight != null ? windowIconLight : LoadByGuid<Texture2D>(LightIconGuid)
                : windowIconDark != null ? windowIconDark : LoadByGuid<Texture2D>(DarkIconGuid);

            titleContent = new GUIContent(WindowName, icon);
            minSize = new Vector2(400f, 250f);
        }

        private bool BuildLayout()
        {
            VisualTreeAsset layout = consoleViewerAsset != null ? consoleViewerAsset : LoadByGuid<VisualTreeAsset>(LayoutGuid);
            if (layout == null)
            {
                ShowLoadError("The Console Viewer layout could not be loaded. Reimport the ConsoleContainer package to restore it.");
                return false;
            }

            VisualElement root = layout.CloneTree();
            root.style.flexGrow = 1;

            List<string> missing = new List<string>();
            VisualElement viewer = Find<VisualElement>(root, "console-viewer", missing);
            ToolbarButton clearButton = Find<ToolbarButton>(root, "clear-button", missing);
            clearOnPlayToggle = Find<ToolbarToggle>(root, "clear-on-play-toggle", missing);
            ToolbarButton copyButton = Find<ToolbarButton>(root, "copy-button", missing);
            instanceMenu = Find<ToolbarMenu>(root, "instance-menu", missing);
            ToolbarSearchField searchField = Find<ToolbarSearchField>(root, "search-field", missing);
            typeToggles[(int)ConsoleMessageType.Text] = Find<ToolbarToggle>(root, "text-toggle", missing);
            typeToggles[(int)ConsoleMessageType.Warning] = Find<ToolbarToggle>(root, "warning-toggle", missing);
            typeToggles[(int)ConsoleMessageType.Error] = Find<ToolbarToggle>(root, "error-toggle", missing);
            messageList = Find<ListView>(root, "message-list", missing);
            mainSplitView = Find<TwoPaneSplitView>(root, "main-split-view", missing);
            detailsSplitView = Find<TwoPaneSplitView>(root, "details-split-view", missing);
            detailsPane = Find<VisualElement>(root, "details-pane", missing);
            callstackPane = Find<ScrollView>(root, "callstack-pane", missing);
            detailsLabel = Find<Label>(root, "details-label", missing);
            detailsInstanceLabel = Find<Label>(root, "details-instance-label", missing);

            if (missing.Count > 0)
            {
                ShowLoadError($"The Console Viewer layout is missing: {string.Join(", ", missing)}. Reimport the ConsoleContainer package to restore it.");
                return false;
            }

            rootVisualElement.Add(root);
            viewer.EnableInClassList(LightThemeClass, !EditorGUIUtility.isProSkin);

            // Applied before the first layout pass, so the panes come up at their remembered sizes
            // instead of visibly snapping into place.
            RestorePaneSizes();

            clearButton.clicked += OnClearClicked;
            copyButton.clicked += CopySelected;

            clearOnPlayToggle.SetValueWithoutNotify(ClearOnPlay.Enabled);
            clearOnPlayToggle.RegisterValueChangedCallback(evt => ClearOnPlay.Enabled = evt.newValue);

            searchField.SetValueWithoutNotify(searchText ?? string.Empty);
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            feed.SetSearch(searchText);

            SetUpTypeToggle(ConsoleMessageType.Text, showText);
            SetUpTypeToggle(ConsoleMessageType.Warning, showWarnings);
            SetUpTypeToggle(ConsoleMessageType.Error, showErrors);

            detailsLabel.selection.isSelectable = true;

            messageList.fixedItemHeight = RowHeight;
            messageList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            messageList.selectionType = SelectionType.Single;
            messageList.makeItem = () => new MessageRow();
            messageList.bindItem = BindRow;
            messageList.itemsSource = feed.Visible;
            messageList.selectionChanged += OnSelectionChanged;
            messageList.itemsChosen += OnItemsChosen;
            messageList.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            messageList.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
            messageScrollView = messageList.Q<ScrollView>();

            RegisterViewStateTracking();
            ResetDetails();
            return true;
        }

        private void ShowLoadError(string text)
        {
            HelpBox error = new HelpBox(text, HelpBoxMessageType.Error);
            error.AddToClassList(LoadErrorClass);
            rootVisualElement.Add(error);
        }

        private static T Find<T>(VisualElement root, string name, List<string> missing) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
            {
                missing.Add(name);
            }

            return element;
        }

        private static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private void OnRegistryChanged() => dirty = true;

        // The preference is shared, so a change made in one window shows in every other.
        private void OnClearOnPlayChanged() => clearOnPlayToggle.SetValueWithoutNotify(ClearOnPlay.Enabled);

        private void OnEditorUpdate()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;

            if (ConsoleRegistry.Version != lastRegistryVersion)
            {
                RebuildInstances(forceReload: false);
            }

            if (ConsoleRegistry.ClearGeneration != lastClearGeneration)
            {
                lastClearGeneration = ConsoleRegistry.ClearGeneration;
                feed.Reload();
                RefreshList(FeedChange.Reordered, reloaded: true);
                return;
            }

            FeedChange change = feed.Update();
            if (change != FeedChange.None)
            {
                RefreshList(change, reloaded: false);
            }

            // Messages of a hidden type change the counts without changing the list.
            UpdateCounts();
        }

        #region Instances

        private void RebuildInstances(bool forceReload)
        {
            ConsoleInstance previous = SelectedInstance;

            currentInstances = ConsoleRegistry.Snapshot();
            lastRegistryVersion = ConsoleRegistry.Version;

            usedInstanceLabels.Clear();
            usedInstanceLabels.Add(AllInstancesLabel);
            instanceLabels.Clear();
            instanceLabels.Add(AllInstancesLabel);

            int restoredIndex = 0;
            for (int i = 0; i < currentInstances.Length; i++)
            {
                instanceLabels.Add(BuildDisplayName(currentInstances[i]));
                if (currentInstances[i] == previous)
                {
                    restoredIndex = i + 1;
                }
            }

            // The selected instance can disappear between rebuilds — a domain reload drops every
            // instance, and a cleared disposed one is removed — so fall back to the remembered
            // name. Re-running the same code then re-attaches the view to the freshly created
            // instance instead of silently dropping back to "All Instances".
            if (restoredIndex == 0 && !string.IsNullOrEmpty(selectedInstanceName))
            {
                restoredIndex = FindInstanceIndexByName(selectedInstanceName);
            }

            selectedViewIndex = restoredIndex;

            // selectedInstanceName is deliberately kept when nothing matched: that is what lets
            // the view re-attach later. Only an explicit pick in the menu rewrites it.
            if (restoredIndex > 0)
            {
                selectedInstanceName = currentInstances[restoredIndex - 1].Name;
            }

            RebuildInstanceMenu();
            ApplyScope(forceReload);
        }

        private void RebuildInstanceMenu()
        {
            DropdownMenu menu = instanceMenu.menu;
            menu.ClearItems();

            for (int i = 0; i < instanceLabels.Count; i++)
            {
                int index = i;
                menu.AppendAction(
                    instanceLabels[i],
                    _ => SelectView(index),
                    _ => index == selectedViewIndex ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

                if (i == 0 && instanceLabels.Count > 1)
                {
                    menu.AppendSeparator();
                }
            }

            instanceMenu.text = instanceLabels[Mathf.Clamp(selectedViewIndex, 0, instanceLabels.Count - 1)];
        }

        private void SelectView(int index)
        {
            // The registry can change between the menu opening and an entry being picked.
            if (index < 0 || index >= instanceLabels.Count || index > currentInstances.Length)
            {
                return;
            }

            selectedViewIndex = index;
            selectedInstanceName = index > 0 ? currentInstances[index - 1].Name : string.Empty;
            instanceMenu.text = instanceLabels[index];
            ApplyScope(forceReload: false);
        }

        // Points the feed at the instances the selected view covers, reloading only when that set
        // actually changed; a dispose that merely relabels an instance keeps the list as it is.
        private void ApplyScope(bool forceReload)
        {
            ConsoleInstance[] scope = selectedViewIndex == 0
                ? currentInstances
                : new[] { currentInstances[selectedViewIndex - 1] };

            if (!forceReload && HaveSameInstances(scope, feedScope))
            {
                return;
            }

            feedScope = scope;
            feed.SetSources(scope);
            RefreshList(FeedChange.Reordered, reloaded: true);
        }

        private static bool HaveSameInstances(ConsoleInstance[] left, ConsoleInstance[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
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

        /// <summary>
        /// Builds the menu entry for an instance, keeping it unique within the current list.
        /// Instance names are free-form and duplicates are common (a system that recreates its
        /// instance, or two features picking the same label), so a suffix keeps each one
        /// distinguishable.
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

        // Menus treat '/' as a submenu separator, so it is shown as a look-alike division slash.
        private static string DecorateDisplayName(string name, bool isDisposed)
        {
            string shown = name.IndexOf('/') >= 0 ? name.Replace('/', '\u2215') : name;
            return isDisposed ? $"{shown} (disposed)" : shown;
        }

        private void OnClearClicked()
        {
            if (selectedViewIndex == 0)
            {
                foreach (ConsoleInstance instance in currentInstances)
                {
                    instance.Clear();
                }
            }
            else
            {
                SelectedInstance?.Clear();
            }
        }

        #endregion

        #region Filters

        private void SetUpTypeToggle(ConsoleMessageType type, bool shown)
        {
            ToolbarToggle toggle = typeToggles[(int)type];

            // The icon sits before the count inside the toggle's own content.
            VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(TypeIconClass);
            icon.AddToClassList($"{TypeIconClass}--{type.ToString().ToLowerInvariant()}");
            VisualElement content = toggle.Q(className: Toggle.inputUssClassName) ?? toggle;
            content.Insert(0, icon);

            toggle.SetValueWithoutNotify(shown);
            toggle.RegisterValueChangedCallback(evt => OnTypeToggled(type, evt.newValue));
            feed.SetTypeEnabled(type, shown);
        }

        private void OnTypeToggled(ConsoleMessageType type, bool shown)
        {
            switch (type)
            {
                case ConsoleMessageType.Text:
                    showText = shown;
                    break;
                case ConsoleMessageType.Warning:
                    showWarnings = shown;
                    break;
                case ConsoleMessageType.Error:
                    showErrors = shown;
                    break;
            }

            if (feed.SetTypeEnabled(type, shown))
            {
                RefreshList(FeedChange.Reordered, reloaded: true);
            }
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            searchText = evt.newValue ?? string.Empty;
            if (feed.SetSearch(searchText))
            {
                RefreshList(FeedChange.Reordered, reloaded: true);
            }
        }

        // Labels are only rewritten when a count actually changes, so a steady stream of messages
        // allocates no strings for them.
        private void UpdateCounts()
        {
            for (int i = 0; i < typeToggles.Length; i++)
            {
                int count = feed.GetCount((ConsoleMessageType)i);
                if (count == shownCounts[i])
                {
                    continue;
                }

                shownCounts[i] = count;
                typeToggles[i].text = count > MaximumShownCount ? CountOverflowText : count.ToString();
            }
        }

        #endregion

        #region Message list

        private void BindRow(VisualElement element, int index)
        {
            List<ConsoleMessage> visible = feed.Visible;
            if (element is MessageRow row && index >= 0 && index < visible.Count)
            {
                row.Bind(visible[index]);
            }
        }

        private void RefreshList(FeedChange change, bool reloaded)
        {
            // Measured before refreshing, while the scroller still reflects a settled layout. A
            // reloaded view (instance switch, clear) starts at the newest message.
            bool followBottom = reloaded || IsNearBottom();

            if (!ReferenceEquals(messageList.itemsSource, feed.Visible))
            {
                messageList.itemsSource = feed.Visible;
            }
            else
            {
                messageList.RefreshItems();
            }

            if (reloaded || (change & FeedChange.Reordered) != 0)
            {
                ResyncSelection();
            }

            UpdateCounts();

            if (restoreScrollPending)
            {
                // First build after the window came back: land where the user left off rather
                // than jumping to the newest message.
                restoreScrollPending = false;
                RequestScroll(ScrollTarget.RememberedOffset);
            }
            else if (followBottom)
            {
                RequestScroll(ScrollTarget.Bottom);
            }
        }

        private void ResyncSelection()
        {
            int index = feed.IndexOf(selectedMessage);
            if (index < 0)
            {
                messageList.SetSelectionWithoutNotify(Array.Empty<int>());
                ResetDetails();
                return;
            }

            selectionBuffer[0] = index;
            messageList.SetSelectionWithoutNotify(selectionBuffer);
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            ConsoleMessage message = null;
            foreach (object item in items)
            {
                message = item as ConsoleMessage;
                break;
            }

            if (message == null)
            {
                ResetDetails();
            }
            else
            {
                ShowDetails(message);
            }
        }

        // Double-click or Enter opens the log site, like the Unity Console.
        private void OnItemsChosen(IEnumerable<object> items)
        {
            foreach (object item in items)
            {
                if (item is ConsoleMessage message && message.Callstack.Count > 0)
                {
                    OpenFrame(message.Callstack[0]);
                }

                break;
            }
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (evt.commandName == CopyCommand && selectedMessage != null)
            {
                evt.StopPropagation();
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName == CopyCommand && selectedMessage != null)
            {
                CopySelected();
                evt.StopPropagation();
            }
        }

        #endregion

        #region Details

        private void ShowDetails(ConsoleMessage message)
        {
            selectedMessage = message;
            detailsLabel.text = MessageDetails.ForDisplay(message);
            detailsInstanceLabel.text = message.Owner != null ? message.Owner.Name : string.Empty;
            BuildCallstack(message);
        }

        private void ResetDetails()
        {
            selectedMessage = null;
            callstackPane.Clear();
            detailsLabel.text = string.Empty;
            detailsInstanceLabel.text = string.Empty;
        }

        private void BuildCallstack(ConsoleMessage message)
        {
            callstackPane.Clear();

            foreach (CallstackFrame frame in message.Callstack)
            {
                CallstackFrame captured = frame;
                Button button = new Button(() => OpenFrame(captured))
                {
                    text = frame.Label,
                    tooltip = frame.MethodName
                };
                button.AddToClassList(CallstackButtonClass);
                callstackPane.Add(button);
            }
        }

        private static void OpenFrame(CallstackFrame frame)
        {
            if (!string.IsNullOrEmpty(frame.FilePath))
            {
                InternalEditorUtility.OpenFileAtLineExternal(frame.FilePath, frame.Line, 0);
            }
        }

        // The full details (message and call stack) go to the clipboard, so they can be pasted
        // straight into a chat or an issue.
        private void CopySelected()
        {
            if (selectedMessage != null)
            {
                EditorGUIUtility.systemCopyBuffer = MessageDetails.ForClipboard(selectedMessage);
            }
        }

        #endregion

        #region View state

        private void RegisterViewStateTracking()
        {
            detailsPane.RegisterCallback<GeometryChangedEvent>(OnDetailsPaneGeometryChanged);
            callstackPane.RegisterCallback<GeometryChangedEvent>(OnCallstackPaneGeometryChanged);

            if (messageScrollView != null)
            {
                messageScrollView.verticalScroller.valueChanged += OnScrollOffsetChanged;
            }
        }

        private void UnregisterViewStateTracking()
        {
            detailsPane.UnregisterCallback<GeometryChangedEvent>(OnDetailsPaneGeometryChanged);
            callstackPane.UnregisterCallback<GeometryChangedEvent>(OnCallstackPaneGeometryChanged);

            if (messageScrollView != null)
            {
                messageScrollView.verticalScroller.valueChanged -= OnScrollOffsetChanged;
            }
        }

        private void RestorePaneSizes()
        {
            // Zero means "never resized", in which case the dimensions authored in the UXML apply.
            if (detailsPaneSize > 0f)
            {
                mainSplitView.fixedPaneInitialDimension = detailsPaneSize;
            }

            if (callstackPaneSize > 0f)
            {
                detailsSplitView.fixedPaneInitialDimension = callstackPaneSize;
            }
        }

        // Both panes are the fixed side of their split view, so their resolved size is exactly
        // where the dragline was left.
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
            float width = callstackPane.resolvedStyle.width;
            if (width > 0f)
            {
                callstackPaneSize = width;
            }
        }

        private void OnScrollOffsetChanged(float value) => scrollOffset = value;

        private void CaptureViewState()
        {
            OnDetailsPaneGeometryChanged(null);
            OnCallstackPaneGeometryChanged(null);

            if (messageScrollView != null)
            {
                scrollOffset = messageScrollView.verticalScroller.value;
            }
        }

        #endregion

        #region Scrolling

        // True while the view should follow new messages: nothing to scroll yet, or scrolled to
        // within StickToBottomSlack of the bottom.
        private bool IsNearBottom()
        {
            if (messageScrollView == null)
            {
                return true;
            }

            Scroller scroller = messageScrollView.verticalScroller;
            return scroller.highValue <= 0f || scroller.value >= scroller.highValue - StickToBottomSlack;
        }

        // Requests coalesce: however many arrive while one is pending, a single pair of scheduled
        // ticks applies the latest, so sustained logging does not queue new work every frame.
        private void RequestScroll(ScrollTarget target)
        {
            if (messageScrollView == null)
            {
                return;
            }

            bool alreadyScheduled = pendingScroll != ScrollTarget.None;
            pendingScroll = target;

            if (target == ScrollTarget.RememberedOffset)
            {
                // Captured now: following new messages would otherwise overwrite the remembered
                // offset before the scroll is applied.
                pendingScrollOffset = scrollOffset;
            }

            if (!alreadyScheduled)
            {
                applyPendingScrollFirst ??= ApplyPendingScrollFirst;
                messageScrollView.schedule.Execute(applyPendingScrollFirst);
            }
        }

        // Scheduled items can run before freshly added rows get a layout, in which case the
        // scroller range still describes the old content. Acting on two consecutive ticks covers
        // both: the first when layout already ran, the second with the settled range.
        private void ApplyPendingScrollFirst()
        {
            ApplyPendingScroll();

            applyPendingScrollSecond ??= ApplyPendingScrollSecond;
            messageScrollView.schedule.Execute(applyPendingScrollSecond);
        }

        private void ApplyPendingScrollSecond()
        {
            ApplyPendingScroll();
            pendingScroll = ScrollTarget.None;
        }

        private void ApplyPendingScroll()
        {
            Scroller scroller = messageScrollView.verticalScroller;

            // Never below zero: with content shorter than the viewport the range is negative, and
            // scrolling to it would push the content downward.
            float bottom = Mathf.Max(0f, scroller.highValue);

            switch (pendingScroll)
            {
                case ScrollTarget.Bottom:
                    scroller.value = bottom;
                    break;
                case ScrollTarget.RememberedOffset:
                    scroller.value = Mathf.Clamp(pendingScrollOffset, 0f, bottom);
                    break;
            }
        }

        private enum ScrollTarget
        {
            None,
            Bottom,
            RememberedOffset
        }

        #endregion
    }
}
