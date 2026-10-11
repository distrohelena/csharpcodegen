#include "system/diagnostics/stopwatch.hpp"
#include <cstdio>
#include <type_traits>

#if (defined(HE_CPP_PLATFORM_WII) && HE_CPP_PLATFORM_WII) || defined(__gamecube__)
static_assert(std::is_same_v<Stopwatch::TickTimestamp, std::uint64_t>,
    "Wii/GameCube must select the full-width libogc hardware clock, not generic steady_clock");

static void SetMilliseconds(std::uint64_t milliseconds) {
    StopwatchFakeTicks = milliseconds * 60750ULL;
}

int main() {
    SetMilliseconds(0);
    Stopwatch timer;
    timer.Start();
    // Cross the signed-32-bit microsecond boundary that the failed session exceeded.
    SetMilliseconds(2147483ULL);
    double before = timer.get_Elapsed().get_TotalMilliseconds();
    SetMilliseconds(2147484ULL);
    double after = timer.get_Elapsed().get_TotalMilliseconds();
    if (before != 2147483.0 || after != 2147484.0 || after < before) { return 1; }
    SetMilliseconds(2180000ULL); // 36m20s, matching the observed failure interval.
    if (timer.get_Elapsed().get_TotalMilliseconds() != 2180000.0) { return 2; }
    timer.Stop();
    SetMilliseconds(4320000000ULL); // Fifty days also exceeds uint32 milliseconds.
    if (timer.get_Elapsed().get_TotalMilliseconds() != 2180000.0) { return 3; }
    timer.Start();
    SetMilliseconds(4320000008ULL);
    if (timer.get_Elapsed().get_TotalMilliseconds() != 2180008.0) { return 4; }
    timer.Restart();
    SetMilliseconds(4320000016ULL);
    if (timer.get_Elapsed().get_TotalMilliseconds() != 8.0) { return 5; }
    // The installed SDK clock_gettime converts an absolute RTC-origin tick count
    // through (ticks*4000/243)%1e9. Unsigned multiplication overflows for old
    // origins and can make its nanosecond field roll over inside a whole second.
    std::uint64_t oldTicks = 800000000523ULL * 60750ULL;
    std::uint64_t newTicks = 800000000524ULL * 60750ULL;
    std::uint64_t oldNanos = oldTicks / 60750000ULL * 1000000000ULL + (oldTicks * 4000ULL / 243ULL) % 1000000000ULL;
    std::uint64_t newNanos = newTicks / 60750000ULL * 1000000000ULL + (newTicks * 4000ULL / 243ULL) % 1000000000ULL;
    if (newNanos >= oldNanos) { return 6; }
    SetMilliseconds(800000000523ULL);
    timer.Restart();
    SetMilliseconds(800000000524ULL);
    if (timer.get_Elapsed().get_TotalMilliseconds() != 1.0) { return 7; }
    std::printf("native stopwatch long-uptime/pause/resume/restart PASS\n");
    return 0;
}
#else
static_assert(!std::is_same_v<Stopwatch::TickTimestamp, std::uint64_t>,
    "Hosted platforms must preserve the generic steady_clock backend");
int main() {
    Stopwatch timer;
    timer.Start();
    if (timer.get_Elapsed().get_TotalMilliseconds() < 0.0) { return 1; }
    std::printf("hosted stopwatch backend PASS\n");
    return 0;
}
#endif
