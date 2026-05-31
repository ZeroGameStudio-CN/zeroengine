using System.Collections.Generic;

namespace ZeroEngine.Dialog.Providers
{
    /// <summary>
    /// Provider implementation for DialogueSO (ScriptableObject-based dialogues).
    /// </summary>
    public class DialogueSOProvider : IDialogProvider
    {
        private readonly DialogueSO _dialogue;
        private int _currentIndex;
        private readonly Dictionary<string, object> _variables = new();

        public DialogueSOProvider(DialogueSO dialogue)
        {
            _dialogue = dialogue;
        }

        public void Begin()
        {
            _currentIndex = _dialogue.StartIndex;
            _variables.Clear();
        }

        public void End()
        {
            _currentIndex = -1;
        }

        public bool CanContinue => _currentIndex >= 0 && _currentIndex < _dialogue.Entries.Count;

        public bool HasChoices
        {
            get
            {
                if (!CanContinue) return false;
                var entry = _dialogue.Entries[_currentIndex];
                return entry.Choices != null && entry.Choices.Count > 0;
            }
        }

        public DialogLine Continue()
        {
            if (!CanContinue) return default;

            var entry = _dialogue.Entries[_currentIndex];

            var line = new DialogLine
            {
                Speaker = entry.Speaker,
                Text = entry.Text,
                LocalizationKey = entry.LocalizationKey,
                VoiceKey = entry.VoiceKey,
                PortraitKey = entry.PortraitKey
            };

            // If no choices, auto-advance
            if (!HasChoices)
            {
                _currentIndex = entry.NextIndex;
            }

            return line;
        }

        public List<DialogChoice> GetChoices()
        {
            if (!HasChoices) return new List<DialogChoice>();

            var entry = _dialogue.Entries[_currentIndex];
            var choices = new List<DialogChoice>();

            foreach (var c in entry.Choices)
            {
                bool enabled = true;
                if (!string.IsNullOrEmpty(c.Condition))
                {
                    enabled = _variables.TryGetValue(c.Condition, out var val) && IsTruthy(val);
                }

                choices.Add(new DialogChoice
                {
                    Text = c.Text,
                    LocalizationKey = c.LocalizationKey,
                    TargetIndex = c.TargetIndex,
                    Condition = c.Condition,
                    IsEnabled = enabled
                });
            }

            return choices;
        }

        public void SelectChoice(int index)
        {
            if (!HasChoices) return;
            var entry = _dialogue.Entries[_currentIndex];
            if (index >= 0 && index < entry.Choices.Count)
            {
                var choiceEntry = entry.Choices[index];
                _currentIndex = choiceEntry.TargetIndex;
            }
        }

        public void SetVariable(string name, object value)
        {
            _variables[name] = value;
        }

        public object GetVariable(string name)
        {
            return _variables.TryGetValue(name, out var val) ? val : null;
        }

        private static bool IsTruthy(object val)
        {
            if (val == null) return false;
            if (val is bool b) return b;
            if (val is int i) return i != 0;
            if (val is float f) return f != 0f;
            if (val is string s) return !string.IsNullOrEmpty(s);
            return true;
        }
    }
}
