using System;
using System.Collections;
using ZGS.Analytics;

namespace ZeroEngine.Feedback
{
    public sealed class FeedbackSubmissionController
    {
        private readonly FeedbackUiConfiguration _configuration;

        public bool IsSubmitting { get; private set; }

        public FeedbackSubmissionController(FeedbackUiConfiguration configuration = null)
        {
            _configuration = configuration ?? FeedbackPanel.CurrentConfiguration;
            FeedbackStatusCoordinator.Configure(_configuration);
        }

        public IEnumerator Submit(
            FeedbackFormData data,
            Action<FeedbackSubmissionResult> completed)
        {
            if (IsSubmitting)
            {
                var duplicateResult = new FeedbackSubmissionResult(
                    string.Empty,
                    false,
                    FeedbackSubmissionFailure.InvalidRequest);
                FeedbackStatusCoordinator.HandleLocalResult(duplicateResult);
                completed?.Invoke(duplicateResult);
                yield break;
            }

            IsSubmitting = true;
            FeedbackSubmissionResult result = default;
            bool hasResult = false;
            FeedbackSubmissionRequest request = null;

            try
            {
                request = new FeedbackSubmissionRequest
                {
                    UserMessage = data?.Description,
                    Contact = data?.Contact,
                    FilesToInclude = data?.Attachments ?? Array.Empty<string>()
                };
                _configuration.RequestDecorator?.Decorate(request);
            }
            catch
            {
                result = new FeedbackSubmissionResult(
                    string.Empty,
                    false,
                    FeedbackSubmissionFailure.PackageCreationFailed);
                hasResult = true;
            }

            if (!hasResult)
            {
                IEnumerator submission = FeedbackSubmissionService.Submit(
                    request,
                    submissionResult =>
                    {
                        result = submissionResult;
                        hasResult = true;
                    });
                while (true)
                {
                    bool moved;
                    object current = null;
                    try
                    {
                        moved = submission.MoveNext();
                        if (moved)
                            current = submission.Current;
                    }
                    catch
                    {
                        result = new FeedbackSubmissionResult(
                            string.Empty,
                            false,
                            FeedbackSubmissionFailure.PackageCreationFailed);
                        hasResult = true;
                        break;
                    }

                    if (!moved)
                        break;
                    yield return current;
                }
            }

            IsSubmitting = false;
            if (!hasResult)
            {
                result = new FeedbackSubmissionResult(
                    string.Empty,
                    false,
                    FeedbackSubmissionFailure.PackageCreationFailed);
            }

            FeedbackStatusCoordinator.HandleLocalResult(result);
            completed?.Invoke(result);
        }
    }
}
