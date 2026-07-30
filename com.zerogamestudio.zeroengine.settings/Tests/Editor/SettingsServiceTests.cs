using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.PlayerSettings.Tests
{
    public sealed class SettingsServiceTests
    {
        private static readonly SettingId Volume = new("audio.master");
        private static readonly SettingId Quality = new("display.quality");

        [Test]
        public async Task Initialize_InvalidKnownValueFallsBackAndUnknownEntryIsPreserved()
        {
            var store = new MemoryStore
            {
                Document = new SettingsDocument
                {
                    entries = new List<SettingsEntry>
                    {
                        new() { id = Volume.Value, kind = "Float", value = "2" },
                        new() { id = "project.future", kind = "String", value = "kept" }
                    }
                }
            };
            var applier = new RecordingApplier(Volume, Quality);
            var service = CreateService(store, applier);

            var result = await service.InitializeAsync(CancellationToken.None);
            var commit = await service.SetAndCommitAsync(Quality, SettingValue.String("Low"), CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Errors, Contains.Item("invalid-value:audio.master"));
            Assert.That(service.Committed[Volume].AsFloat(), Is.EqualTo(1f));
            Assert.That(commit.Success, Is.True);
            Assert.That(store.Document.entries.Exists(x => x.id == "project.future" && x.value == "kept"), Is.True);
        }

        [Test]
        public async Task PreviewThenCancel_RestoresCommittedSnapshot()
        {
            var applier = new RecordingApplier(Volume, Quality);
            var service = CreateService(new MemoryStore(), applier);
            await service.InitializeAsync(CancellationToken.None);
            var session = service.OpenSession();

            await session.SetAsync(Volume, SettingValue.Float(0.25f), CancellationToken.None);
            Assert.That(applier.Last[Volume].AsFloat(), Is.EqualTo(0.25f));

            await session.CancelAsync(CancellationToken.None);
            Assert.That(applier.Last[Volume].AsFloat(), Is.EqualTo(1f));
            Assert.That(service.Committed[Volume].AsFloat(), Is.EqualTo(1f));
        }

        [Test]
        public async Task SaveFailure_RollsBackAndKeepsSessionOpen()
        {
            var store = new MemoryStore { FailSave = true };
            var applier = new RecordingApplier(Volume, Quality);
            var service = CreateService(store, applier);
            await service.InitializeAsync(CancellationToken.None);
            var session = service.OpenSession();
            await session.SetAsync(Quality, SettingValue.String("Low"), CancellationToken.None);

            var result = await session.CommitAsync(CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Stage, Is.EqualTo("save"));
            Assert.That(session.IsOpen, Is.True);
            Assert.That(service.Committed[Quality].AsString(), Is.EqualTo("High"));
            Assert.That(applier.Last[Quality].AsString(), Is.EqualTo("High"));
        }

        [Test]
        public async Task ResetCategoryAndAll_UseConfiguredDefaults()
        {
            var service = CreateService(new MemoryStore(), new RecordingApplier(Volume, Quality));
            await service.InitializeAsync(CancellationToken.None);
            var session = service.OpenSession();
            await session.SetAsync(Volume, SettingValue.Float(0.4f), CancellationToken.None);
            await session.SetAsync(Quality, SettingValue.String("Low"), CancellationToken.None);

            await session.ResetCategoryAsync("audio", CancellationToken.None);
            Assert.That(session.Working[Volume].AsFloat(), Is.EqualTo(1f));
            Assert.That(session.Working[Quality].AsString(), Is.EqualTo("Low"));

            await session.ResetAllAsync(CancellationToken.None);
            Assert.That(session.Working[Quality].AsString(), Is.EqualTo("High"));
        }

        [Test]
        public void DocumentSerializer_RejectsDuplicateIdsAndNonFiniteFloat()
        {
            const string duplicate = "{\"formatVersion\":1,\"entries\":[{\"id\":\"a\",\"kind\":\"Int\",\"value\":\"1\"},{\"id\":\"a\",\"kind\":\"Int\",\"value\":\"2\"}]}";
            const string nan = "{\"formatVersion\":1,\"entries\":[{\"id\":\"a\",\"kind\":\"Float\",\"value\":\"NaN\"}]}";

            Assert.That(SettingsDocumentSerializer.TryDeserialize(duplicate, out _, out _), Is.False);
            Assert.That(SettingsDocumentSerializer.TryDeserialize(nan, out _, out _), Is.False);
        }

        [Test]
        public void PlayerPrefsStore_InvalidPrimaryLoadsBackupAndDoesNotRotateCorruption()
        {
            var prefix = "ZeroEngine.Settings.Tests." + Guid.NewGuid().ToString("N");
            var primaryKey = prefix + ".Primary";
            var backupKey = prefix + ".Backup";
            var validBackup = SettingsDocumentSerializer.Serialize(new SettingsDocument
            {
                entries = new List<SettingsEntry>
                {
                    new() { id = Volume.Value, kind = "Float", value = "0.5" }
                }
            });
            PlayerPrefs.SetString(primaryKey, "{broken");
            PlayerPrefs.SetString(backupKey, validBackup);
            try
            {
                var store = new PlayerPrefsSettingsStore(prefix);
                var load = store.Load();
                var save = store.Save(new SettingsDocument());

                Assert.That(load.Success, Is.True);
                Assert.That(load.Source, Is.EqualTo(SettingsStoreSource.Backup));
                Assert.That(save.Success, Is.True);
                Assert.That(PlayerPrefs.GetString(backupKey), Is.EqualTo(validBackup));
            }
            finally
            {
                PlayerPrefs.DeleteKey(primaryKey);
                PlayerPrefs.DeleteKey(backupKey);
            }
        }

        [Test]
        public async Task OnlyOneWritableSessionCanBeOpen()
        {
            var service = CreateService(new MemoryStore(), new RecordingApplier(Volume, Quality));
            await service.InitializeAsync(CancellationToken.None);
            var session = service.OpenSession();

            Assert.Throws<InvalidOperationException>(() => service.OpenSession());

            await session.CancelAsync(CancellationToken.None);
            Assert.That(service.OpenSession(), Is.Not.Null);
        }

        [Test]
        public void StandardCatalog_UsesProjectDefaultsForEveryCommonCategory()
        {
            var defaults = new StandardSettingsDefaults
            {
                MasterVolume = 0.8f,
                MusicVolume = 0.7f,
                SfxVolume = 0.6f,
                InvertY = true,
                Vibration = 0.4f,
                GlyphStyle = "PlayStation",
                BindingOverrides = "{\"bindings\":[]}",
                HighContrast = true,
                ReduceMotion = true
            };
            var definitions = StandardSettingsCatalog.Create(
                defaults,
                () => new[] { defaults.LocaleCode },
                () => new[] { defaults.QualityName });

            SettingValue Default(SettingId id) =>
                definitions.Single(definition => definition.Id == id).DefaultValue;

            Assert.That(Default(StandardSettingIds.MasterVolume).AsFloat(), Is.EqualTo(0.8f));
            Assert.That(Default(StandardSettingIds.MusicVolume).AsFloat(), Is.EqualTo(0.7f));
            Assert.That(Default(StandardSettingIds.SfxVolume).AsFloat(), Is.EqualTo(0.6f));
            Assert.That(Default(StandardSettingIds.InvertY).AsBool(), Is.True);
            Assert.That(Default(StandardSettingIds.Vibration).AsFloat(), Is.EqualTo(0.4f));
            Assert.That(Default(StandardSettingIds.GlyphStyle).AsString(), Is.EqualTo("PlayStation"));
            Assert.That(Default(StandardSettingIds.BindingOverrides).AsString(), Is.EqualTo("{\"bindings\":[]}"));
            Assert.That(Default(StandardSettingIds.HighContrast).AsBool(), Is.True);
            Assert.That(Default(StandardSettingIds.ReduceMotion).AsBool(), Is.True);
        }

        private static SettingsService CreateService(MemoryStore store, RecordingApplier applier)
        {
            var definitions = new[]
            {
                new SettingDefinition(Volume, "audio", SettingValue.Float(1f), SettingApplyPolicy.Preview,
                    "volume", validator: x => x.AsFloat() >= 0f && x.AsFloat() <= 1f),
                new SettingDefinition(Quality, "display", SettingValue.String("High"), SettingApplyPolicy.OnCommit,
                    "quality", optionProvider: () => new[] { "Low", "High" })
            };
            return new SettingsService(new SettingsCatalog(definitions), store, new[] { applier });
        }

        private sealed class MemoryStore : ISettingsStore
        {
            public SettingsDocument Document;
            public bool FailSave;

            public SettingsStoreLoadResult Load() => Document == null
                ? SettingsStoreLoadResult.Missing()
                : SettingsStoreLoadResult.Loaded(Document.Clone(), SettingsStoreSource.Primary);

            public SettingsStoreSaveResult Save(SettingsDocument document)
            {
                if (FailSave)
                {
                    return SettingsStoreSaveResult.Failed("test-save-failure");
                }

                Document = document.Clone();
                return SettingsStoreSaveResult.Saved();
            }
        }

        private sealed class RecordingApplier : ISettingApplier
        {
            public RecordingApplier(params SettingId[] ids)
            {
                SettingIds = ids;
            }

            public IReadOnlyCollection<SettingId> SettingIds { get; }
            public SettingsSnapshot Last { get; private set; }

            public Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
            {
                Last = snapshot;
                return Task.FromResult(SettingApplyResult.Applied());
            }
        }
    }
}
