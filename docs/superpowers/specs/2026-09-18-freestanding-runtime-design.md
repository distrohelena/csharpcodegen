# Freestanding Runtime Design

Date: 2026-09-18
Status: approved

## Context

The generated Helengine core already boots on the PlayStation 1 through the
generic runtime capability contract (see
`2026-09-15-generic-runtime-capabilities-design.md`): a platform disables the
standard storage facilities and supplies a provider header, and the PS1 uses an
EASTL-backed provider plus its own math header.

The Super Nintendo target (llvm-mos-65816, clang for the 65C816) has no hosted
C++ library at all. Its `include/` carries `algorithm`, `array`,
`initializer_list`, `iterator`, `limits`, `new`, `type_traits`, `typeinfo`,
`utility`, `exception` and the C headers, and nothing else. There is no
`<string>`, `<vector>`, `<functional>`, `<memory>`, `<cstring>`, `<cmath>`,
`<chrono>`, `<atomic>` or `<thread>`. Its libm provides only `floorf`. Its
`size_t` is 16 bits. Today the shared runtime templates and the `system/`
layer include hosted headers unconditionally even when the matching
capability is disabled, so no provider alone can make generated output
compile there.

This is the first of four sub-projects that lead to the generated core on the
SNES. The others, out of scope here, are far-memory and banked-code emission,
the SNES platform boot in `helengine-snes`, and a fixed-point math profile.

## Goal

Add a `freestanding` runtime profile under which a generated project and the
shared runtime compile with a freestanding C++20 compiler that offers only the
headers listed above plus `<new>`, and prove it with host tests and a
cross-compile gate on the SNES toolchain.

## Non-goals

- Far pointers, address spaces, code banking or any pointer-size lowering.
- Fixed-point arithmetic. Software floating point is accepted.
- Changes to `helengine.core` authored code.
- The SNES platform builder, provider hooks in `helengine-snes`, or booting the
  core on the SNES.
- Replacing the PS1 provider. PS1 keeps EASTL.

## Decisions

| Decision | Choice | Reason |
|---|---|---|
| Provider strategy | Codegen-owned freestanding containers | EASTL fails to build on the 65816 (string SSO layout assert, hashtable float cast, allocator hooks) and is heavy on a preview compiler. A small library sized for 16-bit pointers serves every no-STL target. |
| Where the provider lives | `cs2.cpp/.net.cpp/runtime/freestanding/` | Generic, not platform-specific; a platform may still override the provider header. |
| Math | Codegen-owned portable software math in the same directory | The SNES libm has only `floorf`; `system/math.hpp` already accepts a caller-supplied math header. |
| Hosted services | Forbidden under `freestanding` | Threading, random devices, atomics and x86 intrinsics are not reachable from the pruned core today; forbidding them prevents silent regressions. |
| Test strategy | Host behavioural tests behind a poison include directory, plus SNES cross-compile of the same objects | Catches hosted includes on any machine; the cross-compile catches compiler-specific failures. |

## Runtime profile

`CPPRuntimeKind` gains `Freestanding`. `CPPRuntimeProfile.CreateFreestanding()`
returns:

| Property | Value |
|---|---|
| Name | `freestanding` |
| DefineName | `HE_CPP_RUNTIME_FREESTANDING` |
| UseStdString, UseStdVector, UseStdUnorderedMap, UseStdUnorderedSet | false |
| UseExceptions | false |
| UseRtti | false |

The runtime option resolver sets these defaults for the profile unless the
caller overrides them: `codegen-use-std-function=false`,
`codegen-use-std-shared-ptr=false`, `codegen-use-std-chrono=false`,
`codegen-use-std-math=false`, `codegen-use-hosted-file-system=false`,
`codegen-runtime-provider-header=runtime/freestanding/freestanding_provider.hpp`,
`codegen-runtime-math-header=runtime/freestanding/freestanding_math.hpp`.
A caller-supplied provider or math header replaces the default, matching the
existing precedence rule (caller options over preset defaults).

The CLI maps `--runtime freestanding` to this profile. The generated config
writer emits `#define HE_CPP_RUNTIME_FREESTANDING 1` and the capability
defines as it does for the other profiles. The conversion report records the
profile name. A `native-core-boot-freestanding` preset is added because a
named preset overwrites the CLI runtime profile and the measurement task
needs both.

