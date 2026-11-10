using System;
using System.IO;
using System.Linq;
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
    }
}
