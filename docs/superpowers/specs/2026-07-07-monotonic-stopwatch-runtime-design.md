# Monotonic Stopwatch Runtime Design

## Goal

Trim shared native runtime size for constrained platforms by removing the generated `Stopwatch` dependency on `DateTime::Now()` and the wall-clock `system_clock` path.

## Decision

Keep the generated `Stopwatch` API unchanged, but implement it on top of `std::chrono::steady_clock` and native duration arithmetic instead of `DateTime`.

## Why

- The DS linker map shows `DateTime::Now()` and `std::chrono::_V2::system_clock::now()` as live symbols in the city build.
- The same build pulls `tzset_r` and `siscanf`, which is a strong signal that the current wall-clock runtime path drags in timezone parsing.
- `Stopwatch` only needs monotonic elapsed time, not wall-clock timestamps.

## Non-Goals

- Redesigning the generated `DateTime` runtime.
- Removing `TimeSpan` from the runtime surface.
- Changing engine-side `Stopwatch` call sites.
