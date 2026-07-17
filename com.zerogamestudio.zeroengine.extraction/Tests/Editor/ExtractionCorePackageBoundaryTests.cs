using System.IO;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionCorePackageBoundaryTests
    {
        private const string PackageRoot = "Packages/com.zerogamestudio.zeroengine.extraction";

        [Test]
        public void PackageManifest_UsesZeroEnginePackageName()
        {
            string json = File.ReadAllText($"{PackageRoot}/package.json");

            StringAssert.Contains("\"name\": \"com.zerogamestudio.zeroengine.extraction\"", json);
            StringAssert.Contains("\"version\": \"0.1.0\"", json);
        }

        [Test]
        public void RuntimeAsmdef_PreservesCompatibleCoreAssemblyAndAvoidsPobRuntime()
        {
            string json = File.ReadAllText($"{PackageRoot}/Runtime/POB.Extraction.Core.asmdef");

            StringAssert.Contains("\"name\": \"POB.Extraction.Core\"", json);
            StringAssert.Contains("\"rootNamespace\": \"POB.Extraction\"", json);
            StringAssert.DoesNotContain("POB.Runtime", json);
            StringAssert.DoesNotContain("QFSW.QC", json);
        }

        [Test]
        public void RuntimeSources_DoNotReferenceAdapterOnlySystems()
        {
            foreach (string path in Directory.GetFiles($"{PackageRoot}/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                StringAssert.DoesNotContain("POBSceneManager", source, path);
                StringAssert.DoesNotContain("LevelType", source, path);
                StringAssert.DoesNotContain("RoomType", source, path);
                StringAssert.DoesNotContain("MonoBehaviour", source, path);
                StringAssert.DoesNotContain("QFSW.QC", source, path);
                StringAssert.DoesNotContain("ConsoleCommands", source, path);
                StringAssert.DoesNotContain("IExtractionRoomGateway", source, path);
                StringAssert.DoesNotContain("ExtractionRoomRoute", source, path);
            }
        }
    }
}
