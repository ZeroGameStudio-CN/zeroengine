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
        private int selectedCompletion, completionOffset, completionCaret;
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
            UpdateViewLayout();
            if (input.caretPosition != observedCaret)
            {
                observedCaret = input.caretPosition;
                InputChanged(input.text);
            }
            if (!composing && ConsoleKeyboard.Pressed(KeyCode.Escape)) { Close(); return; }
            if (!composing && input.isFocused)
            {
                if (ConsoleKeyboard.Pressed(KeyCode.Tab) && currentCompletions != null && currentCompletions.Items.Count > 0) ApplyCompletion(selectedCompletion);
                else if (ConsoleKeyboard.Pressed(KeyCode.PageUp)) NavigateCompletion(-1);
                else if (ConsoleKeyboard.Pressed(KeyCode.PageDown)) NavigateCompletion(1);
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
                status.text = wasRunning ? "执行中 · 可取消" : "就绪";
                cancelButton.interactable = wasRunning;
            }
        }

        public Task<ConsoleResult> SubmitAsync(string line)
        {
            if (!host || host.Session == null) return Task.FromResult(ConsoleResult.Failure("host-disabled", "Console host is disabled."));
            if (string.IsNullOrWhiteSpace(line)) return Task.FromResult(ConsoleResult.Success());
            host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "> " + line));
            pendingExecution = host.ExecuteAsync(line);
            historyIndex = -1;
            if (input) { input.SetTextWithoutNotify(string.Empty); input.caretPosition = 0; observedCaret = 0; input.ActivateInputField(); }
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
            int capacity = Mathf.Clamp(Mathf.FloorToInt((panel.rect.height - 208) / rowHeight), 1, completionRows.Count);
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
            completionCaption.text = "补全 " + (selectedCompletion + 1) + " / " + currentCompletions.Items.Count + "   Tab 确认";
            completions.offsetMax = new Vector2(-12, 126 + visibleCount * rowHeight);
            completions.gameObject.SetActive(visibleCount > 0);
        }

        private void ApplyCompletion(int index)
        {
            if (currentCompletions == null || index >= currentCompletions.Items.Count) return;
            try
            {
                string updated = currentCompletions.Apply(input.text, index);
                input.text = updated;
                input.caretPosition = updated.Length;
                observedCaret = updated.Length;
                input.ActivateInputField();
            }
            catch (InvalidOperationException) { }
            CancelCompletion();
        }

        private void CancelCompletion()
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
        }

        private void RenderOutput()
        {
            var snapshot = host.Session.GetOutputSnapshot();
            var entries = snapshot.Entries;
            if (emptyOutput) emptyOutput.gameObject.SetActive(entries.Count == 0);
            while (outputRows.Count < Mathf.Min(256, entries.Count)) outputRows.Add(Text("Line", outputContent, ""));
            Vector2 readingPosition = outputContent.anchoredPosition;
            int first = Mathf.Max(0, entries.Count - outputRows.Count);
            for (int i = 0; i < outputRows.Count; i++)
            {
                int index = first + i;
                bool visible = index < entries.Count;
                var text = outputRows[i];
                text.gameObject.SetActive(visible);
                if (!visible) continue;
                text.text = entries[index].Message;
                text.color = entries[index].Level == ConsoleOutputLevel.Error ? new Color(1f, .48f, .48f)
                    : entries[index].Level == ConsoleOutputLevel.Warning ? new Color(1f, .8f, .4f) : new Color(.88f, .91f, .96f);
            }
            Canvas.ForceUpdateCanvases();
            if (followOutput) scroll.verticalNormalizedPosition = 0;
            else outputContent.anchoredPosition = new Vector2(readingPosition.x,
                Mathf.Clamp(readingPosition.y, 0, Mathf.Max(0, outputContent.rect.height - scroll.viewport.rect.height)));
            renderedRevision = snapshot.Revision;
        }

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
