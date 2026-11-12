using UnityEngine;

namespace ZeroEngine.Audio.Events
{
    public interface IAudioEventService
    {
        void Post(AudioEventId eventId, Vector3 position = default);
        void Post(AudioEventId eventId, GameObject target);
        void SetParameter(AudioParameterId parameterId, float value);
        void Stop(AudioEventId eventId);
    }
}
