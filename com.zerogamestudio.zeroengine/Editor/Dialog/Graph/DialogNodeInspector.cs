using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroEngine.Dialog;

namespace ZeroEngine.Editor.Dialog
{
    /// <summary>
    /// Inspector panel for editing selected dialog node properties.
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class DialogNodeInspector : EditorWindow
    {
        private DialogNode _currentNode;
        private DialogGraphSO _currentGraph;
        private ScrollView _scrollView;

        [MenuItem("ZeroEngine/Dialog/Node Inspector")]
        public static void Open()
        {
            var window = GetWindow<DialogNodeInspector>();
            window.titleContent = new GUIContent("Node Inspector");
            window.minSize = new Vector2(300, 400);
        }

        public static void Inspect(DialogNode node, DialogGraphSO graph)
        {
            var window = GetWindow<DialogNodeInspector>();
            window.titleContent = new GUIContent("Node Inspector");
            window.SetNode(node, graph);
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            ZeroEngine.EditorUI.EditorUiElements.ApplyWindowRoot(rootVisualElement);
            rootVisualElement.Add(ZeroEngine.EditorUI.EditorUiElements.CreateHeader(
                "Dialog Node Inspector",
                "Edit the selected node without leaving the graph"));

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            rootVisualElement.Add(_scrollView);

            ShowNoSelection();
        }

        private void ShowNoSelection()
        {
            _scrollView.Clear();
            var label = new Label("Select a node in the Dialog Graph Editor to inspect its properties.");
            label.style.paddingTop = 20;
            label.style.paddingLeft = 10;
            label.style.paddingRight = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = new Color(0.6f, 0.6f, 0.6f);
            _scrollView.Add(label);
        }

        public void SetNode(DialogNode node, DialogGraphSO graph)
        {
            _currentNode = node;
            _currentGraph = graph;

            if (node == null)
            {
                ShowNoSelection();
                return;
            }

            BuildNodeUI();
        }

        private void BuildNodeUI()
        {
            _scrollView.Clear();

            if (_currentNode == null) return;

            // Header
            var header = new VisualElement();
            header.style.backgroundColor = GetNodeColor(_currentNode.Type);
            header.style.paddingLeft = 10;
            header.style.paddingRight = 10;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;

            var titleLabel = new Label(_currentNode.Type.ToString() + " Node");
            titleLabel.style.fontSize = 16;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            header.Add(titleLabel);

            var idLabel = new Label("ID: " + _currentNode.Id);
            idLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            header.Add(idLabel);

            _scrollView.Add(header);

            // Common fields
            AddSection("Common");
            AddTextField("Comment", _currentNode.Comment, v =>
            {
                _currentNode.Comment = v;
                MarkDirty();
            });

            // Type-specific fields
            switch (_currentNode)
            {
                case DialogTextNode textNode:
                    BuildTextNodeUI(textNode);
                    break;
                case DialogChoiceNode choiceNode:
                    BuildChoiceNodeUI(choiceNode);
                    break;
                case DialogConditionNode condNode:
                    BuildConditionNodeUI(condNode);
                    break;
                case DialogSetVariableNode varNode:
                    BuildSetVariableNodeUI(varNode);
                    break;
                case DialogRandomNode randNode:
                    BuildRandomNodeUI(randNode);
                    break;
                case DialogCallbackNode cbNode:
                    BuildCallbackNodeUI(cbNode);
                    break;
                case DialogEndNode endNode:
                    BuildEndNodeUI(endNode);
                    break;
            }
        }

        private void BuildTextNodeUI(DialogTextNode node)
        {
            AddSection("Text Node");

            AddTextField("Speaker", node.Speaker, v =>
            {
                node.Speaker = v;
                MarkDirty();
            });

            AddTextArea("Text", node.Text, v =>
            {
                node.Text = v;
                MarkDirty();
            });

            AddTextField("Localization Key", node.LocalizationKey, v =>
            {
                node.LocalizationKey = v;
                MarkDirty();
            });

            AddTextField("Portrait Key", node.PortraitKey, v =>
            {
                node.PortraitKey = v;
                MarkDirty();
            });

            AddTextField("Voice Key", node.VoiceKey, v =>
            {
                node.VoiceKey = v;
                MarkDirty();
            });
        }

        private void BuildChoiceNodeUI(DialogChoiceNode node)
        {
            AddSection("Choice Node");

            AddTextField("Prompt Text", node.PromptText, v =>
            {
                node.PromptText = v;
                MarkDirty();
            });

            AddSection("Choices");

            for (int i = 0; i < node.Choices.Count; i++)
            {
                int index = i;
                var choice = node.Choices[i];

                var choiceContainer = new Foldout { text = $"Choice {i + 1}" };
                choiceContainer.style.marginLeft = 10;

                var textField = new TextField("Text") { value = choice.Text ?? "" };
                textField.RegisterValueChangedCallback(evt =>
                {
                    node.Choices[index].Text = evt.newValue;
                    MarkDirty();
                });
                choiceContainer.Add(textField);

                var condField = new TextField("Condition") { value = choice.Condition ?? "" };
                condField.RegisterValueChangedCallback(evt =>
                {
                    node.Choices[index].Condition = evt.newValue;
                    MarkDirty();
                });
                choiceContainer.Add(condField);

                var showIfDisabled = new Toggle("Show If Disabled") { value = choice.ShowIfDisabled };
                showIfDisabled.RegisterValueChangedCallback(evt =>
                {
                    node.Choices[index].ShowIfDisabled = evt.newValue;
                    MarkDirty();
                });
                choiceContainer.Add(showIfDisabled);

                _scrollView.Add(choiceContainer);
            }

            // Add/Remove buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 8;
            buttonRow.style.marginLeft = 10;

            var addButton = new Button(() =>
            {
                node.Choices.Add(new DialogChoiceNode.ChoiceOption());
                MarkDirty();
                BuildNodeUI();
            })
            { text = "+ Add Choice" };
            buttonRow.Add(addButton);

            if (node.Choices.Count > 0)
            {
                var removeButton = new Button(() =>
                {
                    node.Choices.RemoveAt(node.Choices.Count - 1);
                    MarkDirty();
                    BuildNodeUI();
                })
                { text = "- Remove Last" };
                buttonRow.Add(removeButton);
            }

            _scrollView.Add(buttonRow);
        }

        private void BuildConditionNodeUI(DialogConditionNode node)
        {
            AddSection("Condition Node");

            AddTextField("Condition Expression", node.Condition, v =>
            {
                node.Condition = v;
                MarkDirty();
            });

            var helpLabel = new Label("Syntax: gold >= 100, hasKey, !isLocked, a && b, a || b");
            helpLabel.style.paddingLeft = 10;
            helpLabel.style.fontSize = 10;
            helpLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _scrollView.Add(helpLabel);
        }

        private void BuildSetVariableNodeUI(DialogSetVariableNode node)
        {
            AddSection("Set Variable Node");

            AddTextField("Variable Name", node.Variable, v =>
            {
                node.Variable = v;
                MarkDirty();
            });

            var opField = new EnumField("Operation", node.Op);
            opField.style.marginLeft = 10;
            opField.style.marginRight = 10;
            opField.RegisterValueChangedCallback(evt =>
            {
                node.Op = (DialogSetVariableNode.Operation)evt.newValue;
                MarkDirty();
            });
            _scrollView.Add(opField);

            AddTextField("Value", node.Value, v =>
            {
                node.Value = v;
                MarkDirty();
            });

            var typeField = new EnumField("Type Hint", node.TypeHint);
            typeField.style.marginLeft = 10;
            typeField.style.marginRight = 10;
            typeField.RegisterValueChangedCallback(evt =>
            {
                node.TypeHint = (DialogVariableType)evt.newValue;
                MarkDirty();
            });
            _scrollView.Add(typeField);
        }

        private void BuildRandomNodeUI(DialogRandomNode node)
        {
            AddSection("Random Node");
            AddSection("Weighted Outputs");

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                int index = i;
                var output = node.Outputs[i];

                var container = new VisualElement();
                container.style.flexDirection = FlexDirection.Row;
                container.style.marginLeft = 10;
                container.style.marginTop = 4;

                var label = new Label($"Output {i + 1}:");
                label.style.width = 70;
                container.Add(label);

                var weightField = new IntegerField { value = output.Weight };
                weightField.style.width = 50;
                weightField.RegisterValueChangedCallback(evt =>
                {
                    node.Outputs[index].Weight = Mathf.Max(1, evt.newValue);
                    MarkDirty();
                });
                container.Add(weightField);

                var weightLabel = new Label(" weight");
                container.Add(weightLabel);

                _scrollView.Add(container);
            }

            // Add/Remove buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 8;
            buttonRow.style.marginLeft = 10;

            var addButton = new Button(() =>
            {
                node.Outputs.Add(new DialogRandomNode.WeightedOutput { Weight = 1 });
                MarkDirty();
                BuildNodeUI();
            })
            { text = "+ Add Output" };
            buttonRow.Add(addButton);

            if (node.Outputs.Count > 1)
            {
                var removeButton = new Button(() =>
                {
                    node.Outputs.RemoveAt(node.Outputs.Count - 1);
                    MarkDirty();
                    BuildNodeUI();
                })
                { text = "- Remove Last" };
                buttonRow.Add(removeButton);
            }

            _scrollView.Add(buttonRow);
        }

