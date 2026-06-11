using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

#if STEAMWORKS_NET
using Steamworks;
#endif

namespace ZeroEngine.ModSystem.Steam
{
    /// <summary>
    /// Steam Workshop条目数据
    /// </summary>
    [Serializable]
    public class WorkshopItem
    {
        public ulong PublishedFileId;
        public string Title;
        public string Description;
        public string[] Tags;
        public string LocalPath;
        public bool IsSubscribed;
        public bool NeedsUpdate;
    }
    
    /// <summary>
    /// Steam Workshop集成管理器。
    /// 需要安装Steamworks.NET并添加STEAMWORKS_NET宏定义。
    /// </summary>
    public class SteamWorkshopManager
    {
        private bool _isInitialized = false;
        private List<WorkshopItem> _subscribedItems = new();
        
#pragma warning disable 0067 // Event is never used
        public event Action<WorkshopItem> OnItemDownloaded;
        public event Action<List<WorkshopItem>> OnSubscribedItemsLoaded;
        public event Action<ulong, bool, string> OnItemPublished;
#pragma warning restore 0067
        
        public bool IsInitialized => _isInitialized;
        public IReadOnlyList<WorkshopItem> SubscribedItems => _subscribedItems;
        
#if STEAMWORKS_NET
        private Callback<ItemInstalled_t> _itemInstalledCallback;
        private Callback<DownloadItemResult_t> _downloadResultCallback;
        
