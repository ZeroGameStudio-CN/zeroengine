using System;
using System.IO;
using System.Linq;

namespace ZeroEngine.EditorTools
{
    /// <summary>
    /// Byte snapshot for one explicitly supplied file. This grants no write authority and does
    /// not discover files, expand asset/meta pairs, import assets, clear dirty state or own Undo.
    /// </summary>
    public sealed class EditorFileSnapshot
    {
        private readonly byte[] _bytes;

        public EditorFileSnapshot(string path, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Snapshot path is required.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(projectRoot) || !Path.IsPathFullyQualified(projectRoot))
            {
                throw new ArgumentException("An absolute project root is required.", nameof(projectRoot));
            }

            string root = Path.GetFullPath(projectRoot);
            bool absolute = Path.IsPathRooted(path);
            if (absolute && !Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("Rooted snapshot paths must be fully qualified.", nameof(path));
            }

            FullPath = Path.GetFullPath(absolute ? path : Path.Combine(root, path));
            AssetPath = absolute ? FullPath.Replace('\\', '/') : Path.GetRelativePath(root, FullPath).Replace('\\', '/');
            if (!absolute && (AssetPath == ".." || AssetPath.StartsWith("../", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Relative snapshot path escapes the project root.", nameof(path));
            }

            if (Directory.Exists(FullPath))
            {
                throw new ArgumentException("Snapshot requires an exact file, not a directory.", nameof(path));
            }

            IsAssetFile = !absolute && AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
            Existed = File.Exists(FullPath);
            _bytes = Existed ? File.ReadAllBytes(FullPath) : null;
        }

        public string AssetPath { get; }
        public string FullPath { get; }
        public bool Existed { get; }
        public bool IsAssetFile { get; }
        public bool IsMeta => AssetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        public string ImportAssetPath => IsAssetFile && IsMeta ? AssetPath.Substring(0, AssetPath.Length - 5) : AssetPath;
        public bool ShouldDeleteCreatedAsset => !Existed && IsAssetFile && !IsMeta;
        public bool ShouldImportAfterRestore => Existed && IsAssetFile && File.Exists(FullPath);

        public bool MatchesCurrent()
        {
            if (Directory.Exists(FullPath))
            {
                return false;
            }

            bool exists = File.Exists(FullPath);
            return exists == Existed && (!exists || _bytes.SequenceEqual(File.ReadAllBytes(FullPath)));
        }

        /// <summary>
        /// Restore only this file. Parent recreation is opt-in so scoped authoring can fail
        /// closed if its established folder disappears. Unchanged bytes are never rewritten.
        /// </summary>
        public void Restore(bool recreateMissingParent = false)
        {
            if (Directory.Exists(FullPath))
            {
                throw new IOException($"A directory now occupies snapshot file '{AssetPath}'.");
            }

            if (!Existed)
            {
                if (File.Exists(FullPath))
                {
                    File.Delete(FullPath);
                }

                return;
            }

            string parent = Path.GetDirectoryName(FullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                if (!recreateMissingParent || string.IsNullOrEmpty(parent))
                {
                    throw new DirectoryNotFoundException($"Snapshot parent folder is unavailable for '{AssetPath}'.");
                }

                Directory.CreateDirectory(parent);
            }

            if (!MatchesCurrent())
            {
                File.WriteAllBytes(FullPath, _bytes);
            }
        }

        public void AssertRestored()
        {
            if (!MatchesCurrent())
            {
                throw new IOException($"Exact-file rollback did not restore '{AssetPath}' byte-for-byte.");
            }
        }
    }
}
