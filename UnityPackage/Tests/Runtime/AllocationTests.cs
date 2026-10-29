using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace reromanlee.ConsoleContainer.Tests
{
    // Each measured delegate runs once first, so one-time costs (compilation, string literal
    // interning, caches) are not mistaken for per-call allocations.
    public sealed class AllocationTests
    {
        private ConsoleTestScope scope;

        [SetUp]
        public void SetUp() => scope = new ConsoleTestScope();

        [TearDown]
        public void TearDown() => scope.Dispose();

        [Test]
        public void LoggingIntoADisposedInstance_AllocatesNothing()
        {
            ConsoleInstance console = scope.Create();
            console.Dispose();
            TestDelegate log = () => console.CreateText(this, "ignored");
            log();

            Assert.That(log, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ResolvingAnObjectSource_AllocatesNothingOnceSeen()
        {
            TestDelegate resolve = () => SourceNames.Resolve(this);
            resolve();

            Assert.That(resolve, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ReadingDerivedText_AllocatesNothingAfterTheFirstRead()
        {
            ConsoleInstance console = scope.Create();
            ConsoleMessage message = null;
            console.MessageCreated += created => message = created;
            console.CreateText("Source", "content");

            TestDelegate read = () =>
            {
                _ = message.Label;
                _ = message.TimeText;
            };
            read();

            Assert.That(read, Is.Not.AllocatingGCMemory());
        }
    }
}
