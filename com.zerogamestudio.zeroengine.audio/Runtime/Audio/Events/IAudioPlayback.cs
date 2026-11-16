using System;

namespace ZeroEngine.Audio
{
    /// <summary>An owned playback, independent of the backend's pooled objects or event aliases.</summary>
    public interface IAudioPlayback : IDisposable
    {
        /// <summary>Whether this playback still owns the backend resource, including a pending transition.</summary>
        bool IsCurrent { get; }
        bool IsLooping { get; }
        /// <summary>Stops only this playback. Repeated or stale calls have no effect.</summary>
        bool TryStop();
    }
}