## Restriction: hosted services

`CPPRestrictionProfile` gains `ForbidHostedServices`. When set, reaching any of
the following from authored code fails conversion with a deterministic
diagnostic that names the type and the restriction: `System.Threading.*`
(`Thread`, `Interlocked`, `SpinLock`, `SpinWait`, `AutoResetEvent`,
`Volatile`), `System.Random`, `System.Guid` generation, and the
`System.Runtime.Intrinsics` types. The `freestanding` profile does not force
this restriction on; the restriction is a separate axis, as the design of
2026-05-03 requires. The final measurement task enables it together with the
`native-core-boot` restrictions.

## Provider library

Directory `cs2.cpp/.net.cpp/runtime/freestanding/`. Every file includes only
`<new>`, `<type_traits>`, `<utility>`, `<initializer_list>`, `<stdint.h>`,
`<stddef.h>`, `<string.h>` and `<stdlib.h>`. All code is C++20 without
exceptions or RTTI and must compile with `-fno-exceptions -fno-rtti`.

`freestanding_provider.hpp` declares the `he_cpp_custom` names the contract
requires and includes the implementation headers:

| Contract name | Implementation |
|---|---|
| `String` | `FreestandingString` in `freestanding_string.hpp` |
| `Vector<T>` | `FreestandingVector<T>` in `freestanding_vector.hpp` |
| `UnorderedMap<K,V,H,E>` | `FreestandingHashMap<K,V,H,E>` in `freestanding_hash_map.hpp` |
| `UnorderedSet<T,H,E>` | `FreestandingHashSet<T,H,E>` in `freestanding_hash_set.hpp` |
| `Hash<T>` | `FreestandingHash<T>` in `freestanding_hash.hpp` |
| `Function<Signature>` | `FreestandingFunction<Signature>` in `freestanding_function.hpp` |
| `SharedPtr<T>` | `FreestandingSharedPtr<T>` in `freestanding_shared_ptr.hpp` |
| `Allocate`, `Free`, `Fail`, `MonotonicMicroseconds` | declared in `freestanding_hooks.hpp` |

The runtime owns `HeCppOwnedPtr` itself; providers do not supply an owned pointer.

Member sets, derived from what the shared templates and `system/` layer call:

- `FreestandingString`: constructors from nothing, `const char*`,
  `(const char*, size_t)`, `(size_t count, char)`, copy and move;
  `size`, `length`, `empty`, `data`, `c_str`, `operator[]`, `at`, `begin`,
  `end`, `front`, `back`, `push_back`, `pop_back`, `append`, `operator+=`,
  `assign`, `reserve`, `resize`, `clear`, `substr`, `find`, `find_first_of`,
  `erase`, `replace`, `insert`, `swap`, `compare`, `operator==`, `operator!=`,
  `operator<`, and `operator+` with `const char*` and with another string.
  Storage is a heap buffer with length and capacity; there is no small-string
  buffer. `npos` is `size_t(-1)`.
- `FreestandingVector<T>`: `push_back`, `emplace_back`, `pop_back`, `erase`
  of an iterator or range, `insert` at an iterator, `resize`, `reserve`,
  `clear`, `data`, `size`, `empty`, `operator[]`, `at`, `begin`, `end`,
  `front`, `back`, `swap`; growth doubles capacity with a minimum of four.
  Elements are constructed in place and moved on growth.
- `FreestandingHashMap<K,V,H,E>`: open addressing with linear probing and
  tombstones; `find`, `emplace`, `insert`, `insert_or_assign`, `try_emplace`,
  `erase` by key and by iterator, `count`, `contains`, `operator[]`, `begin`,
  `end`, `size`, `empty`, `clear`, `reserve`. Iterators expose `->first` and
  `->second` through a `KeyValue` node. Load factor rehash at 3/4; initial
  capacity 8.
- `FreestandingHashSet<T,H,E>`: the same table specialised for keys only.

Any insertion into the hash map or set may rehash and invalidate all iterators,
pointers and references into it; the shared Dictionary wrapper copies values
before inserting for that reason.

- `FreestandingHash<T>`: FNV-1a 32-bit over the object bytes for integral,
  enum and pointer types; over the characters for `FreestandingString`. Hash
  and equality agree for every key type the templates use.
