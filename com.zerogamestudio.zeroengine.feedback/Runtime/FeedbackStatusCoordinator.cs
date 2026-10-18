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
        private static FeedbackUiConfiguration _configuration = new FeedbackUiConfiguration();
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            if (_subscribed)
            {
                FeedbackSubmissionService.UploadSucceeded -= HandleUploadSucceeded;
                FeedbackSubmissionService.UploadFailed -= HandleUploadFailed;
            }

            _subscribed = false;
            CurrentSubmissions.Clear();
            FailureNotifications.Clear();
            _configuration = new FeedbackUiConfiguration();
        }

        internal static void Configure(FeedbackUiConfiguration configuration)
        {
            _configuration = configuration ?? new FeedbackUiConfiguration();
            EnsureSubscribed();
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
            if (_configuration.ShowUploadingStatus)
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
        }

        internal static void HandleUploadFailed(FeedbackUploadFailure failure)
        {
            if (!_configuration.ShowUploadFailureCodes ||
                string.IsNullOrEmpty(failure.SubmissionId) ||
                !CurrentSubmissions.Contains(failure.SubmissionId) ||
                !FailureNotifications.Add(failure.SubmissionId))
                return;
            Present(FeedbackTextId.UploadFailed, failure.FailureCode);
        }

        internal static int TrackedSubmissionCount => CurrentSubmissions.Count;

        private static void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            FeedbackSubmissionService.UploadSucceeded += HandleUploadSucceeded;
            FeedbackSubmissionService.UploadFailed += HandleUploadFailed;
            _subscribed = true;
        }

        private static void Present(FeedbackTextId status, string failureCode = null)
        {
            bool codedFailure = status == FeedbackTextId.UploadFailed && _configuration.ShowUploadFailureCodes;
            string text = codedFailure ? _configuration.TextResolver?.Resolve(status) : FeedbackTextCatalog.Resolve(status, _configuration.TextResolver);
            if (codedFailure)
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = FeedbackTextCatalog.ResolveShortFailure(Application.systemLanguage);
                text += " (" + (string.IsNullOrEmpty(failureCode) ? FeedbackFailureCodes.Unknown : failureCode) + ")";
            }
            if (_configuration.StatusPresenter != null)
            {
                _configuration.StatusPresenter.Show(status, text);
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
