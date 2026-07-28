using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;

namespace ZeroEngine.InputSystem
{
    public enum InputDeviceFamily
    {
        KeyboardMouse,
        Gamepad,
        Touch
    }

    public enum GamepadGlyphStyle
    {
        Auto,
        Xbox,
        PlayStation,
        Nintendo
    }

    public readonly struct InputDevicePresentation : IEquatable<InputDevicePresentation>
    {
        public InputDevicePresentation(InputDeviceFamily family, GamepadGlyphStyle glyphStyle)
        {
            Family = family;
            GlyphStyle = glyphStyle;
        }

        public InputDeviceFamily Family { get; }
        public GamepadGlyphStyle GlyphStyle { get; }
        public bool Equals(InputDevicePresentation other) => Family == other.Family && GlyphStyle == other.GlyphStyle;
        public override bool Equals(object obj) => obj is InputDevicePresentation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Family, (int)GlyphStyle);
    }

    public sealed class InputDevicePresentationTracker
    {
        public InputDevicePresentation Current { get; private set; } =
            new(InputDeviceFamily.KeyboardMouse, GamepadGlyphStyle.Auto);

        public event Action<InputDevicePresentation> Changed;

        public void RecordIntent(InputDevice device)
        {
            if (device == null)
            {
                return;
            }

            InputDevicePresentation next;
            if (device is Gamepad gamepad)
            {
                next = new InputDevicePresentation(InputDeviceFamily.Gamepad, DetectGlyphStyle(gamepad));
            }
            else if (device is Touchscreen)
            {
                next = new InputDevicePresentation(InputDeviceFamily.Touch, GamepadGlyphStyle.Auto);
            }
            else if (device is Keyboard || device is Mouse)
            {
                next = new InputDevicePresentation(InputDeviceFamily.KeyboardMouse, GamepadGlyphStyle.Auto);
            }
            else
            {
                return;
            }

            if (!next.Equals(Current))
            {
                Current = next;
                Changed?.Invoke(Current);
            }
        }

        public static bool IsDeliberatePointerIntent(InputControl control)
        {
            if (control == null)
            {
                return false;
            }

            var path = control.path;
            return path.IndexOf("/delta", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("/position", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static GamepadGlyphStyle DetectGlyphStyle(Gamepad gamepad)
        {
            if (gamepad is DualShockGamepad)
            {
                return GamepadGlyphStyle.PlayStation;
            }

            if (gamepad is SwitchProControllerHID)
            {
                return GamepadGlyphStyle.Nintendo;
            }

            return gamepad is XInputController
                ? GamepadGlyphStyle.Xbox
                : GamepadGlyphStyle.Auto;
        }
    }
}