- `FreestandingFunction<R(Args...)>`: type-erased callable with a 16-byte
  inline buffer and heap fallback; copyable, movable, `explicit operator bool`,
  `operator()`. Empty invocation calls `Fail`.
- `FreestandingSharedPtr<T>`: the single-thread control-block design of the
  integration fixture's `SingleThreadSharedPtr`, moved into the library and
  renamed; same member set (`get`, `use_count`, `reset`, `swap`, conversion
  from derived, custom deleter).
- `FreestandingOwnedPtr<T>`: `get`, `release`, `reset`, `operator->`,
  `operator*`, `explicit operator bool`, move only.

`freestanding_hooks.hpp` declares, in `he_cpp_custom`: `void* Allocate(size_t)`,
`void Free(void*)`, `[[noreturn]] void Fail(const char*)`,
`uint64_t MonotonicMicroseconds()`. `freestanding_hooks_default.cpp` defines
`Allocate` and `Free` over `malloc` and `free` as weak symbols where the
compiler supports weak linkage, and as plain definitions the platform may
omit from its link otherwise. `Fail` and `MonotonicMicroseconds` have no
default; the platform must define them, and a missing definition is a link
error by design.

`freestanding_math.hpp` declares and `freestanding_math.cpp` defines, with C
linkage, the set `system/math.hpp` requires when standard math is off, which
is the set the PS1 header supplies: `ceil`, `floor`, `fabs`, `acos`, `asin`,
`sin`, `cos`, `tan`, `sqrt`, `log`, `log2`, `fmod`, `atan2` in `double`, and
`int finite(double)`. Implementations are portable software routines:
Newton iteration for `sqrt`, range-reduced polynomial approximations for the
trigonometric and logarithmic functions, exact handling of NaN and infinity
through the IEEE bit patterns. Accuracy target: relative error at or below
1e-9 across the domains the host tests sample. When a hosted `<math.h>` is
present the header forwards to it, following the PS1 pattern, so host tests
of everything else are not affected by the software math.

## Hosted-header hygiene

Every hosted include in `runtime/` and `system/` is placed behind the
capability that needs it. The mapping:

| Header | Files | Guard or replacement |
|---|---|---|
| `<string>`, `<vector>`, `<unordered_map>`, `<unordered_set>` | `native_runtime.hpp` and any file that still includes them directly | already guarded in `native_runtime.hpp`; direct includes elsewhere are removed in favour of `HeCpp*` aliases |
| `<functional>` | `native_runtime.hpp`, `native_event.hpp`, `native_hash.hpp`, `native_dictionary.hpp` | behind `HE_CPP_USE_STD_FUNCTION`; hashing uses `HeCppHash` |
| `<memory>` | `native_runtime.hpp`, `native_event.hpp`, `native_span.hpp` | behind `HE_CPP_USE_STD_SHARED_PTR`; `std::unique_ptr` becomes `HeCppOwnedPtr` |
| `<stdexcept>`, `<exception>` | `native_exceptions.hpp`, `native_string.hpp`, `not_implemented_exception.hpp` | behind `HE_CPP_USE_EXCEPTIONS` |
| `<typeinfo>` | `native_string.hpp` | behind `HE_CPP_USE_RTTI` |
| `<chrono>` | `native_datetime.hpp`, `stopwatch.hpp`, `spin_wait.hpp` | behind `HE_CPP_USE_STD_CHRONO` |
| `<charconv>` | `number.hpp` | already behind `HE_CPP_USE_STD_STRING` |
| `<cmath>` | `math.hpp`, `number.hpp`, `numerics/vector.hpp`, intrinsics | behind `HE_CPP_USE_STD_MATH` |
| `<cstring>`, `<cstdlib>`, `<cstdio>`, `<cctype>`, `<cerrno>` | `native_memory_ops.hpp`, `native_string.hpp`, `native_memory.hpp`, `path.cpp`, `console.cpp`, `file-stream.*` | C spellings `<string.h>`, `<stdlib.h>`, `<stdio.h>`, `<ctype.h>`, `<errno.h>` |
| `<string_view>` | `guid.hpp`, `string-builder.hpp` | removed; use `HeCppString` and pointer-length pairs |
| `<atomic>`, `<thread>`, `<mutex>`, `<condition_variable>`, `<random>` | `system/threading/*`, `guid.hpp`, `random.hpp` | hosted-services-only |
| x86 intrinsics headers | `system/runtime/intrinsics/*` | hosted-services-only |

