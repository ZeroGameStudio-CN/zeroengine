# ZeroEngine Haptics

`ZeroEngine.Haptics` turns unbounded gameplay intent into conservative dual-motor pulses. It contains no device, Input System, mobile SDK, or project-specific dependency.

## Safety baseline

- Low-frequency motor: `0..0.40`
- High-frequency motor: `0..0.36`
- Pulse duration: `0..0.18` seconds
- Minimum playable perceived strength: `0.03`
- Arbitration: 60 ms cooldown, overlap protection, stronger-pulse replacement, and a rolling energy budget

The response is monotonic and soft-saturating. Non-finite requests and timestamps are rejected before they can enter arbitration state.

## Usage

```csharp
using ZeroEngine.Haptics;

var request = new HapticRequest(
    intensity: authoredImpact,
    sharpness: authoredFrequency,
    sourceDuration: authoredDuration,
    gain: playerStrength * projectHapticScale);

if (HapticResponseResolver.TryResolve(request, out var resolved)
    && arbiter.TryAccept(resolved, unscaledMonotonicTime, out var pulse))
{
    device.SetMotors(pulse.LowFrequencyMotor, pulse.HighFrequencyMotor);
    ScheduleStop(pulse.Duration);
}
```

Call `HapticArbiter.Reset()` whenever the owning gameplay lifecycle restarts or haptics are disabled. Device output and stop scheduling belong to the consuming project.
