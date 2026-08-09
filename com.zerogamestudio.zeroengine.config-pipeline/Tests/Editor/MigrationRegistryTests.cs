using System;
using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class MigrationRegistryTests
    {
        [Test]
        public void Migrate_RequiresAnExplicitCompleteRoute()
        {
            ConfigDocument source = Document(1);
            var registry = new ConfigMigrationRegistry(new IConfigMigration[]
            {
                new TestMigration(1, 2)
            });

            Assert.That(registry.Migrate(source, 2).SchemaVersion, Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() => registry.Migrate(source, 3));
        }

        [Test]
        public void Constructor_RejectsAmbiguousRoutes()
        {
            Assert.Throws<ArgumentException>(
                () => new ConfigMigrationRegistry(new IConfigMigration[]
                {
                    new TestMigration(1, 2),
                    new TestMigration(1, 3)
                }));
        }

        private static ConfigDocument Document(int version)
        {
            return new ConfigDocument(
                "sample",
                "zgs.sample",
                version,
                new ConfigObjectNode(Array.Empty<ConfigProperty>()));
        }

        private sealed class TestMigration : IConfigMigration
        {
            public TestMigration(int sourceVersion, int targetVersion)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
            }

            public string SchemaId => "zgs.sample";

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public ConfigDocument Migrate(ConfigDocument source)
            {
                return Document(TargetVersion);
            }
        }
    }
}
