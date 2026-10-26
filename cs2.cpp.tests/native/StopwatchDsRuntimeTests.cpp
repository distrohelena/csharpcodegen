#include <calico/system/tick.h>
#include "system/diagnostics/stopwatch.hpp"
#include <cmath>
#include <cstdio>
#include <type_traits>

/// <summary>Checks duration without relying on host clock resolution or scheduling.</summary>
static bool EqualsMilliseconds(double actual, double expected) {
    return std::fabs(actual - expected) < 0.000001;
}

/// <summary>Exercises the actual stopwatch template against the DS SDK full-width tick contract.</summary>
int main() {
    StopwatchDsFakeTicks = 0;
    Stopwatch timer;
    timer.Start();
    StopwatchDsFakeTicks = static_cast<std::uint64_t>(TICK_FREQ) * 2U;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 2000.0)) {
        std::printf("DS elapsed time did not follow the native tick counter\n");
        return 1;
    }
    // Starting a second stopwatch must not reset the scene clock's origin.
    Stopwatch other;
    other.Start();
    StopwatchDsFakeTicks += TICK_FREQ;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 3000.0) ||
        !EqualsMilliseconds(other.get_Elapsed().get_TotalMilliseconds(), 1000.0)) { return 2; }
    timer.Stop();
    StopwatchDsFakeTicks += static_cast<std::uint64_t>(TICK_FREQ) * 86400U * 50U;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 3000.0)) { return 3; }
    timer.Start();
    StopwatchDsFakeTicks += TICK_FREQ;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 4000.0)) { return 4; }
    timer.Restart();
    StopwatchDsFakeTicks += TICK_FREQ;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 1000.0)) { return 5; }
    // A wrapping cpuGetTiming result cannot satisfy this low-word boundary.
    StopwatchDsFakeTicks = (1ULL << 32U) - 8U;
    timer.Restart();
    StopwatchDsFakeTicks += 16U;
    if (!EqualsMilliseconds(timer.get_Elapsed().get_TotalMilliseconds(), 16000.0 / TICK_FREQ)) { return 6; }
    StopwatchDsFakeTicks = 1ULL << 52U;
    timer.Restart();
    StopwatchDsFakeTicks += 1U;
    double before = timer.get_Elapsed().get_TotalMilliseconds();
    StopwatchDsFakeTicks += 1U;
    double after = timer.get_Elapsed().get_TotalMilliseconds();
    if (!EqualsMilliseconds(before, 1000.0 / TICK_FREQ) ||
        !EqualsMilliseconds(after, 2000.0 / TICK_FREQ) || after < before) { return 7; }
    if (StopwatchDsTickInitCalls == 0 || !std::is_same_v<Stopwatch::TickTimestamp, std::uint64_t>) { return 8; }
    std::printf("DS native stopwatch elapsed/pause/resume/restart/shared-clock/64-bit-boundary PASS\n");
    return 0;
}
