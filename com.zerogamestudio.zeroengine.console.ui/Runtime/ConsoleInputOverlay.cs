using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.UI
{
    public sealed class ConsoleInputAccess
    {
        public Func<string> ReadText { get; }
        public Action<string> WriteText { get; }
        public Func<int> ReadCaret { get; }
        public Func<bool> IsFocused { get; }
        public Func<bool> IsComposing { get; }
        public ConsoleInputAccess(Func<string> readText, Action<string> writeText, Func<int> readCaret,
            Func<bool> isFocused, Func<bool> isComposing)
        {
            ReadText = readText ?? throw new ArgumentNullException(nameof(readText));
            WriteText = writeText ?? throw new ArgumentNullException(nameof(writeText));
            ReadCaret = readCaret ?? throw new ArgumentNullException(nameof(readCaret));
            IsFocused = isFocused ?? throw new ArgumentNullException(nameof(isFocused));
            IsComposing = isComposing ?? throw new ArgumentNullException(nameof(isComposing));
        }
    }

    // Shared native completion/history presentation for a public third-party input widget.
    public sealed class ConsoleInputOverlay : MonoBehaviour
    {
        private ConsoleHost host;
        private ConsoleInputAccess input;
        private GameObject panel;
        private GameObject ownedCanvas;
        private Font ownedFont;
        private readonly List<UnityEngine.UI.Button> rows = new List<UnityEngine.UI.Button>();
        private CancellationTokenSource cancellation;
        private Task<ConsoleCompletions> pending;
        private ConsoleCompletions completions;
        private string observedText, requestedText;
        private int observedCaret = -1, historyIndex = -1;
        private string draft;
        private float due;
        private bool dirty;
        private Action submit;

        public void Initialize(ConsoleHost owner, ConsoleInputAccess access, Action submitInput = null, Action close = null)
        {
            host = owner ? owner : throw new ArgumentNullException(nameof(owner));
            input = access ?? throw new ArgumentNullException(nameof(access));
            submit = submitInput;
            Build();
            BuildControls(close);
        }

        private void Update()
        {
            if (host == null || host.Session == null || input == null) return;
            string text = input.ReadText() ?? string.Empty;
            int caret = Mathf.Clamp(input.ReadCaret(), 0, text.Length);
            if (text != observedText || caret != observedCaret)
            {
                CancelPending();
                completions = null;
                panel.SetActive(false);
                observedText = text; observedCaret = caret; due = Time.unscaledTime + .04f; dirty = true;
            }
            if (input.IsComposing()) { panel.SetActive(false); return; }
            if (input.IsFocused())
            {
                if (ConsoleKeyboard.Pressed(KeyCode.Return) || ConsoleKeyboard.Pressed(KeyCode.KeypadEnter)) submit?.Invoke();
                else if (ConsoleKeyboard.Pressed(KeyCode.Tab) && completions != null && completions.Items.Count > 0) Apply(0);
                else if (ConsoleKeyboard.Pressed(KeyCode.UpArrow)) History(-1);
                else if (ConsoleKeyboard.Pressed(KeyCode.DownArrow)) History(1);
            }
            if (dirty && Time.unscaledTime >= due)
            {
                dirty = false;
                CancelPending();
                cancellation = new CancellationTokenSource();
                requestedText = text;
                pending = host.Session.CompleteAsync(text, caret, host.CreateCaller(), cancellation.Token);
            }
            if (pending == null || !pending.IsCompleted) return;
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (input.ReadText() == requestedText && input.ReadCaret() == observedCaret)
                {
                    completions = result;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        bool active = i < result.Items.Count;
                        rows[i].gameObject.SetActive(active);
                        if (active) rows[i].GetComponentInChildren<UnityEngine.UI.Text>().text = result.Items[i].Label;
                    }
                    panel.SetActive(result.Items.Count > 0 && input.IsFocused());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { host.Session.Write(new ConsoleOutput("completion", ConsoleOutputLevel.Error, exception.Message)); }
            pending = null;
        }

        private void Apply(int index)
        {
            if (completions == null || index >= completions.Items.Count) return;
            try { input.WriteText(completions.Apply(input.ReadText(), index)); }
            catch (InvalidOperationException) { }
            completions = null;
            panel.SetActive(false);
        }

        private void History(int direction)
        {
            var history = host.Session.GetHistory();
            if (history.Length == 0) return;
            if (historyIndex == -1) draft = input.ReadText();
            historyIndex = Mathf.Clamp(historyIndex - direction, -1, history.Length - 1);
            input.WriteText(historyIndex < 0 ? draft : history[history.Length - 1 - historyIndex]);
        }

        public void ResetAfterSubmit() { historyIndex = -1; completions = null; if (panel) panel.SetActive(false); }

        private void BuildControls(Action close)
        {
            string[] names = { "运行", "取消", "复制", "关闭" };
            Action[] actions = { () => submit?.Invoke(), () => host.Session?.CancelCurrent(),
                () => GUIUtility.systemCopyBuffer = string.Join("\n", Array.ConvertAll(host.Session.GetOutput(), entry => entry.Message)),
                () => close?.Invoke() };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var root = new GameObject(names[i], typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                root.transform.SetParent(ownedCanvas.transform, false);
                var rect = root.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(1, 0);
                rect.sizeDelta = new Vector2(64, 32); rect.anchoredPosition = new Vector2(-16 - (3 - i) * 68, 16);
                var background = root.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(.12f, .17f, .25f);
                var button = root.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
                button.onClick.AddListener(() => { if (!input.IsComposing()) actions[index](); });
                var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                labelRoot.transform.SetParent(root.transform, false);
                var labelRect = labelRoot.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                var label = labelRoot.GetComponent<UnityEngine.UI.Text>(); label.text = names[i]; label.font = rows[0].GetComponentInChildren<UnityEngine.UI.Text>().font;
                label.fontSize = 16; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            }
        }

        private void CancelPending()
        {
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            var abandoned = pending;
            pending = null;
            if (abandoned != null)
                _ = abandoned.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void Build()
        {
            var canvasRoot = new GameObject("ZE completion overlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            ownedCanvas = canvasRoot;
            canvasRoot.transform.SetParent(host.transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            panel = new GameObject("Native suggestions", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup)); panel.transform.SetParent(canvasRoot.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.05f, 0); rect.anchorMax = new Vector2(.7f, 0); rect.pivot = new Vector2(.5f, 0); rect.offsetMin = new Vector2(0, 70); rect.offsetMax = new Vector2(0, 294);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(.06f, .08f, .12f, .98f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childForceExpandHeight = false;
            try { ownedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16); } catch (ArgumentException) { }
            Font font = ownedFont ? ownedFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var root = new GameObject("Native completion", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement)); root.transform.SetParent(rect, false);
                root.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 28;
                root.GetComponent<UnityEngine.UI.Image>().color = new Color(.12f, .17f, .25f);
                var button = root.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = root.GetComponent<UnityEngine.UI.Image>(); button.onClick.AddListener(() => Apply(index));
                var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text)); labelRoot.transform.SetParent(root.transform, false);
                var labelRect = labelRoot.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = new Vector2(8, 2); labelRect.offsetMax = new Vector2(-8, -2);
                var label = labelRoot.GetComponent<UnityEngine.UI.Text>(); label.font = font; label.fontSize = 16; label.color = Color.white; label.supportRichText = false; label.raycastTarget = false;
                rows.Add(button);
            }
            panel.SetActive(false);
        }

        private void OnDisable() { CancelPending(); if (ownedCanvas) ownedCanvas.SetActive(false); }
        private void OnDestroy() { CancelPending(); if (ownedFont) Destroy(ownedFont); if (ownedCanvas) Destroy(ownedCanvas); }
    }
}
