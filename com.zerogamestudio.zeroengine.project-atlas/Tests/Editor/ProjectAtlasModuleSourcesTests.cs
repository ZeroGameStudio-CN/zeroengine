using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ZeroEngine.ProjectAtlas.Tests
{
    // Core contract: editor and headless readers share one deterministic authored graph.
    public sealed class ProjectAtlasModuleSourcesTests
    {
        private string root;
        private string modules;
        private string catalog;
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "atlas-modules-" + Guid.NewGuid().ToString("N"));
            modules = Path.Combine(root, "docs/architecture/project-atlas/modules");
            Directory.CreateDirectory(modules);
            catalog = Path.Combine(root, "docs/architecture/project-atlas.json");
            File.WriteAllText(catalog, "{\"schemaVersion\":2,\"project\":{\"id\":\"fixture\",\"displayName\":\"Fixture\",\"summary\":\"fixture\",\"rootAgentRule\":\"rules.agents\"},\"sourceDirectories\":[\"docs/architecture/project-atlas/modules\"]}");
            File.WriteAllText(Path.Combine(modules, "foundation.atlas.json"), "{\"schemaVersion\":1,\"references\":[{\"id\":\"rules.agents\",\"kind\":\"doc\",\"target\":\"AGENTS.md\",\"displayName\":\"Rules\",\"required\":true}],\"systems\":[]}");
        }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private ProjectAtlasGraph Load() => ProjectAtlasCatalogLoader.LoadAuthoringProject(root);
        private void ChangeRoot(Action<JObject> action)
        { var value = JObject.Parse(File.ReadAllText(catalog)); action(value); File.WriteAllText(catalog, value.ToString()); }
        private void Reference(string file, string id, string title)
        { File.WriteAllText(Path.Combine(modules, file), new JObject { ["schemaVersion"]=1, ["references"]=new JArray(new JObject { ["id"]=id,["kind"]="path",["target"]="Assets",["displayName"]=title,["required"]=false }),["systems"]=new JArray() }.ToString()); }

        [Test] public void IndependentModuleAdditionNeedsNoRootEditAndOrderIsStable()
        {
            string before=File.ReadAllText(catalog);
            Reference("z.atlas.json", "module.z", "Z"); Reference("a.atlas.json", "module.a", "A");
            var first=Load(); Assert.That(first.HasErrors, Is.False);
            Assert.That(first.References.Select(x=>x.Id), Is.EqualTo(new[]{"module.a","module.z","rules.agents"}));
            File.Move(Path.Combine(modules,"a.atlas.json"),Path.Combine(modules,"y.atlas.json"));
            Assert.That(ProjectAtlasMarkdownProjector.Render(Load()).Split('\n').Where(x=>!x.Contains("authored-source-sha256")), Is.EqualTo(ProjectAtlasMarkdownProjector.Render(first).Split('\n').Where(x=>!x.Contains("authored-source-sha256"))));
            Assert.That(File.ReadAllText(catalog), Is.EqualTo(before));
        }
        [Test] public void ReadAlwaysRecomputesAfterEditAddAndDeleteAndNeverTouchesLegacyIndex()
        {
            string legacy=Path.Combine(root,ProjectAtlasCatalogLoader.GeneratedIndexPath);
            File.WriteAllText(legacy,"preserve old tracked content");
            var first=Load(); string cache=ProjectAtlasProjectWriter.WriteGeneratedIndex(first);
            Assert.That(cache.Replace('\\','/'), Does.EndWith(ProjectAtlasCatalogLoader.CacheIndexPath));
            Reference("new.atlas.json","module.new","new");
            // Add changes graph even if index text has no system reference to it; it must be validated.
            Reference("duplicate.atlas.json","module.new","duplicate");
            Assert.That(Load().HasErrors, Is.True);
            Assert.Throws<InvalidOperationException>(()=>ProjectAtlasProjectWriter.WriteGeneratedIndex(Load()));
            File.Delete(Path.Combine(modules,"duplicate.atlas.json"));
            ChangeRoot(x=>x["project"]["summary"]="edited");
            Assert.That(ProjectAtlasValidator.IsProjectionCurrent(Load()), Is.False);
            ProjectAtlasProjectWriter.WriteGeneratedIndex(Load());
            Assert.That(ProjectAtlasValidator.IsProjectionCurrent(Load()), Is.True);
            File.Delete(Path.Combine(modules,"foundation.atlas.json"));
            Assert.That(Load().HasErrors, Is.True, "Deletion must invalidate, not serve the cached graph.");
            Assert.That(File.ReadAllText(legacy), Is.EqualTo("preserve old tracked content"));
        }
        [Test] public void OnlyAtlasSuffixInsideDeclaredDirectoriesIsDiscovered()
        {
            File.WriteAllText(Path.Combine(modules,"unrelated.json"),"invalid");
            File.WriteAllText(Path.Combine(root,"docs/architecture/project-atlas/ignored.atlas.json"),"invalid");
            Assert.That(Load().HasErrors,Is.False);
        }
        [TestCase("../outside")]
        [TestCase("docs/architecture/project-atlas/../../outside")]
        [TestCase("docs/architecture/project-atlas/missing")]
        [TestCase("C:/outside")]
        [TestCase("docs/architecture/project-atlas/modules/")]
        public void UnsafeOrMissingDirectoryFailsClosed(string path)
        { ChangeRoot(x=>x["sourceDirectories"]=new JArray(path)); Assert.That(Load().HasErrors,Is.True); }
        [Test] public void OverlappingExplicitSourceAndDiscoveryFailsInsteadOfLoadingTwice()
        { ChangeRoot(x=>x["sources"]=new JArray("docs/architecture/project-atlas/modules/foundation.atlas.json")); Assert.That(Load().Diagnostics.Any(x=>x.Code=="duplicate-source"),Is.True); }
        [Test] public void DuplicateReferenceIdsFailInsteadOfLastWriterWinning()
        { Reference("duplicate.atlas.json","rules.agents","replacement"); Assert.That(Load().Diagnostics.Any(x=>x.Code=="duplicate-reference-id"),Is.True); }
        [Test] public void MalformedFragmentCannotBeHiddenByExistingCache()
        { ProjectAtlasProjectWriter.WriteGeneratedIndex(Load()); File.WriteAllText(Path.Combine(modules,"broken.atlas.json"),"{"); Assert.That(Load().HasErrors,Is.True); }
        [Test] public void AdditiveModuleRoutesPreserveOwnerAndRejectDuplicateModuleIds()
        {
            var system = JObject.Parse(@"{'id':'foundation','displayName':'Foundation','summary':'summary','category':'foundation','order':1,'keywords':[],'ownerRoles':['owner'],'lifecycle':'active','ownership':'project','team':{'purpose':'purpose','audiences':['dev'],'workflows':['read'],'configurationMode':'none','configurationReason':'no config'},'program':{'entryRefs':['rules.agents']},'agent':{'readFirstRefs':['rules.agents'],'changeBoundary':'owned files','verificationRefs':['rules.agents'],'updateTriggers':['change']}}");
            File.WriteAllText(Path.Combine(modules,"system.atlas.json"),new JObject{["schemaVersion"]=1,["systems"]=new JArray(system)}.ToString());
            Reference("module.atlas.json","module.new","new");
            var module=JObject.Parse(File.ReadAllText(Path.Combine(modules,"module.atlas.json")));
            module["contributions"]=JArray.Parse(@"[{'id':'feature.new','systemId':'foundation','structureRefs':['module.new'],'dataFlow':['new module route']}] ");
            File.WriteAllText(Path.Combine(modules,"module.atlas.json"),module.ToString());
            var graph=Load(); Assert.That(graph.HasErrors,Is.False);
            Assert.That(graph.FindSystem("foundation").Program.StructureRefs,Does.Contain("module.new"));
            Assert.That(graph.FindSystem("foundation").OwnerRoles,Is.EqualTo(new[]{"owner"}));
            File.WriteAllText(Path.Combine(modules,"duplicate.atlas.json"),new JObject{["schemaVersion"]=1,["contributions"]=module["contributions"].DeepClone()}.ToString());
            Assert.That(Load().Diagnostics.Any(x=>x.Code=="duplicate-contribution-id"),Is.True);
        }
        [Test] public void UnknownModuleOwnerAndOverridesAreRejected()
        {
            File.WriteAllText(Path.Combine(modules,"module.atlas.json"),@"{'schemaVersion':1,'contributions':[{'id':'feature.new','systemId':'missing'}]}");
            Assert.That(Load().Diagnostics.Any(x=>x.Code=="invalid-contribution-owner"),Is.True);
            File.WriteAllText(Path.Combine(modules,"module.atlas.json"),@"{'schemaVersion':1,'contributions':[{'id':'feature.new','systemId':'missing','ownership':'project'}]}");
            Assert.That(Load().Diagnostics.Any(x=>x.Code=="invalid-fragment-json"),Is.True);
        }

        [Test] public void VersionOneExplicitSourcesAndOutputPathRemainCompatible()
        {
            File.Move(Path.Combine(modules,"foundation.atlas.json"),Path.Combine(root,"docs/architecture/project-atlas/foundation.json"));
            ChangeRoot(x=> { x["schemaVersion"]=1; x.Remove("sourceDirectories"); x["sources"]=new JArray("docs/architecture/project-atlas/foundation.json"); });
            var graph=Load(); Assert.That(graph.HasErrors,Is.False); Assert.That(graph.UsesLocalIndex,Is.False);
            Assert.That(ProjectAtlasProjectWriter.WriteGeneratedIndex(graph).Replace('\\','/'),Does.EndWith(ProjectAtlasCatalogLoader.GeneratedIndexPath));
        }

        [Test]
        public void NullSystemAndMissingContributionOwnerReturnDiagnostics()
        {
            File.WriteAllText(Path.Combine(modules, "invalid-owner.atlas.json"),
                "{'schemaVersion':1,'systems':[null],'contributions':[{'id':'feature.new'}]}");
            var graph = Load();
            Assert.That(graph.HasErrors, Is.True);
            Assert.That(graph.Diagnostics.Any(value => value.Code == "invalid-contribution-owner"), Is.True);
        }
    }
}
