using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using ZeroEngine.Console.Unity;
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ZeroEngine.Console.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConsoleHost))]
    public sealed class NativeConsole : MonoBehaviour, IConsoleFrontend
    {
        [SerializeField] private ConsoleHost host;
        [SerializeField] private Font font;
        [SerializeField, Range(12, 28)] private int fontSize = 16;
        private GameObject view;
        private Font ownedFont;
        private UnityEngine.UI.InputField input;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.ScrollRect scroll;
        private readonly List<UnityEngine.UI.Text> outputRows = new List<UnityEngine.UI.Text>();
        private readonly List<UnityEngine.UI.Button> completionRows = new List<UnityEngine.UI.Button>();
        private RectTransform outputContent, completions;
        private CancellationTokenSource completionCancellation;
        private Task<ConsoleCompletions> pendingCompletion;
        private Task<ConsoleResult> pendingExecution;
        private ConsoleCompletions currentCompletions;
        private string completionInput;
        private int selectedCompletion;
        private bool completionDirty;
        private int observedCaret = -1;
        private float completionAt;
        private long renderedRevision = -1;
        private bool wasRunning;
        private int historyIndex = -1;
        private string draftInput = string.Empty;
        private IDisposable inputLease;
        private ConsoleEventSystemLease eventSystemLease;
        private CursorLockMode previousLock;
        private bool previousCursor;
        private GameObject previousSelection;
        private BaseInputModule borrowedInputModule;
        private BaseInput previousInputOverride;
        private ConsoleInputOverride inputOverride;
        public bool IsOpen { get; private set; }
        public string BackendId => "native";
        public bool KeyboardShortcutEnabled { get; set; } = true;
        public ConsoleHost Host => host;
        public UnityEngine.UI.InputField InputField => input;
        public Func<IDisposable> AcquireInputIsolation { get; set; }
        public event Action<bool> OpenChanged;

        public bool TryInitialize(ConsoleHost owner, out string reason)
        {
            if (owner) host = owner;
            if (!host) host = GetComponent<ConsoleHost>();
            if (host) host.Initialize();
            reason = host && host.Session != null ? string.Empty : "Native Console host is disabled.";
            return string.IsNullOrEmpty(reason);
        }

        private void OnEnable()
        {
            if (!host) host = GetComponent<ConsoleHost>();
            host.Initialize();
        }

        public void Open()
        {
            if (IsOpen || !host || host.Session == null) return;
            EnsureView();
            previousLock = Cursor.lockState;
            previousCursor = Cursor.visible;
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            inputLease = AcquireInputIsolation?.Invoke();
            eventSystemLease = ConsoleEventSystemLease.Acquire(transform);
            IsOpen = true;
            view.SetActive(true);
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            borrowedInputModule = EventSystem.current ? EventSystem.current.currentInputModule : null;
            if (!borrowedInputModule && EventSystem.current) borrowedInputModule = EventSystem.current.GetComponent<BaseInputModule>();
            if (borrowedInputModule)
            {
                if (!inputOverride) inputOverride = view.AddComponent<ConsoleInputOverride>();
                previousInputOverride = borrowedInputModule.inputOverride;
                borrowedInputModule.inputOverride = inputOverride;
            }
#endif
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            renderedRevision = -1;
            input.ActivateInputField();
            input.Select();
            OpenChanged?.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            CancelCompletion();
            try
            {
                if (borrowedInputModule && borrowedInputModule.inputOverride == inputOverride)
                    borrowedInputModule.inputOverride = previousInputOverride;
                borrowedInputModule = null; previousInputOverride = null;
                if (view) view.SetActive(false);
            }
            finally
            {
                try { inputLease?.Dispose(); }
                finally
                {
                    inputLease = null;
                    eventSystemLease?.Dispose(); eventSystemLease = null;
                    if (Cursor.lockState == CursorLockMode.None && Cursor.visible)
                    { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
                    var events = EventSystem.current;
                    if (events && (!events.currentSelectedGameObject || events.currentSelectedGameObject.transform.IsChildOf(view.transform)))
                        events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
                    OpenChanged?.Invoke(false);
                }
            }
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        private void Update()
        {
            bool composing = IsComposing;
            if (KeyboardShortcutEnabled && !composing && ConsoleKeyboard.Pressed(KeyCode.BackQuote)) Toggle();
            if (!IsOpen || host.Session == null) return;
            if (input.caretPosition != observedCaret)
            {
                observedCaret = input.caretPosition;
                InputChanged(input.text);
            }
            if (!composing && ConsoleKeyboard.Pressed(KeyCode.Escape)) { Close(); return; }
            if (!composing && input.isFocused)
            {
                if (ConsoleKeyboard.Pressed(KeyCode.Tab) && currentCompletions != null && currentCompletions.Items.Count > 0) ApplyCompletion(selectedCompletion);
                else if (ConsoleKeyboard.Pressed(KeyCode.UpArrow)) NavigateHistory(-1);
                else if (ConsoleKeyboard.Pressed(KeyCode.DownArrow)) NavigateHistory(1);
            }
            if (completionDirty && Time.unscaledTime >= completionAt)
            {
                completionDirty = false;
                CancelCompletion();
                completionCancellation = new CancellationTokenSource();
                completionInput = input.text;
                pendingCompletion = host.Session.CompleteAsync(input.text, input.caretPosition, host.CreateCaller(), completionCancellation.Token);
            }
            if (pendingCompletion != null && pendingCompletion.IsCompleted)
            {
                try
                {
                    var result = pendingCompletion.GetAwaiter().GetResult();
                    if (input.text == completionInput) ShowCompletions(result);
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Error, exception.Message)); }
                pendingCompletion = null;
            }
            if (pendingExecution != null && pendingExecution.IsCompleted)
            {
                try { pendingExecution.GetAwaiter().GetResult(); }
                catch (Exception exception) { host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Error, exception.Message)); }
                pendingExecution = null;
            }
            if (host.Session.OutputRevision != renderedRevision) RenderOutput();
            if (wasRunning != host.Session.IsRunning)
            {
                wasRunning = host.Session.IsRunning;
                status.text = wasRunning ? "执行中 · 可取消" : "就绪";
            }
        }

        public Task<ConsoleResult> SubmitAsync(string line)
        {
            if (!host || host.Session == null) return Task.FromResult(ConsoleResult.Failure("host-disabled", "Console host is disabled."));
            if (string.IsNullOrWhiteSpace(line)) return Task.FromResult(ConsoleResult.Success());
            host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "> " + line));
            pendingExecution = host.ExecuteAsync(line);
            historyIndex = -1;
            if (input) { input.SetTextWithoutNotify(string.Empty); input.ActivateInputField(); }
            CancelCompletion();
            return pendingExecution;
        }

        public bool IsComposing => ConsoleKeyboard.IsComposing;
        private void SubmitInput(string line) { if (!IsComposing) _ = SubmitAsync(line); }
        private void InputChanged(string value) { CancelCompletion(); completionDirty = true; completionAt = Time.unscaledTime + .04f; }

        private void NavigateHistory(int direction)
        {
            string[] values = host.Session.GetHistory();
            if (values.Length == 0) return;
            if (historyIndex == -1) draftInput = input.text;
            historyIndex = Mathf.Clamp(historyIndex - direction, -1, values.Length - 1);
            input.text = historyIndex < 0 ? draftInput : values[values.Length - 1 - historyIndex];
            input.caretPosition = input.text.Length;
        }

        private void ShowCompletions(ConsoleCompletions result)
        {
            currentCompletions = result;
            selectedCompletion = 0;
            for (int i = 0; i < completionRows.Count; i++)
            {
                bool visible = i < result.Items.Count;
                completionRows[i].gameObject.SetActive(visible);
                if (visible) completionRows[i].GetComponentInChildren<UnityEngine.UI.Text>().text = result.Items[i].Label;
            }
            completions.gameObject.SetActive(result.Items.Count > 0);
        }

        private void ApplyCompletion(int index)
        {
            if (currentCompletions == null || index >= currentCompletions.Items.Count) return;
            try
            {
                string updated = currentCompletions.Apply(input.text, index);
                input.text = updated;
                input.caretPosition = updated.Length;
                input.ActivateInputField();
            }
            catch (InvalidOperationException) { }
            CancelCompletion();
        }

        private void CancelCompletion()
        {
            completionCancellation?.Cancel();
            completionCancellation?.Dispose();
            completionCancellation = null;
            var abandoned = pendingCompletion;
            pendingCompletion = null;
            if (abandoned != null)
                _ = abandoned.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            currentCompletions = null;
            if (completions) completions.gameObject.SetActive(false);
        }

        private void RenderOutput()
        {
            var snapshot = host.Session.GetOutputSnapshot();
            var entries = snapshot.Entries;
            int first = Mathf.Max(0, entries.Count - outputRows.Count);
            for (int i = 0; i < outputRows.Count; i++)
            {
                int index = first + i;
                bool visible = index < entries.Count;
                var text = outputRows[i];
                text.gameObject.SetActive(visible);
                if (!visible) continue;
                string message = entries[index].Message;
                text.text = message.Length <= 1024 ? message : message.Substring(0, 1024) + "…";
                text.color = entries[index].Level == ConsoleOutputLevel.Error ? new Color(1f, .48f, .48f)
                    : entries[index].Level == ConsoleOutputLevel.Warning ? new Color(1f, .8f, .4f) : new Color(.88f, .91f, .96f);
            }
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0;
            renderedRevision = snapshot.Revision;
        }

        private void EnsureView()
        {
            if (view) return;
            if (!font)
            {
                try { ownedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" }, fontSize); }
                catch (ArgumentException) { }
                font = ownedFont;
#if UNITY_6000_0_OR_NEWER
                if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                if (!font) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }
            view = new GameObject("ZE Native Console", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            view.transform.SetParent(transform, false);
            var canvas = view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
            var scaler = view.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            var panel = Rect("Panel", view.transform); Stretch(panel, new Vector2(0, .38f), Vector2.one, new Vector2(16, 12), new Vector2(-16, -16));
            panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.045f, .055f, .075f, .97f);
            var title = Text("Title", panel, "ZE CONSOLE"); Stretch(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -40), new Vector2(-300, -8)); title.fontStyle = FontStyle.Bold;
            status = Text("Status", panel, "就绪"); Stretch(status.rectTransform, new Vector2(1, 1), Vector2.one, new Vector2(-285, -40), new Vector2(-16, -8)); status.alignment = TextAnchor.MiddleRight;
            var scrollRoot = Rect("Output", panel); Stretch(scrollRoot, Vector2.zero, Vector2.one, new Vector2(12, 60), new Vector2(-12, -46));
            scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = false; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", scrollRoot); Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.03f, .04f, .055f, .8f);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            outputContent = Rect("Content", viewport); Stretch(outputContent, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero); outputContent.pivot = new Vector2(.5f, 1);
            var layout = outputContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing = 4; layout.padding = new RectOffset(6, 6, 6, 6); layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            outputContent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = outputContent;
            for (int i = 0; i < 64; i++) { var row = Text("Line", outputContent, ""); row.gameObject.SetActive(false); outputRows.Add(row); }
            var field = Rect("Input", panel); Stretch(field, Vector2.zero, new Vector2(1, 0), new Vector2(12, 14), new Vector2(-292, 48));
            field.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.1f, .13f, .18f);
            input = field.gameObject.AddComponent<UnityEngine.UI.InputField>(); input.characterLimit = ConsoleLexer.MaximumLineLength;
            var fieldText = Text("Text", field, ""); Stretch(fieldText.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3)); input.textComponent = fieldText;
            var placeholder = Text("Placeholder", field, "输入 ze-help 查看指令"); Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3)); placeholder.color = new Color(.5f, .57f, .67f); input.placeholder = placeholder;
            input.onValueChanged.AddListener(InputChanged);
