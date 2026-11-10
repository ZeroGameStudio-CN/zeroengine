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
        public string SdkVersion => sdkVersion;
        public string SourceFingerprint => sourceFingerprint;
        public const string SupportedSdkVersion = "2.6.7";
        public const string SupportedConsoleSourceHash = "7bffb3987abc0fd3eb914d499a98f135514657b8fc95c8700df89cc435132227";

        public bool Matches(Type consoleType, out string reason)
        {
            if (sdkVersion != SupportedSdkVersion || sourceFingerprint != SupportedConsoleSourceHash)
            { reason = "Quantum SDK source profile is not verified."; return false; }
            if (consoleType == null || consoleType.FullName != "QFSW.QC.QuantumConsole")
            { reason = "Quantum Console component is unavailable."; return false; }
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
        }
    }
}
