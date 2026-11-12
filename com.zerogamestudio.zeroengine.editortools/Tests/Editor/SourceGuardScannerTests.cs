using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ZeroEngine.EditorTools.SourceGuards;

namespace ZeroEngine.EditorTools.Tests
{
    [TestFixture]
    public sealed class SourceGuardScannerTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), $"ze_source_guard_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        [Test]
        public void FindLineMatches_ReturnsNormalizedPathLineAndKey()
        {
            var projectRoot = Path.Combine(_tempRoot, "Project");
            var scriptsRoot = Path.Combine(projectRoot, "Source", "Runtime");
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllLines(
                Path.Combine(scriptsRoot, "Sample.cs"),
                new[]
                {
                    "public sealed class Sample",
                    "{",
                    "    TargetCall(   value );",
                    "}"
                });

            var matches = SourceGuardScanner.FindLineMatches(
                scriptsRoot,
                projectRoot,
                new Regex(@"TargetCall\s*\(", RegexOptions.Compiled));

            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].Path, Is.EqualTo("Source/Runtime/Sample.cs"));
            Assert.That(matches[0].LineNumber, Is.EqualTo(3));
            Assert.That(matches[0].Line, Is.EqualTo("TargetCall( value );"));
            Assert.That(matches[0].Key, Is.EqualTo("Source/Runtime/Sample.cs :: TargetCall( value );"));
        }

        [Test]
        public void FindLineMatches_UsesRelativePathExclusion()
        {
            var projectRoot = Path.Combine(_tempRoot, "Project");
            var scriptsRoot = Path.Combine(projectRoot, "Source");
            var runtimeRoot = Path.Combine(scriptsRoot, "Runtime");
            var editorRoot = Path.Combine(scriptsRoot, "Editor");
            Directory.CreateDirectory(runtimeRoot);
            Directory.CreateDirectory(editorRoot);
            File.WriteAllText(Path.Combine(runtimeRoot, "RuntimeSample.cs"), "TargetCall();");
            File.WriteAllText(Path.Combine(editorRoot, "EditorSample.cs"), "TargetCall();");

            var matches = SourceGuardScanner.FindLineMatches(
                scriptsRoot,
                projectRoot,
                new Regex(@"TargetCall\s*\(", RegexOptions.Compiled),
                relativePath => relativePath.Contains("/Editor/", StringComparison.Ordinal));

            Assert.That(matches.Select(match => match.Path), Is.EqualTo(new[] { "Source/Runtime/RuntimeSample.cs" }));
        }

        [Test]
        public void NormalizeRelativePath_UsesForwardSlashes()
        {
            var root = Path.Combine(_tempRoot, "Project");
            var path = Path.Combine(root, "Source", "Runtime", "Nested", "Sample.cs");

            var relativePath = SourceGuardScanner.NormalizeRelativePath(root, path);

            Assert.That(relativePath, Is.EqualTo("Source/Runtime/Nested/Sample.cs"));
        }
    }
}
