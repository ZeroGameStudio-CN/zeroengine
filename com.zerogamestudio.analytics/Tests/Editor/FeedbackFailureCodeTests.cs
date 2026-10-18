using NUnit.Framework;
using UnityEngine.Networking;

namespace ZGS.Analytics.Tests.Editor
{
    public sealed class FeedbackFailureCodeTests
    {
        // Core contract: classify actual outcomes without depending on a live server or its configuration.
        [TestCase(UnityWebRequest.Result.ConnectionError, 0, "Cannot resolve destination host", "F201")]
        [TestCase(UnityWebRequest.Result.ConnectionError, 0, "Request timeout", "F202")]
        [TestCase(UnityWebRequest.Result.ConnectionError, 0, "Operation timed out", "F202")]
        [TestCase(UnityWebRequest.Result.ConnectionError, 0, "SSL CA certificate error", "F203")]
        [TestCase(UnityWebRequest.Result.DataProcessingError, 200, "invalid response", "F204")]
        [TestCase(UnityWebRequest.Result.ProtocolError, 403, "Forbidden", "F403")]
        [TestCase(UnityWebRequest.Result.ProtocolError, 413, "too large", "F413")]
        [TestCase(UnityWebRequest.Result.ProtocolError, 429, "rate limited", "F429")]
        [TestCase(UnityWebRequest.Result.ProtocolError, 503, "unavailable", "F503")]
        public void FailureCode_PreservesHttpStatusAndDistinguishesTransportCauses(
            UnityWebRequest.Result result, long status, string error, string expected)
        {
            Assert.AreEqual(expected, FeedbackFailureCodes.FromTransport(result, status, error));
        }

        [Test]
        public void FailureDetails_DoNotAcceptLogInjectionOrArbitraryDisplayCodes()
        {
            var failure = new FeedbackUploadFailure("submission", "<b>secret</b>", 403, "id\r\nsecret=value");
            Assert.AreEqual(FeedbackFailureCodes.Unknown, failure.FailureCode);
            Assert.IsEmpty(failure.RequestId);
            Assert.AreEqual("dns_resolution_failed", FeedbackFailureCodes.TransportReason(
                UnityWebRequest.Result.ConnectionError, 0, "Cannot resolve destination host https://user:secret@invalid/player"));
        }

    }
}
