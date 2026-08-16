using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.AssetCatalog.Tests
{
    public sealed class AssetCatalogCoreTests
    {
        [Test]
        public void Identity_NormalizesGuidAndKeepsLongSubAssetsDistinctAcrossMove()
        {
            AssetCatalogIdentity main = AssetCatalogContracts.CreateIdentity("pob", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0);
            AssetCatalogIdentity clip = AssetCatalogContracts.CreateIdentity("pob", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 9223372036854L);

            Assert.That(main.guid, Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
            Assert.That(main, Is.Not.EqualTo(clip));
            Assert.That(main.StableKey, Is.EqualTo("pob:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:0"));
            Assert.That(main.StableKey, Does.Not.Contain("Assets/"));
        }

        [Test]
        public void Proposal_RequiresBilingualCurrentTaxonomyAndModelProvenance()
        {
            AssetCatalogTaxonomy taxonomy = Taxonomy();
            AssetCatalogProposalInput proposal = Proposal("hash-v1", AssetCatalogRevisionSource.Model);
            proposal.modelLabel = "luna-max";
            proposal.promptVersion = "v1";
            proposal.classifierVersion = "v1";

            Assert.DoesNotThrow(() => AssetCatalogContracts.ValidateProposal(proposal, taxonomy, "hash-v1"));
            proposal.descriptionEn = null;
            Assert.Throws<ArgumentException>(() => AssetCatalogContracts.ValidateProposal(proposal, taxonomy, "hash-v1"));
            proposal.descriptionEn = "blue liquid impact";
            proposal.controlledTags = new[] { "subject.unknown" };
            Assert.Throws<ArgumentException>(() => AssetCatalogContracts.ValidateProposal(proposal, taxonomy, "hash-v1"));
        }

        [Test]
        public void Snapshot_IsStableAndRejectsTamperedManifest()
        {
            AssetCatalogSnapshot snapshot = new AssetCatalogSnapshot
            {
                manifest = new AssetCatalogSnapshotManifest { taxonomyVersion = 1, catalogCursor = 9 },
                records = new[] { SnapshotRecord("b", "Assets/Assets/Vfx/B.prefab"), SnapshotRecord("a", "Assets/Assets/Vfx/A.prefab") }
            };
            string first = AssetCatalogSnapshotStore.ToStableJson(snapshot);
            string second = AssetCatalogSnapshotStore.ToStableJson(snapshot);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(AssetCatalogSnapshotStore.FromJson(first).records[0].record.identity.guid, Is.EqualTo(GuidFor("a")));

            AssetCatalogSnapshot tampered = JsonUtility.FromJson<AssetCatalogSnapshot>(first);
            tampered.manifest.recordsSha256 = "bad";
            Assert.Throws<InvalidDataException>(() => AssetCatalogSnapshotStore.FromJson(JsonUtility.ToJson(tampered)));
        }

        [Test]
        public void LocalSearch_UsesApprovedBilingualDescriptionsAndTaxonomyAliases()
        {
            AssetCatalogSnapshot snapshot = new AssetCatalogSnapshot
            {
                manifest = new AssetCatalogSnapshotManifest { taxonomyVersion = 1 },
                records = new[] { SnapshotRecord("a", "Assets/Assets/Vfx/Impact_01.prefab") }
            };
            List<AssetCatalogSearchResult> found = AssetCatalogSearch.SearchLocal(snapshot, Taxonomy(), new AssetCatalogSearchQuery { text = "水", facet = "particle" });
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].record.path, Does.Not.Contain("water"));
            Assert.That(found[0].approvedRevision.controlledTags, Does.Contain("subject.water"));
        }

        [Test]
        public void PersonalAi_RestrictsEndpointCandidatesAndRecommendationWhitelist()
        {
            Assert.That(AssetCatalogContracts.IsEndpointAllowed("https://api.deepseek.com"), Is.True);
            Assert.That(AssetCatalogContracts.IsEndpointAllowed("http://localhost:11434"), Is.True);
            Assert.That(AssetCatalogContracts.IsEndpointAllowed("http://example.com"), Is.False);
            Assert.That(AssetCatalogCredentialStore.PersonalAiKeyTarget("DeepSeek", "https://api.deepseek.com"), Does.Not.Contain("api.deepseek.com"));

            AssetCatalogAiCandidate candidate = new AssetCatalogAiCandidate { identity = AssetCatalogContracts.CreateIdentity("pob", GuidFor("a"), 0), path = "Assets/Assets/Vfx/Impact_01.prefab" };
            AssetCatalogAiAnswer answer = new AssetCatalogAiAnswer
            {
                answer = "推荐",
                recommendations = new[]
                {
                    new AssetCatalogAiRecommendation { identity = AssetCatalogContracts.CreateIdentity("pob", GuidFor("b"), 0), path = candidate.path, role = "primary", reason = "x", order = 1 }
                }
            };
            Assert.Throws<InvalidOperationException>(() => AssetCatalogAiClient.ValidateAnswer(answer, new[] { candidate }));
        }

        [Test]
        public void ClassificationExchange_ValidatesFullIdentityHashAndModelProposal()
        {
            string root = Path.Combine(Path.GetTempPath(), "ZE-AssetCatalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                AssetCatalogClassificationRun run = new AssetCatalogClassificationRun
                {
                    runId = "pilot-01",
                    createdAtUtc = DateTime.UtcNow.ToString("O"),
                    classifierVersion = "v1",
                    classifierModel = "luna-max",
                    taxonomyVersion = 1,
                    previewProfileVersion = "sheet-v1",
                    sourceRevision = SourceRevision()
                };
                AssetCatalogClassificationItem item = new AssetCatalogClassificationItem
                {
                    identity = AssetCatalogContracts.CreateIdentity("pob", GuidFor("a"), 0),
                    path = "Assets/Assets/Vfx/Impact_01.prefab",
                    assetType = AssetCatalogAssetType.Prefab,
                    facets = new[] { "particle" },
                    dependencyHash = "hash-v1",
                    sourceRevision = SourceRevision(),
                    previewProfileVersion = "sheet-v1",
                    previewRelativePath = "previews/a.png"
                };
                string runDirectory = AssetCatalogClassificationExchange.CreateRunDirectory(root, run, new[] { item });
                AssetCatalogClassificationExchange.AppendResult(runDirectory, new AssetCatalogClassificationResult
                {
                    runId = run.runId,
                    identity = item.identity,
                    dependencyHash = item.dependencyHash,
                    descriptionZh = "蓝色液体冲击",
                    descriptionEn = "blue liquid impact",
                    controlledTags = new[] { "subject.water" },
                    confidence = 0.9f,
                    modelLabel = "luna-max",
                    promptVersion = "v1",
                    classifierVersion = "v1",
                    taxonomyVersion = 1
                });

                Assert.That(AssetCatalogClassificationExchange.ReadAndValidateResults(runDirectory, Taxonomy()), Has.Count.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void BrowserLayout_StacksWhenNarrowAndRejectsStalePreviewCompletion()
        {
            AssetCatalogBrowserLayout wide = AssetCatalogBrowserLayoutPolicy.Calculate(1500f);
            AssetCatalogBrowserLayout narrow = AssetCatalogBrowserLayoutPolicy.Calculate(700f);
            Assert.That(wide.Mode, Is.EqualTo(AssetCatalogBrowserLayoutMode.SideBySide));
            Assert.That(wide.ResultWidth, Is.GreaterThanOrEqualTo(AssetCatalogBrowserLayoutPolicy.MinimumResultWidth));
            Assert.That(narrow.Mode, Is.EqualTo(AssetCatalogBrowserLayoutMode.Stacked));

            AssetCatalogSelectionState selection = new AssetCatalogSelectionState();
            AssetCatalogIdentity first = AssetCatalogContracts.CreateIdentity("pob", GuidFor("a"), 0);
            AssetCatalogIdentity second = AssetCatalogContracts.CreateIdentity("pob", GuidFor("b"), 0);
            selection.Select(first);
            long firstGeneration = selection.PreviewGeneration;
            selection.Select(second);
            Assert.That(selection.IsCurrentPreview(first.StableKey, firstGeneration), Is.False);
            Assert.That(selection.IsCurrentPreview(second.StableKey, selection.PreviewGeneration), Is.True);
        }

        private static AssetCatalogSnapshotRecord SnapshotRecord(string key, string path)
        {
            return new AssetCatalogSnapshotRecord
            {
                record = new AssetCatalogRecord
                {
                    identity = AssetCatalogContracts.CreateIdentity("pob", GuidFor(key), 0),
                    path = path,
                    assetType = AssetCatalogAssetType.Prefab,
                    facets = new[] { "particle" },
                    dependencyHash = "hash-v1",
                    sourceRevision = SourceRevision(),
                    reviewStatus = AssetCatalogReviewStatus.Approved
                },
                approvedRevision = new AssetCatalogSemanticRevision
                {
                    revisionId = "revision-" + key,
                    descriptionZh = "蓝色水花冲击",
                    descriptionEn = "blue liquid water impact",
                    controlledTags = new[] { "subject.water" },
                    freeTags = Array.Empty<string>(),
                    source = AssetCatalogRevisionSource.Human,
                    taxonomyVersion = 1,
                    basedOnDependencyHash = "hash-v1",
                    status = AssetCatalogRevisionStatus.Approved
                }
            };
        }

        private static AssetCatalogTaxonomy Taxonomy()
        {
            return new AssetCatalogTaxonomy
            {
                version = 1,
                tagDefinitions = new[]
                {
                    new AssetCatalogTagDefinition { tagId = "subject.water", axis = "subject", nameZh = "水", nameEn = "water", aliases = new[] { "liquid" }, enabled = true }
                }
            };
        }

        private static AssetCatalogProposalInput Proposal(string hash, string source)
        {
            return new AssetCatalogProposalInput
            {
                descriptionZh = "蓝色液体冲击",
                descriptionEn = "blue liquid impact",
                controlledTags = new[] { "subject.water" },
                freeTags = Array.Empty<string>(),
                confidence = 0.9f,
                source = source,
                taxonomyVersion = 1,
                basedOnDependencyHash = hash
            };
        }

        private static AssetCatalogSourceRevision SourceRevision()
        {
            return new AssetCatalogSourceRevision { repository = "POB", branch = "/main", changeset = "cs:100" };
        }

        private static string GuidFor(string suffix)
        {
            return new string('0', 31) + suffix;
        }
    }
}
