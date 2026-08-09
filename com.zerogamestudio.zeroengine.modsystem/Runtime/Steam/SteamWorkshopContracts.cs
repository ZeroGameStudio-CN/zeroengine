using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem.Steam
{
    public static class SteamWorkshopReasonCodes
    {
        public const string SteamUnavailable = "steam_unavailable";
        public const string QueryStartFailed = "query_start_failed";
        public const string QueryFailed = "query_failed";
        public const string QueryTimeout = "query_timeout";
        public const string QueryCancelled = "query_cancelled";
        public const string DownloadStartFailed = "download_start_failed";
        public const string CreateStartFailed = "create_start_failed";
        public const string UpdateStartFailed = "update_start_failed";
        public const string SubmitStartFailed = "submit_start_failed";
        public const string PublishFailed = "publish_failed";
        public const string PublishTimeout = "publish_timeout";
        public const string PublishCancelled = "publish_cancelled";
        public const string PublishPolicyMissing = "publish_policy_missing";
        public const string PublishVisibilityDenied = "publish_visibility_denied";
        public const string LegalAgreementRequired = "LEGAL_AGREEMENT_REQUIRED";
        public const string ContentFolderInvalid = "content_folder_invalid";
        public const string TitleInvalid = "title_invalid";
        public const string PageOpenFailed = "page_open_failed";
    }

    public enum WorkshopVisibility
    {
        Public,
        FriendsOnly,
        Private,
        Unlisted
    }

    public enum WorkshopOperationStatus
    {
        Succeeded,
        Failed,
        TimedOut,
        Cancelled
    }

    [Serializable]
    public sealed class WorkshopItemInfo
    {
        public ulong PublishedFileId;
        public string Title;
        public string Description;
        public ulong OwnerId;
        public uint TimeCreated;
        public uint TimeUpdated;
        public ulong FileSize;
        public string InstallPath;
        public bool IsInstalled;
        public bool NeedsUpdate;
        public bool IsDownloading;
    }

    public readonly struct WorkshopQueryResult
    {
        public WorkshopQueryResult(
            WorkshopOperationStatus status,
            IReadOnlyList<WorkshopItemInfo> items,
            string reasonCode)
        {
            Status = status;
            Items = items ?? Array.Empty<WorkshopItemInfo>();
            ReasonCode = reasonCode ?? string.Empty;
        }

        public WorkshopOperationStatus Status { get; }
        public IReadOnlyList<WorkshopItemInfo> Items { get; }
        public string ReasonCode { get; }
        public bool Succeeded => Status == WorkshopOperationStatus.Succeeded;
    }

    public readonly struct WorkshopActionResult
    {
        public WorkshopActionResult(bool succeeded, string reasonCode = "")
        {
            Succeeded = succeeded;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string ReasonCode { get; }
    }

    public readonly struct WorkshopPublishResult
    {
        public WorkshopPublishResult(
            WorkshopOperationStatus status,
            ulong publishedFileId,
            string reasonCode)
        {
            Status = status;
            PublishedFileId = publishedFileId;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public WorkshopOperationStatus Status { get; }
        public ulong PublishedFileId { get; }
        public string ReasonCode { get; }
        public bool Succeeded => Status == WorkshopOperationStatus.Succeeded;
    }

    public sealed class WorkshopPublishRequest
    {
        public ulong PublishedFileId { get; set; }
        public string ContentFolder { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string PreviewImage { get; set; }
        public string[] Tags { get; set; }
        public WorkshopVisibility Visibility { get; set; }
        public string ChangeNote { get; set; }
    }

    public interface IModPublishPolicy
    {
        bool CanPublish(WorkshopVisibility visibility, out string reasonCode);
    }

    public readonly struct SteamWorkshopApiQueryResult
    {
        public SteamWorkshopApiQueryResult(
            bool succeeded,
            IReadOnlyList<WorkshopItemInfo> items,
            string reasonCode)
        {
            Succeeded = succeeded;
            Items = items ?? Array.Empty<WorkshopItemInfo>();
            ReasonCode = reasonCode ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<WorkshopItemInfo> Items { get; }
        public string ReasonCode { get; }
    }

    public readonly struct SteamWorkshopApiPublishResult
    {
        public SteamWorkshopApiPublishResult(
            bool succeeded,
            ulong publishedFileId,
            bool legalAgreementRequired,
            string reasonCode)
        {
            Succeeded = succeeded;
            PublishedFileId = publishedFileId;
            LegalAgreementRequired = legalAgreementRequired;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public bool Succeeded { get; }
        public ulong PublishedFileId { get; }
        public bool LegalAgreementRequired { get; }
        public string ReasonCode { get; }
    }

    public interface ISteamWorkshopApi
    {
        bool IsAvailable { get; }
        uint AppId { get; }

        bool TryStartQuery(
            Action<SteamWorkshopApiQueryResult> onCompleted,
            out IDisposable operation,
            out string reasonCode);

        bool TryStartCreate(
            Action<SteamWorkshopApiPublishResult> onCompleted,
            out IDisposable operation,
            out string reasonCode);

        bool TryStartUpdate(
            WorkshopPublishRequest request,
            Action<SteamWorkshopApiPublishResult> onCompleted,
            out IDisposable operation,
            out string reasonCode);

        bool TryStartDownload(ulong publishedFileId, bool highPriority, out string reasonCode);
        bool TryOpenCatalog(out string reasonCode);
        bool TryOpenItemPage(ulong publishedFileId, out string reasonCode);
        bool TryOpenLegalAgreementPage(out string reasonCode);
    }

    public interface ISteamWorkshopClient : IModCatalogActions
    {
        uint AppId { get; }
        Task<WorkshopQueryResult> QuerySubscribedItemsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken);
        WorkshopActionResult DownloadItem(ulong publishedFileId, bool highPriority = true);
        Task<WorkshopPublishResult> CreateItemAsync(
            WorkshopVisibility visibility,
            TimeSpan timeout,
            CancellationToken cancellationToken);
        Task<WorkshopPublishResult> UpdateItemAsync(
            WorkshopPublishRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken);
        WorkshopActionResult OpenItemPage(ulong publishedFileId);
        WorkshopActionResult OpenLegalAgreementPage();
    }
}
