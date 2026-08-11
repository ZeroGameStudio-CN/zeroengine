using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroEngine.Tutorial;

namespace ZeroEngine.Editor.Tutorial
{
    /// <summary>
    /// Tutorial GraphView Editor Window (v1.14.0+)
    /// Visual editor for creating and editing tutorial sequences
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class TutorialGraphEditorWindow : EditorWindow
    {
        private TutorialGraphView _graphView;
        private TutorialSequenceSO _currentSequence;
        private TutorialNodeInspector _inspector;
        private Label _titleLabel;
        private VisualElement _inspectorContainer;

        public static void Open()
        {
            var window = GetWindow<TutorialGraphEditorWindow>();
            window.titleContent = new GUIContent("Tutorial Graph", EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
            window.minSize = new Vector2(1000, 600);
        }

        /// <summary>
        /// Open a specific sequence for editing
        /// </summary>
        public static void Open(TutorialSequenceSO sequence)
        {
            var window = GetWindow<TutorialGraphEditorWindow>();
            window.titleContent = new GUIContent("Tutorial Graph", EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
            window.minSize = new Vector2(1000, 600);
            window.LoadSequence(sequence);
        }

        /// <summary>
        /// Double-click on TutorialSequenceSO to open in editor
        /// </summary>
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618
            var asset = EditorUtility.InstanceIDToObject(instanceId) as TutorialSequenceSO;
#pragma warning restore CS0618
            if (asset != null)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            BuildUI();

            // Restore last edited sequence
            if (_currentSequence == null)
            {
                string lastPath = EditorPrefs.GetString("ZeroEngine_LastTutorialSequence", "");
                if (!string.IsNullOrEmpty(lastPath))
                {
                    var sequence = AssetDatabase.LoadAssetAtPath<TutorialSequenceSO>(lastPath);
                    if (sequence != null)
                    {
                        LoadSequence(sequence);
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                _graphView.SaveGraphPositions();
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            ZeroEngine.EditorUI.EditorUiElements.ApplyWindowRoot(rootVisualElement);
            rootVisualElement.Add(ZeroEngine.EditorUI.EditorUiElements.CreateHeader(
                "Tutorial Graph",
                "Build and inspect tutorial sequences"));

            // Main container with split view
            var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            rootVisualElement.Add(splitView);

            // Left pane - Graph View
            var leftPane = new VisualElement();
            leftPane.style.flexGrow = 1;
            splitView.Add(leftPane);

            // Toolbar
            var toolbar = new Toolbar();

            // File menu
            var fileButton = new ToolbarButton(() => ShowFileMenu()) { text = "File" };
            toolbar.Add(fileButton);

            // Add step menu
            var addStepButton = new ToolbarButton(() => ShowAddStepMenu()) { text = "+ Add Step" };
            toolbar.Add(addStepButton);

            // Spacer
            toolbar.Add(new ToolbarSpacer { flex = true });

            // Title
            _titleLabel = new Label("No Sequence Loaded");
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.fontSize = 14;
            toolbar.Add(_titleLabel);

            toolbar.Add(new ToolbarSpacer { flex = true });

            // Validate button
            var validateButton = new ToolbarButton(ValidateSequence) { text = "Validate" };
            toolbar.Add(validateButton);

            // Save button
            var saveButton = new ToolbarButton(SaveSequence) { text = "Save" };
            toolbar.Add(saveButton);

            leftPane.Add(toolbar);

            // Graph view
            _graphView = new TutorialGraphView(this);
            _graphView.StretchToParentSize();
            _graphView.style.top = 22;
            leftPane.Add(_graphView);

            // Object field for sequence selection
            var sequenceField = new ObjectField("Sequence")
            {
                objectType = typeof(TutorialSequenceSO),
                value = _currentSequence
            };
            sequenceField.style.position = Position.Absolute;
            sequenceField.style.right = 10;
            sequenceField.style.top = 26;
            sequenceField.style.width = 250;
            sequenceField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != _currentSequence)
                {
                    LoadSequence(evt.newValue as TutorialSequenceSO);
                }
            });
            leftPane.Add(sequenceField);

            // Right pane - Inspector
            var rightPane = new VisualElement();
            rightPane.style.minWidth = 250;
            rightPane.style.backgroundColor = ZeroEngine.EditorUI.EditorUiPalette.Current.Surface;
            splitView.Add(rightPane);

            // Inspector header
            var inspectorHeader = new Label("Step Inspector");
            inspectorHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            inspectorHeader.style.fontSize = 14;
            inspectorHeader.style.paddingLeft = 10;
            inspectorHeader.style.paddingTop = 10;
            inspectorHeader.style.paddingBottom = 10;
            inspectorHeader.style.backgroundColor = ZeroEngine.EditorUI.EditorUiPalette.Current.RaisedSurface;
            rightPane.Add(inspectorHeader);

            // Inspector container
            _inspectorContainer = new VisualElement();
            _inspectorContainer.style.paddingLeft = 10;
            _inspectorContainer.style.paddingRight = 10;
            _inspectorContainer.style.paddingTop = 10;
            rightPane.Add(_inspectorContainer);

            _inspector = new TutorialNodeInspector(_inspectorContainer);
        }

        public void LoadSequence(TutorialSequenceSO sequence)
        {
            if (_graphView != null && _currentSequence != null)
            {
                _graphView.SaveGraphPositions();
            }

            _currentSequence = sequence;

            if (_currentSequence != null)
            {
                _titleLabel.text = string.IsNullOrEmpty(_currentSequence.DisplayName)
                    ? _currentSequence.name
                    : _currentSequence.DisplayName;
                EditorPrefs.SetString("ZeroEngine_LastTutorialSequence", AssetDatabase.GetAssetPath(_currentSequence));
            }
            else
            {
                _titleLabel.text = "No Sequence Loaded";
            }

            _graphView?.LoadSequence(_currentSequence);

            // Update object field
            var objectField = rootVisualElement.Q<ObjectField>();
            if (objectField != null)
            {
                objectField.SetValueWithoutNotify(_currentSequence);
            }

            // Clear inspector
            _inspector?.ClearSelection();
        }

        public TutorialSequenceSO CurrentSequence => _currentSequence;

        public void SelectStep(TutorialStep step)
        {
            _inspector?.ShowStepInspector(step, _currentSequence);
        }

        public void ClearSelection()
        {
            _inspector?.ClearSelection();
        }

        private void ShowFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Sequence"), false, CreateNewSequence);
            menu.AddItem(new GUIContent("Open Sequence..."), false, OpenSequenceDialog);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save"), false, SaveSequence);
            menu.AddItem(new GUIContent("Save As..."), false, SaveSequenceAs);
            menu.ShowAsContext();
        }

        private void ShowAddStepMenu()
        {
            if (_currentSequence == null)
            {
                EditorUtility.DisplayDialog("No Sequence", "Please load or create a sequence first.", "OK");
                return;
            }

            var menu = new GenericMenu();

            // Basic steps
            menu.AddItem(new GUIContent("Dialogue Step"), false, () => _graphView.CreateStep(typeof(DialogueStep)));
            menu.AddItem(new GUIContent("Highlight Step"), false, () => _graphView.CreateStep(typeof(HighlightStep)));
            menu.AddSeparator("");

            // Wait steps
            menu.AddItem(new GUIContent("Wait/Wait Input"), false, () => _graphView.CreateStep(typeof(WaitInputStep)));
            menu.AddItem(new GUIContent("Wait/Wait Interaction"), false, () => _graphView.CreateStep(typeof(WaitInteractionStep)));
            menu.AddItem(new GUIContent("Wait/Wait Event"), false, () => _graphView.CreateStep(typeof(WaitEventStep)));
            menu.AddSeparator("");

            // Navigation steps
            menu.AddItem(new GUIContent("Navigation/Move To"), false, () => _graphView.CreateStep(typeof(MoveToStep)));
            menu.AddSeparator("");

            // Utility steps
            menu.AddItem(new GUIContent("Utility/Delay"), false, () => _graphView.CreateStep(typeof(DelayStep)));
            menu.AddItem(new GUIContent("Utility/Callback"), false, () => _graphView.CreateStep(typeof(CallbackStep)));
            menu.AddItem(new GUIContent("Utility/Composite"), false, () => _graphView.CreateStep(typeof(CompositeStep)));

            menu.ShowAsContext();
        }

        private void CreateNewSequence()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create New Tutorial Sequence",
                "NewTutorialSequence",
                "asset",
                "Create a new tutorial sequence asset");

