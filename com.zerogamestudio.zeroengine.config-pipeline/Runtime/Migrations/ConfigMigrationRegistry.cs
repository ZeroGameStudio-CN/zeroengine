using System;
using System.Collections.Generic;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigMigrationRegistry
    {
        private readonly Dictionary<string, Dictionary<int, IConfigMigration>> migrationsBySchema =
            new Dictionary<string, Dictionary<int, IConfigMigration>>(StringComparer.Ordinal);

        public ConfigMigrationRegistry(IEnumerable<IConfigMigration> migrations)
        {
            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            foreach (IConfigMigration migration in migrations)
            {
                if (migration == null)
                {
                    throw new ArgumentException("Migration entries cannot be null.", nameof(migrations));
                }

                if (migration.SourceVersion <= 0 ||
                    migration.TargetVersion <= migration.SourceVersion)
                {
                    throw new ArgumentException(
                        "Migrations must move from a positive version to a greater version.",
                        nameof(migrations));
                }

                if (!migrationsBySchema.TryGetValue(
                        migration.SchemaId,
                        out Dictionary<int, IConfigMigration> bySourceVersion))
                {
                    bySourceVersion = new Dictionary<int, IConfigMigration>();
                    migrationsBySchema.Add(migration.SchemaId, bySourceVersion);
                }

                if (bySourceVersion.ContainsKey(migration.SourceVersion))
                {
                    throw new ArgumentException(
                        "Migration routes cannot be ambiguous for schema '" +
                        migration.SchemaId + "' version " + migration.SourceVersion + ".",
                        nameof(migrations));
                }

                bySourceVersion.Add(migration.SourceVersion, migration);
            }
        }

        public ConfigDocument Migrate(ConfigDocument source, int targetVersion)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (targetVersion < source.SchemaVersion)
            {
                throw new NotSupportedException("Schema downgrade migrations are not supported.");
            }

            if (targetVersion == source.SchemaVersion)
            {
                return source;
            }

            ConfigDocument current = source;
            while (current.SchemaVersion < targetVersion)
            {
                if (!migrationsBySchema.TryGetValue(
                        current.SchemaId,
                        out Dictionary<int, IConfigMigration> bySourceVersion) ||
                    !bySourceVersion.TryGetValue(
                        current.SchemaVersion,
                        out IConfigMigration migration) ||
                    migration.TargetVersion > targetVersion)
                {
                    throw new NotSupportedException(
                        "No explicit migration is registered for schema '" +
                        current.SchemaId + "' version " + current.SchemaVersion +
                        " toward version " + targetVersion + ".");
                }

                ConfigDocument next = migration.Migrate(current);
                if (next == null ||
                    !string.Equals(next.ConfigSetId, current.ConfigSetId, StringComparison.Ordinal) ||
                    !string.Equals(next.SchemaId, current.SchemaId, StringComparison.Ordinal) ||
                    next.SchemaVersion != migration.TargetVersion)
                {
                    throw new InvalidOperationException(
                        "Migration returned a document with an invalid identity or target version.");
                }

                current = next;
            }

            return current;
        }
    }
}
