using System;
using NUnit.Framework;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class MessageDetailsTests
    {
        private static ConsoleMessage NewMessage(string content, Exception exception = null, CallstackFrame[] callstack = null)
            => new ConsoleMessage(null, ConsoleMessageType.Text, "Source", content, exception, callstack);

        [Test]
        public void RowText_IsTheFirstLine()
        {
            ConsoleMessage message = NewMessage("first line\nsecond line");

            Assert.That(MessageDetails.RowText(message), Is.EqualTo("Source: first line…"));
        }

        [Test]
        public void RowText_ReusesShortSingleLineLabels()
        {
            ConsoleMessage message = NewMessage("short");

            Assert.That(MessageDetails.RowText(message), Is.SameAs(message.Label));
        }

        [Test]
        public void RowText_CutsVeryLongLines()
        {
            ConsoleMessage message = NewMessage(new string('x', 10000));

            Assert.That(MessageDetails.RowText(message).Length, Is.LessThan(400));
        }

        [Test]
        public void Display_CutsHugeText_ButClipboardKeepsAllOfIt()
        {
            ConsoleMessage message = NewMessage(new string('x', MessageDetails.DisplayTextLimit * 3));

            string display = MessageDetails.ForDisplay(message);
            string clipboard = MessageDetails.ForClipboard(message);

            Assert.That(display.Length, Is.LessThan(MessageDetails.DisplayTextLimit + 200));
            Assert.That(display, Does.Contain("Copy includes the full text"));
            Assert.That(clipboard, Does.Contain(message.Label));
        }

        [Test]
        public void Details_ListFramesWithProjectRelativePaths()
        {
            string absolute = Application.dataPath + "/Scripts/Network.cs";
            ConsoleMessage message = NewMessage("content", callstack: new[] { new CallstackFrame(absolute, 12, "Network.Connect") });

            Assert.That(MessageDetails.ForClipboard(message), Does.EndWith("Network.Connect (at Assets/Scripts/Network.cs:12)"));
        }

        [Test]
        public void Details_ListExceptionCauses()
        {
            Exception exception = new InvalidOperationException("outer", new TimeoutException("inner"));
            ConsoleMessage message = NewMessage("InvalidOperationException: outer", exception);

            Assert.That(MessageDetails.ForClipboard(message), Does.Contain("Caused by TimeoutException: inner"));
        }

        [Test]
        public void Display_CapsTheListedFrames()
        {
            CallstackFrame[] frames = new CallstackFrame[MessageDetails.DisplayFrameLimit + 10];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new CallstackFrame("/Frame.cs", i + 1, "Frame.Method");
            }

            ConsoleMessage message = NewMessage("content", callstack: frames);

            Assert.That(MessageDetails.ForDisplay(message), Does.EndWith("… 10 more frames"));
            Assert.That(MessageDetails.ForClipboard(message), Does.Not.Contain("more frames"));
        }

        [Test]
        public void ProjectRelativePath_LeavesOutsidePathsAlone()
        {
            Assert.That(MessageDetails.ProjectRelativePath("/elsewhere/File.cs"), Is.EqualTo("/elsewhere/File.cs"));
            Assert.That(MessageDetails.ProjectRelativePath(null), Is.Null);
        }
    }
}
