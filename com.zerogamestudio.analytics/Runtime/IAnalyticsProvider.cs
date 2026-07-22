using System.Collections.Generic;

namespace ZGS.Analytics
{
    public interface IAnalyticsProvider
    {
        void Initialize(string userId);
        void LogEvent(string eventName, Dictionary<string, object> parameters = null);
        void SetUserProperty(string key, object value);
        void TrackScreen(string screenName);
        void LogError(string error, string stackTrace);
    }

    /// <summary>
    /// Optional provider capability for explicitly starting delivery of queued events.
    /// </summary>
    public interface IAnalyticsFlushProvider : IAnalyticsProvider
    {
        void Flush();
    }
}