            if (string.IsNullOrEmpty(path)) return;

            var sequence = CreateInstance<TutorialSequenceSO>();
            sequence.SequenceId = System.IO.Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(sequence, path);
            AssetDatabase.SaveAssets();

            LoadSequence(sequence);
        }

        private void OpenSequenceDialog()
        {
            string path = EditorUtility.OpenFilePanel("Open Tutorial Sequence", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
            {
                path = "Assets" + path.Substring(Application.dataPath.Length);
            }

            var sequence = AssetDatabase.LoadAssetAtPath<TutorialSequenceSO>(path);
            if (sequence != null)
            {
                LoadSequence(sequence);
            }
        }

        private void SaveSequence()
        {
            if (_currentSequence == null) return;

            _graphView.SaveGraphPositions();
            EditorUtility.SetDirty(_currentSequence);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TutorialGraphEditor] Saved: {_currentSequence.name}");
        }

        private void SaveSequenceAs()
        {
            if (_currentSequence == null) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Tutorial Sequence As",
                _currentSequence.name,
                "asset",
                "Save tutorial sequence to a new asset");

            if (string.IsNullOrEmpty(path)) return;

            _graphView.SaveGraphPositions();

            var copy = Instantiate(_currentSequence);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            LoadSequence(copy);
        }

        private void ValidateSequence()
        {
            if (_currentSequence == null)
            {
                EditorUtility.DisplayDialog("No Sequence", "Please load or create a sequence first.", "OK");
                return;
            }

            var errors = new System.Collections.Generic.List<string>();

            // Validate sequence
            if (string.IsNullOrEmpty(_currentSequence.SequenceId))
            {
                errors.Add("Sequence ID is empty");
            }

            if (_currentSequence.Steps == null || _currentSequence.Steps.Count == 0)
            {
                errors.Add("Sequence has no steps");
            }
            else
            {
                for (int i = 0; i < _currentSequence.Steps.Count; i++)
                {
                    var step = _currentSequence.Steps[i];
                    if (step == null)
                    {
                        errors.Add($"Step {i} is null");
                        continue;
                    }

                    if (!step.Validate(out string stepError))
                    {
                        errors.Add($"Step {i} ({step.StepType}): {stepError}");
                    }
                }
            }

            if (errors.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed", "Tutorial sequence is valid.", "OK");
            }
            else
            {
                string message = $"Found {errors.Count} issue(s):\n\n";
                foreach (var error in errors)
                {
                    message += "• " + error + "\n";
                }
                EditorUtility.DisplayDialog("Validation Failed", message, "OK");
            }
        }
    }
}
