using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Console.Quantum.Editor
{
    public static class QuantumProfileVerifier
    {
        // Explicit authoring tool: returns an owned profile and writes no asset or global configuration.
        public static QuantumCompatibilityProfile VerifyPrefab(GameObject prefab)
        {
            if (!prefab) throw new ArgumentNullException(nameof(prefab));
            var console = prefab.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(component => component && component.GetType().FullName == "QFSW.QC.QuantumConsole");
            if (!console) throw new InvalidOperationException("No Quantum Console component exists on the supplied prefab.");
            var script = MonoScript.FromMonoBehaviour(console);
            string path = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new InvalidOperationException("Quantum source cannot be verified in this installation.");
            string fingerprint;
            using (var hash = SHA256.Create()) fingerprint = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            if (fingerprint != QuantumCompatibilityProfile.SupportedConsoleSourceHash)
                throw new NotSupportedException("Quantum source differs from the verified 2.6.7 profile; use Native until this SDK is checked.");
            var profile = ScriptableObject.CreateInstance<QuantumCompatibilityProfile>();
            profile.name = "Verified Quantum " + QuantumCompatibilityProfile.SupportedSdkVersion;
            profile.SetVerifiedIdentity(QuantumCompatibilityProfile.SupportedSdkVersion, fingerprint, console.GetType());
            return profile;
        }

        // Call after PlayerBuildInterface.CompilePlayerScripts for the same build settings.
        // This reads public PE metadata through Unity's bundled Cecil; the vendor assembly
        // is never loaded into the Editor AppDomain or executed by this operation.
        public static QuantumCompatibilityProfile VerifyPlayerAssembly(GameObject prefab, string assemblyPath,
            RuntimePlatform platform, string backend)
        {
            var profile = VerifyPrefab(prefab);
            try
            {
                if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath) ||
                    Path.GetFileName(assemblyPath) != "QFSW.QC.dll")
                    throw new ArgumentException("Compiled QFSW.QC.dll is required.", nameof(assemblyPath));
                var moduleType = Type.GetType("Mono.Cecil.ModuleDefinition, Unity.Cecil", false);
                var read = moduleType?.GetMethod("ReadModule", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (read == null) throw new NotSupportedException("Unity's public assembly metadata reader is unavailable.");
                object module = read.Invoke(null, new object[] { Path.GetFullPath(assemblyPath) });
                try
                {
                    string id = ((Guid)moduleType.GetProperty("Mvid").GetValue(module)).ToString("N");
                    object assembly = moduleType.GetProperty("Assembly").GetValue(module);
                    object name = assembly.GetType().GetProperty("Name").GetValue(assembly);
                    string identity = (string)name.GetType().GetProperty("FullName").GetValue(name);
                    string digest;
                    using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(assemblyPath))).Replace("-", "").ToLowerInvariant();
                    profile.SetVerifiedPlayerIdentity(profile.SdkVersion, profile.SourceFingerprint, identity,
                        id, digest, Application.unityVersion, platform, backend);
                    profile.name += " " + platform + " " + backend;
                    return profile;
                }
                finally { (module as IDisposable)?.Dispose(); }
            }
            catch { UnityEngine.Object.DestroyImmediate(profile); throw; }
        }
    }
}
