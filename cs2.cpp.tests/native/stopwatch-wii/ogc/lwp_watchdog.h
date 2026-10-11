#pragma once
#include <cstdint>

// Fake hardware counter with the installed Wii libogc tick frequency and
// PPCTicksToMs(u64) conversion: timer=60750000 Hz, so ms=ticks/60750.
inline std::uint64_t StopwatchFakeTicks = 0;
inline std::uint64_t gettime() { return StopwatchFakeTicks; }
inline std::uint64_t ticks_to_millisecs(std::uint64_t ticks) { return ticks / 60750ULL; }
