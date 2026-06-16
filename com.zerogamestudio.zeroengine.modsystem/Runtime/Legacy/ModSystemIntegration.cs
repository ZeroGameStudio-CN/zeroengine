using UnityEngine;

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// ModSystem集成组件。
    /// 添加到场景中自动完成ModSystem的初始化和mod加载。
    /// </summary>
    public class ModSystemIntegration : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        [Tooltip("自定义Mods文件夹路径（留空使用默认路径）")]
        private string _customModsFolder;
        
        [SerializeField]
        [Tooltip("是否在Awake时自动加载所有Mods")]
        private bool _autoLoadOnAwake = true;
        
        [SerializeField]
        [Tooltip("是否启用热重载")]
        private bool _enableHotReload = true;
        
        [SerializeField]
        [Tooltip("是否使用ZeroEngine内置类型注册表")]
        private bool _useBuiltinTypeRegistry = true;
        
        [Header("References (Optional)")]
        [SerializeField]
        private ModHotReloader _hotReloader;
        
        // 事件
        public event System.Action OnModsLoaded;
        public event System.Action<string> OnModLoaded;
        public event System.Action<string> OnModReloaded;
        
        private void Awake()
        {
            Initialize();
            
            if (_autoLoadOnAwake)
            {
                LoadMods();
            }
        }
        
        /// <summary>
        /// 初始化ModSystem
        /// </summary>
        public void Initialize()
        {
            // 创建类型注册表
            ITypeRegistry typeRegistry;
            if (_useBuiltinTypeRegistry)
            {
                typeRegistry = new ZeroEngineTypeRegistry();
            }
            else
            {
                typeRegistry = new DefaultTypeRegistry();
            }
            
            // 创建资源注册表
            var assetRegistry = new DefaultAssetRegistry();
            
            // 初始化ModLoader
            string modsFolder = string.IsNullOrEmpty(_customModsFolder) ? null : _customModsFolder;
            ModLoader.Instance.Initialize(typeRegistry, assetRegistry, modsFolder);
            
            // 绑定事件
            ModLoader.Instance.OnModLoaded += HandleModLoaded;
            ModLoader.Instance.OnModReloaded += HandleModReloaded;
            
            // 设置热重载
            if (_enableHotReload)
            {
                SetupHotReload();
            }
            
            Debug.Log("[ModSystemIntegration] Initialized.");
        }
        
        /// <summary>
        /// 加载所有Mods
        /// </summary>
        public void LoadMods()
        {
            ModLoader.Instance.LoadAllMods();
            OnModsLoaded?.Invoke();
        }
        
        /// <summary>
        /// 设置热重载
        /// </summary>
        private void SetupHotReload()
        {
            if (_hotReloader == null)
            {
                _hotReloader = gameObject.AddComponent<ModHotReloader>();
            }
            
            _hotReloader.EnableHotReload = true;
            _hotReloader.OnModReloaded += HandleModReloaded;
        }
        
        private void HandleModLoaded(string modId)
        {
            OnModLoaded?.Invoke(modId);
        }
        
        private void HandleModReloaded(string modId)
        {
            OnModReloaded?.Invoke(modId);
        }
        
        private void OnDestroy()
        {
            if (ModLoader.Instance != null)
            {
                ModLoader.Instance.OnModLoaded -= HandleModLoaded;
                ModLoader.Instance.OnModReloaded -= HandleModReloaded;
            }
        }
        
        /// <summary>
        /// 获取已加载的Mod资源
        /// </summary>
        public T GetModAsset<T>(string modId, string assetName) where T : Object
        {
            var key = $"{modId}:{assetName}";
            return ModLoader.Instance.AssetRegistry.GetAsset<T>(key);
        }
        
        /// <summary>
        /// 扩展类型注册表（游戏项目可调用此方法注册自定义类型）
        /// </summary>
        public void RegisterCustomTypes(System.Action<ITypeRegistry> registrationAction)
        {
            registrationAction?.Invoke(ModLoader.Instance.TypeRegistry);
        }
    }
}
