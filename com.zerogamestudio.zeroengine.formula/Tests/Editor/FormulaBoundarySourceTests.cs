using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    [Category("ZeroEngine.Formula.SourceCheck")]
    public sealed class FormulaBoundarySourceTests
    {
        [Test]
        public void RuntimeSources_DoNotReferencePobOdinOrUnityEditor()
        {
            var files = Directory.GetFiles(
                "Packages/com.zerogamestudio.zeroengine.formula/Runtime",
                "*.cs",
                SearchOption.AllDirectories);

            Assert.Greater(files.Length, 0);
            var source = string.Join("\n", files.Select(File.ReadAllText));
            StringAssert.DoesNotContain("namespace POB", source);
            StringAssert.DoesNotContain("using POB", source);
            StringAssert.DoesNotContain("POB.", source);
            StringAssert.DoesNotContain("Sirenix", source);
            StringAssert.DoesNotContain("UnityEditor", source);
        }

        [Test]
        public void RuntimeAsmdef_HasNoProjectSpecificReferences()
        {
            var json = File.ReadAllText("Packages/com.zerogamestudio.zeroengine.formula/Runtime/ZeroEngine.Formula.asmdef");

            StringAssert.DoesNotContain("POB", json);
            StringAssert.DoesNotContain("Sirenix", json);
            StringAssert.DoesNotContain("UnityEditor", json);
        }
    }
}
