using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.Quantum.Tests
{
    public sealed class QuantumCompatibilityProfileTests
    {
        [Test]
        public async Task ResponseCallback_AnswersThePresentedConfirmationWithoutRunningAnotherCommand()
        {
            var root = new GameObject("Quantum response adapter");
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                host.Initialize();
                var frontend = root.AddComponent<QuantumConsoleFrontend>();
                using var interaction = host.Session.TryAcquireInteraction();
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(QuantumConsoleFrontend);
                type.GetField("host", fields).SetValue(frontend, host);
                type.GetField("interaction", fields).SetValue(frontend, interaction);
                type.GetField("sendLog", fields).SetValue(frontend, new Action<string, LogType>((_, _) => { }));
                var receive = type.GetMethod("Receive", fields);
                int applied = 0;
                using var binding = host.Registry.Register("test", new[]
                {
                    new ConsoleBinding(new ConsoleCommand("test.apply", "test-apply", "test", "Apply."),
                        async (invocation, arguments, token) =>
                        {
                            if (!await invocation.ConfirmAsync("Apply?")) return ConsoleResult.Failure("declined", "Discarded.");
                            applied++;
                            return ConsoleResult.Success();
                        }),
                    new ConsoleBinding(new ConsoleCommand("test.pick", "test-pick", "test", "Pick."),
                        async (invocation, arguments, token) => ConsoleResult.Success(data:
                            await invocation.ChooseAsync("Pick a plan?", new[]
                            {
                                new ConsoleCompletionItem("quick", "", "Quick"),
                                new ConsoleCompletionItem("full", "", "Full")
                            })))
                });
                var execution = host.ExecuteAsync("test-apply");
                Assert.That(interaction.Pending, Is.Not.Null);
                receive.Invoke(frontend, new object[] { "yes" });
                Assert.That(applied, Is.Zero);
                type.GetField("shownPrompt", fields).SetValue(frontend, interaction.Pending);
                receive.Invoke(frontend, new object[] { "test-apply" });
                Assert.That(applied, Is.Zero);
                receive.Invoke(frontend, new object[] { " YES " });
                Assert.That((await execution).IsSuccess, Is.True);
                Assert.That(applied, Is.EqualTo(1));
                var choice = host.ExecuteAsync("test-pick");
                type.GetField("shownPrompt", fields).SetValue(frontend, interaction.PendingPrompt);
                receive.Invoke(frontend, new object[] { "not an option" });
                Assert.That(choice.IsCompleted, Is.False);
                receive.Invoke(frontend, new object[] { " 2 " });
                receive.Invoke(frontend, new object[] { " 2 " });
                Assert.That((await choice).Data, Is.EqualTo("full"));
                Assert.That(host.Session.GetHistory(), Is.EqualTo(new[] { "test-apply", "test-pick" }));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

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
