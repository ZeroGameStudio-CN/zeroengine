using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleInteractionTests
    {
        private static readonly ConsoleCaller Caller = new ConsoleCaller(ConsoleEnvironment.Editor);

        [Test]
        public async Task NamedReferencesShareParsingAndCompletion_AndHonorTheirLifetime()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            bool available = true;
            using var names = new ConsoleNamedValues<Target>(session.Values, value => available);
            var camera = new Target();
            using var target = names.Register("main-camera", camera, "Camera 主相机");
            Assert.Throws<InvalidOperationException>(() => names.Register("MAIN-CAMERA", new Target()));
            using var commands = registry.Register("sample", new[]
            {
                new ConsoleBinding(new ConsoleCommand("sample.inspect", "inspect", "sample", "Inspect targets.",
                    new[] { new ConsoleParameter("targets", typeof(Target[]), variadic: true) }),
                    (context, arguments, cancellation) => Task.FromResult(ConsoleResult.Success(data: arguments.Get<Target[]>(0))),
                    names.CompleteAsync)
            });

            var choices = await session.CompleteAsync("inspect @Main-camera Cam", 24, Caller);
            Assert.That(choices.Items.Count, Is.EqualTo(1));
            Assert.That(choices.Items[0].Value, Is.EqualTo("@main-camera"));
            var result = await session.ExecuteLineAsync(choices.Apply("inspect @Main-camera Cam", 0), Caller);
            Assert.That(result.Data, Is.EqualTo(new[] { camera, camera }));

            available = false;
            Assert.That((await session.CompleteAsync("inspect @", 9, Caller)).Items, Is.Empty);
            Assert.That((await session.ExecuteLineAsync("inspect @main-camera", Caller)).IsSuccess, Is.False);
            available = true;
            target.Dispose();
            Assert.That((await session.ExecuteLineAsync("inspect @main-camera", Caller)).IsSuccess, Is.False);
        }

        [Test]
        public async Task ConfirmationRequiresTheOwningFrontendAndLivePermission_AndRespondsOnce()
        {
            using var registry = new ConsoleRegistry();
            var policy = new PromptPolicy();
            using var session = new ConsoleSession(registry, policy: policy);
            int applications = 0;
            using var commands = RegisterConfirmation(registry, () => applications++);
            using var channel = session.TryAcquireInteraction();
            Assert.That(channel, Is.Not.Null);
            Assert.That(session.TryAcquireInteraction(), Is.Null);
            var execution = session.ExecuteLineAsync("sample-apply", Caller);
            var request = channel.Pending;
            Assert.That(request?.Message, Is.EqualTo("Apply the pending changes?"));
            Assert.That(applications, Is.Zero);
            Assert.That((await session.ExecuteLineAsync("sample-apply", Caller)).Code, Is.EqualTo("busy"));
            Assert.That(channel.Respond(request, new ConsoleCaller(ConsoleEnvironment.Editor, "other"), true), Is.False);
            policy.Allowed = false;
            Assert.That(channel.Respond(request, Caller, true), Is.False);
            policy.Allowed = true;
            Assert.That(channel.Respond(request, Caller, true), Is.True);
            Assert.That(channel.Respond(request, Caller, true), Is.False);
            Assert.That((await execution).IsSuccess, Is.True);
            Assert.That(channel.Pending, Is.Null);
            Assert.That(applications, Is.EqualTo(1));
        }

        [Test]
        public async Task NoFrontendDeclineCloseAndCancellation_DoNotApply()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            int applications = 0;
            using var commands = RegisterConfirmation(registry, () => applications++);
            Assert.That((await session.ExecuteLineAsync("sample-apply", Caller)).Code, Is.EqualTo("declined"));
            using var first = session.TryAcquireInteraction();
            var declined = session.ExecuteLineAsync("sample-apply", Caller);
            Assert.That(first.Respond(first.Pending, Caller, false), Is.True);
            Assert.That((await declined).Code, Is.EqualTo("declined"));
            var closed = session.ExecuteLineAsync("sample-apply", Caller);
            var oldRequest = first.Pending;
            first.Dispose();
            Assert.That((await closed).Code, Is.EqualTo("declined"));
            using var second = session.TryAcquireInteraction();
            using var cancellation = new CancellationTokenSource();
            var cancelled = session.ExecuteLineAsync("sample-apply", Caller, cancellation.Token);
            Assert.That(second.Respond(oldRequest, Caller, true), Is.False);
            cancellation.Cancel();
            Assert.That((await cancelled).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(second.Pending, Is.Null);
            Assert.That(applications, Is.Zero);
        }

        private static IDisposable RegisterConfirmation(ConsoleRegistry registry, Action apply) =>
            registry.Register("sample", new[]
            {
                new ConsoleBinding(new ConsoleCommand("sample.apply", "sample-apply", "sample", "Apply the changes."),
                    async (invocation, _, cancellation) =>
                    {
                        if (!await invocation.ConfirmAsync("Apply the pending changes?"))
                            return ConsoleResult.Failure("declined", "Changes discarded.");
                        apply();
                        return ConsoleResult.Success();
                    })
            });

        private sealed class Target { }

        private sealed class PromptPolicy : IConsolePolicy
        {
            public bool Allowed = true;
            public ConsoleResult Check(ConsoleCommand command, ConsoleCaller caller) => Allowed
                ? ConsoleResult.Success() : ConsoleResult.Failure("not-allowed", "Permission changed.");
        }
    }
}
