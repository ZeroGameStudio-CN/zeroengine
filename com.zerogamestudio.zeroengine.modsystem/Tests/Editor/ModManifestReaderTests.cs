using System.IO;
using NUnit.Framework;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModManifestReaderTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ZeroEngineModManifestReaderTests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        [Test]
        public void TryRead_WithValidManifest_ReturnsManifestWithRootPath()
        {
            File.WriteAllText(Path.Combine(tempRoot, "manifest.json"), @"{
  ""Id"": ""author.mod"",
  ""Name"": ""Author Mod"",
  ""Version"": ""1.0.0"",
  ""ContentPaths"": [ ""content"" ]
}");

            bool ok = ModManifestReader.TryRead(tempRoot, out ModManifest manifest, out ModLoadIssue issue);

            Assert.IsTrue(ok, issue?.Message);
            Assert.AreEqual("author.mod", manifest.Id);
            Assert.AreEqual(tempRoot, manifest.RootPath);
        }

        [Test]
        public void TryRead_WithMissingManifest_ReturnsIssue()
        {
            bool ok = ModManifestReader.TryRead(tempRoot, out ModManifest manifest, out ModLoadIssue issue);

            Assert.IsFalse(ok);
            Assert.IsNull(manifest);
            Assert.AreEqual(ModIssueSeverity.Error, issue.Severity);
            StringAssert.Contains("manifest.json", issue.Message);
        }

        [Test]
        public void TryRead_WithAbsoluteManifestFileName_ReturnsIssue()
        {
            string outsideManifest = Path.Combine(Path.GetTempPath(), "ZeroEngineModManifestReaderTestsOutside.json");
            File.WriteAllText(outsideManifest, @"{
  ""Id"": ""outside.mod"",
  ""Name"": ""Outside Mod"",
  ""Version"": ""1.0.0""
}");

            try
            {
                bool ok = ModManifestReader.TryRead(tempRoot, out ModManifest manifest, out ModLoadIssue issue, outsideManifest);

                Assert.IsFalse(ok);
                Assert.IsNull(manifest);
                Assert.AreEqual(ModIssueSeverity.Error, issue.Severity);
                StringAssert.Contains("relative", issue.Message);
            }
            finally
            {
                if (File.Exists(outsideManifest))
                    File.Delete(outsideManifest);
            }
        }

        [Test]
        public void TryRead_WithTraversalManifestFileName_ReturnsIssue()
        {
            string siblingRoot = Path.Combine(Path.GetDirectoryName(tempRoot), "Sibling");
            Directory.CreateDirectory(siblingRoot);
            File.WriteAllText(Path.Combine(siblingRoot, "manifest.json"), @"{
  ""Id"": ""sibling.mod"",
  ""Name"": ""Sibling Mod"",
  ""Version"": ""1.0.0""
}");

            try
            {
                bool ok = ModManifestReader.TryRead(tempRoot, out ModManifest manifest, out ModLoadIssue issue, "../Sibling/manifest.json");

                Assert.IsFalse(ok);
                Assert.IsNull(manifest);
                Assert.AreEqual(ModIssueSeverity.Error, issue.Severity);
                StringAssert.Contains("mod root", issue.Message);
            }
            finally
            {
                if (Directory.Exists(siblingRoot))
                    Directory.Delete(siblingRoot, true);
            }
        }
    }
}
