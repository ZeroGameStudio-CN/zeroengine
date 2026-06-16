using System;

namespace ZeroEngine.Timing
{
    public interface ITimeScaleHandle : IDisposable
    {
        bool IsActive { get; }
        TimeDomainId Domain { get; }
        object Token { get; }
        string Reason { get; }
        void Release(float recoveryDuration = 0f);
    }
}
