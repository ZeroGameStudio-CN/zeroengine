using System;
using System.IO;
using UnityEditor.PackageManager;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    internal static class AbilityEditorTestPaths
    {
        public static string PackageFile(string relativePath)
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(AbilityDefinition).Assembly);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                throw new InvalidOperationException("Unable to resolve ZeroEngine.Combat package path.");
            }

            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(packageInfo.resolvedPath, normalized);
        }
    }
}
