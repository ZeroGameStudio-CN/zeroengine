using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZGS.Analytics
{
    public static class FeedbackSubmissionService
    {
        private const string PreviousPlayerLogFileName = "Player-prev.log";
        private static bool _queueEventSubscribed;

        public static event Action<FeedbackUploadCompletion> UploadSucceeded;
        public static event Action<FeedbackUploadFailure> UploadFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            if (_queueEventSubscribed)
            {
                FeedbackUploadQueue.QueuedSubmissionSucceeded -= HandleQueuedSubmissionSucceeded;
                FeedbackUploadQueue.QueuedSubmissionFailed -= HandleQueuedSubmissionFailed;
            }

            _queueEventSubscribed = false;
            UploadSucceeded = null;
            UploadFailed = null;
        }

        public static IEnumerator Submit(
            FeedbackSubmissionRequest request,
            Action<FeedbackSubmissionResult> completed)
        {
            bool callbackInvoked = false;
            string submissionId = string.Empty;

            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.UserMessage))
                {
                    Complete(
                        completed,
                        ref callbackInvoked,
                        new FeedbackSubmissionResult(
                            string.Empty,
                            false,
                            FeedbackSubmissionFailure.InvalidRequest));
                }
                else if (!AnalyticsConfig.IsUploadConfigured)
                {
                    Complete(
                        completed,
                        ref callbackInvoked,
                        new FeedbackSubmissionResult(
                            string.Empty,
                            false,
                            FeedbackSubmissionFailure.NotConfigured));
                }
                else
                {
                    submissionId = Guid.NewGuid().ToString("N");
                    TimelineLogger.TimelineEntry[] entries = request.TimelineEntries ?? TimelineLogger.GetSnapshot();
                    FeedbackTimelineSnapshot timeline = FeedbackTimelineSerializer.Create(entries);
                    var collector = new FeedbackPackageCollector();
                    CollectDefaultAttachments(request, collector);
                    CollectProjectAttachments(request, submissionId, collector);

                    if (!ZipAttachmentUploader.TryCreateFeedbackZip(
                            request,
                            collector,
                            submissionId,
                            timeline.Json,
                            out string zipPath,
                            out string version,
                            out string safeUserName))
                    {
                        Complete(
                            completed,
                            ref callbackInvoked,
                            new FeedbackSubmissionResult(
                                submissionId,
                                false,
                                FeedbackSubmissionFailure.PackageCreationFailed));
                    }
                    else
                    {
                        EnsureQueueEventSubscribed();
                        if (!FeedbackUploadQueue.TryEnqueue(
                                submissionId,
                                zipPath,
                                version,
                                safeUserName))
                        {
                            DeleteIfExists(zipPath);
                            Complete(
                                completed,
                                ref callbackInvoked,
                                new FeedbackSubmissionResult(
                                    submissionId,
                                    false,
                                    FeedbackSubmissionFailure.QueuePersistenceFailed));
                        }
                        else
                        {
                            Complete(
                                completed,
                                ref callbackInvoked,
                                new FeedbackSubmissionResult(
                                    submissionId,
                                    true,
                                    FeedbackSubmissionFailure.None));
                            LogStructuredBugReport(request, submissionId, timeline.StructuredEvents);
                            Debug.Log($"[FeedbackSubmission] queued submission={submissionId}");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                AnalyticsLog.LogWarning(
                    $"[FeedbackSubmission] Package creation failed for {submissionId}: {exception.GetType().Name}");
                Complete(
                    completed,
                    ref callbackInvoked,
                    new FeedbackSubmissionResult(
                        submissionId,
                        false,
                        FeedbackSubmissionFailure.PackageCreationFailed));
            }

            yield break;
        }

        private static void CollectDefaultAttachments(
            FeedbackSubmissionRequest request,
            FeedbackPackageCollector collector)
        {
            AddLogIfPresent(collector, Application.consoleLogPath, "Player.log");
            string logDirectory = Path.GetDirectoryName(Application.consoleLogPath);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                AddLogIfPresent(
                    collector,
                    Path.Combine(logDirectory, PreviousPlayerLogFileName),
                    PreviousPlayerLogFileName);
            }

            if (request.FilesToInclude == null)
                return;

            foreach (string path in request.FilesToInclude)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                bool screenshot = IsImageFile(path);
                collector.AddFile(
                    path,
                    Path.GetFileName(path),
                    screenshot ? FeedbackAttachmentKind.Screenshot : FeedbackAttachmentKind.Generic,
                    screenshot ? FeedbackAttachmentPriority.Screenshot : FeedbackAttachmentPriority.Generic);
            }
        }

        private static void CollectProjectAttachments(
            FeedbackSubmissionRequest request,
            string submissionId,
            FeedbackPackageCollector collector)
        {
            if (request.Contributors == null)
                return;

            var context = new FeedbackPackageContext(submissionId);
            for (int i = 0; i < request.Contributors.Length; i++)
            {
                IFeedbackPackageContributor contributor = request.Contributors[i];
                if (contributor == null)
                    continue;

                try
                {
                    contributor.Collect(context, collector);
                }
                catch (Exception exception)
                {
                    collector.AddMetadata(
                        "contributor_error_" + i,
                        exception.GetType().Name);
                    AnalyticsLog.LogWarning(
                        $"[FeedbackSubmission] Contributor {i} failed: {exception.GetType().Name}");
                }
            }
        }

        private static void AddLogIfPresent(
            FeedbackPackageCollector collector,
            string path,
            string archiveName)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            collector.AddFile(
                path,
                archiveName,
                FeedbackAttachmentKind.Log,
                FeedbackAttachmentPriority.Diagnostic);
        }

        private static void LogStructuredBugReport(
            FeedbackSubmissionRequest request,
            string submissionId,
            List<Dictionary<string, object>> timeline)
        {
            var props = new Dictionary<string, object>
            {
                ["report_type"] = "bug",
                ["error_type"] = "UserReport",
                ["message"] = request.UserMessage ?? string.Empty,
                ["submission_id"] = submissionId,
                ["timeline"] = timeline,
                ["device"] = new Dictionary<string, object>
                {
                    ["platform"] = Application.platform.ToString(),
                    ["os"] = SystemInfo.operatingSystem,
                    ["device_model"] = SystemInfo.deviceModel,
                    ["ram_mb"] = SystemInfo.systemMemorySize,
                    ["gpu"] = SystemInfo.graphicsDeviceName,
                    ["app_version"] = Application.version
                }
            };

            if (!string.IsNullOrWhiteSpace(request.Contact))
                props["contact"] = request.Contact;
            if (!string.IsNullOrWhiteSpace(request.UserName))
                props["user_name"] = request.UserName;

            if (request.ExtraData != null)
            {
                foreach (KeyValuePair<string, object> pair in request.ExtraData)
                {
                    if (!FeedbackTimelineSerializer.IsSensitiveKey(pair.Key))
                        props[pair.Key] = pair.Value;
                }
            }

            bool accepted = AnalyticsService.TryLogEvent(
                "bug_report",
                props,
                new AnalyticsEventOptions("feedback." + submissionId, 0, true));
            if (!accepted)
            {
                AnalyticsLog.LogWarning(
                    $"[FeedbackSubmission] Structured event was not accepted for {submissionId}");
            }
        }

        private static void EnsureQueueEventSubscribed()
        {
            if (_queueEventSubscribed)
                return;

            FeedbackUploadQueue.QueuedSubmissionSucceeded += HandleQueuedSubmissionSucceeded;
            FeedbackUploadQueue.QueuedSubmissionFailed += HandleQueuedSubmissionFailed;
            _queueEventSubscribed = true;
        }

        private static void HandleQueuedSubmissionSucceeded(FeedbackUploadCompletion completion)
        {
            Action<FeedbackUploadCompletion> handlers = UploadSucceeded;
            if (handlers == null)
                return;

            foreach (Action<FeedbackUploadCompletion> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(completion);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[FeedbackSubmission] status_callback_failed code=F299 submission={FeedbackFailureCodes.SafeDiagnosticId(completion.SubmissionId)} stage=success reason={exception.GetType().Name}");
                }
            }
        }

        private static void HandleQueuedSubmissionFailed(FeedbackUploadFailure failure)
        {
            LogUploadFailureDiagnostic(failure);
            Action<FeedbackUploadFailure> handlers = UploadFailed;
            if (handlers == null)
                return;
            foreach (Action<FeedbackUploadFailure> handler in handlers.GetInvocationList())
            {
                try { handler(failure); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[FeedbackSubmission] status_callback_failed code={failure.FailureCode} submission={FeedbackFailureCodes.SafeDiagnosticId(failure.SubmissionId)} stage=failure reason={exception.GetType().Name}");
                }
            }
        }

        private static void LogUploadFailureDiagnostic(FeedbackUploadFailure failure)
        {
            if (string.IsNullOrEmpty(failure.SubmissionId))
                return;

            var properties = new Dictionary<string, object>
            {
                ["submission_id"] = failure.SubmissionId,
                ["failure_code"] = failure.FailureCode,
                ["retryable"] = failure.Retryable,
                ["attempt"] = failure.Attempt,
                ["reason"] = failure.Reason
            };

            if (failure.HttpStatusCode > 0)
                properties["http_status"] = failure.HttpStatusCode;
            if (!string.IsNullOrEmpty(failure.RequestId))
                properties["request_id"] = failure.RequestId;

            bool accepted = AnalyticsService.TryLogEvent(
                "feedback_upload_failure",
                properties,
                new AnalyticsEventOptions(
                    "feedback.upload.failure." + failure.SubmissionId + "." + failure.Attempt,
                    0,
                    true));
            if (!accepted)
            {
                AnalyticsLog.LogWarning(
                    $"[FeedbackSubmission] Upload failure diagnostic was not accepted code={failure.FailureCode} submission={FeedbackFailureCodes.SafeDiagnosticId(failure.SubmissionId)}");
            }
        }

        private static void Complete(
            Action<FeedbackSubmissionResult> completed,
            ref bool callbackInvoked,
            FeedbackSubmissionResult result)
        {
            if (callbackInvoked)
                return;

            callbackInvoked = true;
            if (!result.AcceptedLocally)
                Debug.LogWarning($"[FeedbackSubmission] failed code={result.FailureCode} submission={FeedbackFailureCodes.SafeDiagnosticId(result.SubmissionId)} reason={result.Failure}");
            try
            {
                completed?.Invoke(result);
            }
            catch (Exception exception)
            {
                AnalyticsLog.LogWarning(
                    $"[FeedbackSubmission] Completion callback failed: {exception.GetType().Name}");
            }
        }

        private static bool IsImageFile(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
