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
        private bool followOutput = true;

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
            var title = Chrome("Title", panel, "控制台", 18); title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -38), new Vector2(90, -8));
            var badge = Chrome("Backend", panel, "NATIVE", 10); badge.color = new Color(.48f, .74f, .76f);
            Stretch(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(98, -38), new Vector2(166, -8));
            status = Chrome("Status", panel, "就绪"); status.alignment = TextAnchor.MiddleRight;
            status.color = new Color(.46f, .82f, .73f);
            Stretch(status.rectTransform, new Vector2(1, 1), Vector2.one, new Vector2(-198, -38), new Vector2(-58, -8));
            MakeButton(panel, "关闭", "×", new Vector2(1, 1), new Vector2(-44, -36), new Vector2(-12, -8), Close, 18);

            MakeButton(panel, "清空", "清空", new Vector2(0, 1), new Vector2(12, -74), new Vector2(64, -46), () => host.Session.ClearOutput());
            MakeButton(panel, "复制", "复制", new Vector2(0, 1), new Vector2(70, -74), new Vector2(122, -46), CopyOutput);
            MakeButton(panel, "字号减小", "A−", new Vector2(0, 1), new Vector2(140, -74), new Vector2(174, -46), () => ChangeFontSize(-2));
            MakeButton(panel, "字号增大", "A+", new Vector2(0, 1), new Vector2(180, -74), new Vector2(214, -46), () => ChangeFontSize(2));
            fontSizeLabel = Chrome("FontSize", panel, fontSize.ToString());
            Stretch(fontSizeLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(222, -74), new Vector2(252, -46));
            followButton = MakeButton(panel, "跟随", "跟随：开", new Vector2(1, 1), new Vector2(-104, -74), new Vector2(-12, -46), ToggleFollow);
            followButton.targetGraphic.color = SelectedColor;

            var scrollRoot = Rect("Output", panel); Stretch(scrollRoot, Vector2.zero, Vector2.one, new Vector2(12, 90), new Vector2(-12, -86));
            scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", scrollRoot); Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12, 0));
            viewport.gameObject.AddComponent<Image>().color = new Color(.02f, .032f, .048f, .9f);
            viewport.gameObject.AddComponent<RectMask2D>();
            emptyOutput = Chrome("EmptyOutput", viewport, "暂无输出记录\n输入 ze-help 查看可用指令", 14);
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

            var prompt = Chrome("Prompt", panel, ">", 18); prompt.color = new Color(.42f, .86f, .77f);
            Stretch(prompt.rectTransform, Vector2.zero, Vector2.zero, new Vector2(12, 36), new Vector2(30, 74));
            var field = Rect("Input", panel); Stretch(field, Vector2.zero, new Vector2(1, 0), new Vector2(34, 36), new Vector2(-162, 74));
            var inputSurface = Surface(field, new Color(.09f, .14f, .20f), 5);
            input = field.gameObject.AddComponent<InputField>(); input.characterLimit = ConsoleLexer.MaximumLineLength;
            input.targetGraphic = inputSurface;
            input.customCaretColor = true; input.caretColor = new Color(.42f, .86f, .77f);
            input.selectionColor = new Color(.18f, .53f, .55f, .55f);
            var fieldText = Text("Text", field, ""); Stretch(fieldText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            fieldText.alignment = TextAnchor.MiddleLeft; fieldText.supportRichText = false; input.textComponent = fieldText;
            var placeholder = Text("Placeholder", field, "输入命令，按 Tab 补全"); Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
            placeholder.alignment = TextAnchor.MiddleLeft; placeholder.color = new Color(.45f, .53f, .61f); input.placeholder = placeholder;
            input.onValueChanged.AddListener(InputChanged);
            input.onSubmit.AddListener(SubmitInput);
            var run = MakeButton(panel, "运行", "运行", new Vector2(1, 0), new Vector2(-152, 36), new Vector2(-84, 74), () => SubmitInput(input.text));
            run.targetGraphic.color = SelectedColor;
            cancelButton = MakeButton(panel, "取消", "取消", new Vector2(1, 0), new Vector2(-78, 36), new Vector2(-12, 74), () => host.Session.CancelCurrent());
            cancelButton.interactable = host.Session.IsRunning;
            var hint = Chrome("KeyboardHint", panel, "↑↓ 历史  ·  PgUp/PgDn 选择补全  ·  Esc 关闭", 11);
            hint.color = new Color(.49f, .57f, .65f);
            Stretch(hint.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 30));

            completions = Rect("Completions", panel); Stretch(completions, Vector2.zero, new Vector2(1, 0), new Vector2(12, 90), new Vector2(-12, 330));
            Surface(completions, new Color(.06f, .095f, .14f, 1), 6);
            completionCaption = Chrome("CompletionCaption", completions, "");
            Stretch(completionCaption.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(10, -30), new Vector2(-144, -2));
            MakeButton(completions, "上一项", "↑", new Vector2(1, 1), new Vector2(-78, -28), new Vector2(-44, -4), () => NavigateCompletion(-1));
            MakeButton(completions, "下一项", "↓", new Vector2(1, 1), new Vector2(-40, -28), new Vector2(-6, -4), () => NavigateCompletion(1));
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
            followButton.GetComponentInChildren<Text>().text = followOutput ? "跟随：开" : "跟随：关";
            followButton.targetGraphic.color = followOutput ? SelectedColor : SurfaceColor;
            if (followOutput) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0; }
        }

        private void CopyOutput()
        {
            var values = host.Session.GetOutput();
            var lines = new string[values.Length];
            for (int i = 0; i < lines.Length; i++) lines[i] = values[i].Message;
            GUIUtility.systemCopyBuffer = string.Join("\n", lines);
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
            var rect = Rect(name, parent); Stretch(rect, anchor, anchor, min, max);
            var image = Surface(rect, SurfaceColor, 4);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors; colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f); colors.pressedColor = new Color(.7f, .85f, .9f); colors.disabledColor = new Color(.55f, .62f, .67f, .5f); colors.fadeDuration = .08f; button.colors = colors;
            button.onClick.AddListener(() => { action(); if (IsOpen && input) input.ActivateInputField(); });
            var text = Chrome("Label", rect, label, size); text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
