using System.IO;
using NUnit.Framework;
using ZeroEngine.ModSystem;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModPathResolverTests
    {
        [Test]
        public void ModManifest_DeclaresTceGraphPaths()
        {
            var manifest = new ModManifest
            {
                TceGraphs = new[] { "content/graphs/burning_hit.tce.json" }
            };

            Assert.AreEqual("content/graphs/burning_hit.tce.json", manifest.TceGraphs[0]);
        }

        [Test]
        public void TryResolveRelativePath_RelativePath_ReturnsPathInsideRoot()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "ZeroEngineModPathResolverTests", "Root");

            bool resolved = ModPathResolver.TryResolveRelativePath(
                rootPath,
                "content/graphs/burning_hit.tce.json",
                out string fullPath,
                out string error);

            Assert.IsTrue(resolved, error);
            string expected = Path.GetFullPath(Path.Combine(rootPath, "content/graphs/burning_hit.tce.json"));
            Assert.AreEqual(expected, fullPath);
        }

        [Test]
        public void TryResolveRelativePath_AbsolutePath_ReturnsFalse()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "ZeroEngineModPathResolverTests", "Root");
            string absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "escape.tce.json"));

            bool resolved = ModPathResolver.TryResolveRelativePath(
                rootPath,
                absolutePath,
                out _,
                out string error);

            Assert.IsFalse(resolved);
            StringAssert.Contains("relative", error);
        }

        [Test]
        public void TryResolveRelativePath_TraversalPath_ReturnsFalse()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "ZeroEngineModPathResolverTests", "Root");

            bool resolved = ModPathResolver.TryResolveRelativePath(
                rootPath,
                "../escape.tce.json",
                out _,
                out string error);

            Assert.IsFalse(resolved);
            StringAssert.Contains("mod root", error);
        }

        [Test]
        public void TryResolveRelativePath_CaseVariantTraversal_ReturnsFalse()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "ZeroEngineModPathResolverTests", "Root");

            bool resolved = ModPathResolver.TryResolveRelativePath(
                rootPath,
                "../ROOT/content/graphs/escape.tce.json",
                out _,
                out string error);

            Assert.IsFalse(resolved);
            StringAssert.Contains("mod root", error);
        }
    }
}
