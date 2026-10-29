using NUnit.Framework;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Tests
{
    public sealed class SettingsOverrideTests
    {
        private ConsoleContainerSettings created;

        [TearDown]
        public void TearDown()
        {
            ConsoleContainerSettings.Active = null;
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        [Test]
        public void Override_TakesPrecedence_UntilCleared()
        {
            ConsoleContainerSettings asset = ConsoleContainerSettings.Active;
            created = ScriptableObject.CreateInstance<ConsoleContainerSettings>();

            ConsoleContainerSettings.Active = created;
            Assert.That(ConsoleContainerSettings.Active, Is.SameAs(created));

            ConsoleContainerSettings.Active = null;
            Assert.That(ConsoleContainerSettings.Active, Is.SameAs(asset));
        }

        [Test]
        public void DestroyedOverride_FallsBackToTheAsset()
        {
            ConsoleContainerSettings asset = ConsoleContainerSettings.Active;
            created = ScriptableObject.CreateInstance<ConsoleContainerSettings>();
            ConsoleContainerSettings.Active = created;

            Object.DestroyImmediate(created);

            Assert.That(ConsoleContainerSettings.Active, Is.SameAs(asset));
        }

        [Test]
        public void MaxMessagesPerInstance_NeverGoesNegative()
        {
            created = ScriptableObject.CreateInstance<ConsoleContainerSettings>();

            created.MaxMessagesPerInstance = -5;

            Assert.That(created.MaxMessagesPerInstance, Is.Zero);
        }

        [Test]
        public void NewSettings_MatchThePackageDefaults()
        {
            created = ScriptableObject.CreateInstance<ConsoleContainerSettings>();

            Assert.That(created.EditorForwarding, Is.EqualTo(EditorForwardingMode.DuringTestRuns));
            Assert.That(created.MaxMessagesPerInstance, Is.EqualTo(10000));
            Assert.That(created.CaptureTextCallstack, Is.True);
            Assert.That(created.CaptureWarningCallstack, Is.True);
            Assert.That(created.CaptureErrorCallstack, Is.True);
        }
    }
}
