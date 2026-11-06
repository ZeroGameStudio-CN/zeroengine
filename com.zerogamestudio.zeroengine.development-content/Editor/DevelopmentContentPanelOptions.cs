using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZeroEngine.DevelopmentContent.Editor
{
    public sealed class DevelopmentContentPanelOptions
    {
        public string ProjectId { get; }
        public string ProjectRoot { get; }
        public string RegistryPath { get; }
        public IDevelopmentContentInventory Inventory { get; }
        public string SessionStatePrefix { get; }

        public DevelopmentContentPanelOptions(string projectId, string projectRoot, string registryRelativePath,
            IDevelopmentContentInventory inventory, string sessionStatePrefix = null)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("Project ID is required.", nameof(projectId));
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
            ProjectId = projectId;
            ProjectRoot = Path.GetFullPath(projectRoot);
            RegistryPath = DevelopmentContentPaths.ResolveProjectPath(ProjectRoot, registryRelativePath)
                ?? throw new ArgumentException("Registry must be a project-relative file.", nameof(registryRelativePath));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            string sessionRoot = Path.DirectorySeparatorChar == '\\' ? ProjectRoot.ToUpperInvariant() : ProjectRoot;
            using var hash = SHA256.Create();
            string rootKey = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(sessionRoot))).Replace("-", string.Empty);
            SessionStatePrefix = sessionStatePrefix ?? "ZeroEngine.DevelopmentContent." + projectId + "." + rootKey + ".";
        }
    }
}