        private void BuildCallbackNodeUI(DialogCallbackNode node)
        {
            AddSection("Callback Node");

            AddTextField("Callback ID", node.CallbackId, v =>
            {
                node.CallbackId = v;
                MarkDirty();
            });

            AddTextField("Parameter", node.Parameter, v =>
            {
                node.Parameter = v;
                MarkDirty();
            });

            var waitToggle = new Toggle("Wait For Completion") { value = node.WaitForCompletion };
            waitToggle.style.marginLeft = 10;
            waitToggle.style.marginTop = 8;
            waitToggle.RegisterValueChangedCallback(evt =>
            {
                node.WaitForCompletion = evt.newValue;
                MarkDirty();
            });
            _scrollView.Add(waitToggle);
        }

        private void BuildEndNodeUI(DialogEndNode node)
        {
            AddSection("End Node");

            AddTextField("End Tag", node.EndTag, v =>
            {
                node.EndTag = v;
                MarkDirty();
            });

            var helpLabel = new Label("Use different tags to identify different endings.");
            helpLabel.style.paddingLeft = 10;
            helpLabel.style.fontSize = 10;
            helpLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _scrollView.Add(helpLabel);
        }

        private void AddSection(string title)
        {
            var section = new Label(title);
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.marginTop = 12;
            section.style.marginLeft = 10;
            section.style.marginBottom = 4;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            section.style.paddingBottom = 4;
            _scrollView.Add(section);
        }

