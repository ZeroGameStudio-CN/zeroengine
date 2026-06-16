using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Dialog.Providers;

namespace ZeroEngine.Dialog
{
    /// <summary>
    /// High-level runner for DialogGraph execution.
    /// Provides events and easy integration with UI systems.
    /// </summary>
    public class DialogRunner : MonoBehaviour
    {
        #region Settings

        [Header("Configuration")]
        [Tooltip("Auto-start dialog on Awake")]
        [SerializeField] private bool _autoStart;

        [Tooltip("Dialog graph to run (optional, can be set via code)")]
        [SerializeField] private DialogGraphSO _dialogGraph;

        [Header("Typewriter")]
        [SerializeField] private bool _enableTypewriter = true;
        [SerializeField] private float _charactersPerSecond = 30f;

        #endregion

        #region Events

        /// <summary>Fired when dialog starts.</summary>
        public event Action OnDialogStart;

        /// <summary>Fired when dialog ends.</summary>
        public event Action<string> OnDialogEnd; // endTag

        /// <summary>Fired when a line should be displayed.</summary>
        public event Action<DialogLine> OnLineDisplay;

        /// <summary>Fired for typewriter updates.</summary>
        public event Action<string, float> OnTypewriterUpdate; // text, progress (0-1)

        /// <summary>Fired when typewriter finishes.</summary>
        public event Action OnTypewriterComplete;

        /// <summary>Fired when choices are available.</summary>
        public event Action<List<DialogChoice>> OnChoicesAvailable;

        /// <summary>Fired when a choice is selected.</summary>
        public event Action<int, DialogChoice> OnChoiceSelected;

        /// <summary>Fired when an external callback is triggered.</summary>
        public event Action<string, string> OnCallback;

        /// <summary>Fired when a callback is parsed as a structured command.</summary>
        public event Action<DialogCommand> OnCommand;

        /// <summary>Fired when a variable changes.</summary>
        public event Action<string, object, object> OnVariableChanged;

        #endregion

        #region State

        private DialogGraphProvider _provider;
        private Coroutine _typewriterCoroutine;
        private bool _isTypewriting;
        private string _currentFullText;
        private bool _isWaitingForInput;

        /// <summary>Is a dialog currently running?</summary>
        public bool IsRunning => _provider != null && _provider.CanContinue;

        /// <summary>Is typewriter effect active?</summary>
        public bool IsTypewriting => _isTypewriting;

        /// <summary>Is waiting for user input (advance or choice)?</summary>
        public bool IsWaitingForInput => _isWaitingForInput;

        /// <summary>Current dialog graph.</summary>
        public DialogGraphSO CurrentGraph => _dialogGraph;

