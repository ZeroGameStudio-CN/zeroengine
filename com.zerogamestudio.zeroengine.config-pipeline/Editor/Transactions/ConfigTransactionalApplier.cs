using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigApplyResult
    {
        internal ConfigApplyResult(string planId, int changedFileCount)
        {
            PlanId = planId;
            ChangedFileCount = changedFileCount;
        }

        public string PlanId { get; }

        public int ChangedFileCount { get; }
    }

    public sealed class ConfigTransactionalApplier
    {
        public ConfigApplyResult Apply(
            string projectRoot,
            ConfigPipelinePlan plan,
            string currentPackageIdentity,
            IReadOnlyList<ConfigArtifact> regeneratedArtifacts,
            Func<bool> postCommitCheck)
        {
            return Apply(
                projectRoot,
                plan,
                currentPackageIdentity,
                regeneratedArtifacts,
                postCommitCheck,
                ConfigTransactionFault.None);
        }

        internal ConfigApplyResult Apply(
            string projectRoot,
            ConfigPipelinePlan plan,
            string currentPackageIdentity,
            IReadOnlyList<ConfigArtifact> regeneratedArtifacts,
            Func<bool> postCommitCheck,
            ConfigTransactionFault fault)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (postCommitCheck == null)
            {
                throw new ArgumentNullException(nameof(postCommitCheck));
            }

            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            string operationRoot = Path.Combine(root, ".zgs-config");
            string lockDirectory = Path.Combine(operationRoot, "locks");
            Directory.CreateDirectory(lockDirectory);
            string lockPath = Path.Combine(lockDirectory, "pipeline.lock");
            using (var configLock = new FileStream(
                       lockPath,
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
                RecoverPendingLocked(root);
                RevalidatePlan(root, plan, currentPackageIdentity, regeneratedArtifacts);
                List<ConfigPlanEntry> changes = plan.Entries
                    .Where(entry => entry.Action != ConfigPlanAction.Unchanged)
                    .ToList();
                if (changes.Count == 0)
                {
                    if (!postCommitCheck())
                    {
                        throw new InvalidOperationException("Post-Apply Check failed for a current plan.");
                    }

                    return new ConfigApplyResult(plan.PlanId, 0);
                }

                string transactionDirectory = TransactionDirectory(root, plan.PlanId);
                if (Directory.Exists(transactionDirectory))
                {
                    throw new InvalidOperationException(
                        "Transaction directory still exists after recovery: " + plan.PlanId);
                }

                Directory.CreateDirectory(transactionDirectory);
                string stageDirectory = Path.Combine(transactionDirectory, "stage");
                string backupDirectory = Path.Combine(transactionDirectory, "backup");
                Directory.CreateDirectory(stageDirectory);
                Directory.CreateDirectory(backupDirectory);
                var artifacts = regeneratedArtifacts.ToDictionary(
                    artifact => ConfigPathGuard.NormalizeRelativePath(artifact.RelativePath),
                    artifact => artifact,
                    StringComparer.Ordinal);
                var journalEntries = Prepare(
                    root,
                    changes,
                    artifacts,
                    stageDirectory,
                    backupDirectory);
                WriteJournal(transactionDirectory, plan.PlanId, journalEntries);
                if (fault == ConfigTransactionFault.AfterPrepared)
                {
                    throw new ConfigSimulatedCrashException("Simulated crash after transaction prepare.");
                }

                bool leaveForRecovery = false;
                try
                {
                    int committed = 0;
                    foreach (JournalEntry entry in journalEntries)
                    {
                        ApplyEntry(root, transactionDirectory, entry);
                        committed++;
                        if (fault == ConfigTransactionFault.AfterFirstCommit && committed == 1)
                        {
                            leaveForRecovery = true;
                            throw new ConfigSimulatedCrashException(
                                "Simulated crash after first committed file.");
                        }
                    }

                    if (!postCommitCheck())
                    {
                        throw new InvalidOperationException("Post-Apply Check failed.");
                    }

                    DeleteTransactionDirectory(transactionDirectory, root);
                    return new ConfigApplyResult(plan.PlanId, changes.Count);
                }
                catch
                {
                    if (!leaveForRecovery && fault != ConfigTransactionFault.AfterPrepared)
                    {
                        Rollback(root, transactionDirectory, journalEntries);
                        DeleteTransactionDirectory(transactionDirectory, root);
                    }

                    throw;
                }
            }
        }

        public void RecoverPending(string projectRoot)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            string operationRoot = Path.Combine(root, ".zgs-config");
            string lockDirectory = Path.Combine(operationRoot, "locks");
            Directory.CreateDirectory(lockDirectory);
            string recoveryLockPath = Path.Combine(lockDirectory, "pipeline.lock");
            using (var recoveryLock = new FileStream(
                       recoveryLockPath,
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
                RecoverPendingLocked(root);
            }
        }

        private static void RecoverPendingLocked(string projectRoot)
        {
            string transactionRoot = Path.Combine(projectRoot, ".zgs-config", "transactions");
            if (!Directory.Exists(transactionRoot))
            {
                return;
            }

            foreach (string transactionDirectory in Directory
                         .EnumerateDirectories(transactionRoot)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string journalPath = Path.Combine(transactionDirectory, "journal.json");
                if (!File.Exists(journalPath))
                {
                    DeleteTransactionDirectory(transactionDirectory, projectRoot);
                    continue;
                }

                IReadOnlyList<JournalEntry> entries = ReadJournal(journalPath);
                Rollback(projectRoot, transactionDirectory, entries);
                DeleteTransactionDirectory(transactionDirectory, projectRoot);
            }
        }

        private static void RevalidatePlan(
            string projectRoot,
            ConfigPipelinePlan plan,
            string packageIdentity,
            IReadOnlyList<ConfigArtifact> regeneratedArtifacts)
        {
            if (!string.Equals(
                    plan.PackageIdentity,
                    packageIdentity,
                    StringComparison.Ordinal))
            {
                throw new ConfigPlanStaleException("Package identity changed after Plan.");
            }

            foreach (KeyValuePair<string, string> input in plan.InputHashes)
            {
                string absolute = ConfigPathGuard.ResolveInside(projectRoot, input.Key);
                if (!File.Exists(absolute) ||
                    !string.Equals(
                        ConfigPipelinePlanBuilder.HashFile(absolute),
                        input.Value,
                        StringComparison.Ordinal))
                {
                    throw new ConfigPlanStaleException(
                        "Plan input changed after Plan: " + input.Key);
                }
            }

            var regenerated = new Dictionary<string, ConfigArtifact>(StringComparer.Ordinal);
            foreach (ConfigArtifact artifact in regeneratedArtifacts)
            {
                string path = ConfigPathGuard.NormalizeRelativePath(artifact.RelativePath);
                if (regenerated.ContainsKey(path))
                {
                    throw new ConfigPlanStaleException("Regeneration produced duplicate path: " + path);
                }

                regenerated.Add(path, artifact);
            }

            foreach (ConfigPlanEntry entry in plan.Entries)
            {
                string absolute = ConfigPathGuard.ResolveInside(projectRoot, entry.RelativePath);
                string currentHash = File.Exists(absolute)
                    ? ConfigPipelinePlanBuilder.HashFile(absolute)
                    : null;
                if (!string.Equals(currentHash, entry.ExistingHash, StringComparison.Ordinal))
                {
                    throw new ConfigPlanStaleException(
                        "Planned output baseline changed: " + entry.RelativePath);
                }

                if (entry.PlannedHash != null)
                {
                    if (!regenerated.TryGetValue(entry.RelativePath, out ConfigArtifact artifact) ||
                        !string.Equals(
                            ConfigHash.Sha256(artifact.Content),
                            entry.PlannedHash,
                            StringComparison.Ordinal))
                    {
                        throw new ConfigPlanStaleException(
                            "Regenerated artifact differs from Plan: " + entry.RelativePath);
                    }
                }
            }

            var allowedArtifacts = new HashSet<string>(
                plan.Entries
                    .Where(entry => entry.PlannedHash != null)
                    .Select(entry => entry.RelativePath),
                StringComparer.Ordinal);
            if (regenerated.Keys.Any(path => !allowedArtifacts.Contains(path)))
            {
                throw new ConfigPlanStaleException("Regeneration produced a path outside the Plan.");
            }
        }

        private static List<JournalEntry> Prepare(
            string projectRoot,
            IReadOnlyList<ConfigPlanEntry> changes,
            IReadOnlyDictionary<string, ConfigArtifact> artifacts,
            string stageDirectory,
            string backupDirectory)
        {
            var entries = new List<JournalEntry>();
            for (int index = 0; index < changes.Count; index++)
            {
                ConfigPlanEntry change = changes[index];
                string target = ConfigPathGuard.ResolveInside(projectRoot, change.RelativePath);
                bool hadOriginal = File.Exists(target);
                string backupName = index.ToString("D6") + ".bak";
                string stageName = index.ToString("D6") + ".new";
                if (hadOriginal)
                {
                    CopyDurable(target, Path.Combine(backupDirectory, backupName));
                }

                if (change.Action == ConfigPlanAction.Create ||
                    change.Action == ConfigPlanAction.Update)
                {
                    byte[] content = artifacts[change.RelativePath].Content;
                    using (var stream = new FileStream(
                               Path.Combine(stageDirectory, stageName),
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        stream.Write(content, 0, content.Length);
                        stream.Flush(true);
                    }
                }

                entries.Add(new JournalEntry(
                    change.RelativePath,
                    change.Action,
                    hadOriginal,
                    backupName,
                    stageName));
            }

            return entries;
        }

        private static void CopyDurable(string source, string destination)
        {
            using (FileStream input = File.OpenRead(source))
            using (var output = new FileStream(
                       destination,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }

        private static void ApplyEntry(
            string projectRoot,
            string transactionDirectory,
            JournalEntry entry)
        {
            string target = ConfigPathGuard.ResolveInside(projectRoot, entry.RelativePath);
            if (entry.Action == ConfigPlanAction.Delete)
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                return;
            }

            string parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            string staged = Path.Combine(transactionDirectory, "stage", entry.StageName);
            File.Copy(staged, target, true);
        }

        private static void Rollback(
            string projectRoot,
            string transactionDirectory,
            IEnumerable<JournalEntry> entries)
        {
            foreach (JournalEntry entry in entries.Reverse())
            {
                string target = ConfigPathGuard.ResolveInside(projectRoot, entry.RelativePath);
                if (entry.HadOriginal)
                {
                    string backup = Path.Combine(
                        transactionDirectory,
                        "backup",
                        entry.BackupName);
                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    File.Copy(backup, target, true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
        }

        private static void WriteJournal(
            string transactionDirectory,
            string planId,
            IEnumerable<JournalEntry> entries)
        {
            var entryNodes = entries.Select(entry => (ConfigNode)new ConfigObjectNode(new[]
            {
                new ConfigProperty("path", new ConfigStringNode(entry.RelativePath)),
                new ConfigProperty(
                    "action",
                    new ConfigStringNode(entry.Action.ToString().ToLowerInvariant())),
                new ConfigProperty("hadOriginal", new ConfigBooleanNode(entry.HadOriginal)),
                new ConfigProperty("backup", new ConfigStringNode(entry.BackupName)),
                new ConfigProperty("stage", new ConfigStringNode(entry.StageName))
            }));
            byte[] bytes = CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
            {
                new ConfigProperty("formatVersion", new ConfigIntegerNode(1)),
                new ConfigProperty("planId", new ConfigStringNode(planId)),
                new ConfigProperty("entries", new ConfigArrayNode(entryNodes))
            }));
            string path = Path.Combine(transactionDirectory, "journal.json");
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static IReadOnlyList<JournalEntry> ReadJournal(string path)
        {
            ConfigNode parsed = ConfigJsonParser.Parse(File.ReadAllBytes(path));
            if (!(parsed is ConfigObjectNode root) ||
                !root.TryGetValue("entries", out ConfigNode entriesNode) ||
                !(entriesNode is ConfigArrayNode entries))
            {
                throw new InvalidDataException("Transaction journal is invalid.");
            }

            var result = new List<JournalEntry>();
            foreach (ConfigNode entryNode in entries.Items)
            {
                if (!(entryNode is ConfigObjectNode entry))
                {
                    throw new InvalidDataException("Transaction journal entry is invalid.");
                }

                string relativePath = ConfigPathGuard.NormalizeRelativePath(ReadString(entry, "path"));
                string backup = RequireJournalFileName(ReadString(entry, "backup"), ".bak");
                string stage = RequireJournalFileName(ReadString(entry, "stage"), ".new");
                result.Add(new JournalEntry(
                    relativePath,
                    ParseAction(ReadString(entry, "action")),
                    ReadBoolean(entry, "hadOriginal"),
                    backup,
                    stage));
            }

            return result;
        }

        private static string RequireJournalFileName(string value, string suffix)
        {
            if (value.Length != 10 || !value.EndsWith(suffix, StringComparison.Ordinal) ||
                value.Take(6).Any(character => character < '0' || character > '9'))
            {
                throw new InvalidDataException("Transaction journal file name is invalid.");
            }

            return value;
        }

        private static string ReadString(ConfigObjectNode node, string property)
        {
            if (!node.TryGetValue(property, out ConfigNode value) ||
                !(value is ConfigStringNode text))
            {
                throw new InvalidDataException("Transaction journal property is invalid: " + property);
            }

            return text.Value;
        }

        private static bool ReadBoolean(ConfigObjectNode node, string property)
        {
            if (!node.TryGetValue(property, out ConfigNode value) ||
                !(value is ConfigBooleanNode boolean))
            {
                throw new InvalidDataException("Transaction journal property is invalid: " + property);
            }

            return boolean.Value;
        }

        private static ConfigPlanAction ParseAction(string value)
        {
            switch (value)
            {
                case "create":
                    return ConfigPlanAction.Create;
                case "update":
                    return ConfigPlanAction.Update;
                case "delete":
                    return ConfigPlanAction.Delete;
                default:
                    throw new InvalidDataException("Transaction journal action is invalid.");
            }
        }

        private static string TransactionDirectory(string projectRoot, string planId)
        {
            return Path.Combine(projectRoot, ".zgs-config", "transactions", planId);
        }

        private static void DeleteTransactionDirectory(
            string transactionDirectory,
            string projectRoot)
        {
            string expectedRoot = Path.Combine(
                ConfigPathGuard.NormalizeProjectRoot(projectRoot),
                ".zgs-config",
                "transactions") + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(transactionDirectory);
            if (!resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a transaction outside its root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, true);
            }
        }

        private sealed class JournalEntry
        {
            public JournalEntry(
                string relativePath,
                ConfigPlanAction action,
                bool hadOriginal,
                string backupName,
                string stageName)
            {
                RelativePath = relativePath;
                Action = action;
                HadOriginal = hadOriginal;
                BackupName = backupName;
                StageName = stageName;
            }

            public string RelativePath { get; }

            public ConfigPlanAction Action { get; }

            public bool HadOriginal { get; }

            public string BackupName { get; }

            public string StageName { get; }
        }
    }

    internal enum ConfigTransactionFault
    {
        None,
        AfterPrepared,
        AfterFirstCommit
    }

    internal sealed class ConfigSimulatedCrashException : Exception
    {
        public ConfigSimulatedCrashException(string message)
            : base(message)
        {
        }
    }

    public sealed class ConfigPlanStaleException : Exception
    {
        public ConfigPlanStaleException(string message)
            : base(message)
        {
        }
    }
}
