using UnityEngine;

namespace ZeroEngine.Console.UI
{
    internal sealed class ConsoleCommandHintView
    {
        public RectTransform Root { get; }
        private readonly UnityEngine.UI.Text signature, parameter;
        public bool IsVisible => Root.gameObject.activeSelf;
        public float Height { get; private set; }

        public ConsoleCommandHintView(Transform parent, Font font, int fontSize)
        {
            Root = new GameObject("CommandHint", typeof(RectTransform)).GetComponent<RectTransform>();
            Root.SetParent(parent, false);
            signature = Label("Signature", font, new Color(.71f, .75f, .82f));
            parameter = Label("Parameter", font, new Color(.61f, .72f, .88f));
            SetFontSize(fontSize);
            Root.gameObject.SetActive(false);
        }

        private UnityEngine.UI.Text Label(string name, Font font, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();
            label.transform.SetParent(Root, false);
            label.font = font; label.color = color; label.raycastTarget = false; label.supportRichText = false;
            label.alignment = TextAnchor.MiddleLeft; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            return label;
        }

        public void SetFontSize(int fontSize)
        {
            int size = Mathf.Clamp(fontSize - 2, 12, 18);
            signature.fontSize = parameter.fontSize = size;
            Height = size * 2 + 8;
            signature.rectTransform.offsetMin = new Vector2(0, size + 4); signature.rectTransform.offsetMax = Vector2.zero;
            parameter.rectTransform.offsetMin = Vector2.zero; parameter.rectTransform.offsetMax = new Vector2(0, -size - 4);
        }

        public void Show(ConsoleCommandHint hint)
        {
            bool visible = hint != null;
            if (visible) { signature.text = hint.Signature; parameter.text = hint.Detail; }
            else { signature.text = string.Empty; parameter.text = string.Empty; }
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }
    }
}
