using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using ZeroEngine.DevelopmentContent.Editor;

namespace ZeroEngine.DevelopmentContent.Tests.Editor
{
    public sealed class PortableDevelopmentContentTests
    {
        private string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "DevelopmentContentPortable-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(root)) return;
            foreach (string file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }

        [TestCase("Playable", "EditorOnly")]
        [TestCase("Previewable", "DevelopmentOnly")]
        public void ExistingAndPortableStateNames_KeepLegacyWireValues(string stage, string availability)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "app-content.json");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"projectId\":\"invoice-app\",\"releases\":[],\"records\":[{" +
                "\"id\":\"import.csv\",\"title\":\"CSV import\",\"stage\":\"" + stage + "\",\"availability\":\"" + availability + "\"}]}");
            var store = new DevelopmentContentPlanStore(path, "invoice-app");
            var plan = store.Load();
            Assert.That(plan.Records.Single().Stage, Is.EqualTo(ContentDevelopmentStage.Previewable));
            Assert.That(plan.Records.Single().Availability, Is.EqualTo(ContentAvailability.DevelopmentOnly));
            store.Save(plan);
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"stage\": \"Playable\""));
            Assert.That(json, Does.Contain("\"availability\": \"EditorOnly\""));
            Assert.That(new DevelopmentContentPlanStore(path, "invoice-app").Load().Records.Single().Id, Is.EqualTo("import.csv"));
        }

        [Test]
        public void ProjectPaths_RejectOutsideRootAbsolutePathsAndUrls()
        {
            Assert.That(DevelopmentContentPaths.ResolveProjectPath(root, "planning/contents.json"),
                Is.EqualTo(Path.Combine(root, "planning", "contents.json")));
            foreach (string invalid in new[] { "../outside.json", Path.GetFullPath("outside.json"), "https://example.com/file", "" })
                Assert.That(DevelopmentContentPaths.ResolveProjectPath(root, invalid), Is.Null, invalid);
            Assert.Throws<ArgumentException>(() => new DevelopmentContentPanelOptions("app", root, "../outside.json", new Inventory()));
        }

        [Test]
        public void AdapterDrivenPanel_UsesCustomProjectAndDoesNotCreateRegistryOnOpen()
        {
            var inventory = new Inventory();
            var options = new DevelopmentContentPanelOptions("invoice-app", root, "content.json", inventory);
            var other = new DevelopmentContentPanelOptions("invoice-app", root + "-other", "content.json", inventory);
            Assert.That(other.SessionStatePrefix, Is.Not.EqualTo(options.SessionStatePrefix));
            var panel = new DevelopmentContentPlanPanel(options);
            try
            {
                panel.Activate(null);
                panel.Tick(null, 1);
                Assert.That(inventory.Refreshes, Is.EqualTo(2));
                Assert.That(File.Exists(options.RegistryPath), Is.False);
                Assert.That(Directory.Exists(root), Is.False);
            }
            finally
            {
                panel.Dispose();
                SessionState.EraseInt(options.SessionStatePrefix + "View");
                SessionState.EraseString(options.SessionStatePrefix + "Search");
                SessionState.EraseString(options.SessionStatePrefix + "Selected");
                SessionState.EraseBool(options.SessionStatePrefix + "Ready");
                SessionState.EraseString(options.SessionStatePrefix + "Draft");
            }
        }

        private sealed class Inventory : IDevelopmentContentInventory
        {
            public int Refreshes;
            public IReadOnlyList<DevelopmentContentInventoryEntry> Entries { get; } = Array.Empty<DevelopmentContentInventoryEntry>();
            public string Error => null;
            public void Refresh() => Refreshes++;
        }
    }
}
