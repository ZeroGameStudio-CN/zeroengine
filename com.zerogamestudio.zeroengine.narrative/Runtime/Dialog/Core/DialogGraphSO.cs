using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Dialog
{
    /// <summary>
    /// ScriptableObject container for a dialog graph.
    /// Stores nodes and their connections.
    /// </summary>
    [CreateAssetMenu(fileName = "NewDialogGraph", menuName = "ZeroEngine/Dialog/Dialog Graph")]
    public class DialogGraphSO : ScriptableObject
    {
        [Tooltip("Graph display name")]
        public string DisplayName;

        [Tooltip("Description/notes for this dialog")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("Default variable values")]
        public List<DialogVariable> DefaultVariables = new();

        [SerializeReference]
        [Tooltip("All nodes in this graph")]
        public List<DialogNode> Nodes = new();

        // Cached lookup
        private Dictionary<string, DialogNode> _nodeCache;
        private bool _cacheValid;

        #region Public API

        /// <summary>
        /// Get the start node.
        /// </summary>
        public DialogNode GetStartNode()
        {
            EnsureCache();
            return _nodeCache.TryGetValue("Start", out var node) ? node : null;
        }

        /// <summary>
        /// Get a node by ID.
        /// </summary>
        public DialogNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureCache();
            return _nodeCache.TryGetValue(id, out var node) ? node : null;
        }

        /// <summary>
        /// Get all nodes of a specific type.
        /// </summary>
        public List<T> GetNodes<T>() where T : DialogNode
        {
            var result = new List<T>();
            foreach (var node in Nodes)
            {
                if (node is T typed)
                {
                    result.Add(typed);
                }
            }
            return result;
        }

        /// <summary>
        /// Check if a node exists.
        /// </summary>
        public bool HasNode(string id)
        {
            EnsureCache();
            return _nodeCache.ContainsKey(id);
        }

        #endregion

        #region Editor API

        /// <summary>
        /// Add a node to the graph.
        /// </summary>
        public void AddNode(DialogNode node)
        {
            if (node == null) return;

            // Generate unique ID if needed
            if (string.IsNullOrEmpty(node.Id))
            {
                node.Id = GenerateUniqueId(node.Type.ToString());
            }

            Nodes.Add(node);
            InvalidateCache();
        }

        /// <summary>
        /// Remove a node from the graph.
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
        /// Generate a unique node ID.
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
        /// Validate the graph structure.
        /// </summary>
        public List<string> Validate()
        {
            var issues = DialogGraphValidator.Validate(this, DialogGraphValidationOptions.Default);
            var errors = new List<string>(issues.Count);
            foreach (var issue in issues)
            {
                errors.Add(issue.Message);
            }
            return errors;
        }

        /// <summary>
        /// Create a basic graph with Start and End nodes.
        /// </summary>
        public void CreateDefaultGraph()
        {
            Nodes.Clear();

            var startNode = new DialogStartNode
            {
                EditorPosition = new Vector2(100, 200)
            };

            var textNode = new DialogTextNode
            {
                Id = "Text_1",
                Speaker = "NPC",
                Text = "Hello, adventurer!",
                EditorPosition = new Vector2(350, 200)
            };

            var endNode = new DialogEndNode
            {
                EditorPosition = new Vector2(600, 200)
            };

            startNode.OutputNodeId = textNode.Id;
            textNode.OutputNodeId = endNode.Id;

            Nodes.Add(startNode);
            Nodes.Add(textNode);
            Nodes.Add(endNode);

            InvalidateCache();
        }

        #endregion

        #region Internal

        private void EnsureCache()
        {
            if (_cacheValid && _nodeCache != null) return;

            _nodeCache = new Dictionary<string, DialogNode>(Nodes.Count);
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

        #region Serialization Helpers

        /// <summary>
        /// Create a node of the specified type.
        /// </summary>
        public static DialogNode CreateNode(DialogNodeType type)
        {
            return type switch
            {
                DialogNodeType.Start => new DialogStartNode(),
                DialogNodeType.Text => new DialogTextNode(),
                DialogNodeType.Choice => new DialogChoiceNode(),
                DialogNodeType.Condition => new DialogConditionNode(),
                DialogNodeType.SetVariable => new DialogSetVariableNode(),
                DialogNodeType.Random => new DialogRandomNode(),
                DialogNodeType.Callback => new DialogCallbackNode(),
                DialogNodeType.End => new DialogEndNode(),
                _ => null
            };
        }

        #endregion
    }
}
