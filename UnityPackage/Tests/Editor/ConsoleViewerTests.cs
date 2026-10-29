using System.Collections;
using System.Linq;
using NUnit.Framework;
using reromanlee.ConsoleContainer.Tests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class ConsoleViewerTests
    {
        // Enough editor updates for the window to pick up registry changes and refresh its list.
        private const int SettleFrames = 5;

        private ConsoleTestScope scope;
        private ConsoleViewer window;

        [SetUp]
        public void SetUp()
        {
            scope = new ConsoleTestScope();

            // Built and updated without being shown, which needs no graphics device, so the tests
            // also run in headless (batch mode, CI) editors.
            window = ScriptableObject.CreateInstance<ConsoleViewer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null)
            {
                Object.DestroyImmediate(window);
            }

            scope.Dispose();
        }

        private ListView MessageList => window.rootVisualElement.Q<ListView>("message-list");

        private static IEnumerator Settle()
        {
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
        }

        [Test]
        public void Window_BuildsItsLayout()
        {
            VisualElement root = window.rootVisualElement;

            Assert.That(root.Query<HelpBox>().ToList(), Is.Empty, "The layout reported a load error.");
            Assert.That(MessageList, Is.Not.Null);
            Assert.That(root.Q<ToolbarSearchField>("search-field"), Is.Not.Null);
            Assert.That(root.Q<ToolbarMenu>("instance-menu"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator NewMessages_AppearInTheList()
        {
            ConsoleInstance console = scope.Create("Viewer Test");
            yield return Settle();

            console.CreateText("Source", "viewer-test-text");
            console.CreateError("Source", "viewer-test-error");
            yield return Settle();

            string[] shown = MessageList.itemsSource.Cast<ConsoleMessage>().Select(message => message.Content).ToArray();
            Assert.That(shown, Does.Contain("viewer-test-text"));
            Assert.That(shown, Does.Contain("viewer-test-error"));
        }

        [UnityTest]
        public IEnumerator Search_FiltersTheList()
        {
            ConsoleInstance console = scope.Create("Viewer Test");
            console.CreateText("Source", "viewer-search-match");
            console.CreateText("Source", "something else");
            yield return Settle();

            // A window that is not shown dispatches no UI events, so the search is applied the way the field applies it.
            window.ApplySearch("viewer-search");
            yield return Settle();

            string[] shown = MessageList.itemsSource.Cast<ConsoleMessage>().Select(message => message.Content).ToArray();
            Assert.That(shown, Is.EqualTo(new[] { "viewer-search-match" }));
        }

        [UnityTest]
        public IEnumerator Clear_EmptiesTheList()
        {
            ConsoleInstance console = scope.Create("Viewer Test");
            console.CreateText("Source", "viewer-clear");
            yield return Settle();

            console.Clear();
            yield return Settle();

            string[] shown = MessageList.itemsSource.Cast<ConsoleMessage>().Select(message => message.Content).ToArray();
            Assert.That(shown, Does.Not.Contain("viewer-clear"));
        }
    }
}
