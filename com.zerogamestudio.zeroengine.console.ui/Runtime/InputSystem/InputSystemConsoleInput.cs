using UnityEngine;
using UnityEngine.EventSystems;
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace ZeroEngine.Console.UI
{
    internal sealed class InputSystemConsoleInput : BaseInput
    {
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private Keyboard keyboard;
        private string composition = string.Empty;
        private IMECompositionMode mode;
        private Vector2 imeCursor;
        protected override void OnEnable() { base.OnEnable(); RefreshKeyboard(); }
        private void Update() => RefreshKeyboard();
        private void RefreshKeyboard()
        {
            if (keyboard == Keyboard.current) return;
            if (keyboard != null) keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard = Keyboard.current;
            composition = string.Empty;
            if (keyboard != null) keyboard.onIMECompositionChange += CompositionChanged;
        }
        private void CompositionChanged(IMECompositionString value) => composition = value.ToString();
        protected override void OnDisable()
        {
            if (keyboard != null) keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard = null; composition = string.Empty;
            base.OnDisable();
        }
        public override string compositionString => composition;
        public override IMECompositionMode imeCompositionMode
        {
            get => mode;
            set { mode = value; keyboard?.SetIMEEnabled(value != IMECompositionMode.Off); }
        }
        public override Vector2 compositionCursorPos
        {
            get => imeCursor;
            set { imeCursor = value; keyboard?.SetIMECursorPosition(value); }
        }
        private static ButtonControl MouseButton(int index)
        {
            var mouse = Mouse.current;
            return mouse == null ? null : index == 0 ? mouse.leftButton : index == 1 ? mouse.rightButton : mouse.middleButton;
        }
        public override bool mousePresent => Mouse.current != null;
        public override Vector2 mousePosition => Mouse.current == null ? Vector2.zero : Mouse.current.position.ReadValue();
        public override Vector2 mouseScrollDelta => Mouse.current == null ? Vector2.zero : Mouse.current.scroll.ReadValue();
        public override bool GetMouseButton(int button) => MouseButton(button)?.isPressed ?? false;
        public override bool GetMouseButtonDown(int button) => MouseButton(button)?.wasPressedThisFrame ?? false;
        public override bool GetMouseButtonUp(int button) => MouseButton(button)?.wasReleasedThisFrame ?? false;
        public override bool touchSupported => Touchscreen.current != null;
        public override int touchCount
        {
            get
            {
                var screen = Touchscreen.current;
                if (screen == null) return 0;
                int count = 0;
                foreach (var touch in screen.touches) if (touch.isInProgress || touch.press.wasReleasedThisFrame) count++;
                return count;
            }
        }
        public override Touch GetTouch(int index)
        {
            var screen = Touchscreen.current;
            if (screen == null) return default;
            foreach (var touch in screen.touches)
            {
                if (!touch.isInProgress && !touch.press.wasReleasedThisFrame) continue;
                if (index-- != 0) continue;
                var phase = touch.phase.ReadValue();
                return new Touch { fingerId = touch.touchId.ReadValue(), position = touch.position.ReadValue(),
                    rawPosition = touch.position.ReadValue(), deltaPosition = touch.delta.ReadValue(),
                    pressure = touch.pressure.ReadValue(), maximumPossiblePressure = 1,
                    phase = phase == UnityEngine.InputSystem.TouchPhase.Began ? UnityEngine.TouchPhase.Began :
                        phase == UnityEngine.InputSystem.TouchPhase.Moved ? UnityEngine.TouchPhase.Moved :
                        phase == UnityEngine.InputSystem.TouchPhase.Stationary ? UnityEngine.TouchPhase.Stationary :
                        phase == UnityEngine.InputSystem.TouchPhase.Ended ? UnityEngine.TouchPhase.Ended : UnityEngine.TouchPhase.Canceled };
            }
            return default;
        }
        public override float GetAxisRaw(string axisName) => 0;
        public override bool GetButtonDown(string buttonName) => false;
#endif
    }
}
