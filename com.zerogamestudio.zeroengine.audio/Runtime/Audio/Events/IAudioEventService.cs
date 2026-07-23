using UnityEngine;

namespace ZeroEngine.Audio
{
    public interface IAudioEventService
    {
        void Play(AudioEventId eventId, Vector3 position = default);
        void Stop(AudioEventId eventId);
        void SetParameter(AudioParameterId parameterId, float value);
        void SetState(AudioParameterId stateGroupId, AudioParameterId stateId);
    }
}
