using System.Collections.Generic;
using UnityEngine;
using ZGS.Analytics;
using ZeroEngine.UI.Toast;

namespace ZeroEngine.Feedback
{
    internal static class FeedbackStatusCoordinator
    {
        private static readonly HashSet<string> CurrentSubmissions = new();
        private static FeedbackUiConfiguration _configuration = new FeedbackUiConfiguration();
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            if (_subscribed)
                FeedbackSubmissionService.UploadSucceeded -= HandleUploadSucceeded;

            _subscribed = false;
            CurrentSubmissions.Clear();
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
                Present(FeedbackTextId.UploadFailed);
                return;
            }

            if (!string.IsNullOrEmpty(result.SubmissionId))
                CurrentSubmissions.Add(result.SubmissionId);
            Present(FeedbackTextId.Uploading);
        }

        internal static void HandleUploadSucceeded(FeedbackUploadCompletion completion)
        {
            if (string.IsNullOrEmpty(completion.SubmissionId) ||
                !CurrentSubmissions.Remove(completion.SubmissionId))
            {
                return;
            }

            Present(FeedbackTextId.Uploaded);
        }

        internal static int TrackedSubmissionCount => CurrentSubmissions.Count;

        private static void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            FeedbackSubmissionService.UploadSucceeded += HandleUploadSucceeded;
            _subscribed = true;
        }

        private static void Present(FeedbackTextId status)
        {
            string text = FeedbackTextCatalog.Resolve(status, _configuration.TextResolver);
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
