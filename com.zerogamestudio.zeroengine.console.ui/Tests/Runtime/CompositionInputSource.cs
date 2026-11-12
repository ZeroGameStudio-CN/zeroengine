using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Console.UI.Tests
{
    public sealed class CompositionInputSource : BaseInput
    {
        public string Composition = string.Empty;
        private IMECompositionMode mode;
        private Vector2 cursor;
        public override string compositionString => Composition;
        public override IMECompositionMode imeCompositionMode { get => mode; set => mode = value; }
        public override Vector2 compositionCursorPos { get => cursor; set => cursor = value; }
        public override bool mousePresent => true;
        public override Vector2 mousePosition => Vector2.zero;
        public override Vector2 mouseScrollDelta => Vector2.zero;
        public override bool GetMouseButton(int button) => false;
        public override bool GetMouseButtonDown(int button) => false;
        public override bool GetMouseButtonUp(int button) => false;
        public override bool touchSupported => false;
        public override int touchCount => 0;
        public override bool GetButtonDown(string buttonName) => false;
        public override float GetAxisRaw(string axisName) => 0;
    }
}
