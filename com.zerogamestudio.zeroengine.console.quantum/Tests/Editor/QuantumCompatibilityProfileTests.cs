using System;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Console.Quantum.Tests
{
    public sealed class QuantumCompatibilityProfileTests
    {
        [TestCase("unknown")]
        [TestCase("MONO")]
        [TestCase("")]
        public void PlayerStamp_RefusesUnqualifiedBackend(string backend)
        {
            var profile = ScriptableObject.CreateInstance<QuantumCompatibilityProfile>();
            try
            {
                Assert.Throws<NotSupportedException>(() => Stamp(profile, backend));
                Assert.That(profile.IsPlayerProfile, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void PlayerStamp_RejectsEmptyModuleIdentity()
        {
            var profile = ScriptableObject.CreateInstance<QuantumCompatibilityProfile>();
            try
            {
                Assert.Throws<ArgumentException>(() => Stamp(profile, "mono", Guid.Empty.ToString("N")));
                Assert.That(profile.IsPlayerProfile, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void PlayerStamp_PreservesExactIdentityAndDoesNotAcceptMissingSdk()
        {
            var profile = ScriptableObject.CreateInstance<QuantumCompatibilityProfile>();
            try
            {
                string module = Guid.NewGuid().ToString("N");
                Stamp(profile, "il2cpp", module);
                Assert.That(profile.IsPlayerProfile, Is.True);
                Assert.That(profile.ModuleIdentity, Is.EqualTo(module));
                Assert.That(profile.PlayerBackend, Is.EqualTo("il2cpp"));
                Assert.That(profile.PlayerAssemblySha256, Is.EqualTo(new string('a', 64)));
                Assert.That(profile.Matches(null, out string reason), Is.False);
                Assert.That(reason, Does.Contain("unavailable"));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        private static void Stamp(QuantumCompatibilityProfile profile, string backend, string module = "00112233445566778899aabbccddeeff") =>
            profile.SetVerifiedPlayerIdentity(QuantumCompatibilityProfile.SupportedSdkVersion,
                QuantumCompatibilityProfile.SupportedConsoleSourceHash,
                "QFSW.QC, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", module,
                new string('a', 64), "6000.6.4f1", RuntimePlatform.WindowsPlayer, backend);
    }
}
