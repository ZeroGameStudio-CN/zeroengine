using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
#endif

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// 已加载的Mod信息
    /// </summary>
    public class LoadedMod
    {
        public ModManifest Manifest;
        public DateTime LoadTime;
        public List<UnityEngine.Object> LoadedAssets = new();
        
        /// <summary>
        /// Mod内容文件的最后修改时间（用于热重载检测）
        /// </summary>
        public Dictionary<string, DateTime> FileLastModified = new();
    }
    
    /// <summary>
    /// Mod加载器核心类
    /// </summary>
    public class ModLoader
    {
        private static ModLoader _instance;
        public static ModLoader Instance => _instance ??= new ModLoader();
        
        private readonly Dictionary<string, LoadedMod> _loadedMods = new();
        private ITypeRegistry _typeRegistry;
        private IAssetRegistry _assetRegistry;
        private string _modsFolder;
        
        public IReadOnlyDictionary<string, LoadedMod> LoadedMods => _loadedMods;
        public ITypeRegistry TypeRegistry => _typeRegistry;
        public IAssetRegistry AssetRegistry => _assetRegistry;
        
        public event Action<string> OnModLoaded;
        public event Action<string> OnModUnloaded;
        public event Action<string> OnModReloaded;
        public event Action<string, Exception> OnModLoadError;
        
        /// <summary>
        /// 初始化Mod加载器
        /// </summary>
        /// <param name="typeRegistry">类型注册表（游戏项目提供）</param>
        /// <param name="assetRegistry">资源注册表（游戏项目提供）</param>
        /// <param name="modsFolder">Mods文件夹路径（可选，默认为游戏根目录/Mods）</param>
        public void Initialize(ITypeRegistry typeRegistry, IAssetRegistry assetRegistry, string modsFolder = null)
        {
            _typeRegistry = typeRegistry ?? new DefaultTypeRegistry();
            _assetRegistry = assetRegistry ?? new DefaultAssetRegistry();
            
#if UNITY_EDITOR
            _modsFolder = modsFolder ?? Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, "Mods");
#else
            _modsFolder = modsFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
#endif
            
            EnsureModsFolderExists();
            Debug.Log($"[ModLoader] Initialized. Mods folder: {_modsFolder}");
        }
        
        /// <summary>
        /// 加载所有Mods
        /// </summary>
        public void LoadAllMods()
        {
            if (string.IsNullOrEmpty(_modsFolder))
            {
                Debug.LogError("[ModLoader] Not initialized. Call Initialize() first.");
                return;
            }
            
            var manifests = DiscoverMods();
            var sortedManifests = ResolveDependencies(manifests);
            
            foreach (var manifest in sortedManifests)
            {
                if (!manifest.Enabled)
                {
                    Debug.Log($"[ModLoader] Skipping disabled mod: {manifest.Id}");
                    continue;
                }
                
                try
                {
                    LoadMod(manifest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModLoader] Failed to load mod {manifest.Id}: {ex.Message}");
                    OnModLoadError?.Invoke(manifest.Id, ex);
                }
            }
            
            Debug.Log($"[ModLoader] Loaded {_loadedMods.Count} mods.");
        }
        
        /// <summary>
        /// 加载单个Mod
        /// </summary>
        public void LoadMod(string modPath)
        {
            var manifest = LoadManifest(modPath);
            if (manifest != null)
            {
                LoadMod(manifest);
            }
        }
        
        /// <summary>
        /// 加载Mod（内部）
        /// </summary>
        private void LoadMod(ModManifest manifest)
        {
            if (_loadedMods.ContainsKey(manifest.Id))
            {
                Debug.LogWarning($"[ModLoader] Mod {manifest.Id} is already loaded. Use ReloadMod() to reload.");
                return;
            }
            
            // 检查依赖
            if (manifest.Dependencies != null)
            {
                foreach (var dep in manifest.Dependencies)
                {
                    if (!_loadedMods.ContainsKey(dep))
                    {
                        throw new Exception($"Missing dependency: {dep}");
                    }
                }
            }
            
            // 检查冲突
            if (manifest.Conflicts != null)
            {
                foreach (var conflict in manifest.Conflicts)
                {
                    if (_loadedMods.ContainsKey(conflict))
                    {
                        throw new Exception($"Conflicts with loaded mod: {conflict}");
                    }
                }
            }
            
            var loadedMod = new LoadedMod
            {
                Manifest = manifest,
                LoadTime = DateTime.Now
            };
            
            // 加载内容
            LoadModContent(loadedMod);
            
            _loadedMods[manifest.Id] = loadedMod;
            OnModLoaded?.Invoke(manifest.Id);
            Debug.Log($"[ModLoader] Loaded mod: {manifest}");
        }
        
        /// <summary>
        /// 卸载Mod
        /// </summary>
        public void UnloadMod(string modId)
        {
            if (!_loadedMods.TryGetValue(modId, out var mod))
            {
                Debug.LogWarning($"[ModLoader] Mod {modId} is not loaded.");
                return;
            }
            
            // 检查是否有其他mod依赖此mod
            foreach (var otherMod in _loadedMods.Values)
            {
                if (otherMod.Manifest.Dependencies?.Contains(modId) == true)
                {
                    Debug.LogWarning($"[ModLoader] Cannot unload {modId}: {otherMod.Manifest.Id} depends on it.");
                    return;
                }
            }
            
            // 注销资源
            _assetRegistry.UnregisterAssetsByPrefix($"{modId}:");
            
            _loadedMods.Remove(modId);
            OnModUnloaded?.Invoke(modId);
            Debug.Log($"[ModLoader] Unloaded mod: {modId}");
        }
        
        /// <summary>
        /// 重新加载Mod（用于热重载）
        /// </summary>
        public void ReloadMod(string modId)
        {
            if (!_loadedMods.TryGetValue(modId, out var mod))
            {
                Debug.LogWarning($"[ModLoader] Mod {modId} is not loaded. Cannot reload.");
                return;
            }
            
            var manifest = mod.Manifest;
            var rootPath = manifest.RootPath;
            
            // 重新加载manifest（可能有更新）
            var newManifest = LoadManifest(rootPath);
            if (newManifest == null)
            {
                Debug.LogError($"[ModLoader] Failed to reload manifest for {modId}");
                return;
            }
            
            // 注销旧资源
            _assetRegistry.UnregisterAssetsByPrefix($"{modId}:");
            
            // 创建新的LoadedMod
            var newLoadedMod = new LoadedMod
            {
                Manifest = newManifest,
                LoadTime = DateTime.Now
            };
            
            // 重新加载内容
            LoadModContent(newLoadedMod);
            
            _loadedMods[modId] = newLoadedMod;
            OnModReloaded?.Invoke(modId);
            Debug.Log($"[ModLoader] Reloaded mod: {modId}");
        }
        
        /// <summary>
        /// 检测需要热重载的Mods
        /// </summary>
        public List<string> DetectChangedMods()
        {
            var changedMods = new List<string>();
            
            foreach (var kvp in _loadedMods)
            {
                var mod = kvp.Value;
                foreach (var fileKvp in mod.FileLastModified)
                {
                    if (File.Exists(fileKvp.Key))
                    {
                        var currentModTime = File.GetLastWriteTime(fileKvp.Key);
                        if (currentModTime > fileKvp.Value)
                        {
                            changedMods.Add(kvp.Key);
                            break;
                        }
                    }
                }
            }
            
            return changedMods;
        }
        
        // ============ Private Methods ============
        
        private void EnsureModsFolderExists()
        {
            if (!Directory.Exists(_modsFolder))
            {
                Directory.CreateDirectory(_modsFolder);
                CreateExampleMod();
            }
        }
        
        private void CreateExampleMod()
        {
            var examplePath = Path.Combine(_modsFolder, "ExampleMod");
            if (Directory.Exists(examplePath)) return;
            
            Directory.CreateDirectory(examplePath);
            
            var manifest = new ModManifest
            {
                Id = "example.mod",
                Name = "Example Mod",
                Version = "1.0.0",
                Author = "ZeroEngine",
                Description = "An example mod to demonstrate the mod system.",
                ContentPaths = new[] { "content" }
            };
            
#if NEWTONSOFT_JSON
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
#else
            var json = JsonUtility.ToJson(manifest, true);
#endif
            
            File.WriteAllText(Path.Combine(examplePath, "manifest.json"), json);
            Directory.CreateDirectory(Path.Combine(examplePath, "content"));
            
            Debug.Log($"[ModLoader] Created example mod at: {examplePath}");
        }
        
        private List<ModManifest> DiscoverMods()
        {
            var manifests = new List<ModManifest>();
            
            if (!Directory.Exists(_modsFolder)) return manifests;
            
            foreach (var dir in Directory.GetDirectories(_modsFolder))
            {
                var manifest = LoadManifest(dir);
                if (manifest != null)
                {
                    manifests.Add(manifest);
                }
            }
            
            return manifests;
        }
        
        private ModManifest LoadManifest(string modPath)
        {
            var manifestPath = Path.Combine(modPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[ModLoader] No manifest.json found in: {modPath}");
                return null;
            }
            
            try
            {
                var json = File.ReadAllText(manifestPath);
                
#if NEWTONSOFT_JSON
                var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
#else
                var manifest = JsonUtility.FromJson<ModManifest>(json);
#endif
                
                manifest.RootPath = modPath;
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModLoader] Failed to parse manifest in {modPath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 依赖解析 - 拓扑排序
        /// </summary>
        private List<ModManifest> ResolveDependencies(List<ModManifest> manifests)
        {
            var result = new List<ModManifest>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            var manifestDict = manifests.ToDictionary(m => m.Id);
            
            void Visit(ModManifest manifest)
            {
                if (visited.Contains(manifest.Id)) return;
                
                if (visiting.Contains(manifest.Id))
                {
                    throw new Exception($"Circular dependency detected involving: {manifest.Id}");
                }
                
                visiting.Add(manifest.Id);
                
                if (manifest.Dependencies != null)
                {
                    foreach (var dep in manifest.Dependencies)
                    {
                        if (manifestDict.TryGetValue(dep, out var depManifest))
                        {
                            Visit(depManifest);
                        }
                        else
                        {
                            Debug.LogWarning($"[ModLoader] Mod {manifest.Id} depends on missing mod: {dep}");
                        }
                    }
                }
                
                visiting.Remove(manifest.Id);
                visited.Add(manifest.Id);
                result.Add(manifest);
            }
            
            foreach (var manifest in manifests)
            {
                Visit(manifest);
            }
            
            // 设置加载顺序
            for (int i = 0; i < result.Count; i++)
            {
                result[i].LoadOrder = i;
            }
            
            return result;
        }
        
        /// <summary>
        /// 加载Mod内容（由ModContentParser处理）
        /// </summary>
        private void LoadModContent(LoadedMod mod)
        {
            var contentPaths = mod.Manifest.ContentPaths ?? new[] { "" };
            
            foreach (var contentPath in contentPaths)
            {
                var fullPath = Path.Combine(mod.Manifest.RootPath, contentPath);
                if (!Directory.Exists(fullPath))
                {
                    Debug.LogWarning($"[ModLoader] Content path not found: {fullPath}");
                    continue;
                }
                
                // 记录文件修改时间（用于热重载）
                foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    mod.FileLastModified[file] = File.GetLastWriteTime(file);
                }
                
                // Legacy loader records files for hot reload; projects call ModContentParser through their compatibility flow.
            }
        }
    }
}
