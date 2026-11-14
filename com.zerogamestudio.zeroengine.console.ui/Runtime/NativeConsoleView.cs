using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.Console.UI
{
    public sealed partial class NativeConsole
    {
        private static readonly Color SurfaceColor = new Color(.09f, .13f, .18f);
        private static readonly Color SelectedColor = new Color(.12f, .34f, .38f);
        private RectTransform panel;
        private Text completionCaption, fontSizeLabel, emptyOutput;
        private Button cancelButton, followButton;
        private ConsoleIcon stateIcon, cancelIcon, followIcon;
        private ConsoleActionHint stateHint, followHint;
        private RectTransform actionHint, actionHintTarget;
        private Text actionHintLabel;
        private readonly Vector3[] hintCorners = new Vector3[4];
        private bool followOutput = true;
        private RectTransform searchRow;
        private UnityEngine.UI.InputField logSearch;
        private UnityEngine.UI.Button searchButton, clearSearchButton;
        private ConsoleActionHint copyHint;
        private string outputQuery = string.Empty;

        private void EnsureView()
        {
            if (view) return;
            if (!font)
            {
                try { ownedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" }, fontSize); }
                catch (ArgumentException) { }
                font = ownedFont;
                if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            view = new GameObject("ZE Native Console", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            view.transform.SetParent(transform, false);
            var canvas = view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
            var scaler = view.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;

            panel = Rect("Panel", view.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            panel.anchoredPosition = new Vector2(16, 16);
            Surface(panel, new Color(.18f, .28f, .34f, .95f), 10);
            var shadow = panel.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .45f); shadow.effectDistance = new Vector2(3, -3);
            var fill = Rect("Surface", panel); Stretch(fill, Vector2.zero, Vector2.one, Vector2.one, -Vector2.one);
            Surface(fill, new Color(.035f, .052f, .075f, .98f), 9).raycastTarget = false;
            var accent = Rect("Accent", panel); Stretch(accent, new Vector2(0, 1), Vector2.one, new Vector2(12, -2), new Vector2(-12, -1));
            accent.gameObject.AddComponent<Image>().color = new Color(.29f, .78f, .72f);
            var terminal = Icon("Terminal", panel, ConsoleGlyph.Terminal);
            Stretch(terminal.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -34), new Vector2(36, -12));
            var title = Chrome("Title", panel, "CONSOLE", 14); title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -38), new Vector2(120, -8));
            var badge = Chrome("Backend", panel, "NATIVE", 10); badge.color = new Color(.48f, .74f, .76f);
            Stretch(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(126, -38), new Vector2(184, -8));
            stateIcon = Icon("Status", panel, ConsoleGlyph.Ready); stateIcon.raycastTarget = true;
            Stretch(stateIcon.rectTransform, Vector2.one, Vector2.one, new Vector2(-74, -35), new Vector2(-50, -11));
            stateHint = Hint(stateIcon.gameObject, "就绪");
            MakeIconButton(panel, "关闭", ConsoleGlyph.Close, "关闭控制台 · Esc", Vector2.one, new Vector2(-44, -36), new Vector2(-12, -8), Close);

            MakeIconButton(panel, "清空", ConsoleGlyph.Clear, "清空日志", new Vector2(0, 1), new Vector2(12, -76), new Vector2(44, -44), () => host.Session.ClearOutput());
            copyHint = MakeIconButton(panel, "复制", ConsoleGlyph.Copy, "复制全部日志", new Vector2(0, 1), new Vector2(50, -76), new Vector2(82, -44), CopyOutput).GetComponent<ConsoleActionHint>();
            MakeIconButton(panel, "字号减小", ConsoleGlyph.FontSmaller, "减小字号", new Vector2(0, 1), new Vector2(98, -76), new Vector2(130, -44), () => ChangeFontSize(-2));
            MakeIconButton(panel, "字号增大", ConsoleGlyph.FontLarger, "增大字号", new Vector2(0, 1), new Vector2(136, -76), new Vector2(168, -44), () => ChangeFontSize(2));
            fontSizeLabel = Chrome("FontSize", panel, fontSize.ToString());
            fontSizeLabel.color = new Color(.49f, .59f, .67f);
            Stretch(fontSizeLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(178, -74), new Vector2(210, -46));
            searchButton = MakeIconButton(panel, "搜索日志", ConsoleGlyph.Search, "搜索日志", new Vector2(0, 1), new Vector2(212, -76), new Vector2(244, -44), ToggleLogSearch);
            followButton = MakeIconButton(panel, "跟随", ConsoleGlyph.Follow, "自动跟随新日志：开 · 点击暂停", new Vector2(1, 1), new Vector2(-44, -76), new Vector2(-12, -44), ToggleFollow);
            followButton.targetGraphic.color = SelectedColor;
            followIcon = followButton.GetComponentInChildren<ConsoleIcon>();
            followHint = followButton.GetComponent<ConsoleActionHint>();

            var scrollRoot = Rect("Output", panel); Stretch(scrollRoot, Vector2.zero, Vector2.one, new Vector2(12, 90), new Vector2(-12, -86));
            scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", scrollRoot); Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12, 0));
            viewport.gameObject.AddComponent<Image>().color = new Color(.02f, .032f, .048f, .9f);
            viewport.gameObject.AddComponent<RectMask2D>();
            emptyOutput = Chrome("EmptyOutput", viewport, "ze-help", 14);
            emptyOutput.alignment = TextAnchor.MiddleCenter; emptyOutput.color = new Color(.43f, .53f, .62f);
            Stretch(emptyOutput.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 12), new Vector2(-16, -12));
            outputContent = Rect("Content", viewport); Stretch(outputContent, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero); outputContent.pivot = new Vector2(.5f, 1);
            var layout = outputContent.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 5; layout.padding = new RectOffset(10, 10, 8, 8);
            layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            outputContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = outputContent;
            var track = Rect("Scrollbar", scrollRoot); Stretch(track, new Vector2(1, 0), Vector2.one, new Vector2(-7, 2), new Vector2(-1, -2));
            track.gameObject.AddComponent<Image>().color = SurfaceColor;
            var handle = Rect("Handle", track); Stretch(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(.26f, .35f, .44f);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            searchRow = Rect("LogSearchRow", panel);
            Stretch(searchRow, new Vector2(0, 1), Vector2.one, new Vector2(12, -118), new Vector2(-12, -86));
            var searchField = Rect("LogSearch", searchRow);
            Stretch(searchField, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-38, 0));
            var searchSurface = Surface(searchField, SurfaceColor, 5);
            logSearch = searchField.gameObject.AddComponent<UnityEngine.UI.InputField>();
            logSearch.targetGraphic = searchSurface; logSearch.characterLimit = 256;
            logSearch.customCaretColor = true; logSearch.caretColor = new Color(.42f, .86f, .77f);
            var searchText = Chrome("Text", searchField, "", 13);
            Stretch(searchText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-10, -2));
            logSearch.textComponent = searchText;
            var searchPlaceholder = Chrome("Placeholder", searchField, "查找日志…", 13);
            searchPlaceholder.color = new Color(.49f, .59f, .67f);
            Stretch(searchPlaceholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-10, -2));
            logSearch.placeholder = searchPlaceholder;
            logSearch.onValueChanged.AddListener(SetOutputQuery);
            clearSearchButton = MakeIconButton(searchRow, "清除搜索", ConsoleGlyph.Close, "清除搜索条件", Vector2.one, new Vector2(-32, -32), Vector2.zero,
                () => { logSearch.text = string.Empty; logSearch.ActivateInputField(); });
            clearSearchButton.interactable = false;
            searchRow.gameObject.SetActive(false);

            var prompt = Chrome("Prompt", panel, ">", 18); prompt.color = new Color(.42f, .86f, .77f);
            Stretch(prompt.rectTransform, Vector2.zero, Vector2.zero, new Vector2(12, 36), new Vector2(30, 74));
            var field = Rect("Input", panel); Stretch(field, Vector2.zero, new Vector2(1, 0), new Vector2(34, 36), new Vector2(-104, 74));
            var inputSurface = Surface(field, new Color(.09f, .14f, .20f), 5);
            input = field.gameObject.AddComponent<InputField>(); input.characterLimit = ConsoleLexer.MaximumLineLength;
            input.targetGraphic = inputSurface;
            input.customCaretColor = true; input.caretColor = new Color(.42f, .86f, .77f);
            input.selectionColor = new Color(.18f, .53f, .55f, .55f);
            var fieldText = Text("Text", field, ""); Stretch(fieldText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            fieldText.alignment = TextAnchor.MiddleLeft; fieldText.supportRichText = false; input.textComponent = fieldText;
            var placeholder = Text("Placeholder", field, "ze-help"); Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            placeholder.alignment = TextAnchor.MiddleLeft; placeholder.color = new Color(.45f, .53f, .61f); input.placeholder = placeholder;
            input.onValueChanged.AddListener(InputChanged);
            input.onSubmit.AddListener(SubmitInput);
            var run = MakeIconButton(panel, "运行", ConsoleGlyph.Run, "执行命令 · Enter", new Vector2(1, 0), new Vector2(-94, 36), new Vector2(-56, 74), () => SubmitInput(input.text));
            run.targetGraphic.color = SelectedColor;
            cancelButton = MakeIconButton(panel, "取消", ConsoleGlyph.Stop, "取消当前执行", new Vector2(1, 0), new Vector2(-50, 36), new Vector2(-12, 74), () => host.Session.CancelCurrent());
            cancelIcon = cancelButton.GetComponentInChildren<ConsoleIcon>();
            RefreshActivityState();
            var hint = Chrome("KeyboardHint", panel, "↑↓   ·   PgUp / PgDn   ·   Tab   ·   Esc", 11);
            hint.color = new Color(.49f, .57f, .65f);
            Stretch(hint.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 30));
            hint.raycastTarget = true;
            Hint(hint.gameObject, "↑↓ 历史 · PgUp/PgDn 选择补全 · Tab 填入 · Esc 关闭");

            completions = Rect("Completions", panel); Stretch(completions, Vector2.zero, new Vector2(1, 0), new Vector2(12, 90), new Vector2(-12, 330));
            Surface(completions, new Color(.06f, .095f, .14f, 1), 6);
            completionCaption = Chrome("CompletionCaption", completions, "");
            Stretch(completionCaption.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(10, -30), new Vector2(-144, -2));
            MakeIconButton(completions, "上一项", ConsoleGlyph.Up, "上一条补全", Vector2.one, new Vector2(-78, -28), new Vector2(-44, -4), () => NavigateCompletion(-1));
            MakeIconButton(completions, "下一项", ConsoleGlyph.Down, "下一条补全", Vector2.one, new Vector2(-40, -28), new Vector2(-6, -4), () => NavigateCompletion(1));
            var list = Rect("Choices", completions); Stretch(list, Vector2.zero, Vector2.one, new Vector2(6, 4), new Vector2(-6, -32));
            var completionLayout = list.gameObject.AddComponent<VerticalLayoutGroup>(); completionLayout.childControlHeight = completionLayout.childControlWidth = true; completionLayout.childForceExpandHeight = false;
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var button = MakeButton(list, "Completion", "", Vector2.zero, Vector2.zero, Vector2.zero, () => ApplyCompletion(completionOffset + index), fontSize);
                var label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft;
                Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-10, -2));
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize + 10;
                completionRows.Add(button);
            }
            completions.gameObject.SetActive(false);
            actionHint = Rect("ActionHint", panel); actionHint.anchorMin = actionHint.anchorMax = actionHint.pivot = Vector2.zero;
            Surface(actionHint, new Color(.13f, .20f, .25f), 5).raycastTarget = false;
            actionHintLabel = Chrome("HintText", actionHint, "", 12);
            Stretch(actionHintLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
            actionHint.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            UpdateViewLayout();
            view.SetActive(false);
        }

        private void UpdateViewLayout()
        {
            var area = ((RectTransform)view.transform).rect.size;
            var size = new Vector2(Mathf.Min(720, Mathf.Max(0, area.x - 32)), Mathf.Min(400, Mathf.Max(0, area.y - 32)));
            if (panel.sizeDelta == size) return;
            panel.sizeDelta = size;
            RefreshCompletions();
        }

        private void ChangeFontSize(int delta)
        {
            fontSize = Mathf.Clamp(fontSize + delta, 12, 28);
            foreach (var row in outputRows) row.fontSize = fontSize;
            foreach (var row in completionRows) row.GetComponentInChildren<Text>(true).fontSize = fontSize;
            input.textComponent.fontSize = fontSize;
            ((Text)input.placeholder).fontSize = fontSize;
            fontSizeLabel.text = fontSize.ToString();
            RefreshCompletions();
            renderedRevision = -1;
        }

        private void ToggleFollow()
        {
            followOutput = !followOutput;
            followIcon.Glyph = followOutput ? ConsoleGlyph.Follow : ConsoleGlyph.FollowPaused;
            followHint.Message = followOutput ? "自动跟随新日志：开 · 点击暂停" : "自动跟随新日志：关 · 点击恢复";
            followButton.targetGraphic.color = followOutput ? SelectedColor : SurfaceColor;
            if (actionHintTarget == followHint.transform) ShowActionHint((RectTransform)followHint.transform, followHint.Message);
            if (followOutput) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0; }
        }

        private void RefreshActivityState()
        {
            bool running = host.Session.IsRunning;
            stateIcon.Glyph = running ? ConsoleGlyph.Running : ConsoleGlyph.Ready;
            stateIcon.color = running ? new Color(.97f, .73f, .35f) : new Color(.46f, .82f, .73f);
            stateHint.Message = running ? "正在执行 · 可用停止按钮取消" : "就绪";
            if (actionHintTarget == stateIcon.rectTransform) ShowActionHint(stateIcon.rectTransform, stateHint.Message);
            cancelButton.interactable = running;
            cancelIcon.color = running ? new Color(1, .65f, .57f) : new Color(.33f, .40f, .46f);
        }

        private ConsoleActionHint Hint(GameObject owner, string message)
        {
            var result = owner.AddComponent<ConsoleActionHint>(); result.Message = message;
            result.Show = ShowActionHint; result.Hide = HideActionHint;
            return result;
        }

        private void ShowActionHint(RectTransform target, string message)
        {
            if (!IsOpen || !actionHint || string.IsNullOrEmpty(message)) return;
            actionHintTarget = target; actionHintLabel.text = message;
            float width = Mathf.Min(actionHintLabel.preferredWidth + 20, Mathf.Max(1, panel.rect.width - 16));
            actionHint.sizeDelta = new Vector2(width, 30);
            target.GetWorldCorners(hintCorners);
            var above = (Vector2)panel.InverseTransformPoint(hintCorners[1]);
            var below = (Vector2)panel.InverseTransformPoint(hintCorners[0]);
            float y = above.y + 6;
            if (y + 30 > panel.rect.yMax - 8) y = below.y - 36;
            actionHint.anchoredPosition = new Vector2(Mathf.Clamp(above.x, 8, Mathf.Max(8, panel.rect.width - width - 8)),
                Mathf.Clamp(y, 8, Mathf.Max(8, panel.rect.height - 38)));
            actionHint.SetAsLastSibling(); actionHint.gameObject.SetActive(true);
        }

        private void HideActionHint(RectTransform target)
        {
            if (actionHintTarget != target) return;
            actionHintTarget = null;
            if (actionHint) actionHint.gameObject.SetActive(false);
        }

        private void CopyOutput()
        {
            var values = host.Session.GetOutput();
            var lines = new System.Collections.Generic.List<string>(values.Length);
            for (int i = 0; i < values.Length; i++) if (MatchesOutput(values[i])) lines.Add(values[i].Message);
            GUIUtility.systemCopyBuffer = string.Join("\n", lines);
        }

        private bool MatchesOutput(ConsoleOutput value) => string.IsNullOrEmpty(outputQuery)
            || value.Message.IndexOf(outputQuery, StringComparison.OrdinalIgnoreCase) >= 0;

        private void SetOutputQuery(string value)
        {
            outputQuery = value ?? string.Empty;
            renderedRevision = -1;
            clearSearchButton.interactable = outputQuery.Length > 0;
            copyHint.Message = outputQuery.Length == 0 ? "复制全部日志" : "复制筛选日志";
            if (actionHintTarget == copyHint.transform) ShowActionHint((RectTransform)copyHint.transform, copyHint.Message);
        }

        private void ToggleLogSearch()
        {
            bool show = !searchRow.gameObject.activeSelf;
            searchRow.gameObject.SetActive(show);
            searchButton.targetGraphic.color = show ? SelectedColor : SurfaceColor;
            scroll.GetComponent<RectTransform>().offsetMax = new Vector2(-12, show ? -126 : -86);
            CancelCompletion();
            if (show) { logSearch.Select(); logSearch.ActivateInputField(); }
            else
            {
                logSearch.SetTextWithoutNotify(string.Empty);
                SetOutputQuery(string.Empty);
                input.Select(); input.ActivateInputField();
            }
            renderedRevision = -1;
        }

        private Text Text(string name, Transform parent, string value)
        {
            var rect = Rect(name, parent); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.color = new Color(.88f, .92f, .96f); text.text = value;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Text Chrome(string name, Transform parent, string value, int size = 12)
        {
            var text = Text(name, parent, value); text.fontSize = size; text.alignment = TextAnchor.MiddleLeft;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Button MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 min, Vector2 max, Action action, int size = 12)
        {
            var button = ButtonFrame(parent, name, anchor, min, max, action);
            var text = Chrome("Label", button.transform, label, size); text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private Button MakeIconButton(Transform parent, string name, ConsoleGlyph glyph, string hint, Vector2 anchor, Vector2 min, Vector2 max, Action action)
        {
            var button = ButtonFrame(parent, name, anchor, min, max, action);
            var graphic = Icon("Icon", button.transform, glyph);
            graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = new Vector2(.5f, .5f);
            graphic.rectTransform.sizeDelta = new Vector2(20, 20);
            Hint(button.gameObject, hint);
            return button;
        }

        private static ConsoleIcon Icon(string name, Transform parent, ConsoleGlyph glyph)
        {
            var result = Rect(name, parent).gameObject.AddComponent<ConsoleIcon>(); result.Glyph = glyph;
            result.color = new Color(.79f, .87f, .91f); result.raycastTarget = false;
            return result;
        }

        private Button ButtonFrame(Transform parent, string name, Vector2 anchor, Vector2 min, Vector2 max, Action action)
        {
            var rect = Rect(name, parent); Stretch(rect, anchor, anchor, min, max);
            var image = Surface(rect, SurfaceColor, 4);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors; colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f); colors.pressedColor = new Color(.7f, .85f, .9f); colors.disabledColor = new Color(.55f, .62f, .67f, .5f); colors.fadeDuration = .08f; button.colors = colors;
            button.onClick.AddListener(() => { action(); if (IsOpen && input) input.ActivateInputField(); });
            return button;
        }

        private static ConsoleSurface Surface(RectTransform rect, Color color, float radius)
        { var graphic = rect.gameObject.AddComponent<ConsoleSurface>(); graphic.Radius = radius; graphic.color = color; return graphic; }

        private static RectTransform Rect(string name, Transform parent)
        { var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); result.SetParent(parent, false); return result; }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
