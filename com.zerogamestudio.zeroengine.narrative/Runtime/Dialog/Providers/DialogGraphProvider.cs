using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Dialog.Providers
{
    /// <summary>
    /// IDialogProvider implementation for DialogGraphSO.
    /// Executes dialog graphs with full branching/condition/variable support.
    /// </summary>
    public class DialogGraphProvider : IDialogProvider
    {
        private readonly DialogGraphSO _graph;
        private DialogGraphContext _context;
        private bool _isActive;
        private DialogNodeResult _pendingResult;
        private bool _hasPendingResult;

        /// <summary>
        /// The execution context.
        /// </summary>
        public DialogGraphContext Context => _context;

        /// <summary>
        /// Event fired when a callback node is triggered.
        /// </summary>
        public event Action<string, string> OnCallback;

        public DialogGraphProvider(DialogGraphSO graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _context = new DialogGraphContext(_graph);
        }

        public DialogGraphProvider(DialogGraphSO graph, DialogVariables sharedVariables)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _context = new DialogGraphContext(_graph, sharedVariables);
        }

        #region IDialogProvider

        public void Begin()
        {
            _context = _context ?? new DialogGraphContext(_graph);
            _context.Reset();
            _context.OnCallback += HandleCallback;

            var startNode = _graph.GetStartNode();
            if (startNode == null)
            {
                Debug.LogError($"[DialogGraphProvider] Graph '{_graph.name}' has no Start node");
                return;
            }

            _context.CurrentNode = startNode;
            _isActive = true;
            _hasPendingResult = false;

            // Execute start node immediately (it just passes through)
            ExecuteCurrentNode();
        }

        public void End()
        {
            if (_context != null)
            {
                _context.OnCallback -= HandleCallback;
            }
            _isActive = false;
            _hasPendingResult = false;
        }

        public bool CanContinue
        {
            get
            {
                if (!_isActive || _context == null) return false;
                if (_hasPendingResult) return true;
                return _context.CurrentNode != null;
            }
        }

        public bool HasChoices
        {
            get
            {
                if (!_isActive || _context == null) return false;
                if (_hasPendingResult && _pendingResult.Choices != null && _pendingResult.Choices.Count > 0)
                {
                    return true;
                }
                return false;
            }
        }

        public DialogLine Continue()
        {
            if (!_isActive || _context == null)
                return default;

            // Return pending line if available
            if (_hasPendingResult && _pendingResult.WaitForInput && _pendingResult.Line.Text != null)
            {
                var line = _pendingResult.Line;
                // Don't clear pending yet - will be cleared when DisplayNext is called again
                return line;
            }

            // Execute nodes until we hit a wait point or end
            while (_context.CurrentNode != null)
            {
                var result = ExecuteCurrentNode();

                if (result.NextNodeId == "__END__")
                {
                    _isActive = false;
                    return default;
                }

                if (result.WaitForInput)
                {
                    if (result.Line.Text != null)
                    {
                        return result.Line;
                    }
                    // Waiting for choices or callback
                    return default;
                }

                // Move to next node
                AdvanceToNode(result.NextNodeId);
            }

            _isActive = false;
            return default;
        }

        public List<DialogChoice> GetChoices()
        {
            if (!_isActive || !_hasPendingResult || _pendingResult.Choices == null)
            {
                return new List<DialogChoice>();
            }
            return _pendingResult.Choices;
        }

        public void SelectChoice(int index)
        {
            if (!_isActive || _context == null || _context.PendingChoiceTargets == null)
                return;

            if (index >= 0 && index < _context.PendingChoiceTargets.Count)
            {
                string targetNodeId = _context.PendingChoiceTargets[index];
                _context.PendingChoiceTargets = null;
                _hasPendingResult = false;

                AdvanceToNode(targetNodeId);
            }
        }

        public void SetVariable(string name, object value)
        {
            _context?.Variables.Set(name, value);
        }

        public object GetVariable(string name)
        {
            return _context?.Variables.Get(name);
        }

        #endregion

        #region Execution

        private DialogNodeResult ExecuteCurrentNode()
        {
            var node = _context.CurrentNode;
            if (node == null)
            {
                return DialogNodeResult.End();
            }

            // Record visit
            _context.RecordVisit(node.Id);

            // Execute
            var result = node.Execute(_context);

            if (result.WaitForInput)
            {
                _pendingResult = result;
                _hasPendingResult = true;
            }
            else
            {
                _hasPendingResult = false;
            }

            return result;
        }

        private void AdvanceToNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || nodeId == "__END__")
            {
                _context.CurrentNode = null;
                _isActive = false;
                return;
            }

            var nextNode = _graph.GetNode(nodeId);
            if (nextNode == null)
            {
                Debug.LogWarning($"[DialogGraphProvider] Node '{nodeId}' not found in graph");
                _context.CurrentNode = null;
                _isActive = false;
                return;
            }

            _context.CurrentNode = nextNode;

            // Auto-execute non-blocking nodes
            if (nextNode.Type == DialogNodeType.Condition ||
                nextNode.Type == DialogNodeType.SetVariable ||
                nextNode.Type == DialogNodeType.Random ||
                (nextNode.Type == DialogNodeType.Callback && !((DialogCallbackNode)nextNode).WaitForCompletion))
            {
                var result = ExecuteCurrentNode();
                if (!result.WaitForInput && result.NextNodeId != null)
                {
                    AdvanceToNode(result.NextNodeId);
                }
            }
        }

        /// <summary>
        /// Called when the user advances past a text node.
        /// </summary>
        public void AdvanceFromCurrentNode()
        {
            if (!_isActive || _context == null) return;

            _hasPendingResult = false;

            // Get next node from current
            var current = _context.CurrentNode;
            if (current != null && !string.IsNullOrEmpty(current.OutputNodeId))
            {
                AdvanceToNode(current.OutputNodeId);
            }
            else
            {
                _context.CurrentNode = null;
            }
        }

        private void HandleCallback(string callbackId, string parameter)
        {
            OnCallback?.Invoke(callbackId, parameter);
        }

        /// <summary>
        /// Complete an external callback.
        /// Call this when an async callback finishes.
        /// </summary>
        public void CompleteCallback(string callbackId)
        {
            if (_context == null) return;

            _context.CompleteCallback(callbackId);

            // If we were waiting for this callback, continue
            if (_hasPendingResult && _context.WaitingForCallback == null)
            {
                _hasPendingResult = false;
                if (_context.CurrentNode != null)
                {
                    AdvanceToNode(_context.CurrentNode.OutputNodeId);
                }
            }
        }

        #endregion
    }
}
