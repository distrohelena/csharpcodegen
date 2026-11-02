#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <windows.h>
#include "system/diagnostics/stopwatch.hpp"

/// Native counter observations supplied to the real stopwatch template.
static std::int64_t Counter = 0;
/// The retail Xbox timer frequency; a large origin must not change its intervals.
static std::int64_t Frequency = 733333333;
/// Counts calls so a host clock cannot accidentally satisfy the target test.
static unsigned CounterCalls = 0;
/// Makes native API failures observable without manufacturing a fallback clock.
static bool CounterAvailable = true;
/// Makes invalid frequency handling independently testable.
static bool FrequencyAvailable = true;

/// Implements the declared Xbox counter API using exact test timestamps.
BOOL QueryPerformanceCounter(LARGE_INTEGER* value) {
    ++CounterCalls;
    value->QuadPart = Counter;
    return CounterAvailable;
}

/// Implements the declared Xbox frequency API without elapsed-time rounding.
BOOL QueryPerformanceFrequency(LARGE_INTEGER* value) {
    value->QuadPart = Frequency;
    return FrequencyAvailable;
}

/// Exercises counter origins, elapsed intervals, lifecycle and explicit failure behavior.
int main() {
    Counter = std::numeric_limits<std::int64_t>::max() / 1000000000 - 1000;
    Stopwatch timer;
    timer.Start();
    Counter += 2000;
    double elapsed = timer.get_Elapsed().get_TotalMilliseconds();
    const double expected = 2000.0 * 1000.0 / 733333333.0;
    if (CounterCalls < 2 || std::abs(elapsed - expected) > 1e-12) {
        std::cerr << "Xbox elapsed interval must use native ticks across the SDK absolute-nanosecond overflow boundary; calls="
                  << CounterCalls << " actual=" << elapsed << " expected=" << expected << '\n';
        return 1;
    }
    if (std::abs(static_cast<double>(timer.Elapsed.TotalMilliseconds) - expected) > 1e-12) return 2;
    timer.Stop();
    Counter += Frequency;
    if (std::abs(timer.get_Elapsed().get_TotalMilliseconds() - expected) > 1e-12) return 3;
    timer.Start();
    Counter += Frequency;
    if (std::abs(timer.get_Elapsed().get_TotalMilliseconds() - (expected + 1000.0)) > 1e-9) return 4;

    Counter = std::numeric_limits<std::int64_t>::max() - Frequency * 3;
    timer.Restart();
    Counter += Frequency * 2;
    if (std::abs(timer.get_Elapsed().get_TotalMilliseconds() - 2000.0) > 1e-9) return 5;
    timer.Restart();
    if (timer.get_Elapsed().get_TotalMilliseconds() != 0.0) return 6;

    CounterAvailable = false;
    bool rejected = false;
    try { timer.Restart(); } catch (const std::runtime_error&) { rejected = true; }
    if (!rejected) return 7;
    CounterAvailable = true;
    timer.Restart();
    FrequencyAvailable = false;
    rejected = false;
    try { static_cast<void>(timer.get_Elapsed()); } catch (const std::runtime_error&) { rejected = true; }
    if (!rejected) return 8;
    FrequencyAvailable = true;
    Frequency = 0;
    rejected = false;
    try { static_cast<void>(timer.get_Elapsed()); } catch (const std::runtime_error&) { rejected = true; }
    if (!rejected) return 9;
    std::cout << "PASS Xbox stopwatch: overflow boundary, high origin, live/stop/resume/restart, API/frequency failures\n";
}
