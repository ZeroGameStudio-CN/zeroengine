using System;

namespace ZeroEngine.Timing
{
    public interface ITimeControlService
    {
        event Action<TimeDomainId, float> DomainScaleChanged;
        float GetScale(TimeDomainId domain);
        bool IsFrozen(TimeDomainId domain);
        void SetBaseScale(TimeDomainId domain, float scale, float recoveryDuration = 0f);
        ITimeScaleHandle SetScaleModifier(object token, TimeDomainId domain, float scale, float durationSeconds = 0f, float recoveryDuration = 0f, string reason = null);
        ITimeScaleHandle Freeze(object token, TimeDomainId domain, float durationSeconds = 0f, float recoveryDuration = 0f, string reason = null);
        void Release(object token, TimeDomainId domain, float recoveryDuration = 0f);
        void ResetDomain(TimeDomainId domain);
        void ResetAll();
        void Tick(float unscaledDeltaTime);
    }
}
