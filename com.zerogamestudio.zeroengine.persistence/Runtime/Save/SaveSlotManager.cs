using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;

namespace ZeroEngine.Save
{
    /// <summary>
    /// 存档槽位管理器
    /// 管理多个存档槽位，支持自动存档和截图
    /// </summary>
    public class SaveSlotManager : Singleton<SaveSlotManager>
    {
        #region Constants

        private const string MetaKey = "SlotMeta";
        private const string SystemDataKey = "SystemData";
        private const string ScreenshotKey = "Screenshot";
        private const string MetaListKey = "AllSlotMetas";
        private const string MetaListFile = "SaveMetas.es3";

        #endregion

        #region Configuration

        [SerializeField]
        private SaveSystemConfig _config = new SaveSystemConfig();

        public SaveSystemConfig Config => _config;

        #endregion

        #region State

        /// <summary>已注册的可存档系统</summary>
        private readonly List<ISaveable> _saveables = new List<ISaveable>(8);

        /// <summary>所有槽位元信息缓存</summary>
        private List<SaveSlotMeta> _slotMetas;

        /// <summary>当前加载的槽位索引 (-2 表示未加载)</summary>
        private int _currentSlotIndex = -2;

        /// <summary>游戏开始时间 (用于计算游玩时长)</summary>
        private float _sessionStartTime;

        /// <summary>当前会话累计游戏时长</summary>
        private float _sessionPlayTime;

        /// <summary>截图捕获组件</summary>
        private ScreenshotCapture _screenshotCapture;

        /// <summary>自动存档控制器</summary>
        private AutoSaveController _autoSaveController;

        #endregion

        #region Events

        /// <summary>存档完成事件</summary>
        public event Action<SaveEventArgs> OnSaveCompleted;

        /// <summary>加载完成事件</summary>
        public event Action<SaveEventArgs> OnLoadCompleted;

        /// <summary>存档删除事件</summary>
        public event Action<int> OnSlotDeleted;

        #endregion

        #region Properties

        /// <summary>当前加载的槽位索引</summary>
        public int CurrentSlotIndex => _currentSlotIndex;

        /// <summary>是否已加载存档</summary>
        public bool HasLoadedSave => _currentSlotIndex >= -1;

        /// <summary>当前会话游戏时长</summary>
        public float CurrentPlayTime => _sessionPlayTime + (Time.realtimeSinceStartup - _sessionStartTime);

        /// <summary>最大槽位数</summary>
        public int MaxSlots => _config.MaxSlots;

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();
            _sessionStartTime = Time.realtimeSinceStartup;
            LoadSlotMetas();
        }

        private void Start()
        {
            // 初始化截图捕获
            _screenshotCapture = gameObject.AddComponent<ScreenshotCapture>();
            _screenshotCapture.Initialize(_config.ScreenshotWidth, _config.ScreenshotHeight, _config.ScreenshotQuality);

            // 初始化自动存档
            if (_config.EnableAutoSave)
            {
                _autoSaveController = gameObject.AddComponent<AutoSaveController>();
                _autoSaveController.Initialize(this, _config);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause && _config.EnableAutoSave && (_config.AutoSaveTriggers & AutoSaveTrigger.OnPause) != 0)
            {
                AutoSave();
            }
        }

        protected override void OnApplicationQuit()
        {
            if (ShouldAutoSaveOnQuit())
            {
                AutoSave();
            }

            base.OnApplicationQuit();
        }

        private bool ShouldAutoSaveOnQuit()
        {
            return _config != null
                   && _config.EnableAutoSave
                   && (_config.AutoSaveTriggers & AutoSaveTrigger.OnPause) != 0
                   && FindFirstObjectByType<SaveManager>() != null;
        }

        #endregion

        #region Registration

        /// <summary>
        /// 注册可存档系统
        /// </summary>
        public void Register(ISaveable saveable)
        {
            if (saveable != null && !_saveables.Contains(saveable))
            {
                _saveables.Add(saveable);
                ZeroLog.Info(ZeroLog.Modules.Save, $"Registered saveable: {saveable.SaveKey}");
            }
        }

        /// <summary>
        /// 取消注册
        /// </summary>
        public void Unregister(ISaveable saveable)
        {
            if (saveable != null)
            {
                _saveables.Remove(saveable);
            }
        }

