#if STEAMWORKS_NET && !UNITY_ANDROID
using System;
using System.Collections.Generic;
using Steamworks;

namespace ZeroEngine.ModSystem.Steam
{
    public sealed class SteamworksWorkshopApi : ISteamWorkshopApi
    {
        private readonly Func<bool> availability;
        private readonly uint? configuredAppId;

        public SteamworksWorkshopApi(Func<bool> availability, uint? appId = null)
        {
            this.availability = availability;
            configuredAppId = appId;
        }

        public bool IsAvailable
        {
            get
            {
                try
                {
                    return availability?.Invoke() == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public uint AppId => configuredAppId ?? SteamUtils.GetAppID().m_AppId;

        public bool TryStartQuery(
            Action<SteamWorkshopApiQueryResult> onCompleted,
            out IDisposable operation,
            out string reasonCode)
        {
            operation = null;
            reasonCode = string.Empty;
            if (!IsAvailable)
            {
                reasonCode = SteamWorkshopReasonCodes.SteamUnavailable;
                return false;
            }

            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0)
            {
                operation = EmptyOperation.Instance;
                onCompleted?.Invoke(new SteamWorkshopApiQueryResult(
                    true,
                    Array.Empty<WorkshopItemInfo>(),
                    string.Empty));
                return true;
            }

            var ids = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(ids, count);
            UGCQueryHandle_t queryHandle = SteamUGC.CreateQueryUGCDetailsRequest(ids, count);
            if (queryHandle == UGCQueryHandle_t.Invalid)
            {
                reasonCode = "query_create_failed";
                return false;
            }

            SteamAPICall_t call = SteamUGC.SendQueryUGCRequest(queryHandle);
            if (call == SteamAPICall_t.Invalid)
            {
                SteamUGC.ReleaseQueryUGCRequest(queryHandle);
                reasonCode = SteamWorkshopReasonCodes.QueryStartFailed;
                return false;
            }

            var steamOperation = new SteamCallOperation(queryHandle);
            var callResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, ioFailure) =>
            {
                if (ioFailure || result.m_eResult != EResult.k_EResultOK)
                {
                    onCompleted?.Invoke(new SteamWorkshopApiQueryResult(
                        false,
                        Array.Empty<WorkshopItemInfo>(),
                        result.m_eResult.ToString()));
                    return;
                }

                var items = new List<WorkshopItemInfo>((int)result.m_unNumResultsReturned);
                for (uint index = 0; index < result.m_unNumResultsReturned; index++)
                {
                    if (!SteamUGC.GetQueryUGCResult(result.m_handle, index, out SteamUGCDetails_t details))
                        continue;

                    uint state = SteamUGC.GetItemState(details.m_nPublishedFileId);
                    bool installed = (state & (uint)EItemState.k_EItemStateInstalled) != 0;
                    string installPath = string.Empty;
                    if (installed)
                    {
                        SteamUGC.GetItemInstallInfo(
                            details.m_nPublishedFileId,
                            out _,
                            out installPath,
                            1024,
                            out _);
                    }

                    items.Add(new WorkshopItemInfo
                    {
                        PublishedFileId = details.m_nPublishedFileId.m_PublishedFileId,
                        Title = details.m_rgchTitle,
                        Description = details.m_rgchDescription,
                        OwnerId = details.m_ulSteamIDOwner,
                        TimeCreated = details.m_rtimeCreated,
                        TimeUpdated = details.m_rtimeUpdated,
                        FileSize = details.m_nFileSize < 0 ? 0UL : (ulong)details.m_nFileSize,
                        InstallPath = installPath,
                        IsInstalled = installed,
                        NeedsUpdate = (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0,
                        IsDownloading = (state & (uint)EItemState.k_EItemStateDownloading) != 0
                    });
                }

                onCompleted?.Invoke(new SteamWorkshopApiQueryResult(true, items, string.Empty));
            });
            steamOperation.Attach(callResult);
            callResult.Set(call);
            operation = steamOperation;
            return true;
        }

        public bool TryStartCreate(
            Action<SteamWorkshopApiPublishResult> onCompleted,
            out IDisposable operation,
            out string reasonCode)
        {
            operation = null;
            reasonCode = string.Empty;
            if (!IsAvailable)
            {
                reasonCode = SteamWorkshopReasonCodes.SteamUnavailable;
                return false;
            }

            SteamAPICall_t call = SteamUGC.CreateItem(
                new AppId_t(AppId),
                EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            if (call == SteamAPICall_t.Invalid)
            {
                reasonCode = SteamWorkshopReasonCodes.CreateStartFailed;
                return false;
            }

            var steamOperation = new SteamCallOperation();
            var callResult = CallResult<CreateItemResult_t>.Create((result, ioFailure) =>
            {
                bool succeeded = !ioFailure && result.m_eResult == EResult.k_EResultOK;
                onCompleted?.Invoke(new SteamWorkshopApiPublishResult(
                    succeeded && !result.m_bUserNeedsToAcceptWorkshopLegalAgreement,
                    result.m_nPublishedFileId.m_PublishedFileId,
                    result.m_bUserNeedsToAcceptWorkshopLegalAgreement,
                    succeeded ? string.Empty : result.m_eResult.ToString()));
            });
            steamOperation.Attach(callResult);
            callResult.Set(call);
            operation = steamOperation;
            return true;
        }

        public bool TryStartUpdate(
            WorkshopPublishRequest request,
            Action<SteamWorkshopApiPublishResult> onCompleted,
            out IDisposable operation,
            out string reasonCode)
        {
            operation = null;
            reasonCode = string.Empty;
            if (!IsAvailable)
            {
                reasonCode = SteamWorkshopReasonCodes.SteamUnavailable;
                return false;
            }

            var fileId = new PublishedFileId_t(request.PublishedFileId);
            UGCUpdateHandle_t updateHandle = SteamUGC.StartItemUpdate(new AppId_t(AppId), fileId);
            if (updateHandle == UGCUpdateHandle_t.Invalid)
            {
                reasonCode = SteamWorkshopReasonCodes.UpdateStartFailed;
                return false;
            }
            if (!SteamUGC.SetItemContent(updateHandle, request.ContentFolder))
            {
                reasonCode = "set_content_failed";
                return false;
            }
            if (!SteamUGC.SetItemTitle(updateHandle, request.Title))
            {
                reasonCode = "set_title_failed";
                return false;
            }
            if (!SteamUGC.SetItemDescription(updateHandle, request.Description ?? string.Empty))
            {
                reasonCode = "set_description_failed";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(request.PreviewImage) &&
                !SteamUGC.SetItemPreview(updateHandle, request.PreviewImage))
            {
                reasonCode = "set_preview_failed";
                return false;
            }
            if (request.Tags != null && request.Tags.Length > 0 &&
                !SteamUGC.SetItemTags(updateHandle, request.Tags))
            {
                reasonCode = "set_tags_failed";
                return false;
            }
            if (!SteamUGC.SetItemVisibility(updateHandle, ToSteamVisibility(request.Visibility)))
            {
                reasonCode = "set_visibility_failed";
                return false;
            }

            SteamAPICall_t call = SteamUGC.SubmitItemUpdate(
                updateHandle,
                request.ChangeNote ?? string.Empty);
            if (call == SteamAPICall_t.Invalid)
            {
                reasonCode = SteamWorkshopReasonCodes.SubmitStartFailed;
                return false;
            }

            var steamOperation = new SteamCallOperation();
            var callResult = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
            {
                bool succeeded = !ioFailure && result.m_eResult == EResult.k_EResultOK;
                ulong resultFileId = result.m_nPublishedFileId.m_PublishedFileId != 0
                    ? result.m_nPublishedFileId.m_PublishedFileId
                    : request.PublishedFileId;
                onCompleted?.Invoke(new SteamWorkshopApiPublishResult(
                    succeeded && !result.m_bUserNeedsToAcceptWorkshopLegalAgreement,
                    resultFileId,
                    result.m_bUserNeedsToAcceptWorkshopLegalAgreement,
                    succeeded ? string.Empty : result.m_eResult.ToString()));
            });
            steamOperation.Attach(callResult);
            callResult.Set(call);
            operation = steamOperation;
            return true;
        }

        public bool TryStartDownload(ulong publishedFileId, bool highPriority, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsAvailable)
            {
                reasonCode = SteamWorkshopReasonCodes.SteamUnavailable;
                return false;
            }

            if (SteamUGC.DownloadItem(new PublishedFileId_t(publishedFileId), highPriority))
                return true;
            reasonCode = SteamWorkshopReasonCodes.DownloadStartFailed;
            return false;
        }

