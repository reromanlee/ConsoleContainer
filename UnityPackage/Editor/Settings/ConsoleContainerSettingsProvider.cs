using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// The <c>Project Settings ▸ Console Container</c> page. Shows the settings asset inline,
    /// creates it exactly where the runtime looks for it, and warns about copies the runtime would
    /// ignore, leave out of builds, or pick between arbitrarily.
    /// </summary>
    internal static class ConsoleContainerSettingsProvider
    {
        internal const string SettingsPath = "Project/Console Container";
        internal const string DefaultAssetPath = "Assets/Resources/" + ConsoleContainerSettings.ResourcesPath + ".asset";

        private const string ResourcesFolderName = "Resources";
        private const string EditorFolderSegment = "/Editor/";

        private static VisualElement _activeRoot;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Console Container",
                keywords = new HashSet<string> { "Console", "Logging", "Forwarding", "Call stack", "Test Runner", "History" },
                activateHandler = (searchContext, root) =>
                {
                    _activeRoot = root;
                    EditorApplication.projectChanged += Rebuild;
                    Rebuild();
                },
                deactivateHandler = () =>
                {
                    EditorApplication.projectChanged -= Rebuild;
                    _activeRoot = null;
                }
            };
        }

        /// <summary>Where a settings asset at <paramref name="assetPath"/> stands with the runtime lookup.</summary>
        internal static AssetPlacement Classify(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(normalized);
            string folderName = Path.GetFileName(Path.GetDirectoryName(normalized) ?? string.Empty);

            // Resources.Load finds an asset with exactly this name at the root of a Resources folder.
            if (!string.Equals(fileName, ConsoleContainerSettings.ResourcesPath, StringComparison.Ordinal)
                || !string.Equals(folderName, ResourcesFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return AssetPlacement.Ignored;
            }

            // Resources folders inside an Editor folder are left out of player builds.
            return normalized.IndexOf(EditorFolderSegment, StringComparison.OrdinalIgnoreCase) >= 0
                ? AssetPlacement.EditorOnly
                : AssetPlacement.Loaded;
        }

        private static void Rebuild()
        {
            VisualElement root = _activeRoot;
            if (root == null)
            {
                return;
            }

            root.Clear();
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 2f;

            Label title = new Label("Console Container");
            title.style.fontSize = 19f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10f;
            root.Add(title);

            List<string> loaded = new List<string>();
            List<string> editorOnly = new List<string>();
            List<string> ignored = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ConsoleContainerSettings)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                switch (Classify(path))
                {
                    case AssetPlacement.Loaded:
                        loaded.Add(path);
                        break;
                    case AssetPlacement.EditorOnly:
                        editorOnly.Add(path);
                        break;
                    default:
                        ignored.Add(path);
                        break;
                }
            }

            List<string> usable = new List<string>(loaded);
            usable.AddRange(editorOnly);

            if (usable.Count == 0)
            {
                AddNotice(root, HelpBoxMessageType.Info,
                    "No settings asset exists, so the package defaults apply: nothing is logged in player builds, " +
                    "and in the editor messages are forwarded to the Unity Console only during test runs.");

                Button create = new Button(() => CreateAsset()) { text = "Create Settings Asset" };
                create.style.alignSelf = Align.FlexStart;
                create.style.marginBottom = 8f;
                root.Add(create);
            }
            else
            {
                if (usable.Count > 1)
                {
                    AddNotice(root, HelpBoxMessageType.Warning,
                        "Several settings assets can be loaded and Unity picks one of them arbitrarily. Keep only one:\n" +
                        string.Join("\n", usable));
                }

                AddInspector(root, usable[0]);
            }

            if (editorOnly.Count > 0)
            {
                AddNotice(root, HelpBoxMessageType.Warning,
                    "These settings assets sit in a Resources folder inside an Editor folder. They apply in the editor " +
                    $"but are left out of player builds. Move them to {Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/')}:\n" +
                    string.Join("\n", editorOnly));
            }

            if (ignored.Count > 0)
            {
                AddNotice(root, HelpBoxMessageType.Warning,
                    $"These settings assets are ignored. The runtime only loads one named \"{ConsoleContainerSettings.ResourcesPath}\" " +
                    "placed directly inside a Resources folder:\n" + string.Join("\n", ignored));
            }
        }

        private static void AddInspector(VisualElement root, string assetPath)
        {
            ConsoleContainerSettings asset = AssetDatabase.LoadAssetAtPath<ConsoleContainerSettings>(assetPath);
            if (asset == null)
            {
                return;
            }

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;

            Label path = new Label(assetPath);
            path.style.flexGrow = 1f;
            path.style.opacity = 0.75f;
            header.Add(path);

            header.Add(new Button(() =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            })
            {
                text = "Select"
            });

            root.Add(header);
            root.Add(new InspectorElement(asset));
        }

        private static void AddNotice(VisualElement root, HelpBoxMessageType type, string text)
        {
            HelpBox notice = new HelpBox(text, type);
            notice.style.marginBottom = 8f;
            root.Add(notice);
        }

        private static void CreateAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(DefaultAssetPath) != null)
            {
                Debug.LogError($"ConsoleContainer: cannot create the settings asset, because {DefaultAssetPath} already exists.");
                return;
            }

            string folder = Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder)?.Replace('\\', '/'), Path.GetFileName(folder));
            }

            ConsoleContainerSettings asset = ScriptableObject.CreateInstance<ConsoleContainerSettings>();
            AssetDatabase.CreateAsset(asset, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);

            Rebuild();
        }
    }

    /// <summary>Where a settings asset stands with the runtime's <c>Resources</c> lookup.</summary>
    internal enum AssetPlacement
    {
        /// <summary>Loaded in the editor and in player builds.</summary>
        Loaded,

        /// <summary>Loaded in the editor, but left out of player builds.</summary>
        EditorOnly,

        /// <summary>Never loaded: wrong name, or not directly inside a Resources folder.</summary>
        Ignored
    }
}
