#if ZE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.Haptics.Unity
{
    /// <summary>Owns one gameplay stream of gamepad pulses and stops its device on pause, focus loss and teardown.</summary>
    [DisallowMultipleComponent]
    public sealed class GamepadImpactFeedback : MonoBehaviour
    {
        private readonly HapticArbiter arbiter = new();
        private readonly GamepadHapticOutput output = new();
        private Gamepad device;
        private bool paused, ticking, observingDevices;

        public bool Play(Gamepad target, HapticRequest request)
        {
            if (!isActiveAndEnabled || !Application.isFocused || paused || target == null || !target.added)
                return false;

            double now = Time.realtimeSinceStartupAsDouble;
            if (!HapticResponseResolver.TryResolve(request, out var resolved) ||
                !arbiter.TryAccept(resolved, now, out var accepted)) return false;

            device = target;
            bool played = output.Play(target, accepted, now);
            UpdateTickSubscription();
            return played;
        }

        public void Stop()
        {
            arbiter.Reset();
            output.StopAll(device, Time.realtimeSinceStartupAsDouble);
            UpdateTickSubscription();
        }

        private void OnEnable()
        {
            if (observingDevices) return;
            InputSystem.onDeviceChange += HandleDeviceChange;
            observingDevices = true;
        }

        private void OnDisable()
        {
            Stop();
            if (observingDevices)
            {
                InputSystem.onDeviceChange -= HandleDeviceChange;
                observingDevices = false;
            }
        }

        private void Tick()
        {
            output.Tick(Time.realtimeSinceStartupAsDouble);
            UpdateTickSubscription();
        }

        private void UpdateTickSubscription()
        {
            if (ticking == output.HasWork) return;
            ticking = output.HasWork;
            if (ticking) InputSystem.onAfterUpdate += Tick;
            else InputSystem.onAfterUpdate -= Tick;
        }

        private void HandleDeviceChange(InputDevice changed, InputDeviceChange change)
        {
            if (changed != device) return;
            output.OnDeviceChange(device, change, Time.realtimeSinceStartupAsDouble);
            UpdateTickSubscription();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Stop();
        }

        private void OnApplicationPause(bool pause)
        {
            paused = pause;
            if (paused) Stop();
        }

        private void OnApplicationQuit() => Stop();
    }
}
#endif