        public bool TryOpenCatalog(out string reasonCode)
        {
            return TryOpenUrl($"https://steamcommunity.com/app/{AppId}/workshop/", out reasonCode);
        }

        public bool TryOpenItemPage(ulong publishedFileId, out string reasonCode)
        {
            return TryOpenUrl(
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}",
                out reasonCode);
        }

        public bool TryOpenLegalAgreementPage(out string reasonCode)
        {
            return TryOpenUrl(
                $"https://steamcommunity.com/sharedfiles/workshoplegalagreement?appid={AppId}",
                out reasonCode);
        }

        private bool TryOpenUrl(string url, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsAvailable)
            {
                reasonCode = SteamWorkshopReasonCodes.SteamUnavailable;
                return false;
            }

            SteamFriends.ActivateGameOverlayToWebPage(url);
            return true;
        }

        private static ERemoteStoragePublishedFileVisibility ToSteamVisibility(
            WorkshopVisibility visibility)
        {
            return visibility switch
            {
                WorkshopVisibility.FriendsOnly =>
                    ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
                WorkshopVisibility.Private =>
                    ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                WorkshopVisibility.Unlisted =>
                    ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
                _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic
            };
        }

        private sealed class SteamCallOperation : IDisposable
        {
            private readonly object sync = new();
            private readonly UGCQueryHandle_t queryHandle;
            private IDisposable callResult;
            private bool disposed;

            public SteamCallOperation()
                : this(UGCQueryHandle_t.Invalid)
            {
            }

            public SteamCallOperation(UGCQueryHandle_t queryHandle)
            {
                this.queryHandle = queryHandle;
            }

            public void Attach(IDisposable value)
            {
                bool disposeImmediately;
                lock (sync)
                {
                    disposeImmediately = disposed;
                    if (!disposeImmediately)
                        callResult = value;
                }
                if (disposeImmediately)
                    value?.Dispose();
            }

            public void Dispose()
            {
                IDisposable result;
                lock (sync)
                {
                    if (disposed)
                        return;
                    disposed = true;
                    result = callResult;
                    callResult = null;
                }
                result?.Dispose();
                if (queryHandle != UGCQueryHandle_t.Invalid)
                    SteamUGC.ReleaseQueryUGCRequest(queryHandle);
            }
        }

        private sealed class EmptyOperation : IDisposable
        {
            public static readonly EmptyOperation Instance = new();
            public void Dispose() { }
        }
    }
}
#endif
