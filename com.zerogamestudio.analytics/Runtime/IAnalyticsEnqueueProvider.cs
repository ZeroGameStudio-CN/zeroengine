using System.Collections.Generic;

namespace ZGS.Analytics
{
    /// <summary>
    /// Optional provider capability for reporting whether an immutable envelope was accepted.
    /// Existing IAnalyticsProvider implementations remain source-compatible without it.
    /// </summary>
    public interface IAnalyticsEnqueueProvider : IAnalyticsProvider
    {
        bool TryLogEvent(
            string eventName,
            Dictionary<string, object> parameters,
            AnalyticsEventOptions options);
    }
}
