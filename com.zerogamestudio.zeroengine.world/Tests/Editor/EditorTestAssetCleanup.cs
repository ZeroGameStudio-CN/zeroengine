using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.World.Tests.Editor
{
    internal static class EditorTestAssetCleanup
    {
        public static void DeleteAssetFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
            }

            DeletePathIfExists(folderPath);
            DeletePathIfExists(folderPath + ".meta");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ClearPlasticDeleteState(folderPath, recursive: true);
            ClearPlasticDeleteState(folderPath + ".meta", recursive: false);
        }

        private static void DeletePathIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void ClearPlasticDeleteState(string path, bool recursive)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ResolvePlasticExecutable(),
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                startInfo.ArgumentList.Add("undo");
                startInfo.ArgumentList.Add(path.Replace('/', Path.DirectorySeparatorChar));
                if (recursive)
                {
                    startInfo.ArgumentList.Add("-r");
                }

                startInfo.ArgumentList.Add("--silent");

                using Process process = Process.Start(startInfo);
                if (process != null && !process.WaitForExit(10000))
                {
                    process.Kill();
                }
            }
            catch
            {
                // Non-Plastic workspaces do not need version-control cleanup.
            }
        }

        private static string ResolvePlasticExecutable()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidate = Path.Combine(programFiles, "PlasticSCM5", "client", "cm.exe");
            return File.Exists(candidate) ? candidate : "cm";
        }
    }
}
