#pragma once
#include <cstdint>

/// <summary>Matches the installed DS SDK system-tick frequency in hertz.</summary>
inline constexpr std::uint32_t TICK_FREQ = 33513982U / 64U;
/// <summary>Controls the full-width counter without reading host wall time.</summary>
inline std::uint64_t StopwatchDsFakeTicks = 0;
/// <summary>Records initialization requests without resetting a running counter.</summary>
inline unsigned StopwatchDsTickInitCalls = 0;
/// <summary>Models Calico's idempotent monotonic system-counter initialization.</summary>
inline void tickInit() { ++StopwatchDsTickInitCalls; }
/// <summary>Returns the controlled counter independently of CPU profiling origins.</summary>
inline std::uint64_t tickGetCount() { return StopwatchDsFakeTicks; }
