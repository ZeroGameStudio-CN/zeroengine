using System;
using System.Reflection;
using UnityEngine;

namespace ZeroEngine.Console.Quantum
{
    public sealed class QuantumCompatibilityProfile : ScriptableObject
    {
        [SerializeField] private string sdkVersion;
        [SerializeField] private string sourceFingerprint;
        [SerializeField] private string assemblyIdentity;
        [SerializeField] private string moduleIdentity;
        [SerializeField] private bool playerBuild;
        [SerializeField] private string playerUnityVersion;
        [SerializeField] private string playerBackend;
        [SerializeField] private RuntimePlatform playerPlatform;
        [SerializeField] private string playerAssemblySha256;
        public string SdkVersion => sdkVersion;
        public string SourceFingerprint => sourceFingerprint;
        public bool IsPlayerProfile => playerBuild;
        public string PlayerAssemblySha256 => playerAssemblySha256;
        public string PlayerBackend => playerBackend;
        public string AssemblyIdentity => assemblyIdentity;
        public string ModuleIdentity => moduleIdentity;
        public const string SupportedSdkVersion = "2.6.7";
        public const string SupportedConsoleSourceHash = "7bffb3987abc0fd3eb914d499a98f135514657b8fc95c8700df89cc435132227";

        public bool Matches(Type consoleType, out string reason)
        {
            if (sdkVersion != SupportedSdkVersion || sourceFingerprint != SupportedConsoleSourceHash)
            { reason = "Quantum SDK source profile is not verified."; return false; }
            if (consoleType == null || consoleType.FullName != "QFSW.QC.QuantumConsole")
            { reason = "Quantum Console component is unavailable."; return false; }
            if (playerBuild)
            {
                if (Application.isEditor || Application.platform != playerPlatform || Application.unityVersion != playerUnityVersion)
                { reason = "Quantum Player profile does not match this Unity runtime or platform."; return false; }
#if ENABLE_IL2CPP
                const string backend = "il2cpp";
#else
                const string backend = "mono";
#endif
                if (playerBackend != backend || !IsDigest(playerAssemblySha256))
                { reason = "Quantum Player profile does not match this scripting backend."; return false; }
            }
            else if (!Application.isEditor)
            { reason = "Quantum Editor profile requires a separately verified Player build profile."; return false; }
            try
            {
                if (consoleType.Assembly.FullName != assemblyIdentity || consoleType.Module.ModuleVersionId.ToString("N") != moduleIdentity)
                { reason = "Quantum assembly changed; verify its compatibility profile again."; return false; }
            }
            catch (NotSupportedException) { reason = "This runtime needs a separately verified Quantum build profile."; return false; }
            reason = string.Empty;
            return true;
        }

        // Explicit setup/build tooling owns this stamp; no domain-reload file writer is installed.
        public void SetVerifiedIdentity(string version, string fingerprint, Type consoleType)
        {
            if (version != SupportedSdkVersion || fingerprint != SupportedConsoleSourceHash)
                throw new NotSupportedException("This SDK source has not passed the Quantum compatibility profile.");
            if (consoleType == null || consoleType.FullName != "QFSW.QC.QuantumConsole") throw new ArgumentException("Quantum Console type required.", nameof(consoleType));
            sdkVersion = version;
            sourceFingerprint = fingerprint;
            assemblyIdentity = consoleType.Assembly.FullName;
            moduleIdentity = consoleType.Module.ModuleVersionId.ToString("N");
            playerBuild = false;
            playerUnityVersion = playerBackend = playerAssemblySha256 = string.Empty;
        }

        // The Editor verifier reads this identity from CompilePlayerScripts output.
        // Runtime still checks the exact module identity, never a blanket Player bypass.
        public void SetVerifiedPlayerIdentity(string version, string fingerprint, string assemblyName,
            string moduleId, string assemblyHash, string unityVersion, RuntimePlatform platform, string backend)
        {
            if (version != SupportedSdkVersion || fingerprint != SupportedConsoleSourceHash)
                throw new NotSupportedException("This SDK source has not passed the Quantum compatibility profile.");
            if (string.IsNullOrEmpty(assemblyName) || assemblyName.Split(',')[0] != "QFSW.QC" ||
                !Guid.TryParseExact(moduleId, "N", out Guid module) || module == Guid.Empty || !IsDigest(assemblyHash))
                throw new ArgumentException("Compiled Quantum assembly identity is invalid.");
            if (string.IsNullOrEmpty(unityVersion) || platform != RuntimePlatform.WindowsPlayer ||
                backend != "mono" && backend != "il2cpp")
                throw new NotSupportedException("This Quantum Player target is not qualified.");
            sdkVersion = version; sourceFingerprint = fingerprint;
            assemblyIdentity = assemblyName; moduleIdentity = moduleId;
            playerBuild = true; playerUnityVersion = unityVersion;
            playerPlatform = platform; playerBackend = backend; playerAssemblySha256 = assemblyHash;
        }

        private static bool IsDigest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
