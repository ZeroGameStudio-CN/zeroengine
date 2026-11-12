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

        [TestCase("F201", 0, true)]
        [TestCase("F202", 0, true)]
        [TestCase("F204", 0, true)]
        [TestCase("F299", 0, true)]
        [TestCase("F403", 403, false)]
        [TestCase("F413", 413, false)]
        [TestCase("F429", 429, true)]
        [TestCase("F503", 503, true)]
        public void FailureCode_ClassifiesRetryDisposition(
            string code,
            long status,
            bool expectedRetryable)
        {
            Assert.AreEqual(expectedRetryable, FeedbackFailureCodes.IsRetryable(code, status));
        }

        [Test]
        public void ForbiddenTransport_UsesActionableInternalReason()
        {
            Assert.AreEqual(
                "server_forbidden",
                FeedbackFailureCodes.TransportReason(UnityWebRequest.Result.ProtocolError, 403, "Forbidden"));
        }

        [TestCase("SSL certificate has expired", "certificate_validity_error")]
        [TestCase("SSL certificate is not yet valid", "certificate_validity_error")]
        [TestCase("TLS certificate hostname mismatch", "certificate_hostname_mismatch")]
        [TestCase("SSL CA certificate error", "certificate_trust_failed")]
        [TestCase("SSL certificate verify failed", "certificate_trust_failed")]
        [TestCase("TLS handshake failed", "tls_handshake_failed")]
        [TestCase("SSL connect error", "tls_handshake_failed")]
        [TestCase("SSL error", "certificate_tls")]
        public void CertificateReason_PreservesOnlyAnAllowlistedTransportHint(string error, string expected)
        {
            const string privateSuffix = " https://alice:password@private.invalid/player?token=secret\r\ninjected=value";
            Assert.AreEqual("F203", FeedbackFailureCodes.FromTransport(UnityWebRequest.Result.ConnectionError, 0, error + privateSuffix));
            Assert.AreEqual(expected, FeedbackFailureCodes.TransportReason(UnityWebRequest.Result.ConnectionError, 0, error + privateSuffix));
        }

        [TestCase("Curl error 60: SSL CA certificate error https://alice:password@private.invalid", 60)]
        [TestCase("curl error: 35 TLS handshake failed", 35)]
        [TestCase("SSL CA certificate error", 0)]
        [TestCase("Curl error 1234", 0)]
        [TestCase(null, 0)]
        public void NativeError_ExtractsOnlyBoundedCurlNumber(string error, int expected)
        {
            Assert.AreEqual(expected, FeedbackFailureCodes.TransportCurlErrorCode(error));
        }

        [Test]
        public void FailureEnvironment_IsBoundedAndContainsNoRawTransportInput()
        {
            var fields = FeedbackFailureCodes.FailureEnvironment(UnityWebRequest.Result.ConnectionError, 60);
            Assert.AreEqual(8, fields.Count);
            Assert.AreEqual(1, fields["diagnostic_schema"]);
            Assert.AreEqual("ConnectionError", fields["transport_result"]);
            Assert.AreEqual(60, fields["curl_error"]);
            Assert.IsTrue(System.DateTimeOffset.TryParse((string)fields["observed_utc"], out _));
            Assert.AreEqual(UnityEngine.Application.platform.ToString(), fields["platform"]);
            Assert.AreEqual(0, FeedbackFailureCodes.FailureEnvironment(null, 1000)["curl_error"]);
            Assert.AreEqual("not_available", FeedbackFailureCodes.FailureEnvironment(null, -1)["transport_result"]);
            foreach (object value in fields.Values)
            {
                StringAssert.DoesNotContain("://", value.ToString());
                StringAssert.DoesNotContain("\r", value.ToString());
                StringAssert.DoesNotContain("\n", value.ToString());
            }
        }

    }
}
