using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConsoleHost))]
    public sealed partial class NativeConsole : MonoBehaviour, IConsoleFrontend
    {
        [SerializeField] private ConsoleHost host;
        [SerializeField] private Font font;
        [SerializeField, Range(12, 28)] private int fontSize = 16;
        [SerializeField, Tooltip("Preferred panel size in Canvas reference units; fitted to the available screen and keyboard area.")]
        private Vector2 preferredSize = new Vector2(880, 480);
        private GameObject view;
        private readonly ConsoleFontResolver fonts = new ConsoleFontResolver();
        private Font viewFont;
        private UnityEngine.UI.InputField input;
        private UnityEngine.UI.ScrollRect scroll;
        private readonly List<UnityEngine.UI.Text> outputRows = new List<UnityEngine.UI.Text>();
        private ConsoleOutput[] visibleOutput = Array.Empty<ConsoleOutput>();
        private readonly List<UnityEngine.UI.Button> completionRows = new List<UnityEngine.UI.Button>();
        private RectTransform outputContent, completions;
        private CancellationTokenSource completionCancellation;
        private Task<ConsoleCompletions> pendingCompletion;
        private Task<ConsoleResult> pendingExecution;
        private ConsoleCompletions currentCompletions;
        private string completionInput;
        private int selectedCompletion, completionOffset, completionCaret;
        private bool completionDirty;
        private int observedCaret = -1;
        private float completionAt;
        private long renderedRevision = -1;
        private long completionRevision = -1;
        private bool wasRunning;
        private ConsoleInteraction interaction;
        private ConsoleConfirmation shownConfirmation;
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
        public bool TouchLayoutPreview { get; set; }
        public Vector2 PreferredSize
        {
            get => preferredSize;
            set => preferredSize = ClampPreferredSize(value);
        }
        public Func<IDisposable> AcquireInputIsolation { get; set; }
        public Func<Font> FontProvider { get; set; }
        public event Action<bool> OpenChanged;

        public void ConfigureFont(Font value)
        {
            font = value;
            if (view) RefreshFont();
        }

        private void RefreshFont()
        {
            var resolved = fonts.Resolve(font, fontSize);
            if (viewFont == resolved) return;
            viewFont = resolved;
            if (view)
                foreach (var label in view.GetComponentsInChildren<UnityEngine.UI.Text>(true)) label.font = resolved;
        }

        private void OnValidate() => preferredSize = ClampPreferredSize(preferredSize);

        private static Vector2 ClampPreferredSize(Vector2 value) => new Vector2(
            float.IsNaN(value.x) || float.IsInfinity(value.x) ? 880 : Mathf.Max(320, value.x),
            float.IsNaN(value.y) || float.IsInfinity(value.y) ? 480 : Mathf.Max(240, value.y));

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
            if (FontProvider != null)
            {
                try { var supplied = FontProvider(); if (supplied) ConfigureFont(supplied); }
                catch (Exception) { host.Session.Write(new ConsoleOutput("font", ConsoleOutputLevel.Warning, "Console font provider is unavailable; retaining the current font.")); }
            }
            EnsureView();
            previousLock = Cursor.lockState;
            previousCursor = Cursor.visible;
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            inputLease = AcquireInputIsolation?.Invoke();
            eventSystemLease = ConsoleEventSystemLease.Acquire(transform);
            interaction = host.Session.TryAcquireInteraction();
            IsOpen = true;
            view.SetActive(true);
            if (ConsoleInputBackend.NeedsInputOverride)
            {
                borrowedInputModule = EventSystem.current ? EventSystem.current.currentInputModule : null;
                if (!borrowedInputModule && EventSystem.current) borrowedInputModule = EventSystem.current.GetComponent<BaseInputModule>();
                if (borrowedInputModule)
                {
                    if (!inputOverride) inputOverride = view.AddComponent<ConsoleInputOverride>();
                    previousInputOverride = borrowedInputModule.inputOverride;
                    borrowedInputModule.inputOverride = inputOverride;
                }
            }
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
            interaction?.Dispose();
            interaction = null;
            RefreshConfirmation(null);
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
            if (!IsOpen && !KeyboardShortcutEnabled) return;
            bool composing = IsComposing;
            if (KeyboardShortcutEnabled && !composing && ConsoleKeyboard.Pressed(KeyCode.BackQuote)) Toggle();
            if (!IsOpen || host.Session == null) return;
            RefreshConfirmation(interaction?.Pending);
            UpdateViewLayout();
            if (completionRevision != host.Registry.Revision)
            {
                completionRevision = host.Registry.Revision;
                if (!string.IsNullOrWhiteSpace(input.text)) InputChanged(input.text);
            }
            if (input.isFocused && input.caretPosition != observedCaret)
            {
                observedCaret = input.caretPosition;
                InputChanged(input.text);
            }
            if (!composing && ConsoleKeyboard.Pressed(KeyCode.Escape)) { Close(); return; }
            if (!composing && input.isFocused && shownConfirmation == null)
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
                completionCaret = input.caretPosition;
                pendingCompletion = host.Session.CompleteAsync(completionInput, completionCaret, host.CreateCaller(), completionCancellation.Token);
            }
            if (pendingCompletion != null && pendingCompletion.IsCompleted)
            {
                try
                {
                    var result = pendingCompletion.GetAwaiter().GetResult();
                    if (input.text == completionInput && input.caretPosition == completionCaret) ShowCompletions(result);
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
                RefreshActivityState();
            }
        }

        public Task<ConsoleResult> SubmitAsync(string line)
        {
            if (!host || host.Session == null) return Task.FromResult(ConsoleResult.Failure("host-disabled", "Console host is disabled."));
            if (string.IsNullOrWhiteSpace(line)) return Task.FromResult(ConsoleResult.Success());
            host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "> " + line));
            pendingExecution = host.ExecuteAsync(line);
            historyIndex = -1;
            if (input)
            {
                input.SetTextWithoutNotify(string.Empty); input.caretPosition = 0; observedCaret = 0;
                if (IsOpen) { input.Select(); input.ActivateInputField(); }
            }
            CancelCompletion();
            return pendingExecution;
        }

        public bool IsComposing => ConsoleKeyboard.IsComposing;
        private void SubmitInput(string line)
        {
            if (!IsComposing && interaction?.Pending == null) _ = SubmitAsync(line);
        }
        private void InputChanged(string value)
        {
            if (shownConfirmation != null) return;
            bool hasDraft = !string.IsNullOrWhiteSpace(value);
            CancelCompletion(!hasDraft);
            completionDirty = hasDraft;
            completionAt = Time.unscaledTime + .04f;
        }

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
            commandHint.Show(result.Hint);
            UpdateHintLayout();
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void NavigateCompletion(int direction)
        {
            if (currentCompletions == null || currentCompletions.Items.Count == 0 || IsComposing) return;
            selectedCompletion = (selectedCompletion + direction + currentCompletions.Items.Count) % currentCompletions.Items.Count;
            RefreshCompletions();
        }

        private void RefreshCompletions()
        {
            if (currentCompletions == null) return;
            int rowHeight = fontSize + 10;
            int capacity = Mathf.Clamp(Mathf.FloorToInt((panel.rect.height - 208 - HintHeight) / rowHeight), 1, completionRows.Count);
            completionOffset = selectedCompletion / capacity * capacity;
            int visibleCount = Mathf.Min(capacity, currentCompletions.Items.Count - completionOffset);
            for (int i = 0; i < completionRows.Count; i++)
            {
                bool visible = i < visibleCount;
                completionRows[i].gameObject.SetActive(visible);
                if (!visible) continue;
                int index = completionOffset + i;
                completionRows[i].GetComponentInChildren<UnityEngine.UI.Text>(true).text = currentCompletions.Items[index].Label;
                completionRows[i].targetGraphic.color = index == selectedCompletion ? SelectedColor : SurfaceColor;
                completionRows[i].GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = rowHeight;
            }
            completionCaption.text = (selectedCompletion + 1) + " / " + currentCompletions.Items.Count + (usingTouchLayout ? "" : "   ·   Tab");
            completions.offsetMax = new Vector2(-12, 126 + HintHeight + visibleCount * rowHeight);
            completions.gameObject.SetActive(visibleCount > 0);
        }

        private void ApplyCompletion(int index)
        {
            if (currentCompletions == null || index >= currentCompletions.Items.Count) return;
            try
            {
                string updated = currentCompletions.Apply(input.text, index, out int caret);
                input.text = updated;
                input.caretPosition = caret;
                observedCaret = caret;
                input.ActivateInputField();
                commandHint.Show(host.Session.GetHint(updated, caret, host.CreateCaller()));
                UpdateHintLayout();
            }
            catch (InvalidOperationException) { }
            CancelCompletion(false);
        }

        private void CancelCompletion(bool clearHint = true)
        {
            completionDirty = false;
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
            if (clearHint && commandHint != null) { commandHint.Show(null); UpdateHintLayout(); }
        }

        private void RenderOutput()
        {
            int count = host.Session.CopyOutputTo(ref visibleOutput, out long revision);
            if (!string.IsNullOrEmpty(outputQuery))
            {
                int matched = 0;
                for (int i = 0; i < count; i++)
                    if (MatchesOutput(visibleOutput[i])) visibleOutput[matched++] = visibleOutput[i];
                count = matched;
            }
            if (count > 256)
            {
                Array.Copy(visibleOutput, count - 256, visibleOutput, 0, 256);
                count = 256;
            }
            if (count < visibleOutput.Length) Array.Clear(visibleOutput, count, visibleOutput.Length - count);
            if (emptyOutput)
            {
                emptyOutput.text = string.IsNullOrEmpty(outputQuery) ? "ze-help" : "没有匹配日志";
                emptyOutput.gameObject.SetActive(count == 0);
            }
            while (outputRows.Count < count) outputRows.Add(Text("Line", outputContent, ""));
            Vector2 readingPosition = outputContent.anchoredPosition;
            for (int i = 0; i < outputRows.Count; i++)
            {
                bool visible = i < count;
                var text = outputRows[i];
                if (text.gameObject.activeSelf != visible) text.gameObject.SetActive(visible);
                if (!visible) { text.text = string.Empty; continue; }
                text.text = visibleOutput[i].Message;
                text.color = visibleOutput[i].Level == ConsoleOutputLevel.Error ? new Color(1f, .48f, .48f)
                    : visibleOutput[i].Level == ConsoleOutputLevel.Warning ? new Color(1f, .8f, .4f) : new Color(.88f, .91f, .96f);
            }
            Canvas.ForceUpdateCanvases();
            if (followOutput) scroll.verticalNormalizedPosition = 0;
            else outputContent.anchoredPosition = new Vector2(readingPosition.x,
                Mathf.Clamp(readingPosition.y, 0, Mathf.Max(0, outputContent.rect.height - scroll.viewport.rect.height)));
            renderedRevision = revision;
        }

        private void OnApplicationFocus(bool focus) { if (!focus && !TouchScreenKeyboard.visible) Close(); }
        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (view) Destroy(view);
            fonts.Dispose();
        }
    }
}
