#pragma once

#include <cstdint>

#include "native_runtime.hpp"
#include "native_timespan.hpp"

#if HE_CPP_USE_STD_CHRONO
#include <chrono>
#endif

/// <summary>
/// Represents a lightweight managed-style point in time expressed as Unix milliseconds.
/// When the standard clock is disabled this is milliseconds since the provider's monotonic
/// origin, not the Unix epoch; use it for elapsed time only.
/// </summary>
class DateTime {
public:
    int64_t UnixMilliseconds;

    DateTime()
        : UnixMilliseconds(0) {
    }

    explicit DateTime(int64_t unixMilliseconds)
        : UnixMilliseconds(unixMilliseconds) {
    }

    /// <summary>
    /// Returns the current point in time.
    /// When the standard clock is disabled this is milliseconds since the provider's monotonic
    /// origin, not the Unix epoch; use it for elapsed time only.
    /// </summary>
    static DateTime Now() {
        return DateTime(CurrentUnixMilliseconds());
    }

    /// <summary>
    /// Returns the current point in time.
    /// When the standard clock is disabled this is milliseconds since the provider's monotonic
    /// origin, not the Unix epoch; use it for elapsed time only.
    /// </summary>
    static DateTime UtcNow() {
        return DateTime(CurrentUnixMilliseconds());
    }

private:
    static int64_t CurrentUnixMilliseconds() {
#if HE_CPP_USE_STD_CHRONO
        using namespace std::chrono;
        return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
#else
        // Providers expose a monotonic clock only; DateTime::Now measures elapsed time, not wall time.
        return static_cast<int64_t>(he_cpp_custom::MonotonicMicroseconds() / 1000u);
#endif
    }
};

inline TimeSpan operator-(const DateTime& left, const DateTime& right) {
    return TimeSpan(static_cast<double>(left.UnixMilliseconds - right.UnixMilliseconds));
}
