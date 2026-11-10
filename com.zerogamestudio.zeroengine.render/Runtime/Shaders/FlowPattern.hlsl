// Zero Render flow-pattern v1. Scalar-only source shared by HLSL and Godot.
// distance/period use the caller's same spatial unit; phase is in cycles.
// These functions never sample engine time or define gameplay geometry.
#ifndef ZERO_RENDER_FLOW_PATTERN_V1
#define ZERO_RENDER_FLOW_PATTERN_V1

float zero_flow_fraction(float value) {
    return value - floor(value);
}

float zero_flow_period(float perimeter, float requested) {
    float period = max(0.1, requested);
    if (perimeter <= 0.0) return period;
    return perimeter / max(1.0, floor(perimeter / period + 0.5));
}

float zero_flow_phase(float distance, float period, float phase_cycles) {
    return distance / max(0.001, period) - phase_cycles;
}

// Periodic distance makes both dash ends identical, including the wrap seam.
float zero_flow_dash(float phase, float duty, float antialias) {
    if (duty <= 0.0) return 0.0;
    if (duty >= 1.0) return 1.0;
    float aa = clamp(antialias, 0.00001, 0.25);
    float distance = abs(zero_flow_fraction(phase - duty * 0.5 + 0.5) - 0.5);
    return 1.0 - smoothstep(duty * 0.5 - aa, duty * 0.5 + aa, distance);
}

// The open-path variant preserves the existing POB line's appearance at its source.
float zero_flow_open_dash(float phase, float duty, float antialias) {
    float cell = zero_flow_fraction(phase);
    float aa = max(0.00001, antialias);
    return smoothstep(0.0, aa, cell) * (1.0 - smoothstep(duty - aa, duty, cell));
}

float zero_flow_tracer(float phase, float sharpness) {
    return pow(clamp(1.0 - abs(zero_flow_fraction(phase) - 0.5) * 2.0, 0.0, 1.0),
        max(0.1, sharpness));
}

float zero_flow_chevron(float phase, float edge, float slope, float width, float antialias) {
    return 1.0 - smoothstep(width * 0.5, width + max(0.00001, antialias),
        abs(zero_flow_fraction(phase) - (0.68 - edge * slope)));
}
#endif