A new macro `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` is 1 for every profile
except `freestanding`. Files marked hosted-services-only begin with:

```cpp
#if !HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#error "This runtime service needs hosted threading or OS facilities and is unavailable under the freestanding runtime."
#endif
```

`HeCppOwnedPtr<T>` is added to `native_runtime.hpp`: `std::unique_ptr<T>` when
`HE_CPP_USE_STD_SHARED_PTR` is on, otherwise `he_cpp_custom::OwnedPtr<T>`.

`guid.hpp` under the freestanding runtime keeps parsing and formatting and
loses only generation, which needed the atomic counter; generation calls
`Fail` with a clear message.

## Tests

Unit tests in `cs2.cpp.tests`:

- `CPPRuntimeProfile.CreateFreestanding` values; CLI mapping of
  `--runtime freestanding`; config writer emits
  `HE_CPP_RUNTIME_FREESTANDING` and the capability defines; the option
  resolver applies the freestanding defaults and lets caller options win.
- `ForbidHostedServices` produces the expected diagnostic for a fixture that
  uses `System.Threading.Interlocked`, and no diagnostic without the flag.

Integration runner `tests/runtime-capabilities-integration/run.sh` gains a
`freestanding` variant:

- Builds the smoke, services, streams and shared_ptr fixtures against
  `freestanding_provider.hpp` and `freestanding_math.hpp` on the host with
  `-fno-exceptions -fno-rtti` and a poison include directory placed first on
  the include path. The poison directory contains a file for every hosted
  header in the table above whose only content is `#error hosted header
  included under freestanding runtime`. Any surviving hosted include fails the
  build on any developer machine.
- Runs the host executables: string growth, copy, concatenation and the
  single-separator `Split` semantics; vector growth, erase during the
  patterns the engine uses, and iteration; map insert, erase, iteration and
  lookup after rehash; function capture lifetime; shared pointer lifetime and
  custom deleters; hash and equality agreement; the fail policy through the
  existing fatal-path cases; software math accuracy samples against the host
  libm.
- With `TARGET_CXX` set, cross-compiles the same fixture sources to objects
  with the SNES toolchain. The documented command runs the `helengine-snes`
  Docker image: `TARGET_CXX=/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++`.
  Compile evidence only; no link, no size budget.

The generated-code check in the runner's README is repeated with
`--runtime freestanding` and no explicit provider options, to prove the
defaults are enough.

## Final measurement task

After everything above passes, generate `helengine.core` with the
`native-core-boot` preset switched to the `freestanding` runtime and
`ForbidHostedServices`, then compile (do not link) the generated unity file
with `mos-snes-far-clang++ -std=c++20 -fno-exceptions -fno-rtti -Os`. Record
in `docs/superpowers/reports/<date>-freestanding-core-compile.md`, dated the
day it is produced: the
count and categories of errors, the object size when it compiles, and the
list of remaining hosted facilities. That report is the entry point for the
far-memory sub-project. It is a measurement, not an acceptance criterion for
this sub-project.

## Acceptance

- `dotnet test` for `cs2.cpp.tests` passes with the new tests.
- `run.sh` hosted, custom and freestanding variants pass on the host.
- The freestanding fixture objects compile with the SNES toolchain.
- Existing PS1 and EASTL configurations keep building unchanged apart from the
  one-line `OwnedPtr` alias.
- The measurement report exists.

## Process

Work happens in `C:\dev\helworks\csharpcodegen` on `master` in small commits,
following the repository's `AGENTS.md`. When the sub-project is complete the
vendored submodule in `helengine/engine/vendor/csharpcodegen` is advanced to
the new commit in a separate helengine commit.

## Risks

- 16-bit `size_t` caps every container at 64 KB. Acceptable on the target;
  the containers use `size_t` throughout so hosted builds are unaffected.
- The 65816 compiler is a preview and may miscompile template-heavy code. The
  cross-compile gate and, later, the SNES boot will surface it; the provider
  keeps templates simple (no SFINAE beyond what the contract needs).
- Software double math is slow on the 65816. Correctness is the goal here;
  the fixed-point sub-project addresses speed.
- Poison headers can produce confusing errors if a compiler-provided header
  includes a poisoned name indirectly. The runner reports the include chain
  so the offending include is visible.
