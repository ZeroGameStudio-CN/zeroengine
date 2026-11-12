using System.Collections.Generic;
using UnityEngine;
using ZGS.Analytics;
using ZeroEngine.UI.Toast;

namespace ZeroEngine.Feedback
{
    internal static class FeedbackStatusCoordinator
    {
        private static readonly HashSet<string> CurrentSubmissions = new();
        private static readonly HashSet<string> FailureNotifications = new();
        private static readonly HashSet<string> RetryNotifications = new();
        private static FeedbackUiConfiguration _configuration = new FeedbackUiConfiguration();
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            if (_subscribed)
            {
                FeedbackSubmissionService.UploadSucceeded -= HandleUploadSucceeded;
                FeedbackSubmissionService.UploadFailed -= HandleUploadFailed;
                FeedbackSubmissionService.StatusChanged -= HandleDeliveryState;
            }

            _subscribed = false;
            CurrentSubmissions.Clear();
            FailureNotifications.Clear();
            RetryNotifications.Clear();
            _configuration = new FeedbackUiConfiguration();
        }

        internal static void Configure(FeedbackUiConfiguration configuration)
        {
            _configuration = configuration ?? new FeedbackUiConfiguration();
            EnsureSubscribed();
            if (_configuration.UseDeliveryStatus)
            {
                bool restoredRetry = false;
                foreach (FeedbackSubmissionStatus item in FeedbackSubmissionService.GetRecentSubmissions())
                {
                    if (item.State == FeedbackDeliveryState.Received || item.State == FeedbackDeliveryState.Stopped)
                        continue;
                    CurrentSubmissions.Add(item.SubmissionId);
                    if (item.AutomaticRetryScheduled && RetryNotifications.Add(item.SubmissionId))
                        restoredRetry = true;
                }
                if (restoredRetry) Present(FeedbackTextId.Retrying);
            }
        }

        internal static void HandleSubmitting()
        {
            if (_configuration.UseDeliveryStatus && _configuration.ShowUploadingStatus)
                Present(FeedbackTextId.Uploading);
        }

        internal static void HandleLocalResult(FeedbackSubmissionResult result)
        {
            EnsureSubscribed();
            if (!result.AcceptedLocally)
            {
                Present(FeedbackTextId.UploadFailed, result.FailureCode);
                return;
            }

            if (!string.IsNullOrEmpty(result.SubmissionId))
                CurrentSubmissions.Add(result.SubmissionId);
            if (_configuration.ShowUploadingStatus && !_configuration.UseDeliveryStatus)
                Present(FeedbackTextId.Uploading);
        }

        internal static void HandleUploadSucceeded(FeedbackUploadCompletion completion)
        {
            if (string.IsNullOrEmpty(completion.SubmissionId) ||
                !CurrentSubmissions.Remove(completion.SubmissionId))
            {
                return;
            }

            FailureNotifications.Remove(completion.SubmissionId);
            Present(FeedbackTextId.Uploaded);
            if (_configuration.UseDeliveryStatus)
                FeedbackSubmissionService.MarkSuccessNotified(completion.SubmissionId);
        }

        internal static void HandleUploadFailed(FeedbackUploadFailure failure)
        {
            // A retryable attempt is still pending in the durable queue, not a failed submission.
            if (_configuration.UseDeliveryStatus || failure.Retryable || !_configuration.ShowUploadFailureCodes ||
                string.IsNullOrEmpty(failure.SubmissionId) ||
                !CurrentSubmissions.Contains(failure.SubmissionId) ||
                !FailureNotifications.Add(failure.SubmissionId))
                return;
            Present(FeedbackTextId.UploadFailed, failure.FailureCode);
        }

        internal static void HandleDeliveryState(FeedbackSubmissionStatus status)
        {
            if (!_configuration.UseDeliveryStatus || !CurrentSubmissions.Contains(status.SubmissionId)) return;
            if (status.State == FeedbackDeliveryState.Received)
            {
                if (status.SuccessNotified) CurrentSubmissions.Remove(status.SubmissionId);
                else HandleUploadSucceeded(new FeedbackUploadCompletion(status.SubmissionId));
            }
            else if (status.State == FeedbackDeliveryState.Failed)
            {
                if (FailureNotifications.Add(status.SubmissionId))
                    Present(FeedbackTextId.UploadFailed, status.FailureCode);
            }
            else if (status.AutomaticRetryScheduled && status.State != FeedbackDeliveryState.Uploading &&
                     status.State != FeedbackDeliveryState.Queued)
            {
                FailureNotifications.Remove(status.SubmissionId);
                if (RetryNotifications.Add(status.SubmissionId)) Present(FeedbackTextId.Retrying);
            }
        }

        internal static int TrackedSubmissionCount => CurrentSubmissions.Count;

        private static void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            FeedbackSubmissionService.UploadSucceeded += HandleUploadSucceeded;
            FeedbackSubmissionService.UploadFailed += HandleUploadFailed;
            FeedbackSubmissionService.StatusChanged += HandleDeliveryState;
            _subscribed = true;
        }

        private static void Present(FeedbackTextId status, string failureCode = null)
        {
            bool codedFailure = status == FeedbackTextId.UploadFailed && _configuration.ShowUploadFailureCodes;
            string text = codedFailure ? _configuration.TextResolver?.Resolve(status) : FeedbackTextCatalog.Resolve(status, _configuration.TextResolver);
            if (_configuration.UseDeliveryStatus)
            {
                string resolved = _configuration.TextResolver?.Resolve(status);
                text = !string.IsNullOrWhiteSpace(resolved) ? resolved : FeedbackTextCatalog.ResolveSubmissionStatus(
                    status, _configuration.LanguageCodeResolver?.Invoke() ?? System.Globalization.CultureInfo.CurrentUICulture.Name);
            }
            if (codedFailure)
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = FeedbackTextCatalog.ResolveShortFailure(Application.systemLanguage);
                string code = string.IsNullOrEmpty(failureCode) ? FeedbackFailureCodes.Unknown : failureCode;
                text += text == "提交失败" || text == "提交失敗" ? "（" + code + "）" : " (" + code + ")";
            }
            if (_configuration.StatusPresenter != null)
            {
                _configuration.StatusPresenter.Show(status, text);
                return;
            }

            if (_configuration.UseDeliveryStatus)
            {
                ToastSeverity severity = status == FeedbackTextId.Uploaded ? ToastSeverity.Success :
                    status == FeedbackTextId.UploadFailed ? ToastSeverity.Error : ToastSeverity.Info;
                Toast.Show(ToastRequest.Text(text).WithGroup("feedback-submission")
                    .WithDedupe("feedback-submission").WithSeverity(severity));
                return;
            }

            switch (status)
            {
                case FeedbackTextId.Uploaded:
                    Toast.Success(text);
                    break;
                case FeedbackTextId.UploadFailed:
                    Toast.Error(text);
                    break;
                default:
                    Toast.Show(text);
                    break;
            }
        }
    }
}
