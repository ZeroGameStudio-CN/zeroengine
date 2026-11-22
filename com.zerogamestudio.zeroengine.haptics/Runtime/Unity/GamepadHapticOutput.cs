#if ZE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using ZeroEngine.Haptics;

namespace ZeroEngine.Haptics.Unity
{
    /// <summary>
    /// Owns each device's current pulse and bounded stop retries. The caller supplies
    /// realtime so hitstop, pause and a disabled EffectManager cannot extend a pulse.
    /// </summary>
    public sealed class GamepadHapticOutput
    {
        public const int StopAttemptCount = 4;
        public const double StopRetryInterval = 0.05;

        private sealed class Output
        {
            public Gamepad Device;
            public double Deadline;
            public double NextStopTime;
            public int StopsRemaining;
            public int Generation;
            public bool Playing;
        }

        private readonly List<Output> outputs = new();
        private readonly Action<string> diagnostic;
        private double nextDiagnosticTime = double.NegativeInfinity;

        public GamepadHapticOutput(Action<string> diagnostic = null)
        {
            this.diagnostic = diagnostic;
        }

        public bool HasWork => outputs.Count > 0;

        public bool Play(Gamepad device, HapticResolvedPulse pulse, double now)
        {
            if (device == null || !device.added) return false;

            // Stop the old device, but keep its retries separate from the new pulse.
            foreach (Output previous in outputs)
            {
                if (previous.Device != device && previous.Playing)
                    BeginStop(previous, now);
            }

            Tick(now);
            Output output = GetOrAdd(device);
            output.Generation++;
            output.Playing = true;
            output.Deadline = now + pulse.Duration;
            output.StopsRemaining = 0;

            if (!WriteMotors(device, pulse.LowFrequencyMotor, pulse.HighFrequencyMotor, now))
            {
                BeginStop(output, now);
                Tick(now);
                return false;
            }
            return true;
        }

        public void StopAll(Gamepad current, double now)
        {
            if (current != null && current.added)
                GetOrAdd(current);

            foreach (Output output in outputs)
                BeginStop(output, now);

            // The first zero is synchronous, including disable/focus/quit paths.
            Tick(now);
        }

        public void OnDeviceChange(Gamepad device, InputDeviceChange change, double now)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Enabled:
                    // Do not rely on Input System's cached motor speed: it may already
                    // be zero even when the previous stop never reached the hardware.
                    if (device.added)
                    {
                        BeginStop(GetOrAdd(device), now);
                        Tick(now);
                    }
                    break;
                case InputDeviceChange.Disabled:
                    if (Find(device) is { } disabled)
                    {
                        BeginStop(disabled, now);
                        Tick(now);
                    }
                    break;
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Removed:
                    if (Find(device) is { } disconnected)
                    {
                        Report(device, now, "device detached; reset required on reconnect");
                        outputs.Remove(disconnected);
                    }
                    break;
            }
        }

        public void Tick(double now)
        {
            for (int index = outputs.Count - 1; index >= 0; index--)
            {
                Output output = outputs[index];
                if (!output.Device.added)
                {
                    outputs.RemoveAt(index);
                    continue;
                }

                if (output.Playing)
                {
                    if (now < output.Deadline) continue;
                    BeginStop(output, now);
                }

                if (now < output.NextStopTime) continue;

                int generation = output.Generation;
                WriteMotors(output.Device, 0f, 0f, now);
                if (generation != output.Generation) continue;

                output.StopsRemaining--;
                // Schedule from this attempt, not the expired deadline: low FPS must
                // not collapse all retries into the same device update.
                output.NextStopTime = now + StopRetryInterval;
                if (output.StopsRemaining == 0)
                    outputs.Remove(output);
            }
        }

        private static void BeginStop(Output output, double now)
        {
            output.Generation++;
            output.Playing = false;
            output.StopsRemaining = StopAttemptCount;
            output.NextStopTime = now;
        }

        private Output GetOrAdd(Gamepad device)
        {
            Output output = Find(device);
            if (output != null) return output;
            output = new Output { Device = device };
            outputs.Add(output);
            return output;
        }

        private Output Find(Gamepad device)
        {
            foreach (Output output in outputs)
            {
                if (output.Device == device) return output;
            }

            return null;
        }

        private bool WriteMotors(Gamepad device, float low, float high, double now)
        {
            try
            {
                device.SetMotorSpeeds(low, high);
                return true;
            }
            catch (Exception exception)
            {
                Report(device, now, $"motor command failed ({exception.GetType().Name}); bounded stop recovery");
                return false;
            }
        }

        private void Report(Gamepad device, double now, string reason)
        {
            if (diagnostic == null || now < nextDiagnosticTime) return;
            nextDiagnosticTime = now + 10d;
            diagnostic($"[Haptics] device={device.deviceId}, layout={device.layout}: {reason}");
        }
    }
}
#endif
