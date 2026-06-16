using UnityEngine;
using ZeroEngine.Dialog.Providers;
using System.Collections.Generic;
using ZeroEngine.Dialog.XNodeIntegration;

namespace ZeroEngine.Dialog
{
    public class DialogDebugger : MonoBehaviour
    {
        [Header("Data Sources")]
        public DialogueSO dialogueSO;

        public DialogGraph dialogGraph;

        [Header("State")]
        [SerializeField] private bool _autoAdvance = false;
        [SerializeField] private float _autoAdvanceDelay = 2f;

        private void OnEnable()
        {
            DialogManager.Instance.OnDialogStart += OnStart;
            DialogManager.Instance.OnDialogEnd += OnEnd;
            DialogManager.Instance.OnLineDisplayed += OnLine;
            DialogManager.Instance.OnChoicesPresented += OnChoices;
            DialogManager.Instance.OnChoiceSelected += OnChoice;
        }

        private void OnDisable()
        {
            if (DialogManager.Instance != null)
            {
                DialogManager.Instance.OnDialogStart -= OnStart;
                DialogManager.Instance.OnDialogEnd -= OnEnd;
                DialogManager.Instance.OnLineDisplayed -= OnLine;
                DialogManager.Instance.OnChoicesPresented -= OnChoices;
                DialogManager.Instance.OnChoiceSelected -= OnChoice;
            }
        }

        public void StartSO()
        {
            if (dialogueSO != null)
                DialogManager.Instance.StartDialogue(dialogueSO);
            else
                Debug.LogWarning("No DialogueSO assigned");
        }

        public void StartGraph()
        {
            if (dialogGraph != null)
                DialogManager.Instance.StartDialogue(new XNodeDialogProvider(dialogGraph));
            else
                Debug.LogWarning("No DialogGraph assigned");
        }

        public void Next()
        {
            DialogManager.Instance.DisplayNext();
        }

        public void Choose(int index)
        {
            DialogManager.Instance.SelectChoice(index);
        }

        public void Stop()
        {
            DialogManager.Instance.StopDialogue();
        }

        // Event Handlers
        private void OnStart() => Debug.Log("<color=green>[Dialog] Started</color>");
        private void OnEnd() => Debug.Log("<color=red>[Dialog] Ended</color>");
        
        private void OnLine(DialogLine line)
        {
            Debug.Log($"<color=cyan>[Dialog] {line.Speaker}:</color> {line.Text} " + 
                      $"{(string.IsNullOrEmpty(line.VoiceKey) ? "" : $"🔊({line.VoiceKey})")}");
            
            if (_autoAdvance)
            {
                Invoke(nameof(Next), _autoAdvanceDelay);
            }
        }

        private void OnChoices(List<DialogChoice> choices)
        {
            string log = "<color=yellow>[Dialog] Choices:</color>\n";
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                log += $"{i}: {c.Text} {(c.IsEnabled ? "" : "(LOCKED)")}\n";
            }
            Debug.Log(log);
        }

        private void OnChoice(int index)
        {
             Debug.Log($"<color=orange>[Dialog] Selected Choice: {index}</color>");
        }
    }
}
