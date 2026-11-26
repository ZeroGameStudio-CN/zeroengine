using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.BehaviorTree
{
    /// <summary>
    /// Node type enumeration for serialization.
    /// </summary>
    public enum BTNodeType
    {
        // Composites
        Sequence,
        Selector,
        Parallel,

        // Decorators
        Repeater,
        Inverter,
        AlwaysSucceed,
        AlwaysFail,
        Conditional,

        // Leaves
        Action,
        Wait,
        Log,

        // Integration
        RunFSM
    }

    /// <summary>
    /// Serializable node data for BTTreeAsset.
    /// </summary>
    [Serializable]
    public class BTNodeData
    {
        [Tooltip("Unique ID")]
        public string Id;

        [Tooltip("Display name")]
        public string Name;

        [Tooltip("Node type")]
        public BTNodeType Type;

        [Tooltip("Position in editor")]
        public Vector2 EditorPosition;

        [Tooltip("Parent node ID (null for root)")]
        public string ParentId;

        [Tooltip("Child node IDs (for composites)")]
        public List<string> ChildIds = new();

        // Type-specific data
        [Tooltip("Repeat count (Repeater: -1 for infinite)")]
        public int RepeatCount = -1;

        [Tooltip("Wait duration (WaitNode)")]
        public float WaitDuration = 1f;

        [Tooltip("Log message (LogNode)")]
        public string LogMessage;

        [Tooltip("Abort mode (Conditional)")]
        public AbortMode AbortMode = AbortMode.None;

        [Tooltip("Parallel success policy")]
        public ParallelSuccessPolicy SuccessPolicy = ParallelSuccessPolicy.RequireAll;

        [Tooltip("Parallel failure policy")]
        public ParallelFailurePolicy FailurePolicy = ParallelFailurePolicy.RequireOne;

        [Tooltip("Comment/notes")]
        public string Comment;
    }

    /// <summary>
    /// Parallel node success policy.
    /// </summary>
    public enum ParallelSuccessPolicy
    {
        RequireOne,
        RequireAll
    }

    /// <summary>
    /// Parallel node failure policy.
    /// </summary>
    public enum ParallelFailurePolicy
    {
        RequireOne,
        RequireAll
    }

    /// <summary>
    /// ScriptableObject for serializable behavior tree asset.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBTTree", menuName = "ZeroEngine/AI and Behavior/Behavior Trees/Behavior Tree Asset")]
    public class BTTreeAsset : ScriptableObject
    {
        [Tooltip("Tree display name")]
        public string DisplayName;

        [Tooltip("Description/notes")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("Root node ID")]
        public string RootNodeId;

        [Tooltip("All nodes in tree")]
        public List<BTNodeData> Nodes = new();

        // Cached lookup
        private Dictionary<string, BTNodeData> _nodeCache;
        private bool _cacheValid;

        #region Public API

        /// <summary>
        /// Get the root node.
        /// </summary>
        public BTNodeData GetRootNode()
        {
            return GetNode(RootNodeId);
        }

        /// <summary>
        /// Get a node by ID.
        /// </summary>
        public BTNodeData GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureCache();
            return _nodeCache.TryGetValue(id, out var node) ? node : null;
        }

        /// <summary>
        /// Get all nodes of a type.
        /// </summary>
        public List<BTNodeData> GetNodes(BTNodeType type)
        {
            var result = new List<BTNodeData>();
            foreach (var node in Nodes)
            {
                if (node.Type == type)
                {
                    result.Add(node);
                }
            }
            return result;
        }

        /// <summary>
        /// Add a node to the tree.
        /// </summary>
        public void AddNode(BTNodeData node)
        {
            if (node == null) return;

            if (string.IsNullOrEmpty(node.Id))
            {
                node.Id = GenerateUniqueId(node.Type.ToString());
            }

            Nodes.Add(node);
            InvalidateCache();
        }

        /// <summary>
        /// Remove a node from the tree.
        /// </summary>
        public bool RemoveNode(string id)
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
            {
                if (Nodes[i].Id == id)
                {
                    Nodes.RemoveAt(i);
                    InvalidateCache();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Generate unique node ID.
        /// </summary>
        public string GenerateUniqueId(string prefix)
        {
            EnsureCache();
            int counter = 1;
            string id;
            do
            {
                id = $"{prefix}_{counter}";
                counter++;
            } while (_nodeCache.ContainsKey(id));
            return id;
        }

        /// <summary>
        /// Build a runtime BehaviorTree from this asset.
        /// </summary>
        public BehaviorTree BuildRuntimeTree(GameObject owner)
        {
            var tree = new BehaviorTree(owner);

            var rootData = GetRootNode();
            if (rootData != null)
            {
                var rootNode = BuildNode(rootData);
                tree.SetRoot(rootNode);
            }

            return tree;
        }

        private IBTNode BuildNode(BTNodeData data)
        {
            IBTNode node = data.Type switch
            {
                // Composites
                BTNodeType.Sequence => BuildComposite(new Sequence(), data),
                BTNodeType.Selector => BuildComposite(new Selector(), data),
                BTNodeType.Parallel => BuildParallel(data),

                // Decorators
                BTNodeType.Repeater => BuildDecorator(new Repeater(data.RepeatCount), data),
                BTNodeType.Inverter => BuildDecorator(new Inverter(), data),
                BTNodeType.AlwaysSucceed => BuildDecorator(new AlwaysSucceed(), data),
                BTNodeType.AlwaysFail => BuildDecorator(new AlwaysFail(), data),

                // Leaves
                BTNodeType.Wait => new WaitNode(data.WaitDuration),
                BTNodeType.Log => new LogNode(data.LogMessage ?? ""),

                _ => null
            };

            if (node is BTNode btNode)
            {
                btNode.Name = data.Name;
            }

            return node;
        }

        private BTComposite BuildComposite(BTComposite composite, BTNodeData data)
        {
            composite.Name = data.Name;

            foreach (var childId in data.ChildIds)
            {
                var childData = GetNode(childId);
                if (childData != null)
                {
                    var childNode = BuildNode(childData);
                    if (childNode != null)
                    {
                        composite.AddChild(childNode);
                    }
                }
            }

            return composite;
        }

        private Parallel BuildParallel(BTNodeData data)
        {
            // Convert BTNodeData policies to runtime ParallelPolicy
            var successPolicy = data.SuccessPolicy == ParallelSuccessPolicy.RequireAll
                ? ParallelPolicy.RequireAll
                : ParallelPolicy.RequireOne;
            var failurePolicy = data.FailurePolicy == ParallelFailurePolicy.RequireAll
                ? ParallelPolicy.RequireAll
                : ParallelPolicy.RequireOne;

            var parallel = new Parallel(successPolicy, failurePolicy);
            parallel.Name = data.Name;

            foreach (var childId in data.ChildIds)
            {
                var childData = GetNode(childId);
                if (childData != null)
                {
                    var childNode = BuildNode(childData);
                    if (childNode != null)
                    {
                        parallel.AddChild(childNode);
                    }
                }
            }

            return parallel;
        }

        private BTDecorator BuildDecorator(BTDecorator decorator, BTNodeData data)
        {
            decorator.Name = data.Name;

            if (data.ChildIds.Count > 0)
            {
                var childData = GetNode(data.ChildIds[0]);
                if (childData != null)
                {
                    var childNode = BuildNode(childData);
                    if (childNode != null)
                    {
                        decorator.SetChild(childNode);
                    }
                }
            }

            return decorator;
        }

        /// <summary>
        /// Create a default tree with root sequence.
        /// </summary>
        public void CreateDefaultTree()
        {
            Nodes.Clear();

            var rootNode = new BTNodeData
            {
                Id = "Root",
                Name = "Root",
                Type = BTNodeType.Sequence,
                EditorPosition = new Vector2(300, 100)
            };

            var logNode = new BTNodeData
            {
                Id = "Log_1",
                Name = "Log Hello",
                Type = BTNodeType.Log,
                LogMessage = "Hello from BehaviorTree!",
                ParentId = "Root",
                EditorPosition = new Vector2(300, 250)
            };

            rootNode.ChildIds.Add(logNode.Id);

            Nodes.Add(rootNode);
            Nodes.Add(logNode);
            RootNodeId = rootNode.Id;

            InvalidateCache();
        }

        /// <summary>
        /// Validate tree structure.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            EnsureCache();

            if (string.IsNullOrEmpty(RootNodeId))
            {
                errors.Add("No root node specified");
            }
            else if (!_nodeCache.ContainsKey(RootNodeId))
            {
                errors.Add($"Root node '{RootNodeId}' not found");
            }

            // Check for orphaned nodes
            var reachable = new HashSet<string>();
            if (!string.IsNullOrEmpty(RootNodeId))
            {
                CollectReachable(RootNodeId, reachable);
            }

            foreach (var node in Nodes)
            {
                if (!reachable.Contains(node.Id))
                {
                    errors.Add($"Node '{node.Id}' is not reachable from root");
                }

                // Check child references
                foreach (var childId in node.ChildIds)
                {
                    if (!_nodeCache.ContainsKey(childId))
                    {
                        errors.Add($"Node '{node.Id}' references non-existent child '{childId}'");
                    }
                }
            }

            // Check for duplicate IDs
            var seenIds = new HashSet<string>();
            foreach (var node in Nodes)
            {
                if (!seenIds.Add(node.Id))
                {
                    errors.Add($"Duplicate node ID: '{node.Id}'");
                }
            }

            return errors;
        }

        private void CollectReachable(string nodeId, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(nodeId) || visited.Contains(nodeId)) return;
            visited.Add(nodeId);

            var node = GetNode(nodeId);
            if (node == null) return;

            foreach (var childId in node.ChildIds)
            {
                CollectReachable(childId, visited);
            }
        }

        #endregion

        #region Internal

        private void EnsureCache()
        {
            if (_cacheValid && _nodeCache != null) return;

            _nodeCache = new Dictionary<string, BTNodeData>(Nodes.Count);
            foreach (var node in Nodes)
            {
                if (!string.IsNullOrEmpty(node.Id))
                {
                    _nodeCache[node.Id] = node;
                }
            }
            _cacheValid = true;
        }

        private void InvalidateCache()
        {
            _cacheValid = false;
        }

        private void OnValidate()
        {
            InvalidateCache();
        }

        #endregion

        #region Factory

        /// <summary>
        /// Create a node of specified type.
        /// </summary>
        public static BTNodeData CreateNodeData(BTNodeType type)
        {
            return new BTNodeData
            {
                Type = type,
                Name = type.ToString()
            };
        }

        #endregion
    }
}
