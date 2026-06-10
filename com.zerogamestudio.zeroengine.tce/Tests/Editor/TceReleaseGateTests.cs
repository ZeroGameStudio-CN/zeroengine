using System.IO;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceReleaseGateTests
    {
        private const string TcePackagePath = "Packages/com.zerogamestudio.zeroengine.tce";
        private const string BridgePackagePath = "Packages/com.zerogamestudio.zeroengine.tce.modsystem";
        private const string ContractsAsmdefPath = "Packages/com.zerogamestudio.zeroengine/Runtime/ModSystem/Contracts/ZeroEngine.ModSystem.Contracts.asmdef";
        private const string ReleaseGatePath = TcePackagePath + "/Documentation~/release-gates.md";

        [Test]
        public void ReleaseGateRunbook_ListsRequiredBuildsTestsAndStaticChecks()
        {
            Assert.IsTrue(File.Exists(ReleaseGatePath), ReleaseGatePath);

            string document = File.ReadAllText(ReleaseGatePath);

            StringAssert.Contains(@"dotnet build .\ZeroEngine.TCE.Tests.Editor.csproj --no-restore --nologo", document);
            StringAssert.Contains(@"dotnet build .\ZeroEngine.TCE.ModSystem.Tests.Editor.csproj --no-restore --nologo", document);
            StringAssert.Contains(@"dotnet build .\ZeroEngine.Tests.Editor.csproj --no-restore --nologo", document);
            StringAssert.Contains("ZeroEngine.TCE.Tests.Editor", document);
            StringAssert.Contains("ZeroEngine.TCE.ModSystem.Tests.Editor", document);
            StringAssert.Contains("ZeroEngine.Tests.Editor", document);
            StringAssert.Contains("component-catalog.md", document);
            StringAssert.Contains("graph.schema.json", document);
            StringAssert.Contains("cm status --short", document);
            StringAssert.Contains("package hash", document);
        }

        [Test]
        public void PackageManifests_PinReleaseVersionsAndBridgeDependencies()
        {
            string tcePackage = File.ReadAllText($"{TcePackagePath}/package.json");
            string bridgePackage = File.ReadAllText($"{BridgePackagePath}/package.json");

            StringAssert.Contains(@"""name"": ""com.zerogamestudio.zeroengine.tce""", tcePackage);
            StringAssert.Contains(@"""version"": ""0.1.0""", tcePackage);
            StringAssert.Contains(@"""name"": ""com.zerogamestudio.zeroengine.tce.modsystem""", bridgePackage);
            StringAssert.Contains(@"""version"": ""0.1.0""", bridgePackage);
            StringAssert.Contains(@"""com.zerogamestudio.zeroengine.tce"": ""0.1.0""", bridgePackage);
            StringAssert.Contains(@"""com.zerogamestudio.zeroengine"": ""1.17.0""", bridgePackage);
            StringAssert.Contains(@"""com.unity.nuget.newtonsoft-json"": ""3.2.1""", bridgePackage);
            StringAssert.DoesNotContain(@"""file:", tcePackage);
            StringAssert.DoesNotContain(@"""file:", bridgePackage);
        }

        [Test]
        public void AsmdefReleaseBoundary_KeepsCoreBridgeAndContractsSeparated()
        {
            string runtimeAsmdef = File.ReadAllText($"{TcePackagePath}/Runtime/ZeroEngine.TCE.asmdef");
            string editorAsmdef = File.ReadAllText($"{TcePackagePath}/Editor/ZeroEngine.TCE.Editor.asmdef");
            string bridgeAsmdef = File.ReadAllText($"{BridgePackagePath}/Runtime/ZeroEngine.TCE.ModSystem.asmdef");
            string contractsAsmdef = File.ReadAllText(ContractsAsmdefPath);

            StringAssert.Contains(@"""references"": []", runtimeAsmdef);
            StringAssert.Contains(@"""ZeroEngine.TCE""", editorAsmdef);
            StringAssert.Contains(@"""ZeroEngine.TCE""", bridgeAsmdef);
            StringAssert.Contains(@"""ZeroEngine.ModSystem.Contracts""", bridgeAsmdef);
            StringAssert.DoesNotContain(@"""ZeroEngine.ModSystem""", bridgeAsmdef);
            StringAssert.Contains(@"""references"": []", contractsAsmdef);

            AssertNoProjectGameplayReferences(runtimeAsmdef, "TCE runtime asmdef");
            AssertNoProjectGameplayReferences(editorAsmdef, "TCE editor asmdef");
            AssertNoProjectGameplayReferences(bridgeAsmdef, "TCE ModSystem bridge asmdef");
            AssertNoProjectGameplayReferences(contractsAsmdef, "ModSystem contracts asmdef");
        }

        private static void AssertNoProjectGameplayReferences(string text, string context)
        {
            StringAssert.DoesNotContain("ZeroEngine.Combat", text, context);
            StringAssert.DoesNotContain("ZeroEngine.Gameplay", text, context);
            StringAssert.DoesNotContain("POB", text, context);
            StringAssert.DoesNotContain("P5", text, context);
            StringAssert.DoesNotContain("AbilityDataSO", text, context);
        }
    }
}
