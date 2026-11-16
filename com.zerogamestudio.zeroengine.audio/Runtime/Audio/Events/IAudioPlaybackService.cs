using UnityEngine;

namespace ZeroEngine.Audio
{
    /// <summary>Optional instance ownership beside the existing event-level audio facade.</summary>
    public interface IAudioPlaybackService
    {
        /// <summary>Starts a voice or music transition; returns null when the request is not accepted.</summary>
        IAudioPlayback PlayScoped(AudioEventId eventId, Vector3 position = default);
    }
}
