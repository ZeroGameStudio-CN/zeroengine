using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZGS.Analytics
{
    public enum FeedbackSubmissionFailure
    {
        None,
        InvalidRequest,
        NotConfigured,
        PackageCreationFailed,
        QueuePersistenceFailed
    }

    public enum FeedbackAttachmentKind
    {
        Log,
        Screenshot,
        ProjectState,
        Generic
    }

    public enum FeedbackAttachmentPriority
    {
        Diagnostic = 10,
        Screenshot = 20,
        ProjectState = 30,
        Generic = 40
    }

    public sealed class FeedbackSubmissionRequest
    {
        public string UserMessage;
        public string Contact;
        public string UserName;
        public string[] FilesToInclude = Array.Empty<string>();
        public Dictionary<string, object> ExtraData = new Dictionary<string, object>();
        public TimelineLogger.TimelineEntry[] TimelineEntries;
        public IFeedbackPackageContributor[] Contributors = Array.Empty<IFeedbackPackageContributor>();

        internal string LegacyTimelineJson;
        internal string[] LegacyDirectoriesToInclude = Array.Empty<string>();
    }

    public readonly struct FeedbackSubmissionResult
    {
        public string SubmissionId { get; }
        public bool AcceptedLocally { get; }
        public FeedbackSubmissionFailure Failure { get; }

        public FeedbackSubmissionResult(
            string submissionId,
            bool acceptedLocally,
            FeedbackSubmissionFailure failure)
        {
            SubmissionId = submissionId ?? string.Empty;
            AcceptedLocally = acceptedLocally;
            Failure = failure;
        }
    }

    public readonly struct FeedbackUploadCompletion
    {
        public string SubmissionId { get; }

        public FeedbackUploadCompletion(string submissionId)
        {
            SubmissionId = submissionId ?? string.Empty;
        }
    }

    public sealed class FeedbackPackageContext
    {
        public string SubmissionId { get; }
        public string ProductName { get; }
        public string AppVersion { get; }
        public RuntimePlatform Platform { get; }
        public string PersistentDataPath { get; }
        public string TemporaryCachePath { get; }

        internal FeedbackPackageContext(string submissionId)
        {
            SubmissionId = submissionId;
            ProductName = Application.productName;
            AppVersion = Application.version;
            Platform = Application.platform;
            PersistentDataPath = Application.persistentDataPath;
            TemporaryCachePath = Application.temporaryCachePath;
        }
    }

    public interface IFeedbackPackageContributor
    {
        void Collect(FeedbackPackageContext context, FeedbackPackageCollector collector);
    }
}
