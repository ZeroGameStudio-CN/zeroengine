using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Console.UI
{
    // Keep the existing component/GUID; the optional backend supplies platform input.
    public sealed class ConsoleInputOverride : BaseInput
    {
        private BaseInput source;
        protected override void OnEnable()
        {
            base.OnEnable();
            if (!source) source = ConsoleInputBackend.CreateInputSource(gameObject);
            if (source == this) source = null;
            if (source) source.enabled = true;
        }
        protected override void OnDisable()
        {
            if (source) source.enabled = false;
            base.OnDisable();
        }
        public override string compositionString => source ? source.compositionString : base.compositionString;
        public override IMECompositionMode imeCompositionMode
        {
            get => source ? source.imeCompositionMode : base.imeCompositionMode;
            set { if (source) source.imeCompositionMode = value; else base.imeCompositionMode = value; }
        }
        public override Vector2 compositionCursorPos
        {
            get => source ? source.compositionCursorPos : base.compositionCursorPos;
            set { if (source) source.compositionCursorPos = value; else base.compositionCursorPos = value; }
        }
        public override bool mousePresent => source ? source.mousePresent : base.mousePresent;
        public override Vector2 mousePosition => source ? source.mousePosition : base.mousePosition;
        public override Vector2 mouseScrollDelta => source ? source.mouseScrollDelta : base.mouseScrollDelta;
        public override bool GetMouseButton(int button) => source ? source.GetMouseButton(button) : base.GetMouseButton(button);
        public override bool GetMouseButtonDown(int button) => source ? source.GetMouseButtonDown(button) : base.GetMouseButtonDown(button);
        public override bool GetMouseButtonUp(int button) => source ? source.GetMouseButtonUp(button) : base.GetMouseButtonUp(button);
        public override bool touchSupported => source ? source.touchSupported : base.touchSupported;
        public override int touchCount => source ? source.touchCount : base.touchCount;
        public override Touch GetTouch(int index) => source ? source.GetTouch(index) : base.GetTouch(index);
        public override float GetAxisRaw(string axisName) => source ? source.GetAxisRaw(axisName) : base.GetAxisRaw(axisName);
        public override bool GetButtonDown(string buttonName) => source ? source.GetButtonDown(buttonName) : base.GetButtonDown(buttonName);
    }
}
