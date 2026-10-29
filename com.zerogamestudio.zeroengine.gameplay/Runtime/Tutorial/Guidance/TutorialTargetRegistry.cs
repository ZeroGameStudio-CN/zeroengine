using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeroEngine.Tutorial.Guidance
{
    /// <summary>Project-owned targets registered by stable ID, with exact-owner release.</summary>
    public sealed class TutorialTargetRegistry
    {
        private readonly Dictionary<string, Registration> entries = new Dictionary<string, Registration>(StringComparer.Ordinal);

        public IDisposable Register(string id, GameObject target)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A target needs a stable ID.", nameof(id));
            if (!target) throw new ArgumentNullException(nameof(target));
            if (entries.TryGetValue(id, out var previous) && previous.Target)
                throw new InvalidOperationException("A different registration already owns tutorial target: " + id);
            var entry = new Registration(this, id, target);
            entries[id] = entry;
            return entry;
        }

        public GameObject Find(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && entries.TryGetValue(id, out var entry) && entry.Target ? entry.Target : null;
        }

        public GameObject Resolve(TutorialTargetReference reference, Transform scopeRoot)
        {
            var registered = Find(reference.Id);
            if (registered) return registered;
            if (string.IsNullOrWhiteSpace(reference.Path)) return null;
            if (!scopeRoot)
            {
                GameObject candidate = null;
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    Transform found = null;
                    if (reference.Path == root.name) found = root.transform;
                    else if (reference.Path.StartsWith(root.name + "/", StringComparison.Ordinal))
                        found = root.transform.Find(reference.Path.Substring(root.name.Length + 1));
                    else if (!reference.Path.Contains("/"))
                    {
                        foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (child.name != reference.Path) continue;
                            if (candidate) return null;
                            candidate = child.gameObject;
                        }
                    }
                    if (!found) continue;
                    if (candidate) return null;
                    candidate = found.gameObject;
                }
                return candidate;
            }
            if (reference.Path.Contains("/")) return scopeRoot.Find(reference.Path)?.gameObject;
            GameObject result = null;
            foreach (var child in scopeRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != reference.Path) continue;
                if (result) return null; // Never guess among duplicate names.
                result = child.gameObject;
            }
            return result;
        }

        private sealed class Registration : IDisposable
        {
            private TutorialTargetRegistry owner;
            private readonly string id;
            internal readonly GameObject Target;
            internal Registration(TutorialTargetRegistry owner, string id, GameObject target) { this.owner = owner; this.id = id; Target = target; }
            public void Dispose()
            {
                if (owner != null && owner.entries.TryGetValue(id, out var current) && ReferenceEquals(current, this)) owner.entries.Remove(id);
                owner = null;
            }
        }
    }
}
