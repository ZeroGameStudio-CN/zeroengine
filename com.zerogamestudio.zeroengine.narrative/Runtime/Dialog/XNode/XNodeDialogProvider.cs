using System.Collections.Generic;
using ZeroEngine.Dialog.XNodeIntegration;

namespace ZeroEngine.Dialog.Providers
{
    public class XNodeDialogProvider : IDialogProvider
    {
        private readonly DialogGraph _graph;
        private BaseDialogNode _currentNode;
        private readonly Dictionary<string, object> _variables = new Dictionary<string, object>();

        public XNodeDialogProvider(DialogGraph graph)
        {
            _graph = graph;
        }

        public void Begin()
        {
            var startNode = _graph.GetStartNode();
            if (startNode != null)
            {
                _currentNode = startNode.GetNext();
            }
        }

        public void End()
        {
            _currentNode = null;
        }

        public bool CanContinue => _currentNode is LineNode;

        public bool HasChoices => _currentNode is ChoiceNode;

        public DialogLine Continue()
        {
            if (_currentNode is LineNode lineNode)
            {
                var line = new DialogLine
                {
                    Speaker = lineNode.Speaker,
                    Text = lineNode.Text,
                    LocalizationKey = lineNode.LocalizationKey,
                    VoiceKey = lineNode.VoiceKey,
                    PortraitKey = lineNode.PortraitKey
                };

                _currentNode = lineNode.GetNext();
                return line;
            }

            return default;
        }

        public List<DialogChoice> GetChoices()
        {
            if (_currentNode is ChoiceNode choiceNode)
            {
                var list = new List<DialogChoice>();
                for (int i = 0; i < choiceNode.Choices.Count; i++)
                {
                    var opt = choiceNode.Choices[i];
                    // Simple condition check could be added here
                    bool enabled = true;
                    if (!string.IsNullOrEmpty(opt.Condition))
                    {
                        enabled = IsTruthy(GetVariable(opt.Condition));
                    }

                    list.Add(new DialogChoice
                    {
                        Text = opt.Text,
                        LocalizationKey = opt.LocalizationKey,
                        TargetIndex = i, // We use index for xNode dynamic ports
                        IsEnabled = enabled,
                        Condition = opt.Condition
                    });
                }
                return list;
            }
            return new List<DialogChoice>();
        }

        public void SelectChoice(int index)
        {
            if (_currentNode is ChoiceNode choiceNode)
            {
                _currentNode = choiceNode.GetChoiceNode(index);
            }
        }

        public void SetVariable(string name, object value)
        {
            _variables[name] = value;
        }

        public object GetVariable(string name)
        {
            return _variables.ContainsKey(name) ? _variables[name] : null;
        }

        private bool IsTruthy(object val)
        {
            if (val == null) return false;
            if (val is bool b) return b;
            if (val is int i) return i != 0;
            if (val is string s) return !string.IsNullOrEmpty(s);
            return true;
        }
    }
}
