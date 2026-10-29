using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.ConsoleContainer.Tests
{
    public sealed class EventTests
    {
        private ConsoleTestScope scope;

        [SetUp]
        public void SetUp() => scope = new ConsoleTestScope();

        [TearDown]
        public void TearDown() => scope.Dispose();

        [Test]
        public void MessageCreated_IsRaisedForEveryType()
        {
            ConsoleInstance console = scope.Create();
            List<ConsoleMessageType> types = new List<ConsoleMessageType>();
            console.MessageCreated += message => types.Add(message.Type);

            console.CreateText("Source", "text");
            console.CreateWarning("Source", "warning");
            console.CreateError("Source", "error");

            Assert.That(types, Is.EqualTo(new[] { ConsoleMessageType.Text, ConsoleMessageType.Warning, ConsoleMessageType.Error }));
        }

        [Test]
        public void ErrorCreated_IsRaisedForErrorsOnly_AfterMessageCreated()
        {
            ConsoleInstance console = scope.Create();
            List<string> order = new List<string>();
            console.MessageCreated += message => order.Add("message:" + message.Content);
            console.ErrorCreated += message => order.Add("error:" + message.Content);

            console.CreateWarning("Source", "first");
            console.CreateError("Source", "second");

            Assert.That(order, Is.EqualTo(new[] { "message:first", "message:second", "error:second" }));
        }

        [Test]
        public void ThrowingHandler_IsReported_AndLoggingCarriesOn()
        {
            ConsoleInstance console = scope.Create();
            int laterHandlerCalls = 0;
            console.MessageCreated += _ => throw new InvalidOperationException("handler failed");
            console.ErrorCreated += _ => laterHandlerCalls++;

            LogAssert.Expect(LogType.Exception, new Regex("handler failed"));
            console.CreateError("Source", "boom");

            Assert.That(laterHandlerCalls, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_DropsHandlers_AndIgnoresLaterMessages()
        {
            ConsoleInstance console = scope.Create();
            int calls = 0;
            console.MessageCreated += _ => calls++;

            console.Dispose();
            console.CreateText("Source", "ignored");

            Assert.That(console.IsDisposed, Is.True);
            Assert.That(console.IsEnabled(ConsoleMessageType.Text), Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Dispose_IsSafeToRepeat()
        {
            ConsoleInstance console = scope.Create();

            console.Dispose();

            Assert.That(() => console.Dispose(), Throws.Nothing);
        }

        [Test]
        public void Created_IsRaisedWithAFullyBuiltInstance()
        {
            IConsoleInstance observed = null;
            string observedName = null;
            Action<IConsoleInstance> handler = instance =>
            {
                observed = instance;
                observedName = instance.Name;
            };

            ConsoleInstance.Created += handler;
            try
            {
                ConsoleInstance console = scope.Create("Observed");

                Assert.That(observed, Is.SameAs(console));
                Assert.That(observedName, Is.EqualTo("Observed"));
            }
            finally
            {
                ConsoleInstance.Created -= handler;
            }
        }

        [Test]
        public void Created_SubscriberCanObserveEveryLaterInstance()
        {
            List<string> errors = new List<string>();
            Action<IConsoleInstance> handler = instance => instance.ErrorCreated += message => errors.Add(instance.Name);

            ConsoleInstance.Created += handler;
            try
            {
                scope.Create("First").CreateError("Source", "a");
                scope.Create("Second").CreateError("Source", "b");
            }
            finally
            {
                ConsoleInstance.Created -= handler;
            }

            Assert.That(errors, Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public void ThrowingCreatedHandler_DoesNotBreakConstruction()
        {
            Action<IConsoleInstance> handler = _ => throw new InvalidOperationException("created failed");

            ConsoleInstance.Created += handler;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("created failed"));
                ConsoleInstance console = scope.Create("Survives");

                Assert.That(console.Name, Is.EqualTo("Survives"));
            }
            finally
            {
                ConsoleInstance.Created -= handler;
            }
        }

        [Test]
        public void HandlerLoggingIntoItsOwnInstance_StopsInsteadOfOverflowingTheStack()
        {
            ConsoleInstance console = scope.Create();
            int raised = 0;
            console.ErrorCreated += message =>
            {
                raised++;
                console.CreateError("Handler", "again");
            };

            Assert.That(() => console.CreateError("Source", "first"), Throws.Nothing);

            // Bounded by the dispatch depth cap, far below anything that threatens the stack.
            Assert.That(raised, Is.InRange(2, 16));
        }

        [Test]
        public void CreatedHandlerConstructingInstances_StopsInsteadOfOverflowingTheStack()
        {
            List<ConsoleInstance> spawned = new List<ConsoleInstance>();
            Action<IConsoleInstance> handler = _ => spawned.Add(new ConsoleInstance("Spawned"));

            ConsoleInstance.Created += handler;
            try
            {
                Assert.That(() => scope.Create("Root"), Throws.Nothing);
                Assert.That(spawned.Count, Is.InRange(1, 16));
            }
            finally
            {
                ConsoleInstance.Created -= handler;

                foreach (ConsoleInstance instance in spawned)
                {
                    instance.Dispose();
                }
            }
        }

        [Test]
        public void HandlerLoggingIntoAnotherInstance_IsDelivered()
        {
            ConsoleInstance source = scope.Create("Source");
            ConsoleInstance reporter = scope.Create("Reporter");
            string reported = null;
            reporter.MessageCreated += message => reported = message.Content;
            source.ErrorCreated += message => reporter.CreateWarning("CrashReporter", message.Content);

            source.CreateError("Socket", "refused");

            Assert.That(reported, Is.EqualTo("refused"));
        }
    }
}
