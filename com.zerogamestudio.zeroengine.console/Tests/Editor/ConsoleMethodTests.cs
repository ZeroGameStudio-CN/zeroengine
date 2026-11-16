using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        private static ValueTask<int> CustomAwaitable() => new ValueTask<int>(1);
        private static void Reference(ref int value) { value++; }
        private static async void Unobserved() { await Task.Yield(); }
        private sealed class Target { public int Scale(int value, int multiplier = 2) => value * multiplier; }
    }
}
