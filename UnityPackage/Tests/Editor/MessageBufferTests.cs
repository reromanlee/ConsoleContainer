using System.Collections.Generic;
using NUnit.Framework;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class MessageBufferTests
    {
        private readonly List<ConsoleMessage> copied = new List<ConsoleMessage>();

        [SetUp]
        public void SetUp() => copied.Clear();

        private static ConsoleMessage NewMessage(int content)
            => new ConsoleMessage(null, ConsoleMessageType.Text, "Test", content.ToString(), null, null);

        private static MessageBuffer Fill(int count, int limit)
        {
            MessageBuffer buffer = new MessageBuffer();
            for (int i = 0; i < count; i++)
            {
                buffer.Add(NewMessage(i), limit);
            }

            return buffer;
        }

        [Test]
        public void Unlimited_KeepsEverythingInOrder()
        {
            MessageBuffer buffer = Fill(100, limit: 0);

            buffer.CopySince(0, copied);

            Assert.That(copied.Count, Is.EqualTo(100));
            Assert.That(copied[0].Content, Is.EqualTo("0"));
            Assert.That(copied[99].Content, Is.EqualTo("99"));
        }

        [Test]
        public void AtTheLimit_DropsTheOldestFirst()
        {
            MessageBuffer buffer = Fill(25, limit: 10);

            buffer.CopySince(0, copied);

            Assert.That(buffer.Count, Is.EqualTo(10));
            Assert.That(copied.Count, Is.EqualTo(10));
            Assert.That(copied[0].Content, Is.EqualTo("15"));
            Assert.That(copied[9].Content, Is.EqualTo("24"));
        }

        [Test]
        public void Cursor_ResumesExactlyWhereItStopped()
        {
            MessageBuffer buffer = Fill(5, limit: 0);
            long cursor = buffer.CopySince(0, copied);

            buffer.Add(NewMessage(5), 0);
            buffer.Add(NewMessage(6), 0);
            copied.Clear();
            cursor = buffer.CopySince(cursor, copied);

            Assert.That(copied.ConvertAll(message => message.Content), Is.EqualTo(new[] { "5", "6" }));
            Assert.That(cursor, Is.EqualTo(7));
        }

        [Test]
        public void Cursor_PastDroppedMessages_ResumesAtTheOldestHeld()
        {
            MessageBuffer buffer = Fill(3, limit: 4);
            long cursor = buffer.CopySince(0, copied);

            for (int i = 3; i < 12; i++)
            {
                buffer.Add(NewMessage(i), 4);
            }

            copied.Clear();
            buffer.CopySince(cursor, copied);

            Assert.That(copied.ConvertAll(message => message.Content), Is.EqualTo(new[] { "8", "9", "10", "11" }));
        }

        [Test]
        public void Positions_AreAssignedInCursorSpace()
        {
            MessageBuffer buffer = Fill(6, limit: 3);

            buffer.CopySince(0, copied);

            Assert.That(buffer.StartPosition, Is.EqualTo(3));
            Assert.That(copied.ConvertAll(message => message.Position), Is.EqualTo(new long[] { 3, 4, 5 }));
        }

        [Test]
        public void LoweringTheLimit_TakesEffectOnTheNextAdd()
        {
            MessageBuffer buffer = Fill(50, limit: 0);

            buffer.Add(NewMessage(50), 5);

            Assert.That(buffer.Count, Is.EqualTo(5));
            Assert.That(buffer[0].Content, Is.EqualTo("46"));
        }

        [Test]
        public void Clear_KeepsCursorsValid()
        {
            MessageBuffer buffer = Fill(5, limit: 0);
            long cursor = buffer.CopySince(0, copied);

            buffer.Clear();
            buffer.Add(NewMessage(5), 0);
            copied.Clear();
            buffer.CopySince(cursor, copied);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(copied.ConvertAll(message => message.Content), Is.EqualTo(new[] { "5" }));
        }

        [Test]
        public void Wrapping_PreservesOrderAcrossManyCycles()
        {
            MessageBuffer buffer = Fill(1003, limit: 7);

            buffer.CopySince(0, copied);

            Assert.That(copied.ConvertAll(message => message.Content),
                Is.EqualTo(new[] { "996", "997", "998", "999", "1000", "1001", "1002" }));
        }
    }
}
