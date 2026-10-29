using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace reromanlee.ConsoleContainer.Tests
{
    public sealed class MessageCompositionTests
    {
        private ConsoleTestScope scope;
        private ConsoleInstance console;
        private readonly List<ConsoleMessage> received = new List<ConsoleMessage>();

        [SetUp]
        public void SetUp()
        {
            scope = new ConsoleTestScope();
            console = scope.Create();
            received.Clear();
            console.MessageCreated += received.Add;
        }

        [TearDown]
        public void TearDown() => scope.Dispose();

        private ConsoleMessage Last => received[received.Count - 1];

        [Test]
        public void ContentArguments_AreJoinedWithSingleSpaces()
        {
            console.CreateText("SaveSystem", "Loaded", "42", "assets");

            Assert.That(Last.Source, Is.EqualTo("SaveSystem"));
            Assert.That(Last.Content, Is.EqualTo("Loaded 42 assets"));
            Assert.That(Last.Label, Is.EqualTo("SaveSystem: Loaded 42 assets"));
        }

        [Test]
        public void SingleMessage_IsUsedAsContent()
        {
            console.CreateWarning("Socket", "Latency high");

            Assert.That(Last.Type, Is.EqualTo(ConsoleMessageType.Warning));
            Assert.That(Last.Label, Is.EqualTo("Socket: Latency high"));
        }

        [Test]
        public void NoContent_LabelIsTheSourceAlone()
        {
            console.CreateText("Booted");

            Assert.That(Last.Content, Is.Empty);
            Assert.That(Last.Label, Is.EqualTo("Booted"));
        }

        [Test]
        public void NullContentElements_AreTreatedAsEmpty()
        {
            console.CreateText("Source", (string)null);
            Assert.That(Last.Content, Is.Empty);

            console.CreateText("Source", "a", null, "b");
            Assert.That(Last.Content, Is.EqualTo("a  b"));
        }

        [Test]
        public void ObjectSource_UsesItsTypeName()
        {
            console.CreateText(new SampleService(), "Hello");

            Assert.That(Last.Source, Is.EqualTo(nameof(SampleService)));
        }

        [Test]
        public void TypeSource_UsesTheTypeItself()
        {
            console.CreateText(typeof(SampleService), "Hello");

            Assert.That(Last.Source, Is.EqualTo(nameof(SampleService)));
        }

        [Test]
        public void StringPassedAsObject_IsUsedAsIs()
        {
            object source = "Networking";
            console.CreateText(source, "Hello");

            Assert.That(Last.Source, Is.EqualTo("Networking"));
        }

        [Test]
        public void GenericSource_ReadsLikeCSharp()
        {
            console.CreateText(new Dictionary<string, List<int>>(), "Hello");

            Assert.That(Last.Source, Is.EqualTo("Dictionary<String, List<Int32>>"));
        }

        [Test]
        public void MissingSource_IsShownAsNull()
        {
            console.CreateText(null, "Hello");
            Assert.That(Last.Source, Is.EqualTo("null"));

            console.CreateText(string.Empty, "Hello");
            Assert.That(Last.Source, Is.EqualTo("null"));
        }

        [Test]
        public void CreateException_DescribesAndKeepsTheException()
        {
            InvalidOperationException exception = new InvalidOperationException("Socket closed");

            console.CreateException("Network", exception);

            Assert.That(Last.Type, Is.EqualTo(ConsoleMessageType.Error));
            Assert.That(Last.Label, Is.EqualTo("Network: InvalidOperationException: Socket closed"));
            Assert.That(Last.Exception, Is.SameAs(exception));
        }

        [Test]
        public void CreateException_WithoutException_StillLogsAnError()
        {
            console.CreateException("Network", null);

            Assert.That(Last.Type, Is.EqualTo(ConsoleMessageType.Error));
            Assert.That(Last.Exception, Is.Null);
        }

        [Test]
        public void TimeText_IgnoresTheCulturesTimeSeparator()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            CultureInfo dotted = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            dotted.DateTimeFormat.TimeSeparator = ".";

            try
            {
                CultureInfo.CurrentCulture = dotted;
                console.CreateText("Clock", "Tick");

                Assert.That(Last.TimeText, Does.Match(@"^\[\d{2}:\d{2}:\d{2}\]$"));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void Sequence_GrowsAcrossInstances()
        {
            ConsoleInstance other = scope.Create("Other");
            other.MessageCreated += received.Add;

            console.CreateText("A", "1");
            other.CreateText("B", "2");
            console.CreateText("A", "3");

            Assert.That(received[1].Sequence, Is.GreaterThan(received[0].Sequence));
            Assert.That(received[2].Sequence, Is.GreaterThan(received[1].Sequence));
        }

        [Test]
        public void UnnamedInstances_GetDistinctGeneratedNames()
        {
            ConsoleInstance first = scope.Create(null);
            ConsoleInstance second = scope.Create(string.Empty);

            Assert.That(first.Name, Does.StartWith("Instance "));
            Assert.That(second.Name, Is.Not.EqualTo(first.Name));
        }

        [Test]
        public void LoggingFromManyThreads_DeliversEveryMessage()
        {
            const int threadCount = 8;
            const int messagesPerThread = 250;
            int delivered = 0;
            console.MessageCreated += _ => Interlocked.Increment(ref delivered);

            Thread[] threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    for (int message = 0; message < messagesPerThread; message++)
                    {
                        console.CreateText("Worker", "Message");
                    }
                });
                threads[i].Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.That(delivered, Is.EqualTo(threadCount * messagesPerThread));
        }

        private sealed class SampleService
        {
        }
    }
}
