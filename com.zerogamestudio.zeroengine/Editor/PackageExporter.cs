using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace ZeroEngine.Editor
{
    public static class PackageExporter
    {
        public static void Export()
        {
            string fileName = $"ZeroEngine_v{System.DateTime.Now:yyyyMMdd_HHmm}.unitypackage";
            string exportPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), fileName);

            var exportedAssets = new List<string>();

            // 1. Export core ZeroEngine folder
            if (AssetDatabase.IsValidFolder("Assets/ZeroEngine"))
            {
                exportedAssets.Add("Assets/ZeroEngine");
            }
            else
            {
                Debug.LogError("[PackageExporter] Assets/ZeroEngine folder not found!");
                return;
            }

            // 2. Export Configs (Assets/Data) - Optional but good for samples
            if (AssetDatabase.IsValidFolder("Assets/Data"))
            {
                exportedAssets.Add("Assets/Data");
            }

            // 3. Export Plugins?
            // Usually we don't export paid plugins (Odin, EasySave) to avoid license violation distributing public packages.
            // But for internal backup, we can.
            // Let's prompt the user.
            bool includePlugins = EditorUtility.DisplayDialog("Export Options", "Include 'Plugins' folder? (Warning: Check licenses if sharing)", "Yes", "No (Core Only)");
            
            if (includePlugins && AssetDatabase.IsValidFolder("Assets/Plugins"))
            {
                exportedAssets.Add("Assets/Plugins");
            }

            Debug.Log($"[PackageExporter] Exporting {exportedAssets.Count} root folders...");

            AssetDatabase.ExportPackage(exportedAssets.ToArray(), exportPath, ExportPackageOptions.Recurse | ExportPackageOptions.Interactive);
            
            Debug.Log($"[PackageExporter] Export Complete: {exportPath}");
            EditorUtility.RevealInFinder(exportPath);
        }
    }
}