        /// <summary>
        /// 初始化Steam Workshop
        /// </summary>
        public bool Initialize()
        {
            if (_isInitialized) return true;
            
            try
            {
                if (!SteamAPI.IsSteamRunning())
                {
                    Debug.LogWarning("[SteamWorkshop] Steam is not running.");
                    return false;
                }
                
                // 注册回调
                _itemInstalledCallback = Callback<ItemInstalled_t>.Create(OnItemInstalled);
                _downloadResultCallback = Callback<DownloadItemResult_t>.Create(OnDownloadResult);
                
                _isInitialized = true;
                Debug.Log("[SteamWorkshop] Initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamWorkshop] Initialization failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 刷新订阅的Workshop条目列表
        /// </summary>
        public void RefreshSubscribedItems()
        {
            if (!_isInitialized) return;
            
            _subscribedItems.Clear();
            
            var numItems = SteamUGC.GetNumSubscribedItems();
            var ids = new PublishedFileId_t[numItems];
            SteamUGC.GetSubscribedItems(ids, numItems);
            
            foreach (var id in ids)
            {
                var item = new WorkshopItem { PublishedFileId = (ulong)id };
                
                // 获取安装信息
                if (SteamUGC.GetItemInstallInfo(id, out ulong sizeOnDisk, out string folder, 1024, out uint timestamp))
                {
                    item.LocalPath = folder;
                    item.IsSubscribed = true;
                }
                
                // 检查是否需要更新
                var state = SteamUGC.GetItemState(id);
                item.NeedsUpdate = (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0;
                
                _subscribedItems.Add(item);
            }
            
            OnSubscribedItemsLoaded?.Invoke(_subscribedItems);
            Debug.Log($"[SteamWorkshop] Found {_subscribedItems.Count} subscribed items.");
        }
        
        /// <summary>
        /// 获取Workshop条目的本地路径
        /// </summary>
        public string GetItemLocalPath(ulong publishedFileId)
        {
            var id = new PublishedFileId_t(publishedFileId);
            if (SteamUGC.GetItemInstallInfo(id, out _, out string folder, 1024, out _))
            {
                return folder;
            }
            return null;
        }
        
        /// <summary>
        /// 下载Workshop条目
        /// </summary>
        public void DownloadItem(ulong publishedFileId, bool highPriority = false)
        {
            if (!_isInitialized) return;
            
            var id = new PublishedFileId_t(publishedFileId);
            SteamUGC.DownloadItem(id, highPriority);
            Debug.Log($"[SteamWorkshop] Started download: {publishedFileId}");
        }
        
        /// <summary>
        /// 发布新的Workshop条目
        /// </summary>
        public void PublishItem(string title, string description, string contentPath, string previewImagePath, string[] tags)
        {
            if (!_isInitialized) return;
            
            var appId = SteamUtils.GetAppID();
            
            SteamAPICall_t createCall = SteamUGC.CreateItem(appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            CallResult<CreateItemResult_t> createResult = CallResult<CreateItemResult_t>.Create();
            createResult.Set(createCall, (result, failure) =>
            {
                if (failure || result.m_eResult != EResult.k_EResultOK)
                {
                    Debug.LogError($"[SteamWorkshop] Failed to create item: {result.m_eResult}");
                    OnItemPublished?.Invoke(0, false, result.m_eResult.ToString());
                    return;
                }
                
                var fileId = result.m_nPublishedFileId;
                UpdateItem((ulong)fileId, title, description, contentPath, previewImagePath, tags);
            });
        }
        
        /// <summary>
        /// 更新现有Workshop条目
        /// </summary>
        public void UpdateItem(ulong publishedFileId, string title, string description, string contentPath, string previewImagePath, string[] tags)
        {
            if (!_isInitialized) return;
            
            var appId = SteamUtils.GetAppID();
            var id = new PublishedFileId_t(publishedFileId);
            
            var handle = SteamUGC.StartItemUpdate(appId, id);
            
            SteamUGC.SetItemTitle(handle, title);
            SteamUGC.SetItemDescription(handle, description);
            SteamUGC.SetItemContent(handle, contentPath);
            
            if (!string.IsNullOrEmpty(previewImagePath))
            {
                SteamUGC.SetItemPreview(handle, previewImagePath);
            }
            
            if (tags != null && tags.Length > 0)
            {
                SteamUGC.SetItemTags(handle, tags);
            }
            
            SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(handle, "Update from ZeroEngine");
            CallResult<SubmitItemUpdateResult_t> submitResult = CallResult<SubmitItemUpdateResult_t>.Create();
            submitResult.Set(submitCall, (result, failure) =>
            {
                bool success = !failure && result.m_eResult == EResult.k_EResultOK;
                OnItemPublished?.Invoke(publishedFileId, success, result.m_eResult.ToString());
                
                if (success)
                {
                    Debug.Log($"[SteamWorkshop] Item published successfully: {publishedFileId}");
                }
                else
                {
                    Debug.LogError($"[SteamWorkshop] Failed to publish item: {result.m_eResult}");
                }
            });
        }
        
        private void OnItemInstalled(ItemInstalled_t callback)
        {
            var item = new WorkshopItem { PublishedFileId = (ulong)callback.m_nPublishedFileId };
            
            var id = callback.m_nPublishedFileId;
            if (SteamUGC.GetItemInstallInfo(id, out _, out string folder, 1024, out _))
            {
                item.LocalPath = folder;
            }
            
            OnItemDownloaded?.Invoke(item);
        }
        
        private void OnDownloadResult(DownloadItemResult_t callback)
        {
            if (callback.m_eResult == EResult.k_EResultOK)
            {
                Debug.Log($"[SteamWorkshop] Download completed: {callback.m_nPublishedFileId}");
            }
            else
            {
                Debug.LogError($"[SteamWorkshop] Download failed: {callback.m_eResult}");
            }
        }
        
        /// <summary>
        /// 需要在Update中调用以处理回调
        /// </summary>
        public void RunCallbacks()
        {
            if (!_isInitialized) return;
            SteamAPI.RunCallbacks();
        }
        
#else
        public bool Initialize()
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed. " +
                           "Install Steamworks.NET and add STEAMWORKS_NET to scripting define symbols.");
            return false;
        }
        
        public void RefreshSubscribedItems()
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed.");
        }
        
        public string GetItemLocalPath(ulong publishedFileId)
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed.");
            return null;
        }
        
        public void DownloadItem(ulong publishedFileId, bool highPriority = false)
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed.");
        }
        
        public void PublishItem(string title, string description, string contentPath, string previewImagePath, string[] tags)
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed.");
        }
        
        public void UpdateItem(ulong publishedFileId, string title, string description, string contentPath, string previewImagePath, string[] tags)
        {
            Debug.LogWarning("[SteamWorkshop] Steamworks.NET is not installed.");
        }
        
        public void RunCallbacks() { }
#endif
    }
}
