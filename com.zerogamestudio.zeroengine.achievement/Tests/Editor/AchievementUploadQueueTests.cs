using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroEngine.Achievement;

namespace ZeroEngine.Tests.Achievement
{
    public sealed class AchievementUploadQueueTests
    {
        [Test]
        public async Task PendingSurvivesFailureAndLeavesOnlyAfterConfirmation()
        {
            var queue = new AchievementUploadQueue();
            queue.Add("a");
            Assert.ThrowsAsync<InvalidOperationException>(() => queue.FlushAsync((batch, token) =>
                Task.FromException(new InvalidOperationException("server rejected"))));
            Assert.That(queue.Contains("a"), Is.True);
            var confirmation = new TaskCompletionSource<bool>();
            var run = queue.FlushAsync((batch, token) => confirmation.Task);
            Assert.That(queue.Contains("a"), Is.True);
            confirmation.SetResult(true);
            await run;
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public async Task DuplicateIdsCoalesceAndIdsAddedDuringUploadAreNotLost()
        {
            var queue = new AchievementUploadQueue();
            queue.Add("a");
            queue.Add("b");
            Assert.That(queue.Add("a"), Is.False);
            var confirmation = new TaskCompletionSource<bool>();
            var batches = new List<IReadOnlyList<string>>();
            var run = queue.FlushAsync((batch, token) =>
            {
                batches.Add(batch);
                return batches.Count == 1 ? confirmation.Task : Task.CompletedTask;
            });
            queue.Add("c");
            queue.Add("b");
            confirmation.SetResult(true);
            await run;
            Assert.That(batches.Count, Is.EqualTo(2));
            Assert.That(batches[0], Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(batches[1], Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public async Task ResetWaitsForTheWriterAndPausedQueueDoesNotWrite()
        {
            var queue = new AchievementUploadQueue();
            queue.Add("before");
            var confirmation = new TaskCompletionSource<bool>();
            var upload = queue.FlushAsync((batch, token) => confirmation.Task);
            queue.Paused = true;
            bool resetStarted = false;
            var reset = queue.RunExclusiveAsync(() =>
            {
                resetStarted = true;
                queue.Clear();
                return Task.FromResult(1);
            });
            Assert.That(resetStarted, Is.False);
            confirmation.SetResult(true);
            await upload;
            Assert.That(await reset, Is.EqualTo(1));
            queue.Add("after");
            await queue.FlushAsync((batch, token) => throw new AssertionException("Paused writer started."));
            Assert.That(queue.Contains("after"), Is.True);
            queue.Paused = false;
            await queue.FlushAsync((batch, token) => Task.CompletedTask);
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void SessionCancellationPreservesPendingId()
        {
            var queue = new AchievementUploadQueue();
            queue.Add("a");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => queue.FlushAsync((batch, token) =>
                throw new AssertionException("Cancelled writer started."), cancellation.Token));
            Assert.That(queue.Contains("a"), Is.True);
            Assert.That(queue.IsRunning, Is.False);
        }
    }
}
