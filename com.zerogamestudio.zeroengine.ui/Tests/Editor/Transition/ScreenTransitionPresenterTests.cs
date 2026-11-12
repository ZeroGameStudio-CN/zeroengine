using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.UI.Tests.Editor.Transition
{
    [TestFixture]
    [Category("Unit")]
    public sealed class ScreenTransitionPresenterTests
    {
        private GameObject _gameObject;
        private ScreenTransitionPresenter _presenter;
        private FakeAnimationDriver _driver;
        private int _materialCountBeforePresenter;

        [SetUp]
        public void SetUp()
        {
            _materialCountBeforePresenter = Resources.FindObjectsOfTypeAll<Material>().Length;
            _gameObject = new GameObject("ScreenTransitionPresenter.Tests");
            _presenter = _gameObject.AddComponent<ScreenTransitionPresenter>();
            _driver = new FakeAnimationDriver();
            _presenter.SetAnimationDriver(_driver);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void PublicStyleValuesRemainStable()
        {
            Assert.That((int)ScreenTransitionStyle.Fade, Is.EqualTo(0));
            Assert.That((int)ScreenTransitionStyle.CircleWipe, Is.EqualTo(1));
            Assert.That((int)ScreenTransitionStyle.DiamondWipe, Is.EqualTo(2));
            Assert.That((int)ScreenTransitionStyle.Dissolve, Is.EqualTo(3));
        }

        [Test]
        public void PresenterCreation_DoesNotCreateRuntimeMaterial()
        {
            var materialCountAfterPresenter = Resources.FindObjectsOfTypeAll<Material>().Length;
            Assert.That(materialCountAfterPresenter, Is.EqualTo(_materialCountBeforePresenter));
        }

        [Test]
        public void Initialize_IsIdempotent_AndPresenterOwnsRegistryLifetime()
        {
            _presenter.Initialize();
            _presenter.Initialize();

            Assert.That(ServiceRegistry.ResolveOrNull<IScreenTransitionService>(), Is.SameAs(_presenter));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public void RunAsync_RejectsInvalidRequestImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => _presenter.RunAsync(
                new ScreenTransitionRequest(),
                null,
                CancellationToken.None));

            Assert.Throws<ArgumentOutOfRangeException>(() => _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, float.NaN),
                _ => Task.CompletedTask,
                CancellationToken.None));

            Assert.Throws<ArgumentOutOfRangeException>(() => _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, -0.01f),
                _ => Task.CompletedTask,
                CancellationToken.None));
        }

        [Test]
        public void RunAsync_PreCancelled_DoesNotAcquireLeaseOrRunAction()
        {
            var actionCalls = 0;
            var acquireCalls = 0;
            _presenter.ConfigureHooks(new ScreenTransitionHooks(
                acquireInputLockLease: () =>
                {
                    acquireCalls++;
                    return new CountingLease();
                }));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 0f),
                _ =>
                {
                    actionCalls++;
                    return Task.CompletedTask;
                },
                cancellation.Token));

            Assert.That(acquireCalls, Is.EqualTo(0));
            Assert.That(actionCalls, Is.EqualTo(0));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task RunAsync_Busy_DoesNotRunSecondActionOrChangeActiveOwner()
        {
            var firstActionCalls = 0;
            var secondActionCalls = 0;
            _driver.BlockCall = 1;
            var firstTask = _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 1f),
                _ =>
                {
                    firstActionCalls++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            var busy = await _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.DiamondWipe, 0f),
                _ =>
                {
                    secondActionCalls++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.That(busy.Status, Is.EqualTo(ScreenTransitionStatus.Busy));
            Assert.That(_presenter.IsTransitioning, Is.True);
            Assert.That(firstActionCalls, Is.EqualTo(0));
            Assert.That(secondActionCalls, Is.EqualTo(0));

            _driver.Release();
            await firstTask;
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task RunAsync_Success_AcquiresAndReleasesLeaseExactlyOnce()
        {
            var lease = new CountingLease();
            var acquireCalls = 0;
            var telemetry = new List<ScreenTransitionTelemetry>();
            _presenter.ConfigureHooks(new ScreenTransitionHooks(
                acquireInputLockLease: () =>
                {
                    acquireCalls++;
                    return lease;
                },
                telemetry: telemetry.Add));

            var result = await _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.CircleWipe, 0f),
                _ => Task.CompletedTask,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ScreenTransitionStatus.Completed));
            Assert.That(acquireCalls, Is.EqualTo(1));
            Assert.That(lease.DisposeCalls, Is.EqualTo(1));
            Assert.That(telemetry.Count, Is.EqualTo(1));
            Assert.That(telemetry[0].Succeeded, Is.True);
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task RunAsync_RunningCancellation_FailClearsAndReleasesLease()
        {
            var lease = new CountingLease();
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() => lease));
            _driver.BlockCall = 1;
            using var cancellation = new CancellationTokenSource();
            var task = _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 1f),
                _ => Task.CompletedTask,
                cancellation.Token);

            cancellation.Cancel();

            await AssertThrowsAsync<OperationCanceledException>(() => task);
            Assert.That(lease.DisposeCalls, Is.EqualTo(1));
            Assert.That(_presenter.IsTransitioning, Is.False);
            Assert.That(_driver.CancelCalls, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task RunAsync_ActionException_PreservesExceptionAndFailClears()
        {
            var lease = new CountingLease();
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() => lease));

            var exception = await AssertThrowsAsync<InvalidOperationException>(() =>
                _presenter.RunAsync(
                    new ScreenTransitionRequest(ScreenTransitionStyle.Dissolve, 0f),
                    _ => throw new InvalidOperationException("synthetic action failure"),
                    CancellationToken.None));

            Assert.That(exception.Message, Is.EqualTo("synthetic action failure"));
            Assert.That(lease.DisposeCalls, Is.EqualTo(1));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task RunAsync_AnimationException_PreservesExceptionAndFailClears()
        {
            var lease = new CountingLease();
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() => lease));
            _driver.ThrowOnCall = 2;

            var exception = await AssertThrowsAsync<InvalidOperationException>(() =>
                _presenter.RunAsync(
                    new ScreenTransitionRequest(ScreenTransitionStyle.DiamondWipe, 0f),
                    _ => Task.CompletedTask,
                    CancellationToken.None));

            Assert.That(exception.Message, Is.EqualTo("synthetic animation failure"));
            Assert.That(lease.DisposeCalls, Is.EqualTo(1));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task RunAsync_BlockRaycastsFalse_PreservesExistingRaycastState()
        {
            _presenter.SetCovered();

            await _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 0f, blockRaycasts: false),
                _ => Task.CompletedTask,
                CancellationToken.None);

            var canvasGroup = _gameObject.GetComponentInChildren<CanvasGroup>();
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            _presenter.SetClear();
            Assert.That(canvasGroup.blocksRaycasts, Is.False);
        }

        [Test]
        public async Task RunAsync_LockInputFalse_DoesNotAcquireLease()
        {
            var acquireCalls = 0;
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() =>
            {
                acquireCalls++;
                return new CountingLease();
            }));

            await _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 0f, lockInput: false),
                _ => Task.CompletedTask,
                CancellationToken.None);

            Assert.That(acquireCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task SetCoveredAndSetClear_ThrowWhileBusy()
        {
            _driver.BlockCall = 1;
            var task = _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 1f),
                _ => Task.CompletedTask,
                CancellationToken.None);

            Assert.Throws<InvalidOperationException>(() => _presenter.SetCovered());
            Assert.Throws<InvalidOperationException>(() => _presenter.SetClear());

            _driver.Release();
            await task;
        }

        [Test]
        public async Task ZeroDuration_Success_100Requests_CleansEveryLease()
        {
            var acquireCalls = 0;
            var disposeCalls = 0;
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() =>
            {
                acquireCalls++;
                return new CountingLease(() => disposeCalls++);
            }));

            for (var i = 0; i < 100; i++)
            {
                await _presenter.RunAsync(
                    new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 0f),
                    _ => Task.CompletedTask,
                    CancellationToken.None);
            }

            Assert.That(acquireCalls, Is.EqualTo(100));
            Assert.That(disposeCalls, Is.EqualTo(100));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task Cancellation_100Requests_CleansEveryLease()
        {
            var acquireCalls = 0;
            var disposeCalls = 0;
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() =>
            {
                acquireCalls++;
                return new CountingLease(() => disposeCalls++);
            }));

            for (var i = 0; i < 100; i++)
            {
                _driver.BlockCall = _driver.CallCount + 1;
                using var cancellation = new CancellationTokenSource();
                var task = _presenter.RunAsync(
                    new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 1f),
                    _ => Task.CompletedTask,
                    cancellation.Token);
                cancellation.Cancel();
                await AssertThrowsAsync<OperationCanceledException>(() => task);
            }

            Assert.That(acquireCalls, Is.EqualTo(100));
            Assert.That(disposeCalls, Is.EqualTo(100));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public async Task Exceptions_100Requests_CleansEveryLease()
        {
            var acquireCalls = 0;
            var disposeCalls = 0;
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() =>
            {
                acquireCalls++;
                return new CountingLease(() => disposeCalls++);
            }));

            for (var i = 0; i < 100; i++)
            {
                _driver.ThrowOnCall = _driver.CallCount + 1;
                await AssertThrowsAsync<InvalidOperationException>(() =>
                    _presenter.RunAsync(
                        new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 0f),
                        _ => Task.CompletedTask,
                        CancellationToken.None));
            }

            Assert.That(acquireCalls, Is.EqualTo(100));
            Assert.That(disposeCalls, Is.EqualTo(100));
            Assert.That(_presenter.IsTransitioning, Is.False);
        }

        [Test]
        public void OnDestroy_CancelsDriverAndReleasesLeaseSynchronously()
        {
            var lease = new CountingLease();
            _presenter.ConfigureHooks(new ScreenTransitionHooks(() => lease));
            _driver.BlockCall = 1;
            var task = _presenter.RunAsync(
                new ScreenTransitionRequest(ScreenTransitionStyle.Fade, 1f),
                _ => Task.CompletedTask,
                CancellationToken.None);

            var onDestroy = typeof(ScreenTransitionPresenter).GetMethod(
                "OnDestroy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(onDestroy, Is.Not.Null);
            onDestroy.Invoke(_presenter, null);

            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            Assert.That(_driver.CancelCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(lease.DisposeCalls, Is.EqualTo(1));
            Assert.That(_presenter.IsTransitioning, Is.False);
            Assert.That(ServiceRegistry.ResolveOrNull<IScreenTransitionService>(), Is.Null);

            UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
        }

        private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }

            Assert.Fail($"Expected {typeof(TException).Name}.");
            return null;
        }

        private sealed class CountingLease : IDisposable
        {
            private readonly Action _onDispose;

            public CountingLease(Action onDispose = null)
            {
                _onDispose = onDispose;
            }

            public int DisposeCalls { get; private set; }

            public void Dispose()
            {
                DisposeCalls++;
                _onDispose?.Invoke();
            }
        }

        private sealed class FakeAnimationDriver : IUnscaledAnimationDriver, ICancelableUnscaledAnimationDriver
        {
            private TaskCompletionSource<bool> _release = CreateReleaseSource();

            public int CallCount { get; private set; }
            public int CancelCalls { get; private set; }
            public int BlockCall { get; set; } = -1;
            public int ThrowOnCall { get; set; } = -1;

            public Task AnimateAsync(
                float durationSeconds,
                Action<float> applyProgress,
                CancellationToken cancellationToken)
            {
                CallCount++;
                if (CallCount == ThrowOnCall)
                {
                    throw new InvalidOperationException("synthetic animation failure");
                }

                applyProgress(0f);
                if (CallCount != BlockCall)
                {
                    applyProgress(1f);
                    return Task.CompletedTask;
                }

                return WaitForReleaseAsync(applyProgress, cancellationToken);
            }

            public void Release()
            {
                _release.TrySetResult(true);
            }

            public void Cancel()
            {
                CancelCalls++;
                _release.TrySetCanceled();
            }

            private async Task WaitForReleaseAsync(Action<float> applyProgress, CancellationToken cancellationToken)
            {
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
                var completedTask = await Task.WhenAny(_release.Task, cancellationTask);
                if (completedTask == _release.Task)
                {
                    await _release.Task;
                }

                if (completedTask == cancellationTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                applyProgress(1f);
            }

            private static TaskCompletionSource<bool> CreateReleaseSource()
            {
                return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