        /// <summary>Execution context (for variable access).</summary>
        public DialogGraphContext Context => _provider?.Context;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_autoStart && _dialogGraph != null)
            {
                StartDialog(_dialogGraph);
            }
        }

        private void OnDestroy()
        {
            StopDialog();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start running a dialog graph.
        /// </summary>
        public void StartDialog(DialogGraphSO graph)
        {
            StartDialog(graph, new DialogVariables());
        }

        /// <summary>
        /// Start with shared variables (for persistent state across dialogs).
        /// </summary>
        public void StartDialog(DialogGraphSO graph, DialogVariables sharedVariables)
        {
            if (graph == null)
            {
                Debug.LogWarning("[DialogRunner] Cannot start null dialog");
                return;
            }

            StopDialog();

            _dialogGraph = graph;
            _provider = new DialogGraphProvider(graph, sharedVariables);
            _provider.OnCallback += HandleCallback;

            if (_provider.Context != null)
            {
                _provider.Context.Variables.OnVariableChanged += HandleVariableChanged;
            }

            _provider.Begin();

            OnDialogStart?.Invoke();

            ProcessNext();
        }

        /// <summary>
        /// Advance to the next content.
        /// If typewriter is active, skips to end.
        /// </summary>
        public void Advance()
        {
            if (!IsRunning) return;

            if (_isTypewriting)
            {
                SkipTypewriter();
                return;
            }

            if (!_isWaitingForInput) return;

            _isWaitingForInput = false;
            _provider.AdvanceFromCurrentNode();
            ProcessNext();
        }

        /// <summary>
        /// Select a choice by index.
        /// </summary>
        public void SelectChoice(int index)
        {
            if (!IsRunning || !_provider.HasChoices) return;

            var choices = _provider.GetChoices();
            if (index < 0 || index >= choices.Count) return;

            var selectedChoice = choices[index];
            if (!selectedChoice.IsEnabled) return;

            _isWaitingForInput = false;
            OnChoiceSelected?.Invoke(index, selectedChoice);

            _provider.SelectChoice(index);
            ProcessNext();
        }

        /// <summary>
        /// Force stop the current dialog.
        /// </summary>
        public void StopDialog()
        {
            StopTypewriter();

            if (_provider != null)
            {
                if (_provider.Context != null)
                {
                    _provider.Context.Variables.OnVariableChanged -= HandleVariableChanged;
                }
                _provider.OnCallback -= HandleCallback;
                _provider.End();
                _provider = null;
            }

            _isWaitingForInput = false;
        }

        /// <summary>
        /// Skip typewriter to show full text.
        /// </summary>
        public void SkipTypewriter()
        {
            if (!_isTypewriting) return;
            StopTypewriter();
            OnTypewriterUpdate?.Invoke(_currentFullText, 1f);
            OnTypewriterComplete?.Invoke();
        }

        /// <summary>
        /// Complete an external callback (resume dialog).
        /// </summary>
        public void CompleteCallback(string callbackId)
        {
            _provider?.CompleteCallback(callbackId);
            if (!_provider.HasChoices && _provider.CanContinue)
            {
                ProcessNext();
            }
        }

        /// <summary>
        /// Set a variable in the current dialog.
        /// </summary>
        public void SetVariable(string name, object value)
        {
            _provider?.SetVariable(name, value);
        }

        /// <summary>
        /// Get a variable from the current dialog.
        /// </summary>
        public T GetVariable<T>(string name, T defaultValue = default)
        {
            if (_provider?.Context?.Variables == null) return defaultValue;
            return _provider.Context.Variables.Get<T>(name, defaultValue);
        }

        #endregion

        #region Internal

        private void ProcessNext()
        {
            if (_provider == null || !_provider.CanContinue)
            {
                EndDialog();
                return;
            }

            if (_provider.HasChoices)
            {
                _isWaitingForInput = true;
                var choices = _provider.GetChoices();
                OnChoicesAvailable?.Invoke(choices);
            }
            else
            {
                var line = _provider.Continue();

                if (string.IsNullOrEmpty(line.Text) && !_provider.CanContinue)
                {
                    EndDialog();
                    return;
                }

                if (!string.IsNullOrEmpty(line.Text))
                {
                    _isWaitingForInput = true;
                    OnLineDisplay?.Invoke(line);

                    if (_enableTypewriter)
                    {
                        StartTypewriter(line.Text);
                    }
                    else
                    {
                        OnTypewriterUpdate?.Invoke(line.Text, 1f);
                        OnTypewriterComplete?.Invoke();
                    }
                }
                else if (_provider.CanContinue)
                {
                    // Node that doesn't produce visible content, continue
                    ProcessNext();
                }
            }
        }

        private void EndDialog()
        {
            string endTag = _provider?.Context?.EndTag;
            StopDialog();
            OnDialogEnd?.Invoke(endTag);
        }

        private void StartTypewriter(string text)
        {
            StopTypewriter();
            _currentFullText = text;
            _typewriterCoroutine = StartCoroutine(TypewriterRoutine(text));
        }

        private void StopTypewriter()
        {
            if (_typewriterCoroutine != null)
            {
                StopCoroutine(_typewriterCoroutine);
                _typewriterCoroutine = null;
            }
            _isTypewriting = false;
        }

        private System.Collections.IEnumerator TypewriterRoutine(string text)
        {
            _isTypewriting = true;
            float delay = 1f / Mathf.Max(_charactersPerSecond, 1f);
            int length = text.Length;

            for (int i = 1; i <= length; i++)
            {
                OnTypewriterUpdate?.Invoke(text.Substring(0, i), (float)i / length);
                yield return new WaitForSeconds(delay);
            }

            _isTypewriting = false;
            _typewriterCoroutine = null;
            OnTypewriterComplete?.Invoke();
        }

        private void HandleCallback(string callbackId, string parameter)
        {
            if (DialogCommandParser.TryParse(callbackId, parameter, out var command))
            {
                OnCommand?.Invoke(command);
                return;
            }

            OnCallback?.Invoke(callbackId, parameter);
        }

        private void HandleVariableChanged(string name, object oldValue, object newValue)
        {
            OnVariableChanged?.Invoke(name, oldValue, newValue);
        }

        #endregion
    }
}