        /// <summary>
        /// 检查系统是否已注册
        /// </summary>
        public bool IsRegistered(ISaveable saveable)
        {
            return saveable != null && _saveables.Contains(saveable);
        }

        #endregion

        #region Slot Management

        /// <summary>
        /// 获取所有槽位元信息
        /// </summary>
        public IReadOnlyList<SaveSlotMeta> GetAllSlotMetas()
        {
            if (_slotMetas == null)
            {
                LoadSlotMetas();
            }
            return _slotMetas;
        }

        /// <summary>
        /// 获取指定槽位元信息
        /// </summary>
        public SaveSlotMeta GetSlotMeta(int slotIndex)
        {
            var metas = GetAllSlotMetas();
            foreach (var meta in metas)
            {
                if (meta.SlotIndex == slotIndex)
                    return meta;
            }
            return null;
        }

        /// <summary>
        /// 检查槽位是否有存档
        /// </summary>
        public bool HasSave(int slotIndex)
        {
            var meta = GetSlotMeta(slotIndex);
            return meta != null && meta.IsValid;
        }

        /// <summary>
        /// 获取槽位截图
        /// </summary>
        public Texture2D GetSlotScreenshot(int slotIndex)
        {
            var fileName = _config.GetSlotFileName(slotIndex);
            var bytes = SaveManager.Instance.Provider.LoadBytes(ScreenshotKey, fileName);

            if (bytes != null && bytes.Length > 0)
            {
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(bytes))
                {
                    return texture;
                }
            }
            return null;
        }

        private void LoadSlotMetas()
        {
            _slotMetas = SaveManager.Instance.Load(MetaListKey, new List<SaveSlotMeta>(), MetaListFile);

            // 确保槽位数量正确
            EnsureSlotMetas();
        }

        private void SaveSlotMetas()
        {
            SaveManager.Instance.Save(MetaListKey, _slotMetas, MetaListFile);
        }

        private void EnsureSlotMetas()
        {
            if (_slotMetas == null)
                _slotMetas = new List<SaveSlotMeta>();

            // 创建缺失的槽位元信息
            var existingSlots = new HashSet<int>();
            foreach (var meta in _slotMetas)
            {
                existingSlots.Add(meta.SlotIndex);
            }

            // 普通槽位
            for (int i = 0; i < _config.MaxSlots; i++)
            {
                if (!existingSlots.Contains(i))
                {
                    _slotMetas.Add(new SaveSlotMeta(i) { IsValid = false });
                }
            }

            // 自动存档槽位
            if (!existingSlots.Contains(-1))
            {
                _slotMetas.Add(new SaveSlotMeta(-1) { IsValid = false });
            }

            // 按槽位索引排序
            _slotMetas.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        }

        #endregion

        #region Save

        /// <summary>
        /// 保存到指定槽位
        /// </summary>
        public void Save(int slotIndex, Action<bool> callback = null)
        {
            SaveInternal(slotIndex, callback);
        }

        /// <summary>
        /// 保存到当前槽位
        /// </summary>
        public void SaveCurrent(Action<bool> callback = null)
        {
            if (_currentSlotIndex < -1)
            {
                ZeroLog.Warning(ZeroLog.Modules.Save, "No slot loaded. Use Save(slotIndex) instead.");
                callback?.Invoke(false);
                return;
            }
            Save(_currentSlotIndex, callback);
        }

        /// <summary>
        /// 自动存档
        /// </summary>
        public void AutoSave(Action<bool> callback = null)
        {
            Save(-1, callback);
        }

        private void SaveInternal(int slotIndex, Action<bool> callback)
        {
            try
            {
                var fileName = _config.GetSlotFileName(slotIndex);
                var provider = SaveManager.Instance.Provider;

                // 1. 收集系统数据
                var systemData = new Dictionary<string, object>();
                foreach (var saveable in _saveables)
                {
                    try
                    {
                        var data = saveable.ExportSaveData();
                        if (data != null)
                        {
                            systemData[saveable.SaveKey] = data;
                        }
                    }
                    catch (Exception e)
                    {
                        ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to export {saveable.SaveKey}: {e.Message}");
                    }
                }

                // 2. 创建元信息
                var meta = GetSlotMeta(slotIndex) ?? new SaveSlotMeta(slotIndex);
                meta.SlotIndex = slotIndex;
                meta.Timestamp = DateTime.Now;
                meta.PlayTimeSeconds = CurrentPlayTime;
                meta.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                meta.IsValid = true;
                meta.SaveVersion = 1;

                // 更新自定义元数据 (玩家名称、等级等由外部设置)
                UpdateMetaFromSystems(meta, systemData);

                // 3. 保存数据
                provider.Save(MetaKey, meta, fileName);
                provider.Save(SystemDataKey, systemData, fileName);

                // 4. 捕获截图
                if (_screenshotCapture != null)
                {
                    _screenshotCapture.CaptureAsync(bytes =>
                    {
                        if (bytes != null)
                        {
                            provider.SaveBytes(ScreenshotKey, bytes, fileName);
                        }
                        FinalizeSave(slotIndex, meta, true, callback);
                    });
                }
                else
                {
                    FinalizeSave(slotIndex, meta, true, callback);
                }
            }
            catch (Exception e)
            {
                ZeroLog.Error(ZeroLog.Modules.Save, $"Save failed for slot {slotIndex}: {e.Message}");
                var meta = new SaveSlotMeta(slotIndex);
                FinalizeSave(slotIndex, meta, false, callback, e.Message);
            }
        }

        private void FinalizeSave(int slotIndex, SaveSlotMeta meta, bool success, Action<bool> callback, string error = null)
        {
            if (success)
            {
                // 更新缓存
                UpdateSlotMeta(slotIndex, meta);
                SaveSlotMetas();

                _currentSlotIndex = slotIndex;
                ZeroLog.Info(ZeroLog.Modules.Save, $"Saved to slot {slotIndex}");
            }

            OnSaveCompleted?.Invoke(new SaveEventArgs(slotIndex, meta, success, error));
            callback?.Invoke(success);
        }

        private void UpdateSlotMeta(int slotIndex, SaveSlotMeta newMeta)
        {
            for (int i = 0; i < _slotMetas.Count; i++)
            {
                if (_slotMetas[i].SlotIndex == slotIndex)
                {
                    _slotMetas[i] = newMeta;
                    return;
                }
            }
            _slotMetas.Add(newMeta);
        }

        private void UpdateMetaFromSystems(SaveSlotMeta meta, Dictionary<string, object> systemData)
        {
            // 这里可以从系统数据中提取玩家信息
            // 具体实现取决于游戏的数据结构
            // 例如：从 PlayerData 中获取 PlayerName 和 PlayerLevel
        }

        #endregion

        #region Load

        /// <summary>
        /// 加载指定槽位
        /// </summary>
        public void Load(int slotIndex, Action<bool> callback = null)
        {
            LoadInternal(slotIndex, callback);
        }

        /// <summary>
        /// 加载自动存档
        /// </summary>
        public void LoadAutoSave(Action<bool> callback = null)
        {
            Load(-1, callback);
        }

        private void LoadInternal(int slotIndex, Action<bool> callback)
        {
            try
            {
                var meta = GetSlotMeta(slotIndex);
                if (meta == null || !meta.IsValid)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Save, $"Slot {slotIndex} has no valid save.");
                    callback?.Invoke(false);
                    return;
                }

                var fileName = _config.GetSlotFileName(slotIndex);
                var provider = SaveManager.Instance.Provider;

                // 1. 加载系统数据
                var systemData = provider.Load<Dictionary<string, object>>(SystemDataKey, null, fileName);
                if (systemData == null)
                {
                    ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to load system data from slot {slotIndex}");
                    callback?.Invoke(false);
                    return;
                }

                // 2. 分发给各系统
                foreach (var saveable in _saveables)
                {
                    try
                    {
                        if (systemData.TryGetValue(saveable.SaveKey, out var data))
                        {
                            saveable.ImportSaveData(data);
                        }
                        else
                        {
                            // 没有数据时重置
                            saveable.ResetToDefault();
                        }
                    }
                    catch (Exception e)
                    {
                        ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to import {saveable.SaveKey}: {e.Message}");
                    }
                }

                // 3. 恢复会话时间
                _sessionPlayTime = meta.PlayTimeSeconds;
                _sessionStartTime = Time.realtimeSinceStartup;
                _currentSlotIndex = slotIndex;

                ZeroLog.Info(ZeroLog.Modules.Save, $"Loaded slot {slotIndex}");
                OnLoadCompleted?.Invoke(new SaveEventArgs(slotIndex, meta, true));
                callback?.Invoke(true);
            }
            catch (Exception e)
            {
                ZeroLog.Error(ZeroLog.Modules.Save, $"Load failed for slot {slotIndex}: {e.Message}");
                OnLoadCompleted?.Invoke(new SaveEventArgs(slotIndex, null, false, e.Message));
                callback?.Invoke(false);
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 删除指定槽位存档
        /// </summary>
        public void Delete(int slotIndex)
        {
            try
            {
                var fileName = _config.GetSlotFileName(slotIndex);
                SaveManager.Instance.Provider.DeleteFile(fileName);

                // 更新元信息
                var meta = GetSlotMeta(slotIndex);
                if (meta != null)
                {
                    meta.IsValid = false;
                    SaveSlotMetas();
                }

                if (_currentSlotIndex == slotIndex)
                {
                    _currentSlotIndex = -2;
                }

                ZeroLog.Info(ZeroLog.Modules.Save, $"Deleted slot {slotIndex}");
                OnSlotDeleted?.Invoke(slotIndex);
            }
            catch (Exception e)
            {
                ZeroLog.Error(ZeroLog.Modules.Save, $"Delete failed for slot {slotIndex}: {e.Message}");
            }
        }

        /// <summary>
        /// 删除所有存档
        /// </summary>
        public void DeleteAll()
        {
            for (int i = 0; i < _config.MaxSlots; i++)
            {
                Delete(i);
            }
            Delete(-1); // 自动存档
        }

        #endregion

        #region New Game

        /// <summary>
        /// 开始新游戏
        /// </summary>
        public void NewGame(int targetSlot = 0)
        {
            // 重置所有系统
            foreach (var saveable in _saveables)
            {
                try
                {
                    saveable.ResetToDefault();
                }
                catch (Exception e)
                {
                    ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to reset {saveable.SaveKey}: {e.Message}");
                }
            }

            // 重置会话时间
            _sessionPlayTime = 0;
            _sessionStartTime = Time.realtimeSinceStartup;
            _currentSlotIndex = targetSlot;

            ZeroLog.Info(ZeroLog.Modules.Save, $"Started new game, target slot: {targetSlot}");
        }

        #endregion

        #region Quick API

        /// <summary>
        /// 快速保存 (保存到当前槽位或槽位 0)
        /// </summary>
        public void QuickSave()
        {
            var slot = _currentSlotIndex >= 0 ? _currentSlotIndex : 0;
            Save(slot);
        }

        /// <summary>
        /// 快速加载 (加载当前槽位或最近的存档)
        /// </summary>
        public void QuickLoad()
        {
            if (_currentSlotIndex >= -1 && HasSave(_currentSlotIndex))
            {
                Load(_currentSlotIndex);
                return;
            }

            // 查找最近的存档
            SaveSlotMeta latestMeta = null;
            foreach (var meta in GetAllSlotMetas())
            {
                if (meta.IsValid)
                {
                    if (latestMeta == null || meta.TimestampTicks > latestMeta.TimestampTicks)
                    {
                        latestMeta = meta;
                    }
                }
            }

            if (latestMeta != null)
            {
                Load(latestMeta.SlotIndex);
            }
            else
            {
                ZeroLog.Warning(ZeroLog.Modules.Save, "No save found for quick load.");
            }
        }

        #endregion

        #region Meta Setters

        /// <summary>
        /// 设置玩家名称 (用于存档显示)
        /// </summary>
        public void SetPlayerName(string name)
        {
            var meta = GetSlotMeta(_currentSlotIndex);
            if (meta != null)
            {
                meta.PlayerName = name;
            }
        }

        /// <summary>
        /// 设置玩家等级 (用于存档显示)
        /// </summary>
        public void SetPlayerLevel(int level)
        {
            var meta = GetSlotMeta(_currentSlotIndex);
            if (meta != null)
            {
                meta.PlayerLevel = level;
            }
        }

        /// <summary>
        /// 设置自定义元数据
        /// </summary>
        public void SetCustomMeta(string key, string value)
        {
            var meta = GetSlotMeta(_currentSlotIndex);
            if (meta != null)
            {
                meta.CustomMeta ??= new Dictionary<string, string>();
                meta.CustomMeta[key] = value;
            }
        }

        #endregion
    }
}
