using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    // Permanent regression: serialized geometry must survive reveal, transfer and configuration changes.
    public class ExtractionContainerLayoutTests
    {
        private static ExtractionPlayableConfig Config()
        {
            var config = new ExtractionPlayableConfig(4, 4, 2, 2);
            config.ContainerDefinitions.Add(new ExtractionContainerDefinition("box", 6, 1, 3, 1f) { Rows = 2, Columns = 3 });
            config.ItemDefinitions.Add(new ExtractionItemDefinition("large", 2, 2, false, 1));
            config.ItemDefinitions.Add(new ExtractionItemDefinition("small", 1, 2, false, 1));
            return config;
        }
        private static ExtractionRaidContainerManifest Container()
        {
            var c = new ExtractionRaidContainerManifest("c", "r", "box", 6, 3, 3, 1f) { Opened = true };
            c.Entries.Add(new ExtractionContainerLootEntry("a", "ia", "large", 1, 0, false));
            c.Entries.Add(new ExtractionContainerLootEntry("b", "ib", "small", 1, 0, false));
            c.Entries.Add(new ExtractionContainerLootEntry("c", "ic", "large", 1, 0, false));
            return c;
        }
        [Test]
        public void LegacyLayout_UsesRealDimensionsAndPreservesOverflow()
        {
            var c = Container();
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(c, Config(), out bool changed));
            Assert.IsTrue(changed);
            Assert.AreEqual(3, c.Columns); Assert.AreEqual(2, c.Rows);
            Assert.AreEqual(2, c.Entries[1].Layout.X);
            Assert.IsTrue(c.Entries[2].LayoutOverflow);
            Assert.AreEqual(2, c.Entries[2].Layout.Width);
            Assert.AreEqual(3, c.Entries.Count);
        }
        [Test]
        public void Snapshot_RoundTripsAndDoesNotRepackTransferredEntries()
        {
            var c = Container(); var config = Config();
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(c, config, out _));
            c.Entries[0].State = ExtractionContainerLootEntryState.Transferred;
            c.SearchState.CurrentRevealOrder = 1;
            c.SearchState.CurrentEntryElapsedSeconds = 0.4f;
            string json = JsonUtility.ToJson(c);
            c = JsonUtility.FromJson<ExtractionRaidContainerManifest>(json);
            config.ItemDefinitions[1].Width = 3;
            config.ContainerDefinitions[0].Columns = 10;
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(c, config, out bool changed));
            Assert.IsFalse(changed);
            Assert.AreEqual(json, JsonUtility.ToJson(c));
        }
        [Test]
        public void MissingDefinition_DoesNotWritePartialMigration()
        {
            var c = Container(); c.Entries[2].DefinitionId = "missing";
            string json = JsonUtility.ToJson(c);
            Assert.IsFalse(ExtractionContainerLayoutService.TryEnsure(c, Config(), out _));
            Assert.AreEqual(json, JsonUtility.ToJson(c));
        }
        [Test]
        public void Rotation_UsesPermittedOrientation()
        {
            var c = Container(); c.Entries.RemoveRange(1, 2);
            var config = Config(); config.ItemDefinitions[0].Width = 2; config.ItemDefinitions[0].Height = 3;
            config.ItemDefinitions[0].CanRotate = true;
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(c, config, out _));
            Assert.IsTrue(c.Entries[0].Layout.Rotated);
            Assert.IsFalse(c.Entries[0].LayoutOverflow);
            Assert.AreEqual(3, c.Entries[0].Layout.Width);
        }
    }
}
