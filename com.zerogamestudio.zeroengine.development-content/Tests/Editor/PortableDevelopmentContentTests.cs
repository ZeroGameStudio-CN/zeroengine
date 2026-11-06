using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        [TestCase(true)]
        [TestCase(false)]
        public void Panel_UsesCustomProjectAndDoesNotCreateRegistryOnOpen(bool withInventory)
        {
            var inventory = withInventory ? new Inventory() : null;
            var options = withInventory
                ? new DevelopmentContentPanelOptions("invoice-app", root, "content.json", inventory)
                : new DevelopmentContentPanelOptions("invoice-app", root, "content.json");
            var other = new DevelopmentContentPanelOptions("invoice-app", root + "-other", "content.json", inventory);
            Assert.That(other.SessionStatePrefix, Is.Not.EqualTo(options.SessionStatePrefix));
            var panel = new DevelopmentContentPlanPanel(options);
            try
            {
                panel.Activate(null);
                panel.Tick(null, 1);
                if (withInventory) Assert.That(inventory.Refreshes, Is.EqualTo(2));
                Assert.That(File.Exists(options.RegistryPath), Is.False);
                Assert.That(Directory.Exists(root), Is.False);
            }
            finally
            {
                panel.Dispose();
                ClearSession(options.SessionStatePrefix);
            }
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void ReopenedDraft_SavesOnlyAgainstItsOriginalRegistry(bool externalSave, bool legacyDraft, bool canSave)
        {
            const BindingFlags panelState = BindingFlags.Instance | BindingFlags.NonPublic;
            const string localAction = "draft action";
            var options = new DevelopmentContentPanelOptions("invoice-app", root, "content.json");
            var store = new DevelopmentContentPlanStore(options.RegistryPath, options.ProjectId);
            var plan = store.Load();
            plan.Records.Add(new DevelopmentContentRecord { Id = "csv-import", Title = "CSV import" });
            store.Save(plan);
            try
            {
                using (var original = new DevelopmentContentPlanPanel(options))
                {
                    original.Activate(null);
                    var draft = (DevelopmentContentRecord)typeof(DevelopmentContentPlanPanel).GetField("draft", panelState).GetValue(original);
                    draft.NextAction = localAction;
                    typeof(DevelopmentContentPlanPanel).GetField("dirty", panelState).SetValue(original, true);
                    if (legacyDraft)
                    {
                        original.Deactivate();
                        var legacy = JObject.FromObject(draft);
                        legacy["Record"] = JObject.FromObject(draft);
                        legacy["RegistryRevision"] = store.Revision;
                        SessionState.SetString(options.SessionStatePrefix + "Draft", legacy.ToString(Formatting.None));
                    }
                }

                if (externalSave)
                {
                    plan.Records[0].NextAction = "other editor's action";
                    store.Save(plan);
                }

                byte[] before = File.ReadAllBytes(options.RegistryPath);
                using (var reopened = new DevelopmentContentPlanPanel(options))
                {
                    reopened.Activate(null);
                    var save = typeof(DevelopmentContentPlanPanel).GetMethod("SaveDraft", panelState);
                    Assert.That(save.Invoke(reopened, null), Is.EqualTo(canSave));
                    if (canSave)
                        Assert.That(store.Load().Records.Single().NextAction, Is.EqualTo(localAction));
                    else
                        Assert.That(File.ReadAllBytes(options.RegistryPath), Is.EqualTo(before));
                }

                string retained = SessionState.GetString(options.SessionStatePrefix + "Draft", string.Empty);
                if (canSave) Assert.That(retained, Is.Empty);
                else Assert.That(retained, Does.Contain(localAction));
            }
            finally { ClearSession(options.SessionStatePrefix); }
        }

        private static void ClearSession(string prefix)
        {
            SessionState.EraseInt(prefix + "View");
            SessionState.EraseString(prefix + "Search");
            SessionState.EraseString(prefix + "Selected");
            SessionState.EraseBool(prefix + "Ready");
            SessionState.EraseString(prefix + "Draft");
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
