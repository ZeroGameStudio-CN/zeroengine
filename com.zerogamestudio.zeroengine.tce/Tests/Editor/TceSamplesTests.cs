using System.IO;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceSamplesTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce";

        [Test]
        public void PackageJson_RegistersProductizationSamples()
        {
            string packageJson = File.ReadAllText($"{PackagePath}/package.json");

            StringAssert.Contains("Samples~/ModGraphImport", packageJson);
            StringAssert.Contains("Samples~/AdapterTemplate", packageJson);
        }

        [Test]
        public void ModGraphImportSample_UsesStableExternalGraphShape()
        {
            string graphJson = File.ReadAllText($"{PackagePath}/Samples~/ModGraphImport/content/graphs/burning_hit.tce.json");

            StringAssert.Contains("zeroengine-tce-graph", graphJson);
            StringAssert.Contains("\"schemaVersion\"", graphJson);
            StringAssert.Contains("zeroengine.tce.trigger.on_install", graphJson);
            StringAssert.Contains("zeroengine.tce.effect.debug_log", graphJson);
            StringAssert.DoesNotContain("DebugLogEffectData", graphJson);
            StringAssert.DoesNotContain("Assembly-CSharp", graphJson);
            StringAssert.DoesNotContain("managedReferenceFullTypename", graphJson);
        }

        [Test]
        public void AdapterTemplate_DocumentsThinAdapterContract()
        {
            string readme = File.ReadAllText($"{PackagePath}/Samples~/AdapterTemplate/README.md");

            StringAssert.Contains("ITceActor", readme);
            StringAssert.Contains("ITceClock", readme);
            StringAssert.Contains("TceAdapterContractAssertions", readme);
        }
    }
}
