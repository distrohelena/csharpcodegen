# Generic C++ runtime capabilities

Runtime capabilities belong to codegen; SDK integration belongs to the consumer.
Use `CPPConversionOptions.PlatformOptionValues` to override preset defaults:

| Option | Value | Purpose |
| --- | --- | --- |
| `codegen-use-std-string` | `true` / `false` | Standard or provider-owned string storage |
| `codegen-use-std-vector` | `true` / `false` | Standard or provider-owned vector storage |
| `codegen-use-std-unordered-map` | `true` / `false` | Standard or provider-owned map storage |
| `codegen-use-exceptions` | `true` / `false` | C++ unwinding or fatal failure policy |
| `codegen-use-rtti` | `true` / `false` | Compiler runtime type information |
| `codegen-runtime-provider-header` | C++ include path | Consumer-owned provider declarations |

Boolean values are validated. The existing `CPPRuntimeProfile` properties remain
the defaults when no override is supplied. Caller options take precedence over
named presets. A provider header is required when any standard storage facility
is disabled.

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
    template<class T> using Hash = MyHash<T>;
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

Event storage, application context, console/debug output, GUID formatting,
numeric parsing/checked arithmetic, vector formatting, regex and I/O helpers
still have hosted dependencies. These helpers now reject incompatible storage
or exception flags explicitly. They need further generic adapters before they
can be used in a fully restricted engine build.

The current generated unity compile harness includes all copied runtime `.cpp`
files, including hosted I/O. The restricted fixture compiles its generated
`StringGate.cpp` and required helper headers directly; it does not establish that
the full unity harness is freestanding. Runtime-source selection is another
integration requirement for a complete target build.

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
