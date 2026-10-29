using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using reromanlee.ConsoleContainer.Tests;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Editor.Tests
{
    public sealed class HistoryTests
    {
        private ConsoleTestScope scope;
        private readonly List<ConsoleMessage> collected = new List<ConsoleMessage>();

        [SetUp]
        public void SetUp()
        {
            scope = new ConsoleTestScope();
            collected.Clear();
        }

        [TearDown]
        public void TearDown() => scope.Dispose();

        private List<ConsoleMessage> History(ConsoleInstance console)
        {
            collected.Clear();
            console.CollectSince(0, collected, out _);
            return collected;
        }

        [Test]
        public void History_IsBoundedByTheConfiguredLimit()
        {
            scope.Settings.MaxMessagesPerInstance = 5;
            ConsoleInstance console = scope.Create();

            for (int i = 0; i < 8; i++)
            {
                console.CreateText("Source", i.ToString());
            }

            List<ConsoleMessage> history = History(console);
            Assert.That(history.ConvertAll(message => message.Content), Is.EqualTo(new[] { "3", "4", "5", "6", "7" }));
        }

        [Test]
        public void Clear_EmptiesTheHistory()
        {
            ConsoleInstance console = scope.Create();
            console.CreateText("Source", "a");

            console.Clear();

            Assert.That(History(console), Is.Empty);
        }

        [Test]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Callstack_StartsAtTheLoggingCall()
        {
            ConsoleInstance console = scope.Create();

            console.CreateText("Source", "here");

            CallstackFrame top = History(console)[0].Callstack[0];
            Assert.That(top.MethodName, Is.EqualTo($"{nameof(HistoryTests)}.{nameof(Callstack_StartsAtTheLoggingCall)}"));
            Assert.That(top.FilePath, Does.EndWith($"{nameof(HistoryTests)}.cs"));
            Assert.That(top.Line, Is.GreaterThan(0));
        }

        [Test]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Callstack_SkipsWrappersMarkedHideInCallstack()
        {
            ConsoleInstance console = scope.Create();

            HiddenLogWrapper(console);

            CallstackFrame top = History(console)[0].Callstack[0];
            Assert.That(top.MethodName, Is.EqualTo($"{nameof(HistoryTests)}.{nameof(Callstack_SkipsWrappersMarkedHideInCallstack)}"));
        }

        [Test]
        public void Callstack_NamesLambdasAfterTheirMethod()
        {
            ConsoleInstance console = scope.Create();
            Action log = () => console.CreateText("Source", "from a lambda");

            log();

            Assert.That(History(console)[0].Callstack[0].MethodName,
                Is.EqualTo($"{nameof(HistoryTests)}.{nameof(Callstack_NamesLambdasAfterTheirMethod)}"));
        }

        [Test]
        public void Callstack_NamesIteratorsAfterTheirMethod()
        {
            ConsoleInstance console = scope.Create();

            IEnumerator iterator = LogFromIterator(console);
            iterator.MoveNext();

            Assert.That(History(console)[0].Callstack[0].MethodName, Is.EqualTo($"{nameof(HistoryTests)}.{nameof(LogFromIterator)}"));
        }

        [Test]
        public void Callstack_CanBeTurnedOffPerType()
        {
            scope.Settings.CaptureTextCallstack = false;
            ConsoleInstance console = scope.Create();

            console.CreateText("Source", "no stack");
            console.CreateWarning("Source", "with stack");

            List<ConsoleMessage> history = History(console);
            Assert.That(history[0].Callstack, Is.Empty);
            Assert.That(history[1].Callstack, Is.Not.Empty);
        }

        [Test]
        public void ExceptionMessages_ShowWhereTheExceptionWasThrown()
        {
            ConsoleInstance console = scope.Create();
            Exception thrown = CatchFrom(ThrowingMethod);

            console.CreateException("Source", thrown);

            CallstackFrame top = History(console)[0].Callstack[0];
            Assert.That(top.MethodName, Is.EqualTo($"{nameof(HistoryTests)}.{nameof(ThrowingMethod)}"));
        }

        [Test]
        public void ExceptionMessages_StartAtTheInnermostThrowSite()
        {
            ConsoleInstance console = scope.Create();
            Exception thrown = CatchFrom(WrappingMethod);

            console.CreateException("Source", thrown);

            CallstackFrame top = History(console)[0].Callstack[0];
            Assert.That(top.MethodName, Is.EqualTo($"{nameof(HistoryTests)}.{nameof(ThrowingMethod)}"));
        }

        [Test]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void UnthrownExceptions_FallBackToTheLoggingCall()
        {
            ConsoleInstance console = scope.Create();

            console.CreateException("Source", new InvalidOperationException("never thrown"));

            Assert.That(History(console)[0].Callstack[0].MethodName,
                Is.EqualTo($"{nameof(HistoryTests)}.{nameof(UnthrownExceptions_FallBackToTheLoggingCall)}"));
        }

        [Test]
        public void DisposedInstanceWithHistory_StaysInspectable()
        {
            ConsoleInstance console = scope.Create();
            console.CreateText("Source", "kept");

            console.Dispose();

            Assert.That(History(console).Count, Is.EqualTo(1));
            Assert.That(ConsoleRegistry.Snapshot(), Has.Member(console));
        }

        [Test]
        public void DisposedInstance_LeavesTheRegistryOnceCleared()
        {
            ConsoleInstance console = scope.Create();
            console.CreateText("Source", "kept");
            console.Dispose();

            console.Clear();

            Assert.That(ConsoleRegistry.Snapshot(), Has.No.Member(console));
        }

        [Test]
        public void EmptyInstance_LeavesTheRegistryWhenDisposed()
        {
            ConsoleInstance console = scope.Create();

            console.Dispose();

            Assert.That(ConsoleRegistry.Snapshot(), Has.No.Member(console));
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HiddenLogWrapper(IConsoleInstance console) => console.CreateText("Wrapper", "logged");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerator LogFromIterator(IConsoleInstance console)
        {
            console.CreateText("Source", "from an iterator");
            yield break;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowingMethod() => throw new InvalidOperationException("thrown");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WrappingMethod()
        {
            try
            {
                ThrowingMethod();
            }
            catch (Exception inner)
            {
                throw new ApplicationException("wrapped", inner);
            }
        }

        private static Exception CatchFrom(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                return exception;
            }

            throw new AssertionException("Expected an exception.");
        }
    }
}
