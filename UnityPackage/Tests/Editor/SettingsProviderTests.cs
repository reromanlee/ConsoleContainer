using NUnit.Framework;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class SettingsProviderTests
    {
        [TestCase("Assets/Resources/ConsoleContainerSettings.asset", "Loaded")]
        [TestCase("Assets/Game/Resources/ConsoleContainerSettings.asset", "Loaded")]
        [TestCase("Packages/com.studio.config/Resources/ConsoleContainerSettings.asset", "Loaded")]
        [TestCase("Assets/Editor/Resources/ConsoleContainerSettings.asset", "EditorOnly")]
        [TestCase("Assets/Tools/Editor/Config/Resources/ConsoleContainerSettings.asset", "EditorOnly")]
        [TestCase("Assets/Settings/ConsoleContainerSettings.asset", "Ignored")]
        [TestCase("Assets/Resources/Config/ConsoleContainerSettings.asset", "Ignored")]
        [TestCase("Assets/Resources/ConsoleSettings.asset", "Ignored")]
        [TestCase("Assets/Resources/consolecontainersettings.asset", "Loaded")]
        public void Classify_MirrorsTheRuntimeLookup(string path, string expected)
        {
            // The placement is internal, so it is compared by name.
            Assert.That(ConsoleContainerSettingsProvider.Classify(path).ToString(), Is.EqualTo(expected));
        }
    }
}
