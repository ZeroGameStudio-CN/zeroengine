using System;
using UnityEngine;

namespace ZeroEngine.UI.Toast
{
    [Serializable]
    public sealed class ToastStyle
    {
        public ToastSeverity Severity = ToastSeverity.Info;
        public Color BackgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        public Color AccentColor = new Color(0.35f, 0.65f, 1f, 1f);
        public Color TextColor = Color.white;
        public Sprite Icon;
        public float Duration = 2f;
    }
}
