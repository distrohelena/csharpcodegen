# Monotonic Stopwatch Runtime Plan

1. Add a runtime template contract test that fails while `stopwatch.hpp` still calls `DateTime::Now()`.
2. Rework the generated stopwatch template to use `std::chrono::steady_clock` directly while preserving the existing managed-facing API.
3. Run the focused runtime template test.
4. Rebuild the DS city package and compare the native binary size report to confirm whether the `tzset_r` / `siscanf` chain shrinks or disappears.
