using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.BehaviorTree;

namespace ZeroEngine.Editor.BehaviorTree
{
    /// <summary>
    /// Runtime BehaviorTree viewer window.
    /// Displays tree structure and execution state in real-time.
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class BehaviorTreeViewerWindow : EditorWindow
    {
        [MenuItem("ZeroEngine/BehaviorTree Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<BehaviorTreeViewerWindow>("BT Viewer");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        // Selected tree to view
        private ZeroEngine.BehaviorTree.BehaviorTree _selectedTree;
        private Vector2 _scrollPosition;
        private Dictionary<IBTNode, bool> _foldoutStates = new();

        // Colors for different states
        private static readonly Color RunningColor = new Color(0.2f, 0.6f, 1f);
        private static readonly Color SuccessColor = new Color(0.2f, 0.8f, 0.2f);
        private static readonly Color FailureColor = new Color(0.8f, 0.2f, 0.2f);
        private static readonly Color IdleColor = new Color(0.5f, 0.5f, 0.5f);

        // Registered trees for selection
        private static readonly List<WeakReference<ZeroEngine.BehaviorTree.BehaviorTree>> RegisteredTrees = new();

        /// <summary>
        /// Register a tree for viewing (call from BehaviorTree.Start or custom manager).
        /// </summary>
        public static void RegisterTree(ZeroEngine.BehaviorTree.BehaviorTree tree)
        {
            if (tree == null) return;

            // Clean up dead references
            RegisteredTrees.RemoveAll(wr => !wr.TryGetTarget(out _));

            // Check if already registered
            foreach (var wr in RegisteredTrees)
            {
                if (wr.TryGetTarget(out var existing) && existing == tree)
                    return;
            }

            RegisteredTrees.Add(new WeakReference<ZeroEngine.BehaviorTree.BehaviorTree>(tree));
        }

        /// <summary>
        /// Unregister a tree.
        /// </summary>
        public static void UnregisterTree(ZeroEngine.BehaviorTree.BehaviorTree tree)
        {
            RegisteredTrees.RemoveAll(wr =>
            {
                if (!wr.TryGetTarget(out var t)) return true;
                return t == tree;
            });
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _selectedTree = null;
                RegisteredTrees.Clear();
            }
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Behavior Tree Viewer", "Inspect live tree execution state");
            DrawToolbar();
            EditorGUILayout.Space(4);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view behavior trees.", MessageType.Info);
                return;
            }

            if (_selectedTree == null)
            {
                EditorGUILayout.HelpBox("Select a BehaviorTree to view.", MessageType.Info);
                return;
            }

            DrawTreeInfo();
            EditorGUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawTreeView();
            EditorGUILayout.EndScrollView();

            // Auto-repaint during play
            if (Application.isPlaying && _selectedTree != null && _selectedTree.IsRunning)
            {
                Repaint();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Tree selection dropdown
            if (GUILayout.Button("Select Tree", EditorStyles.toolbarDropDown, GUILayout.Width(100)))
            {
                ShowTreeSelectionMenu();
            }

            GUILayout.FlexibleSpace();

            // Refresh button
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RegisteredTrees.RemoveAll(wr => !wr.TryGetTarget(out _));
            }

            // Expand/Collapse all
            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                SetAllFoldouts(true);
            }

            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                SetAllFoldouts(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ShowTreeSelectionMenu()
        {
            var menu = new GenericMenu();

            // Clean up dead references
            RegisteredTrees.RemoveAll(wr => !wr.TryGetTarget(out _));

            if (RegisteredTrees.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No trees registered"));
            }
            else
            {
                foreach (var wr in RegisteredTrees)
                {
                    if (wr.TryGetTarget(out var tree))
                    {
                        string name = tree.Owner?.ToString() ?? "Unknown";
                        menu.AddItem(new GUIContent(name), _selectedTree == tree, () =>
                        {
                            _selectedTree = tree;
                        });
                    }
                }
            }

            menu.ShowAsContext();
        }

        private void DrawTreeInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Owner info
            string ownerName = _selectedTree.Owner?.ToString() ?? "Unknown";
            EditorGUILayout.LabelField("Owner", ownerName);

            // State
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("State", GUILayout.Width(50));

            GUI.color = GetStateColor(_selectedTree.CurrentState);
            EditorGUILayout.LabelField(_selectedTree.CurrentState.ToString(), EditorStyles.boldLabel);
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(_selectedTree.IsRunning ? "Running" : "Stopped",
                _selectedTree.IsRunning ? EditorStyles.boldLabel : EditorStyles.label);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawTreeView()
        {
            if (_selectedTree.Root == null)
            {
                EditorGUILayout.LabelField("No root node", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawNode(_selectedTree.Root, 0);
        }

        private void DrawNode(IBTNode node, int depth)
        {
            if (node == null) return;

            // Indentation
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 20);

            // Get node info
            string typeName = GetNodeTypeName(node);
            NodeState state = node.CurrentState;
            bool hasChildren = HasChildren(node);

            // Foldout for composite/decorator nodes
            if (hasChildren)
            {
                if (!_foldoutStates.ContainsKey(node))
                    _foldoutStates[node] = true;

                _foldoutStates[node] = EditorGUILayout.Foldout(_foldoutStates[node], "", true);
                GUILayout.Space(-18); // Overlap with content
            }
            else
            {
                GUILayout.Space(14);
            }

            // State indicator
            DrawStateIndicator(state);

            // Node type and name
            GUI.color = GetStateColor(state);
            GUIStyle style = state == NodeState.Running ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField(typeName, style);
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();

            // Draw children
            if (hasChildren && _foldoutStates.TryGetValue(node, out bool expanded) && expanded)
            {
                DrawChildren(node, depth + 1);
            }
        }

        private void DrawStateIndicator(NodeState state)
        {
            Color color = GetStateColor(state);
            Rect rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
            rect.y += 2;

            // Draw circle
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, 8, 8), color);
        }

        private bool HasChildren(IBTNode node)
        {
            if (node is BTComposite) return true;
            if (node is BTDecorator) return true;
            return false;
        }

        private void DrawChildren(IBTNode node, int depth)
        {
            if (node is BTComposite composite)
            {
                var childrenField = typeof(BTComposite).GetField("_children",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (childrenField?.GetValue(composite) is List<IBTNode> children)
                {
                    foreach (var child in children)
                    {
                        DrawNode(child, depth);
                    }
                }
            }
            else if (node is BTDecorator decorator)
            {
                var childField = typeof(BTDecorator).GetField("_child",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (childField?.GetValue(decorator) is IBTNode child)
                {
                    DrawNode(child, depth);
                }
            }
        }

        private string GetNodeTypeName(IBTNode node)
        {
            if (node == null) return "null";

            string typeName = node.GetType().Name;

            // Add extra info for specific node types
            if (node is LogNode logNode)
            {
                var messageField = typeof(LogNode).GetField("_message",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                string msg = messageField?.GetValue(logNode) as string;
                if (!string.IsNullOrEmpty(msg))
                {
                    typeName += $" \"{(msg.Length > 20 ? msg.Substring(0, 20) + "..." : msg)}\"";
                }
            }
            else if (node is WaitNode waitNode)
            {
                var durationField = typeof(WaitNode).GetField("_duration",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (durationField != null)
                {
                    float duration = (float)durationField.GetValue(waitNode);
                    typeName += $" ({duration:F1}s)";
                }
            }

            return typeName;
        }

        private static Color GetStateColor(NodeState state)
        {
            return state switch
            {
                NodeState.Running => RunningColor,
                NodeState.Success => SuccessColor,
                NodeState.Failure => FailureColor,
                _ => IdleColor
            };
        }

        private void SetAllFoldouts(bool expanded)
        {
            var keys = new List<IBTNode>(_foldoutStates.Keys);
            foreach (var key in keys)
            {
                _foldoutStates[key] = expanded;
            }
        }
    }
}
