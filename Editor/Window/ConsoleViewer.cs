using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    internal class ConsoleViewer : EditorWindow
    {
        private const string WindowName = "Console Viewer";

        [SerializeField] private Texture2D windowIconDark;
        [SerializeField] private Texture2D windowIconLight;

        [SerializeField] private VisualTreeAsset consoleViewerAsset;

        private VisualElement consoleViewerElement;

        [MenuItem("Tools/Console Viewer")]
        public static void ShowWindow()
        {
            ConsoleViewer window = GetWindow<ConsoleViewer>();
            if (EditorGUIUtility.isProSkin)
            {
                window.titleContent = new GUIContent(WindowName, window.windowIconLight);
            }
            else
            {
                window.titleContent = new GUIContent(WindowName, window.windowIconDark);
            }
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            consoleViewerElement = consoleViewerAsset.CloneTree();
            consoleViewerElement.style.flexGrow = 1;
            rootVisualElement.Add(consoleViewerElement);
        }

        private void OnDisable()
        {

        }
    }
}