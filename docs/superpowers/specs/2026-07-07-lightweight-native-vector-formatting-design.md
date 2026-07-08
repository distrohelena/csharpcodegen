# Lightweight Native Vector Formatting Design

## Goal

Remove the remaining shared-runtime dependency on `std::snprintf` from `cs2.cpp/.net.cpp/system/numerics/vector.hpp` so native builds stop pulling the heavy `printf` and locale formatting stack through vector `ToString()` support.

## Problem

After the earlier generic runtime cleanup, the DS native size report still shows large formatted-I/O contributors:

- `libc.a(libc_a-vfiprintf.o)`
- `libc.a(libc_a-svfprintf.o)`
- `libc.a(libc_a-svfiprintf.o)`
- `libc.a(libc_a-svfiscanf.o)`
- `libc.a(libc_a-categories.o)`
- `libc.a(libc_a-locale.o)`

The shared vector runtime still formats floating-point lanes with `std::snprintf("%.9g")` and `std::snprintf("%.17g")`. That helper is generic, always copied with the numerics runtime, and is a plausible source of the remaining formatted-I/O tax even when native builds do not need rich locale-aware formatting.

## Constraints

- This must stay generic in `csharpcodegen`; no DS-specific branches.
- The public `Vector_1<T>::ToString()` surface must remain available.
- Generated code must not require downstream rewrites or platform patching.
- The fix should prefer simple deterministic formatting over perfect parity with host C runtime formatting details.

## Design

Replace the `std::snprintf` float formatting helper in `vector.hpp` with a lightweight in-template formatter that:

- emits `NaN`, `Infinity`, and `-Infinity` explicitly for non-finite floating-point values
- emits integer lanes with the existing `std::to_string`
- emits floating-point lanes using `std::to_chars` when available for locale-free buffer formatting
- falls back to a compact manual normalization path when `std::to_chars` floating-point support is unavailable in the target standard library

The first implementation step is to remove the direct `snprintf` dependency from the template source and lock that contract with a runtime-template test. If the fallback still needs refinement for correctness, that can happen behind the same template-owned helper without reintroducing `printf` or stream dependencies.

## Verification

- Runtime-template contract test proves `vector.hpp` no longer contains `std::snprintf`.
- `csharpcodegen` test suite passes for the touched contract tests.
- A fresh DS build is produced and compared against the current baseline:
  - package size: `1,933,312`
  - accounted native binary size: `769,374`
- The DS native size report should show whether `vfiprintf` and related locale contributors dropped measurably.
