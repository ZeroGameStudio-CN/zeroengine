using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem.Steam
{
    public sealed class SteamWorkshopClient : ISteamWorkshopClient
    {
        private readonly ISteamWorkshopApi api;
        private readonly IModPublishPolicy publishPolicy;

        public SteamWorkshopClient(ISteamWorkshopApi api, IModPublishPolicy publishPolicy)
        {
            this.api = api;
            this.publishPolicy = publishPolicy;
        }

        public bool IsAvailable => api?.IsAvailable == true;
        public uint AppId => IsAvailable ? api.AppId : 0;

        public Task<WorkshopQueryResult> QuerySubscribedItemsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (!IsAvailable)
            {
                return Task.FromResult(new WorkshopQueryResult(
                    WorkshopOperationStatus.Failed,
                    Array.Empty<WorkshopItemInfo>(),
                    SteamWorkshopReasonCodes.SteamUnavailable));
            }

            return RunQueryAsync(timeout, cancellationToken);
        }

        public WorkshopActionResult DownloadItem(ulong publishedFileId, bool highPriority = true)
        {
            if (!IsAvailable)
                return FailedAction(SteamWorkshopReasonCodes.SteamUnavailable);

            try
            {
                return api.TryStartDownload(publishedFileId, highPriority, out string reasonCode)
                    ? new WorkshopActionResult(true)
                    : FailedAction(DefaultReason(reasonCode, SteamWorkshopReasonCodes.DownloadStartFailed));
            }
            catch
            {
                return FailedAction(SteamWorkshopReasonCodes.DownloadStartFailed);
            }
        }

        public Task<WorkshopPublishResult> CreateItemAsync(
            WorkshopVisibility visibility,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            string policyFailure = GetPolicyFailure(visibility);
            if (!string.IsNullOrEmpty(policyFailure))
                return Task.FromResult(FailedPublish(0, policyFailure));
            if (!IsAvailable)
                return Task.FromResult(FailedPublish(0, SteamWorkshopReasonCodes.SteamUnavailable));

            return RunPublishAsync(
                (Action<SteamWorkshopApiPublishResult> completed, out IDisposable operation, out string reasonCode) =>
                    api.TryStartCreate(completed, out operation, out reasonCode),
                0,
                SteamWorkshopReasonCodes.CreateStartFailed,
                timeout,
                cancellationToken);
        }

        public Task<WorkshopPublishResult> UpdateItemAsync(
            WorkshopPublishRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (request == null)
                return Task.FromResult(FailedPublish(0, SteamWorkshopReasonCodes.UpdateStartFailed));

            string policyFailure = GetPolicyFailure(request.Visibility);
            if (!string.IsNullOrEmpty(policyFailure))
                return Task.FromResult(FailedPublish(request.PublishedFileId, policyFailure));
            if (!IsAvailable)
                return Task.FromResult(FailedPublish(request.PublishedFileId, SteamWorkshopReasonCodes.SteamUnavailable));
            if (string.IsNullOrWhiteSpace(request.ContentFolder) || !Directory.Exists(request.ContentFolder))
                return Task.FromResult(FailedPublish(request.PublishedFileId, SteamWorkshopReasonCodes.ContentFolderInvalid));
            if (string.IsNullOrWhiteSpace(request.Title))
                return Task.FromResult(FailedPublish(request.PublishedFileId, SteamWorkshopReasonCodes.TitleInvalid));

            return RunPublishAsync(
                (Action<SteamWorkshopApiPublishResult> completed, out IDisposable operation, out string reasonCode) =>
                    api.TryStartUpdate(request, completed, out operation, out reasonCode),
                request.PublishedFileId,
                SteamWorkshopReasonCodes.UpdateStartFailed,
                timeout,
                cancellationToken);
        }

        public void OpenCatalog()
        {
            if (api != null)
                TryOpen(api.TryOpenCatalog);
        }

        public WorkshopActionResult OpenItemPage(ulong publishedFileId)
        {
            return TryOpen((out string reasonCode) => api.TryOpenItemPage(publishedFileId, out reasonCode));
        }

        public WorkshopActionResult OpenLegalAgreementPage()
        {
            return api == null
                ? FailedAction(SteamWorkshopReasonCodes.SteamUnavailable)
                : TryOpen(api.TryOpenLegalAgreementPage);
        }

        private async Task<WorkshopQueryResult> RunQueryAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<WorkshopQueryResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable operation = null;
            int terminal = 0;
            using var timeoutCancellation = new CancellationTokenSource();
            if (timeout != Timeout.InfiniteTimeSpan)
                timeoutCancellation.CancelAfter(NormalizeTimeout(timeout));

            void DisposeOperation()
            {
                Interlocked.Exchange(ref operation, null)?.Dispose();
            }

            void Complete(WorkshopQueryResult result)
            {
                if (Interlocked.Exchange(ref terminal, 1) != 0)
                    return;
                DisposeOperation();
                completion.TrySetResult(result);
            }

            using CancellationTokenRegistration callerRegistration = cancellationToken.Register(() =>
                Complete(new WorkshopQueryResult(
                    WorkshopOperationStatus.Cancelled,
                    Array.Empty<WorkshopItemInfo>(),
                    SteamWorkshopReasonCodes.QueryCancelled)));
            using CancellationTokenRegistration timeoutRegistration = timeoutCancellation.Token.Register(() =>
                Complete(new WorkshopQueryResult(
                    WorkshopOperationStatus.TimedOut,
                    Array.Empty<WorkshopItemInfo>(),
                    SteamWorkshopReasonCodes.QueryTimeout)));

            if (Volatile.Read(ref terminal) != 0)
                return await completion.Task;

            try
            {
                bool started = api.TryStartQuery(
                    result => Complete(MapQueryResult(result)),
                    out operation,
                    out string reasonCode);
                if (!started)
                {
                    Complete(new WorkshopQueryResult(
                        WorkshopOperationStatus.Failed,
                        Array.Empty<WorkshopItemInfo>(),
                        DefaultReason(reasonCode, SteamWorkshopReasonCodes.QueryStartFailed)));
                }
                else if (Volatile.Read(ref terminal) != 0)
                {
                    DisposeOperation();
                }
            }
            catch
            {
                Complete(new WorkshopQueryResult(
                    WorkshopOperationStatus.Failed,
                    Array.Empty<WorkshopItemInfo>(),
                    SteamWorkshopReasonCodes.QueryStartFailed));
            }

            return await completion.Task;
        }

        private async Task<WorkshopPublishResult> RunPublishAsync(
            TryStartPublishOperation tryStart,
            ulong fallbackPublishedFileId,
            string startFailureReason,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<WorkshopPublishResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable operation = null;
            int terminal = 0;
            using var timeoutCancellation = new CancellationTokenSource();
            if (timeout != Timeout.InfiniteTimeSpan)
                timeoutCancellation.CancelAfter(NormalizeTimeout(timeout));

            void DisposeOperation()
            {
                Interlocked.Exchange(ref operation, null)?.Dispose();
            }

            void Complete(WorkshopPublishResult result)
            {
                if (Interlocked.Exchange(ref terminal, 1) != 0)
                    return;
                DisposeOperation();
                completion.TrySetResult(result);
            }

            using CancellationTokenRegistration callerRegistration = cancellationToken.Register(() =>
                Complete(new WorkshopPublishResult(
                    WorkshopOperationStatus.Cancelled,
                    fallbackPublishedFileId,
                    SteamWorkshopReasonCodes.PublishCancelled)));
            using CancellationTokenRegistration timeoutRegistration = timeoutCancellation.Token.Register(() =>
                Complete(new WorkshopPublishResult(
                    WorkshopOperationStatus.TimedOut,
                    fallbackPublishedFileId,
                    SteamWorkshopReasonCodes.PublishTimeout)));

            if (Volatile.Read(ref terminal) != 0)
                return await completion.Task;

            try
            {
                bool started = tryStart(
                    result => Complete(MapPublishResult(result, fallbackPublishedFileId)),
                    out operation,
                    out string reasonCode);
                if (!started)
                {
                    Complete(FailedPublish(
                        fallbackPublishedFileId,
                        DefaultReason(reasonCode, startFailureReason)));
                }
                else if (Volatile.Read(ref terminal) != 0)
                {
                    DisposeOperation();
                }
            }
            catch
            {
                Complete(FailedPublish(fallbackPublishedFileId, startFailureReason));
            }

            return await completion.Task;
        }

        private string GetPolicyFailure(WorkshopVisibility visibility)
        {
            if (publishPolicy == null)
                return SteamWorkshopReasonCodes.PublishPolicyMissing;

            try
            {
                if (publishPolicy.CanPublish(visibility, out string reasonCode))
                    return string.Empty;
                return DefaultReason(reasonCode, SteamWorkshopReasonCodes.PublishVisibilityDenied);
            }
            catch
            {
                return SteamWorkshopReasonCodes.PublishVisibilityDenied;
            }
        }

        private WorkshopActionResult TryOpen(TryOpenPage tryOpen)
        {
            if (!IsAvailable || tryOpen == null)
                return FailedAction(SteamWorkshopReasonCodes.SteamUnavailable);

            try
            {
                return tryOpen(out string reasonCode)
                    ? new WorkshopActionResult(true)
                    : FailedAction(DefaultReason(reasonCode, SteamWorkshopReasonCodes.PageOpenFailed));
            }
            catch
            {
                return FailedAction(SteamWorkshopReasonCodes.PageOpenFailed);
            }
        }

        private static WorkshopQueryResult MapQueryResult(SteamWorkshopApiQueryResult result)
        {
            return result.Succeeded
                ? new WorkshopQueryResult(WorkshopOperationStatus.Succeeded, result.Items, string.Empty)
                : new WorkshopQueryResult(
                    WorkshopOperationStatus.Failed,
                    Array.Empty<WorkshopItemInfo>(),
                    DefaultReason(result.ReasonCode, SteamWorkshopReasonCodes.QueryFailed));
        }

        private static WorkshopPublishResult MapPublishResult(
            SteamWorkshopApiPublishResult result,
            ulong fallbackPublishedFileId)
        {
            ulong fileId = result.PublishedFileId != 0
                ? result.PublishedFileId
                : fallbackPublishedFileId;
            if (result.LegalAgreementRequired)
            {
                return FailedPublish(fileId, SteamWorkshopReasonCodes.LegalAgreementRequired);
            }

            return result.Succeeded
                ? new WorkshopPublishResult(WorkshopOperationStatus.Succeeded, fileId, string.Empty)
                : FailedPublish(
                    fileId,
                    DefaultReason(result.ReasonCode, SteamWorkshopReasonCodes.PublishFailed));
        }

        private static TimeSpan NormalizeTimeout(TimeSpan timeout)
        {
            return timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeout;
        }

        private static string DefaultReason(string reasonCode, string fallback)
        {
            return string.IsNullOrWhiteSpace(reasonCode) ? fallback : reasonCode;
        }

        private static WorkshopActionResult FailedAction(string reasonCode)
        {
            return new WorkshopActionResult(false, reasonCode);
        }

        private static WorkshopPublishResult FailedPublish(ulong fileId, string reasonCode)
        {
            return new WorkshopPublishResult(WorkshopOperationStatus.Failed, fileId, reasonCode);
        }

        private delegate bool TryStartPublishOperation(
            Action<SteamWorkshopApiPublishResult> onCompleted,
            out IDisposable operation,
            out string reasonCode);

        private delegate bool TryOpenPage(out string reasonCode);
    }
}
