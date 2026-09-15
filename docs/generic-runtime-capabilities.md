# Generic C++ runtime capabilities

Runtime capabilities belong to codegen; SDK integration belongs to the consumer.
Use `CPPConversionOptions.PlatformOptionValues` to override preset defaults:

| Option | Value | Purpose |
| --- | --- | --- |
| `codegen-use-std-string` | `true` / `false` | Standard or provider-owned string storage |
| `codegen-use-std-vector` | `true` / `false` | Standard or provider-owned vector storage |
| `codegen-use-std-unordered-map` | `true` / `false` | Standard or provider-owned map storage |
| `codegen-use-std-unordered-set` | `true` / `false` | Standard or provider-owned set storage |
| `codegen-use-std-function` | `true` / `false` | Standard or provider-owned delegate storage |
| `codegen-use-std-shared-ptr` | `true` / `false` | Standard or provider-owned shared buffer ownership |
| `codegen-use-std-chrono` | `true` / `false` | Standard or provider-owned monotonic timing |
| `codegen-use-std-math` | `true` / `false` | Standard C++ math or caller-supplied C math functions |
| `codegen-runtime-math-header` | C++ include path | Required declarations when standard math is disabled |
| `codegen-use-hosted-file-system` | `true` / `false` | Enables path-backed FileStream access; memory-backed streams remain usable |
| `codegen-use-exceptions` | `true` / `false` | C++ unwinding or fatal failure policy |
| `codegen-use-rtti` | `true` / `false` | Compiler runtime type information |
| `codegen-runtime-provider-header` | C++ include path | Consumer-owned provider declarations |

Boolean values are validated. The existing `CPPRuntimeProfile` properties remain
the defaults when no override is supplied. Caller options take precedence over
named presets. A provider header is required when any standard storage facility
or delegate, shared ownership, or clock service is disabled.

## Provider contract

The shared `runtime/native_runtime.hpp` selects `HeCppString`,
`HeCppVector<T>`, `HeCppUnorderedMap<K,V,H,E>`, and `HeCppHash<T>`. A provider
header declares the corresponding `he_cpp_custom` names:

```cpp
namespace he_cpp_custom {
    using String = MyString;
    template<class T> using Vector = MyVector<T>;
    template<class K, class V, class H, class E>
    using UnorderedMap = MyMap<K, V, H, E>;
    template<class T, class H, class E> using UnorderedSet = MySet<T, H, E>;
    template<class T> using Hash = MyHash<T>;
    template<class Signature> using Function = MyFunction<Signature>;
    template<class T> using SharedPtr = MySharedPtr<T>;
    std::uint64_t MonotonicMicroseconds();
    [[noreturn]] void Fail(const char* message);
}
```

The provider owns allocation through its storage implementation and the target's
native allocation hooks. Storage must satisfy the operations used by the shared
managed helpers: string copy/growth/find/substr/insert/replace/iteration, vector
growth/iteration/erase, and map lookup/insert-or-assign/erase/iteration. Hash and
equality must agree for keys. The integration fixture supplies a concrete EASTL
adapter as an example; codegen itself does not depend on EASTL.

`Fail` must never return. With exceptions disabled, raised errors terminate;
there is no stack unwinding or recovery. Catch/rethrow and operations requiring
unavailable RTTI are unsupported and must be reported explicitly. Ordinary
scope cleanup for `finally` remains separate from exception unwinding.

## Compatibility

Enabled standard string output retains the `std::string` spelling. Restricted
string output uses `HeCppString` consistently in declarations and expressions.
Do not patch the generated files to substitute types.

Previously, `stl-lite` declared exceptions and RTTI disabled while some emission
paths still used them. Consumers that need these facilities must explicitly
enable them and provide matching compiler settings. Enforcing the declarations
can expose such existing mismatches; changing the platform name does not bypass
the capability contract.

## Validation boundary

See [the integration fixture](../tests/runtime-capabilities-integration/README.md)
for executable standard/custom storage checks and optional cross compilation.
A compiled fixture is not evidence that an entire generated engine links, fits
a console's memory budget, or supports every hosted service. File I/O, timing,
numeric-library calls and other selected runtime services need their own target
integration and validation.

