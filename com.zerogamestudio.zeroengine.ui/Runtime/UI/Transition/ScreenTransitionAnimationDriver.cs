using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.UI
{
    /// <summary>
    /// Internal seam for deterministic package tests. Production animation is
    /// driven by unscaled Unity time and yields through the task scheduler.
    /// </summary>
    internal interface IUnscaledAnimationDriver
    {
        Task AnimateAsync(
            float durationSeconds,
            Action<float> applyProgress,
            CancellationToken cancellationToken);
    }

    internal interface ICancelableUnscaledAnimationDriver
    {
        void Cancel();
    }

    internal sealed class UnityUnscaledAnimationDriver :
        IUnscaledAnimationDriver,
        ICancelableUnscaledAnimationDriver
    {
        private readonly Func<bool> _isAlive;
        private int _generation;

        public UnityUnscaledAnimationDriver(Func<bool> isAlive)
        {
            _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
        }

        public async Task AnimateAsync(
            float durationSeconds,
            Action<float> applyProgress,
            CancellationToken cancellationToken)
        {
            if (applyProgress == null)
            {
                throw new ArgumentNullException(nameof(applyProgress));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var generation = _generation;
            if (durationSeconds <= 0f)
            {
                EnsureAlive(cancellationToken, generation);
                applyProgress(1f);
                return;
            }

            var startTime = Time.unscaledTime;
            while (true)
            {
                EnsureAlive(cancellationToken, generation);
                var progress = Mathf.Clamp01((Time.unscaledTime - startTime) / durationSeconds);
                applyProgress(progress);
                if (progress >= 1f)
                {
                    return;
                }

                await Task.Yield();
            }
        }

        public void Cancel()
        {
            unchecked
            {
                _generation++;
            }
        }

        private void EnsureAlive(CancellationToken cancellationToken, int generation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _generation || !_isAlive())
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}
