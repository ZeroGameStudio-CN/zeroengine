using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Pad
{
    /// <summary>A PAD icon and visible label above a single-line value.</summary>
    [DisallowMultipleComponent]
    public sealed class PadMetricView : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _value;

        public bool HasRequiredReferences => Owns(_icon) && Owns(_label) && Owns(_value) && _label != _value;

        public void ConfigureForRuntime(Image icon, TMP_Text label, TMP_Text value)
        {
            if (!Owns(icon) || !Owns(label) || !Owns(value) || label == value)
                throw new ArgumentException("PAD metric references must be distinct graphics owned by this view.");

            _icon = icon;
            _label = label;
            _value = value;
        }

        public void SetContent(Sprite icon, string label, string value)
        {
            if (!HasRequiredReferences)
                throw new InvalidOperationException("Configure the PAD metric before setting its content.");

            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            SetText(_label, label);
            SetText(_value, value);
        }

        private bool Owns(Component part) => part && part.transform.IsChildOf(transform);

        private static void SetText(TMP_Text target, string text)
        {
            target.enabled = true;
            target.richText = false;
            target.raycastTarget = false;
            target.textWrappingMode = TextWrappingModes.NoWrap;
            target.overflowMode = TextOverflowModes.Ellipsis;
            target.text = text ?? string.Empty;
        }
    }
}
