using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using reromanlee.ConsoleContainer.Tests;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class MessageFeedTests
    {
        private ConsoleTestScope scope;
        private MessageFeed feed;

        [SetUp]
        public void SetUp()
        {
            scope = new ConsoleTestScope();
            feed = new MessageFeed();
        }

        [TearDown]
        public void TearDown() => scope.Dispose();

        private static List<string> Contents(MessageFeed source) => source.Visible.ConvertAll(message => message.Content);

        [Test]
        public void SeveralInstances_MergeInChronologicalOrder()
        {
            ConsoleInstance first = scope.Create("First");
            ConsoleInstance second = scope.Create("Second");
            feed.SetSources(new[] { first, second });

            second.CreateText("Source", "1");
            first.CreateText("Source", "2");
            second.CreateText("Source", "3");
            feed.Update();

            Assert.That(Contents(feed), Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void Update_ReportsAppendsAndNothingElse()
        {
            ConsoleInstance console = scope.Create();
            feed.SetSources(new[] { console });

            Assert.That(feed.Update(), Is.EqualTo(FeedChange.None));

            console.CreateText("Source", "a");
            Assert.That(feed.Update(), Is.EqualTo(FeedChange.Appended));
        }

        [Test]
        public void TypeFilter_HidesTypes_ButKeepsCountingThem()
        {
            ConsoleInstance console = scope.Create();
            feed.SetSources(new[] { console });
            console.CreateText("Source", "text");
            console.CreateWarning("Source", "warning");
            console.CreateError("Source", "error");
            feed.Update();

            feed.SetTypeEnabled(ConsoleMessageType.Text, false);
            console.CreateText("Source", "hidden text");
            feed.Update();

            Assert.That(Contents(feed), Is.EqualTo(new[] { "warning", "error" }));
            Assert.That(feed.GetCount(ConsoleMessageType.Text), Is.EqualTo(2));
            Assert.That(feed.GetCount(ConsoleMessageType.Warning), Is.EqualTo(1));
            Assert.That(feed.GetCount(ConsoleMessageType.Error), Is.EqualTo(1));

            feed.SetTypeEnabled(ConsoleMessageType.Text, true);
            Assert.That(feed.Visible.Count, Is.EqualTo(4));
        }

        [Test]
        public void Search_MatchesSourceOrContent_IgnoringCase()
        {
            ConsoleInstance console = scope.Create();
            feed.SetSources(new[] { console });
            console.CreateText("Socket", "connected");
            console.CreateText("Renderer", "frame ready");
            console.CreateText("Loader", "socket pool warmed");
            feed.Update();

            feed.SetSearch("SOCKET");

            Assert.That(Contents(feed), Is.EqualTo(new[] { "connected", "socket pool warmed" }));
            Assert.That(feed.GetCount(ConsoleMessageType.Text), Is.EqualTo(2));

            console.CreateText("Socket", "closed");
            console.CreateText("Renderer", "frame dropped");
            feed.Update();

            Assert.That(Contents(feed), Is.EqualTo(new[] { "connected", "socket pool warmed", "closed" }));
        }

        [Test]
        public void IndexOf_FindsVisibleMessages_Only()
        {
            ConsoleInstance console = scope.Create();
            feed.SetSources(new[] { console });
            console.CreateText("Source", "text");
            console.CreateError("Source", "error");
            feed.Update();
            ConsoleMessage text = feed.Visible[0];
            ConsoleMessage error = feed.Visible[1];

            feed.SetTypeEnabled(ConsoleMessageType.Text, false);

            Assert.That(feed.IndexOf(error), Is.EqualTo(0));
            Assert.That(feed.IndexOf(text), Is.EqualTo(-1));
            Assert.That(feed.IndexOf(null), Is.EqualTo(-1));
        }

        [Test]
        public void DroppedHistory_IsPrunedFromTheFeed()
        {
            const int limit = 10;
            scope.Settings.MaxMessagesPerInstance = limit;
            ConsoleInstance console = scope.Create();
            feed.SetSources(new[] { console });

            for (int i = 0; i < 2000; i++)
            {
                console.CreateText("Source", i.ToString());
                if (i % 50 == 0)
                {
                    feed.Update();
                }
            }

            feed.Update();

            // Pruning runs in batches, so a bounded number of dropped messages may linger.
            Assert.That(feed.Visible.Count, Is.LessThanOrEqualTo(limit + 300));
            Assert.That(feed.Visible[feed.Visible.Count - 1].Content, Is.EqualTo("1999"));
            Assert.That(feed.GetCount(ConsoleMessageType.Text), Is.EqualTo(feed.Visible.Count));
        }

        [Test]
        public void Reload_AfterClear_ShowsOnlyWhatIsLeft()
        {
            ConsoleInstance first = scope.Create("First");
            ConsoleInstance second = scope.Create("Second");
            feed.SetSources(new[] { first, second });
            first.CreateText("Source", "first");
            second.CreateText("Source", "second");
            feed.Update();

            first.Clear();
            feed.Reload();

            Assert.That(Contents(feed), Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void ConcurrentLogging_NeverSkipsAMessage()
        {
            const int threadCount = 6;
            const int messagesPerThread = 400;
            scope.Settings.MaxMessagesPerInstance = 0;
            scope.Settings.CaptureTextCallstack = false;

            ConsoleInstance[] consoles = { scope.Create("First"), scope.Create("Second"), scope.Create("Third") };
            feed.SetSources(consoles);

            Thread[] threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                ConsoleInstance target = consoles[i % consoles.Length];
                threads[i] = new Thread(() =>
                {
                    for (int message = 0; message < messagesPerThread; message++)
                    {
                        target.CreateText("Worker", "Message");
                    }
                });
            }

            foreach (Thread thread in threads)
            {
                thread.Start();
            }

            // Read while the threads write: this is where a shared watermark used to lose messages.
            bool running = true;
            while (running)
            {
                feed.Update();
                running = false;
                foreach (Thread thread in threads)
                {
                    running |= thread.IsAlive;
                }
            }

            feed.Update();

            List<ConsoleMessage> visible = feed.Visible;
            Assert.That(visible.Count, Is.EqualTo(threadCount * messagesPerThread));
            for (int i = 1; i < visible.Count; i++)
            {
                Assert.That(visible[i].Sequence, Is.GreaterThan(visible[i - 1].Sequence));
            }
        }
    }
}
