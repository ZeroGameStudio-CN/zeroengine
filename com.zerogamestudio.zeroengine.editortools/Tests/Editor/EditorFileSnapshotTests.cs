using System;
using System.IO;
using NUnit.Framework;

namespace ZeroEngine.EditorTools.Tests
{
    [Category("Boundary")]
    public sealed class EditorFileSnapshotTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ZE.EditorFileSnapshot." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void Restore_ExistingBinaryAndMeta_RestoresExactBytesWithoutTouchingSibling()
        {
            string file = Path.Combine(_root, "Assets", "fixture.asset");
            byte[] original = { 0, 1, 255, 128 };
            File.WriteAllBytes(file, original);
            File.WriteAllText(file + ".meta", "guid: synthetic");
            string sibling = Path.Combine(_root, "Assets", "unowned.txt");
            File.WriteAllText(sibling, "unchanged");
            var snapshot = new EditorFileSnapshot("Assets/fixture.asset", _root);
            var meta = new EditorFileSnapshot("Assets/fixture.asset.meta", _root);
            File.WriteAllText(file, "changed");
            File.Delete(file + ".meta");

            snapshot.Restore();
            meta.Restore();

            Assert.That(File.ReadAllBytes(file), Is.EqualTo(original));
            Assert.That(File.ReadAllText(file + ".meta"), Is.EqualTo("guid: synthetic"));
            Assert.That(File.ReadAllText(sibling), Is.EqualTo("unchanged"));
            Assert.That(meta.ImportAssetPath, Is.EqualTo("Assets/fixture.asset"));
            Assert.That(meta.IsMeta, Is.True);
            Assert.That(snapshot.ShouldImportAfterRestore, Is.True);
            Assert.DoesNotThrow(snapshot.AssertRestored);
            Assert.DoesNotThrow(meta.AssertRestored);
        }

        [Test]
        public void Restore_UnchangedFile_PreservesTimestampAndReportsNoChange()
        {
            string file = Path.Combine(_root, "Assets", "fixture.asset");
            File.WriteAllText(file, "stable");
            File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            DateTime timestamp = File.GetLastWriteTimeUtc(file);
            var snapshot = new EditorFileSnapshot("Assets/fixture.asset", _root);

            snapshot.Restore();
            snapshot.Restore();

            Assert.That(snapshot.MatchesCurrent(), Is.True);
            Assert.That(File.GetLastWriteTimeUtc(file), Is.EqualTo(timestamp));
        }

        [Test]
        public void Restore_NewFile_RemovesOnlyExplicitFileAndPreservesUncapturedMeta()
        {
            var snapshot = new EditorFileSnapshot("Assets/new.asset", _root);
            File.WriteAllText(snapshot.FullPath, "new");
            File.WriteAllText(snapshot.FullPath + ".meta", "not-captured");
            Assert.That(snapshot.ShouldDeleteCreatedAsset, Is.True);
            Assert.That(snapshot.MatchesCurrent(), Is.False);

            snapshot.Restore();

            Assert.That(File.Exists(snapshot.FullPath), Is.False);
            Assert.That(File.ReadAllText(snapshot.FullPath + ".meta"), Is.EqualTo("not-captured"));
            Assert.DoesNotThrow(snapshot.AssertRestored);
        }

        [Test]
        public void Restore_MissingParent_FailsClosedUnlessRecreationIsExplicit()
        {
            string parent = Path.Combine(_root, "Assets", "nested");
            Directory.CreateDirectory(parent);
            string file = Path.Combine(parent, "fixture.asset");
            File.WriteAllText(file, "original");
            var snapshot = new EditorFileSnapshot("Assets/nested/fixture.asset", _root);
            File.Delete(file);
            Directory.Delete(parent);

            Assert.Throws<DirectoryNotFoundException>(() => snapshot.Restore());
            Assert.That(Directory.Exists(parent), Is.False);
            snapshot.Restore(recreateMissingParent: true);
            Assert.That(File.ReadAllText(file), Is.EqualTo("original"));
        }

        [Test]
        public void Restore_DirectoryCollision_IsNotReportedAsRestoredOrDeleted()
        {
            var snapshot = new EditorFileSnapshot("Assets/collision.asset", _root);
            Directory.CreateDirectory(snapshot.FullPath);

            Assert.That(snapshot.MatchesCurrent(), Is.False);
            Assert.Throws<IOException>(() => snapshot.Restore());
            Assert.Throws<IOException>(snapshot.AssertRestored);
            Assert.That(Directory.Exists(snapshot.FullPath), Is.True);
        }

        [Test]
        public void Capture_InvalidPaths_RejectsDirectoryTraversalAndImplicitRoot()
        {
            Assert.Throws<ArgumentException>(() => new EditorFileSnapshot("", _root));
            Assert.Throws<ArgumentException>(() => new EditorFileSnapshot("Assets", _root));
            Assert.Throws<ArgumentException>(() => new EditorFileSnapshot("../outside.asset", _root));
            Assert.Throws<ArgumentException>(() => new EditorFileSnapshot("fixture.asset", "."));
        }

        [Test]
        public void Capture_ExplicitAbsoluteFile_DoesNotImplyUnityImportAuthority()
        {
            var snapshot = new EditorFileSnapshot(Path.Combine(_root, "Assets", "absolute.asset"), _root);
            Assert.That(snapshot.IsAssetFile, Is.False);
            Assert.That(snapshot.ShouldDeleteCreatedAsset, Is.False);
            Assert.That(snapshot.ShouldImportAfterRestore, Is.False);
        }
    }
}
