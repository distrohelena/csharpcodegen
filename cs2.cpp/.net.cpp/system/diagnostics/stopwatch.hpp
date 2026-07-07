#pragma once

#include <chrono>

#include "runtime/native_timespan.hpp"

namespace System {
namespace Diagnostics {

/// <summary>
/// Provides lightweight managed-style stopwatch timing for player runtime diagnostics.
/// </summary>
class Stopwatch {
public:
    using TickClock = std::chrono::steady_clock;

    class LiveMilliseconds {
    public:
        LiveMilliseconds()
            : Owner(nullptr) {
        }

        explicit LiveMilliseconds(const Stopwatch* owner)
            : Owner(owner) {
        }

        operator double() const {
            return Owner != nullptr ? Owner->ComputeElapsedMilliseconds() : 0.0;
        }

    private:
        const Stopwatch* Owner;
    };

    class LiveTimeSpan {
    public:
        LiveTimeSpan()
            : TotalMilliseconds(), Owner(nullptr) {
        }

        explicit LiveTimeSpan(const Stopwatch* owner)
            : TotalMilliseconds(owner), Owner(owner) {
        }

        operator TimeSpan() const {
            return Owner != nullptr ? Owner->ComputeElapsed() : TimeSpan();
        }

        LiveMilliseconds TotalMilliseconds;

    private:
        const Stopwatch* Owner;
    };

    /// <summary>
    /// Initializes a new stopwatch in the stopped state.
    /// </summary>
    Stopwatch()
        : Elapsed(this), IsRunningValue(false), StartTimestamp(), TotalElapsedMilliseconds(0.0) {
    }

    /// <summary>
    /// Creates and starts one stopwatch instance in a single call.
    /// </summary>
    /// <returns>Started stopwatch instance.</returns>
    static Stopwatch* StartNew() {
        Stopwatch* stopwatch = new Stopwatch();
        stopwatch->Start();
        return stopwatch;
    }

    /// <summary>
    /// Gets a value indicating whether the stopwatch is currently running.
    /// </summary>
    /// <returns>True when the stopwatch has been started and not stopped yet.</returns>
    bool get_IsRunning() const {
        return IsRunningValue;
    }

    /// <summary>
    /// Starts or resumes the stopwatch.
    /// </summary>
    void Start() {
        if (!IsRunningValue) {
            StartTimestamp = TickClock::now();
            IsRunningValue = true;
        }
    }

    /// <summary>
    /// Restarts the stopwatch from zero elapsed time.
    /// </summary>
    void Restart() {
        TotalElapsedMilliseconds = 0.0;
        StartTimestamp = TickClock::now();
        IsRunningValue = true;
    }

    /// <summary>
    /// Stops the stopwatch and freezes the current elapsed time.
    /// </summary>
    void Stop() {
        if (IsRunningValue) {
            TotalElapsedMilliseconds += ComputeRunningElapsedMilliseconds();
            IsRunningValue = false;
        }
    }

    /// <summary>
    /// Gets the accumulated elapsed time.
    /// </summary>
    /// <returns>Elapsed time as a managed-style duration value.</returns>
    TimeSpan get_Elapsed() {
        return ComputeElapsed();
    }

    /// <summary>
    /// Gets the accumulated elapsed time in a field shape that matches the generated C++ property lowering.
    /// </summary>
    LiveTimeSpan Elapsed;

private:
    /// <summary>
    /// Tracks whether the stopwatch is currently running.
    /// </summary>
    bool IsRunningValue;

    /// <summary>
    /// Captures the instant at which the current running interval started.
    /// </summary>
    TickClock::time_point StartTimestamp;

    /// <summary>
    /// Accumulates elapsed time across stopped and running intervals.
    /// </summary>
    double TotalElapsedMilliseconds;

    /// <summary>
    /// Converts one native monotonic duration into managed-style milliseconds.
    /// </summary>
    /// <param name="duration">Native monotonic duration to convert.</param>
    /// <returns>Elapsed milliseconds represented by the supplied duration.</returns>
    static double ConvertDurationToMilliseconds(TickClock::duration duration) {
        return std::chrono::duration_cast<std::chrono::duration<double, std::milli>>(duration).count();
    }

    /// <summary>
    /// Computes the elapsed milliseconds since the current running interval began.
    /// </summary>
    /// <returns>Elapsed milliseconds since the current start timestamp.</returns>
    double ComputeRunningElapsedMilliseconds() const {
        return ConvertDurationToMilliseconds(TickClock::now() - StartTimestamp);
    }

    double ComputeElapsedMilliseconds() const {
        if (IsRunningValue) {
            return TotalElapsedMilliseconds + ComputeRunningElapsedMilliseconds();
        }

        return TotalElapsedMilliseconds;
    }

    TimeSpan ComputeElapsed() const {
        return TimeSpan(ComputeElapsedMilliseconds());
    }
};

}
}

using Stopwatch = System::Diagnostics::Stopwatch;