#if UNITY_6000_0_OR_NEWER
            input.onSubmit.AddListener(SubmitInput);
#else
            input.onEndEdit.AddListener(value => { if (ConsoleKeyboard.Pressed(KeyCode.Return)) SubmitInput(value); });
#endif
            MakeButton(panel, "运行", -280, 64, () => SubmitInput(input.text));
            MakeButton(panel, "取消", -212, 64, () => host.Session.CancelCurrent());
            MakeButton(panel, "复制", -144, 64, CopyOutput);
            MakeButton(panel, "关闭", -76, 64, Close);
            completions = Rect("Completions", panel); Stretch(completions, Vector2.zero, new Vector2(1, 0), new Vector2(12, 52), new Vector2(-292, 276));
            completions.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.07f, .1f, .15f, .98f);
            var completionLayout = completions.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); completionLayout.childControlHeight = true; completionLayout.childForceExpandHeight = false;
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var buttonRoot = Rect("Completion", completions); var button = buttonRoot.gameObject.AddComponent<UnityEngine.UI.Button>();
                var background = buttonRoot.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = new Color(.1f, .14f, .21f); button.targetGraphic = background;
                var label = Text("Label", buttonRoot, ""); Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 2), new Vector2(-8, -2));
                buttonRoot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 28;
                button.onClick.AddListener(() => ApplyCompletion(index)); completionRows.Add(button);
            }
            completions.gameObject.SetActive(false);
            view.SetActive(false);
        }

        private void CopyOutput()
        {
            var values = host.Session.GetOutput();
            var lines = new string[values.Length];
            for (int i = 0; i < lines.Length; i++) lines[i] = values[i].Message;
            GUIUtility.systemCopyBuffer = string.Join("\n", lines);
        }

        private UnityEngine.UI.Text Text(string name, Transform parent, string value)
        {
            var rect = Rect(name, parent); var text = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
            text.font = font; text.fontSize = fontSize; text.color = new Color(.88f, .91f, .96f); text.text = value;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private void MakeButton(Transform parent, string label, float x, float width, Action action)
        {
            var rect = Rect(label, parent); Stretch(rect, new Vector2(1, 0), new Vector2(1, 0), new Vector2(x, 14), new Vector2(x + width, 48));
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.15f, .22f, .33f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => action()); var text = Text("Label", rect, label); text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static RectTransform Rect(string name, Transform parent)
        { var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); result.SetParent(parent, false); return result; }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
        private void OnApplicationFocus(bool focus) { if (!focus) Close(); }
        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (view) Destroy(view);
            if (ownedFont) Destroy(ownedFont);
        }
    }
}
