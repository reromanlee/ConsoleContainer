using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using reromanlee.ConsoleContainer.Tests;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class ForwardingTests
    {
        private ConsoleTestScope scope;
        private ConsoleInstance console;

        [SetUp]
        public void SetUp()
        {
            scope = new ConsoleTestScope();
            console = scope.Create();
        }

        [TearDown]
        public void TearDown() => scope.Dispose();

        [Test]
        public void Never_KeepsTheUnityConsoleClean()
        {
            scope.Settings.EditorForwarding = EditorForwardingMode.Never;

            console.CreateText("Source", "text");
            console.CreateWarning("Source", "warning");
            console.CreateError("Source", "error");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Always_ForwardsEachTypeThroughItsMatchingChannel()
        {
            scope.Settings.EditorForwarding = EditorForwardingMode.Always;

            LogAssert.Expect(LogType.Log, "Source: text");
            LogAssert.Expect(LogType.Warning, "Source: warning");
            LogAssert.Expect(LogType.Error, "Source: error");

            console.CreateText("Source", "text");
            console.CreateWarning("Source", "warning");
            console.CreateError("Source", "error");
        }

        [Test]
        public void DuringTestRuns_ForwardsWhileATestRunIsActive()
        {
            scope.Settings.EditorForwarding = EditorForwardingMode.DuringTestRuns;

            // This test is itself part of a run, so the bridge reports one as active.
            Assume.That(UnityConsoleForwarding.IsTestRunActive, Is.True);

            LogAssert.Expect(LogType.Log, "Source: text");
            console.CreateText("Source", "text");
        }

        [Test]
        public void PerTypeToggles_ChooseWhatIsForwarded()
        {
            scope.Settings.EditorForwarding = EditorForwardingMode.Always;
            scope.Settings.LogTextInEditor = false;
            scope.Settings.LogErrorsInEditor = false;

            LogAssert.Expect(LogType.Warning, "Source: warning");

            console.CreateText("Source", "text");
            console.CreateWarning("Source", "warning");
            console.CreateError("Source", "error");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Exceptions_AreForwardedAsExceptions()
        {
            scope.Settings.EditorForwarding = EditorForwardingMode.Always;

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Socket closed"));

            console.CreateException("Source", new InvalidOperationException("Socket closed"));
        }
    }
}
