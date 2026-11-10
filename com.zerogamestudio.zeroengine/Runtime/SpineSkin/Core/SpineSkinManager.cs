using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if SPINE_UNITY
using Spine;
using Spine.Unity;
#endif

namespace ZeroEngine.SpineSkin
{
    /// <summary>
    /// Spine皮肤管理器 - 负责皮肤的组合和应用
    /// </summary>
    public class SpineSkinManager : MonoBehaviour
    {
        [SerializeField] private SpineSkinConfig config;
        
#if SPINE_UNITY
        [SerializeField] private SkeletonAnimation skeletonAnimation;
        [SerializeField, Spine.Unity.SpineSkin] private string baseSkinName;
#endif
        
        // 当前装备的皮肤 (SlotId -> FullSkinName)
        private readonly Dictionary<string, string> equippedSkins = new();
        
        // 缓存的组合皮肤
#if SPINE_UNITY
        private Skin combinedSkin;
#endif
        
        /// <summary>
        /// 配置
        /// </summary>
        public SpineSkinConfig Config => config;
        
        /// <summary>
        /// 当前装备的皮肤
        /// </summary>
        public IReadOnlyDictionary<string, string> EquippedSkins => equippedSkins;
        
        /// <summary>
        /// 当前性别
        /// </summary>
        public string CurrentGender { get; private set; }
        
#if SPINE_UNITY
        /// <summary>
        /// Skeleton数据资源
        /// </summary>
        public SkeletonDataAsset SkeletonDataAsset => skeletonAnimation?.SkeletonDataAsset;
        
        /// <summary>
        /// Skeleton对象
        /// </summary>
        public Skeleton Skeleton => skeletonAnimation?.Skeleton;
#endif
        
        /// <summary>
        /// 皮肤变更事件 (SlotId, NewSkinName)
        /// </summary>
        public event Action<string, string> OnSkinChanged;
        
        /// <summary>
        /// 性别变更事件
        /// </summary>
        public event Action<string> OnGenderChanged;
        
        private void Awake()
        {
            Initialize();
        }
        
        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize()
        {
#if SPINE_UNITY
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponent<SkeletonAnimation>();
#endif
            
            if (config != null)
                CurrentGender = config.DefaultGender;
            
            InitializeDefaultSkins();
        }
        
        /// <summary>
        /// 初始化默认皮肤
        /// </summary>
        private void InitializeDefaultSkins()
        {
            if (config?.SkinSlots == null) return;
            
            foreach (var slot in config.SkinSlots)
            {
                if (!string.IsNullOrEmpty(slot.DefaultSkin))
                {
                    equippedSkins[slot.SlotId] = slot.DefaultSkin;
                }
            }
            
            ApplySkins();
        }
        
        /// <summary>
        /// 设置性别
        /// </summary>
        public void SetGender(string gender)
        {
            if (CurrentGender == gender) return;
            
            CurrentGender = gender;
            OnGenderChanged?.Invoke(gender);
            
            // 切换性别后重新应用皮肤
            ApplySkins();
        }
        
        /// <summary>
        /// 装备皮肤
        /// </summary>
        /// <param name="slotId">槽位ID</param>
        /// <param name="skinName">皮肤名称（可以是完整路径或简短名称）</param>
        public void EquipSkin(string slotId, string skinName)
        {
            if (string.IsNullOrEmpty(slotId)) return;
            
            equippedSkins[slotId] = skinName;
            OnSkinChanged?.Invoke(slotId, skinName);
            ApplySkins();
        }
        
        /// <summary>
        /// 卸下皮肤
        /// </summary>
        public void UnequipSkin(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return;
            
            // 检查是否是必须槽位
            var slotConfig = config?.GetSlotConfig(slotId);
            if (slotConfig?.IsRequired == true)
            {
                Debug.LogWarning($"[SpineSkinManager] Slot '{slotId}' is required and cannot be unequipped.");
                return;
            }
            
            if (equippedSkins.Remove(slotId))
            {
                OnSkinChanged?.Invoke(slotId, null);
                ApplySkins();
            }
        }
        
        /// <summary>
        /// 获取槽位当前装备的皮肤
        /// </summary>
        public string GetEquippedSkin(string slotId)
        {
            return equippedSkins.TryGetValue(slotId, out var skin) ? skin : null;
        }
        
        /// <summary>
        /// 批量设置皮肤（用于加载存档）
        /// </summary>
        public void SetAllSkins(string gender, Dictionary<string, string> skins)
        {
            CurrentGender = gender ?? config?.DefaultGender ?? "Female";
            
            equippedSkins.Clear();
            if (skins != null)
            {
                foreach (var kvp in skins)
                    equippedSkins[kvp.Key] = kvp.Value;
            }
            
            ApplySkins();
        }
        
        /// <summary>
        /// 应用所有皮肤到Skeleton
        /// </summary>
        public void ApplySkins()
        {
#if SPINE_UNITY
            if (skeletonAnimation?.Skeleton == null) return;
            
            var skeleton = skeletonAnimation.Skeleton;
            var skeletonData = skeleton.Data;
            
            // 创建新的组合皮肤
            combinedSkin = new Skin("combined");
            
            // 添加基础皮肤
            if (!string.IsNullOrEmpty(baseSkinName))
            {
                var baseSkin = skeletonData.FindSkin(baseSkinName);
                if (baseSkin != null)
                    combinedSkin.AddSkin(baseSkin);
            }
            
            // 添加装备的皮肤
            foreach (var kvp in equippedSkins)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                
                var skin = skeletonData.FindSkin(kvp.Value);
                if (skin != null)
                {
                    combinedSkin.AddSkin(skin);
                }
                else
                {
                    Debug.LogWarning($"[SpineSkinManager] Skin not found: {kvp.Value}");
                }
            }
            
            // 应用组合皮肤
            skeleton.SetSkin(combinedSkin);
            skeleton.SetSlotsToSetupPose();
            skeletonAnimation.AnimationState?.Apply(skeleton);
#endif
        }
        
        /// <summary>
        /// 获取指定槽位的所有可用皮肤
        /// </summary>
        public List<string> GetAvailableSkins(string slotId, string gender = null)
        {
            var result = new List<string>();
            gender ??= CurrentGender;
            
#if SPINE_UNITY
            if (skeletonAnimation?.SkeletonDataAsset == null || config == null) 
                return result;
            
            var skeletonData = skeletonAnimation.SkeletonDataAsset.GetSkeletonData(true);
            if (skeletonData == null) return result;
            
            foreach (var skin in skeletonData.Skins)
            {
                if (config.TryParseSkinName(skin.Name, out var g, out var s, out _))
                {
                    if (s == slotId && (string.IsNullOrEmpty(g) || g == gender))
                    {
                        result.Add(skin.Name);
                    }
                }
            }
#endif
            
            return result;
        }
        
        /// <summary>
        /// 导出当前装备配置
        /// </summary>
        public Dictionary<string, string> ExportEquippedSkins()
        {
            return new Dictionary<string, string>(equippedSkins);
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
#if SPINE_UNITY
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponent<SkeletonAnimation>();
#endif
        }
#endif
    }
}