        private void AddTextField(string label, string value, System.Action<string> onChange)
        {
            var field = new TextField(label) { value = value ?? "" };
            field.style.marginLeft = 10;
            field.style.marginRight = 10;
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            _scrollView.Add(field);
        }

        private void AddTextArea(string label, string value, System.Action<string> onChange)
        {
            var container = new VisualElement();
            container.style.marginLeft = 10;
            container.style.marginRight = 10;
            container.style.marginTop = 4;

            var labelElement = new Label(label);
            container.Add(labelElement);

            var field = new TextField { value = value ?? "", multiline = true };
            field.style.minHeight = 60;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            container.Add(field);

            _scrollView.Add(container);
        }

        private void MarkDirty()
        {
            if (_currentGraph != null)
            {
                EditorUtility.SetDirty(_currentGraph);
            }
        }

        private Color GetNodeColor(DialogNodeType type)
        {
            return type switch
            {
                DialogNodeType.Start => DialogGraphView.StartNodeColor,
                DialogNodeType.End => DialogGraphView.EndNodeColor,
                DialogNodeType.Text => DialogGraphView.TextNodeColor,
                DialogNodeType.Choice => DialogGraphView.ChoiceNodeColor,
                DialogNodeType.Condition => DialogGraphView.ConditionNodeColor,
                DialogNodeType.SetVariable => DialogGraphView.SetVariableNodeColor,
                DialogNodeType.Random => DialogGraphView.RandomNodeColor,
                DialogNodeType.Callback => DialogGraphView.CallbackNodeColor,
                _ => Color.gray
            };
        }
    }
}
