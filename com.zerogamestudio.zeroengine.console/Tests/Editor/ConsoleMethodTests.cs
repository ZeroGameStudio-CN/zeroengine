using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleMethodTests
    {
        private static readonly ConsoleCaller Editor = new ConsoleCaller(ConsoleEnvironment.Editor);

        [Test]
        public async Task StaticAndInstanceMethods_UseDefaultsAndParamArrays()
        {
            using (var registry = new ConsoleRegistry())
            using (var session = new ConsoleSession(registry))
            using (registry.Register("method", new[] {
                Binding("sum", nameof(Sum)),
                Binding("scale", nameof(Target.Scale), new Target()) }))
            {
                Assert.That((await session.ExecuteLineAsync("sum 1 2 3", Editor)).Data, Is.EqualTo(6));
                Assert.That((await session.ExecuteLineAsync("scale 3", Editor)).Data, Is.EqualTo(6));
                Assert.That((await session.ExecuteLineAsync("scale 3 4", Editor)).Data, Is.EqualTo(12));
            }
        }

        [Test]
        public async Task AsyncMethods_PreserveBusinessResultsAndInjectedCancellation()
        {
            using (var registry = new ConsoleRegistry())
            using (var session = new ConsoleSession(registry))
            using (registry.Register("method", new[] { Binding("read", nameof(Read)), Binding("business", nameof(Business)) }))
            {
                Assert.That((await session.ExecuteLineAsync("read 7", Editor)).Data, Is.EqualTo(7));
                var failure = await session.ExecuteLineAsync("business", Editor);
                Assert.That(failure.Code, Is.EqualTo("business-error"));
                Assert.That(failure.IsSuccess, Is.False);
            }
        }

        [Test]
        public async Task VoidAndUntypedTasks_DoNotInventBusinessSuccess()
        {
            using (var registry = new ConsoleRegistry())
            using (var session = new ConsoleSession(registry))
            using (registry.Register("method", new[] { Binding("empty", nameof(Empty)), Binding("pending", nameof(Pending)) }))
            {
                Assert.That((await session.ExecuteLineAsync("empty", Editor)).IsSuccess, Is.False);
                Assert.That((await session.ExecuteLineAsync("pending", Editor)).IsSuccess, Is.False);
            }
        }

        [Test]
        public async Task MethodFailure_IsReportedOnceWithoutReflectionWrapping()
        {
            using (var registry = new ConsoleRegistry())
            using (var session = new ConsoleSession(registry))
            using (registry.Register("method", new[] { Binding("fail", nameof(Fail)) }))
            {
                var result = await session.ExecuteLineAsync("fail", Editor);
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Message, Does.Contain("original method error"));
            }
        }

        [Test]
        public void UnsupportedSignatures_RequireExplicitAdapters()
        {
            Assert.Throws<ArgumentException>(() => Binding("custom", nameof(CustomAwaitable)));
            Assert.Throws<ArgumentException>(() => Binding("reference", nameof(Reference)));
            Assert.Throws<ArgumentException>(() => Binding("unobserved", nameof(Unobserved)));
        }

        [Test]
        public async Task ValueTaskMethods_AwaitTypedUntypedAndStructuredResults()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            using var lease = registry.Register("method", new[] {
                Binding("value", nameof(Value)), Binding("empty-value", nameof(EmptyValue)), Binding("business-value", nameof(BusinessValue)) });
            Assert.That((await session.ExecuteLineAsync("value 8", Editor)).Data, Is.EqualTo(8));
            Assert.That((await session.ExecuteLineAsync("empty-value", Editor)).Status, Is.EqualTo(ConsoleResultStatus.Unconfirmed));
            Assert.That((await session.ExecuteLineAsync("business-value", Editor)).Code, Is.EqualTo("business-error"));
        }

        [Test]
        public async Task SingleUseValueTaskSource_IsConsumedOnceAndCancellationWaitsForItsActualEnd()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var source = new SingleUseSource();
            using var lease = registry.Register("method", new[] { Binding("source", nameof(SingleUseSource.Read), source) });
            var pending = session.ExecuteLineAsync("source", Editor);
            Assert.That(pending.IsCompleted, Is.False);
            session.CancelCurrent();
            Assert.That(session.IsRunning, Is.True);
            source.Complete();
            Assert.That((await pending).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(source.Reads, Is.EqualTo(1));
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(session.IsRunning, Is.False);
        }

        [Test]
        public async Task ExplicitGenericTypes_RegisterClosedAliasesAndRejectConstraintsBeforeExecution()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var definition = typeof(ConsoleMethodTests).GetMethod(nameof(Echo), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.Throws<ArgumentException>(() => ConsoleMethodBinding.CreateGeneric("invalid", "echo<string>", "method", definition, new[] { typeof(string) }));
            using var lease = registry.Register("method", new[] {
                ConsoleMethodBinding.CreateGeneric("echo.integer", "echo<int>", "method", definition, new[] { typeof(int) }),
                ConsoleMethodBinding.CreateGeneric("echo.real", "echo<double>", "method", definition, new[] { typeof(double) }) });
            Assert.That((await session.ExecuteLineAsync("echo<int> 7", Editor)).Data, Is.EqualTo(7));
            Assert.That((await session.ExecuteLineAsync("echo<double> 1.5", Editor)).Data, Is.EqualTo(1.5));
            Assert.That((await session.ExecuteLineAsync("echo<string> text", Editor)).Code, Is.EqualTo("unknown-command"));
            Assert.That(session.GetHint("echo<int> ", 10, Editor).Parameter.ValueType, Is.EqualTo(typeof(int)));
            Assert.That((await session.CompleteAsync("echo<", 5, Editor)).Items.Count, Is.EqualTo(2));
        }

        private static ConsoleBinding Binding(string alias, string method, object target = null)
        {
            var type = target?.GetType() ?? typeof(ConsoleMethodTests);
            return ConsoleMethodBinding.Create("method." + alias, alias, "method",
                type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), target);
        }
        private static int Sum(params int[] values) { int sum = 0; foreach (int value in values) sum += value; return sum; }
        private static async Task<int> Read(int value, CancellationToken cancellation) { await Task.Yield(); cancellation.ThrowIfCancellationRequested(); return value; }
        private static Task<ConsoleResult> Business() => Task.FromResult(ConsoleResult.Failure("business-error", "original business result"));
        private static void Empty() { }
        private static Task Pending() => Task.CompletedTask;
        private static int Fail() => throw new InvalidOperationException("original method error");
        private static UnsupportedAwaitable CustomAwaitable() => default;
        private readonly struct UnsupportedAwaitable { public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter() => Task.FromResult(1).GetAwaiter(); }
        private static async ValueTask<int> Value(int value, CancellationToken cancellation) { await Task.Yield(); cancellation.ThrowIfCancellationRequested(); return value; }
        private static ValueTask EmptyValue() => default;
        private static ValueTask<ConsoleResult> BusinessValue() => new ValueTask<ConsoleResult>(ConsoleResult.Failure("business-error", "Original business result."));
        private static T Echo<T>(T value) where T : struct => value;
        private sealed class SingleUseSource : IValueTaskSource<int>
        {
            private ManualResetValueTaskSourceCore<int> source;
            public int Reads, Calls;
            public SingleUseSource() { source.RunContinuationsAsynchronously = true; }
            public ValueTask<int> Read() { Calls++; return new ValueTask<int>(this, source.Version); }
            public void Complete() => source.SetResult(9);
            public int GetResult(short token) { if (++Reads != 1) throw new InvalidOperationException("ValueTask consumed twice."); return source.GetResult(token); }
            public ValueTaskSourceStatus GetStatus(short token) => source.GetStatus(token);
            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
                => source.OnCompleted(continuation, state, token, flags);
        }
        private static void Reference(ref int value) { value++; }
        private static async void Unobserved() { await Task.Yield(); }
        private sealed class Target { public int Scale(int value, int multiplier = 2) => value * multiplier; }
    }
}