### Supported and guarded helpers

The configurable path covers strings, arrays, lists, dictionaries, stacks,
hashing, equality, exception payloads, safe static casts, type-name tokens,
string builders and UTF-8 byte conversion. Hosted string-view overloads and
standard exception base types remain available in the standard configuration.

Provider-backed event/delegate storage, monotonic stopwatch timing, shared span
ownership, numeric helpers and memory stream I/O are also configurable. Shared
owners must retain custom deleters and release storage exactly once after the
last copy. The consumer defines its synchronization contract. The clock must
return monotonically increasing microseconds in a 64-bit counter.

String splitting lowers `text.Split(char)` and `text.Split(char, StringSplitOptions)`
to `String::Split(text, separator, options)`. The runtime overload preserves
empty leading, trailing and consecutive segments for `None`, returns one empty
segment for empty input, and drops empties for `RemoveEmptyEntries`. Optional
enum parameters emit their named member (`StringSplitOptions::None`) rather than
the underlying constant. Other `Split` overloads keep their existing lowering;
count-limited and separator-array forms are not newly claimed here.

Custom math declares global C-ABI functions used by `system/math.hpp`, including
`ceil`, `floor`, `fabs`, `acos`, `asin`, `sin`, `cos`, `tan`, `sqrt`, `log2`,
`fmod`, `finite`, and `atan2`. The consumer supplies and links their implementation.
Generic floating-point remainder emission selects the configured math path.

Disabling hosted filesystem support retains read-only memory-backed FileStream
and rejects path constructors through the runtime failure policy. Console,
directory and file implementation translation units are skipped when their
requirement is absent from generated configuration. GUID formatting, vector
formatting and other unadapted hosted services still require their declared
capabilities; this is not blanket support for every managed API.
### Verification recorded on 2026-09-15 (Core boot follow-up)

- `WriteOutput_WithSingleCharacterSplit_LowersToNativeSplitWithNamedDefaultOption`
  was written first and failed on the instance-call emission and `::0` default,
  then passed with the lowering fix; the eight capability conversion tests pass.
- The runtime matrix (`tests/runtime-capabilities-integration/run.sh`) passed on
  the host and cross-compiled with `mipsel-none-elf-g++`, including the new
  `Split(char, options)` checks in `smoke.cpp` (`builds/helengine-ps1/split-runtime.log`).
- The full `cs2.cpp.tests` suite is not passing: 125 failures and a test-host
  crash in `CPPCompileValidationRegressionTests` with these changes
  (`split-codegen-full-tests.log`). Every failing test also fails on a copy of
  the same worktree with the Split changes reverted (`split-codegen-baseline-tests.log`,
  156 failures because that run reached further); no failure is unique to this
  change.
- The generated PS1 Core built from this state linked and booted; see
  `helengine-ps1/docs/GeneratedCorePrerequisite.md`.

### Verification recorded on 2026-09-15

- 47 focused codegen/configuration/runtime-contract tests passed after a fresh build.
- Standard storage with and without unwinding, custom EASTL storage, and all six
  mixed storage combinations compiled and ran successfully on the host.
- The native fixture and actual CLI-generated class compiled with the pinned
  PS1 compiler using freestanding mode and disabled exceptions/RTTI. The generated
  class also ran on the host, exercising interpolation, concatenation, string
  switches, direct fatal throws and null-coalescing fatal guards.
- Three representative broader exact-output regression failures were reproduced
  unchanged at baseline `3ff39bf`. The broad regression run was stopped after
  repeated failures; the entire existing suite is not claimed passing.

Local logs and artifacts are under `C:/dev/helworks/builds/helengine-ps1`:
`codegen-runtime-tests/runtime-capabilities-final.trx`,
`codegen-runtime-final.log`, `codegen-runtime-final/`,
`codegen-runtime-generated-restricted-final/`, and
`codegen-runtime-current-representative.log`. The baseline comparison is in
`codegen-runtime-baseline-source/baseline-representative-escalated.log`.

