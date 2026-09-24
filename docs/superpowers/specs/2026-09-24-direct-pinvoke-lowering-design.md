# Direct P/Invoke Lowering — Design

Date: 2026-09-24
Status: Approved (Helena, 2026-09-24 — scope, approach and sections 1–3 approved in conversation; remaining sections delegated)

## Goal

Make calling native code from transpiled C# trivial. The same C# source must work in both modes:

- **Managed (.NET)** — a normal `[DllImport]` P/Invoke, for fast iteration with the debugger and hot reload.
- **Transpiled (cs2.cpp)** — a zero-marshalling direct call to the native symbol, linked against the library's import library or a static library.

This removes today's requirement of writing every native capability twice (a managed `[DllImport]` implementation plus a hand-written C++ implementation of the same interface, e.g. `InputBackendWindows` / `Win32InputBackend`).

First consumer: Gevo (desktop shell on helengine), which needs Win32 (windows, hooks, tray) and a flat C shim (`gevo_native`) over COM APIs such as DirectComposition and Windows.Graphics.Capture.

## Non-goals

- COM interop (`[ComImport]`, vtables). COM stays in hand-written C++ exposed as a flat `extern "C"` API and consumed via `[DllImport]`.
- Marshalling of any kind: `string`, `StringBuilder`, managed arrays, classes, managed delegates, `[MarshalAs]`, `CharSet` conversions.
- `SetLastError = true`, `PreserveSig = false`.
- `[LibraryImport]` (source-generated marshalling adds nothing for blittable signatures).
- Exporting symbols (`[UnmanagedCallersOnly(EntryPoint = ...)]`).
- `GCHandle` for passing managed state through native user-data slots.
- Non-Windows link conventions (the design is portable; only the Windows handoff is wired now).

## 1. The C# contract

Accepted form:

```csharp
[DllImport("user32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true)]
static extern int SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
```

Rules:

- Only `[DllImport]` on `static extern` methods. `[LibraryImport]` is rejected with a diagnostic that says to use `[DllImport]`.
- **Exact symbol.** The native symbol is `EntryPoint` when set, otherwise the method name. No `A`/`W` suffix probing. Unicode entry points are spelled explicitly (`EntryPoint = "SetWindowTextW"`).
- **Allowed parameter and return types (blittable only):**
  `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `nint`, `nuint`,
  pointers `T*` where `T` is blittable or `void`,
  enums (lowered as their underlying type),
  structs (C# default `LayoutKind.Sequential`, optional `Pack`) whose instance fields are all blittable (see the MVP narrowing below for `Explicit` layout and `fixed` buffers),
  `delegate* unmanaged[...]` function pointers whose signature is itself allowed.
  Return type may also be `void`.
- `ref` and `out` are allowed only on blittable primitives and blittable structs; they lower to pointers (the .NET runtime pins them without copying, so semantics match). `in` is rejected in the MVP because C# allows rvalue arguments for `in`, which have no address in the generated C++.
- MVP narrowing (found while mapping the codegen, each is a `CPPPINV` error with a recommendation):
  - structs containing `fixed` buffers — the codegen does not lower fixed-size buffers at all yet;
  - `LayoutKind.Explicit` and `LayoutKind.Auto` structs passed **by value** — pass them by `ref`/`out`/pointer instead (a pointer needs no mirror);
  - function-pointer **return** types — return `nint` and cast instead;
  - by-value structs in `[UnmanagedCallersOnly]` signatures — use pointers;
  - two `[UnmanagedCallersOnly]` methods with the same name in the same type (the trampoline name would collide).
- **Rejected:** `bool`, `char` (their default .NET marshalling changes size — use `int`/`byte`/`ushort`), `string`, `StringBuilder`, arrays, classes, interfaces, managed delegates, generics, `[MarshalAs]`, `SetLastError = true`, `PreserveSig = false`, any `CharSet` other than the default, `BestFitMapping`, `ThrowOnUnmappableChar`.
- **Calling convention:** `CallingConvention.Winapi` and `StdCall` → `__stdcall`; `Cdecl` → `__cdecl`; `ThisCall` and `FastCall` are rejected. (x64 ignores the distinction; emitting it keeps x86 correct.)
- **Ownership:** extern methods carry only value types and raw pointers, so the ownership analysis has nothing to track. They are treated as leaf calls with no ownership summary.

Diagnostics use a new family `CPPPINV001…`, reported as errors that fail the conversion, following the `CPPOWN00x` pattern. No silent fallback.

## 2. Emission and isolation

Constraint: generated code is compiled as a unity build (`generated_unity.cpp`), and the runtime can include `<Windows.h>` (`system/app_context.hpp`). An `extern "C"` prototype with different parameter types (`void*` vs `HWND`) in that translation unit is a hard compile error. So the real prototypes live in their own translation unit.

Three generated artifacts:

1. **`native_imports/native_imports.hpp`** — includes only `<cstdint>`. Included by both sides. Contains:
   - **Mirror structs**: a plain C struct per struct type used by value (at any depth) in an import or callback signature, with identical field order, types and packing, named `he_pinvoke_<SanitizedTypeName>`.
   - **Forwarder declarations**, grouped per library namespace, using only fixed-width integers, `float`/`double`, `intptr_t`/`uintptr_t`, `void*`, mirror structs and function-pointer types built from those:
     `namespace he_pinvoke::user32 { int32_t SetWindowPos(intptr_t, intptr_t, int32_t, int32_t, int32_t, int32_t, uint32_t); }`
2. **`native_imports/native_imports.cpp`** — compiled **outside** the unity build. Includes only its own header. Contains the real `extern "C" <ret> <cc> <Symbol>(...)` prototypes and the forwarder definitions that call them.
3. **Call sites in the unity build** call `he_pinvoke::<lib>::<Symbol>(...)`:
   - `T*` arguments → `reinterpret_cast<void*>`; `ref`/`out` → `reinterpret_cast<void*>(&arg)`; enums → `static_cast` to the underlying integer; function pointers → `reinterpret_cast` of the raw pointer to the mirror function-pointer type. Every pointer crosses the boundary as `void*`, so only by-value structs need mirrors.
   - By-value structs are bit-copied (`memcpy` into a mirror temporary; results copied back).
   - For each generated struct/mirror pair the unity side emits `static_assert`s on `sizeof`, `alignof` and every field `offsetof`, so layout drift breaks the build instead of corrupting memory at runtime.

Consistency rules:

- The same `(library, symbol)` imported more than once with an identical lowered signature is emitted once.
- The same `(library, symbol)` with different lowered signatures is a `CPPPINV` error.
- The same symbol imported from two different libraries is a `CPPPINV` error (both would resolve to one `extern "C"` name).
- Library namespace = library name without directory and without a `.dll`/`.so`/`.dylib`/`.lib` extension, lowercased, sanitized to a C++ identifier (`user32.dll` → `user32`, `gevo_native` → `gevo_native`).

Handoff: `generated_windows_handoff.cmake` gains

- `CPP_GENERATED_NATIVE_IMPORTS_SOURCE` — path to `native_imports.cpp` (empty string when there are no imports), which the host adds as a source.
- `CPP_GENERATED_NATIVE_LINK_LIBRARIES` — the sorted, de-duplicated list of library namespaces, which the host passes to `target_link_libraries`. System libraries resolve to their import library (`user32` → `user32.lib`); project libraries resolve to the CMake target of the same name (linked statically in native builds, built as a shared library for managed runs).

## 3. Callbacks

Accepted form:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
static nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam) { ... }

delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> proc = &WindowProc;
```

- Validation: the method is `static`, non-generic, and its signature follows the Section 1 type rules. `UnmanagedCallersOnly(EntryPoint = ...)` is rejected. `CallConvs` accepts `CallConvStdcall` and `CallConvCdecl`; an empty `CallConvs` means the platform default (`__stdcall` on Windows x86, irrelevant on x64).
- Emission: for each such method the unity build gets a **trampoline**: a free function with the mirror signature, the right calling convention and `noexcept`. It converts arguments to the generated types, calls the generated static method, and converts the result back.
- `&WindowProc` in a `delegate* unmanaged[...]` context lowers to the trampoline's address.
- Exceptions escaping a callback reach `std::terminate` through `noexcept`. In .NET an exception escaping an `UnmanagedCallersOnly` method also fails fast, so behavior matches in both modes and no stack unwinds through C frames.
- Invoking a `delegate* unmanaged[...]` value obtained from native code (e.g. `GetProcAddress`) calls through the raw pointer with the declared calling convention.
- Type lowering: `delegate* unmanaged[Stdcall]<...>` becomes the runtime template `StdcallFunctionPointer<R, Args...>` and `delegate* unmanaged[Cdecl]<...>` becomes `CdeclFunctionPointer<R, Args...>`. Both have the same API as the existing `FunctionPointer<R, Args...>` but their `PointerType` carries the calling-convention macro (`HE_CPP_STDCALL`/`HE_CPP_CDECL` from the new `runtime/native_calling_convention.hpp`, which expand to `__stdcall`/`__cdecl` on `_WIN32` and to nothing elsewhere). A bare `delegate* unmanaged<...>` means the platform default, lowered as stdcall. Managed `delegate*<...>` is unchanged and is rejected in P/Invoke signatures.
- Trampolines are emitted by the class emitter next to the owning class (declaration in its `.hpp`, definition in its `.cpp`), named `he_pinvoke_cb_<GeneratedTypeName>_<MethodName>`.
- The `static_assert`s for a mirrored struct are emitted in that struct's generated `.cpp`, which includes `native_imports/native_imports.hpp`.
- Method identity between the analysis stage and the emission workers uses Roslyn documentation-comment ids (`IMethodSymbol.OriginalDefinition.GetDocumentationCommentId()`), not symbol references.
- Managed state for callbacks is passed through a static registry keyed by an integer (HWND or an id), not `GCHandle`.

## 4. Pipeline placement

- A **P/Invoke analysis stage** runs after document preprocessing and before ownership analysis. It collects every `[DllImport]` method and every `[UnmanagedCallersOnly]` method in the compilation, validates them against Sections 1 and 3, builds the de-duplicated import table (library → symbol → lowered signature) and the set of mirror structs, and reports `CPPPINV` diagnostics. Any error stops the conversion, the same way ownership errors do.
- The ownership stage treats calls to extern methods as leaf calls on value types.
- The class emitter skips `static extern` methods: they have no C++ body in the class; all call sites go to the forwarder.
- The invocation lowering redirects calls to extern methods to `he_pinvoke::<lib>::<Symbol>` and inserts the argument conversions from Section 2.
- The output writer emits `native_imports.hpp`/`.cpp` only when the import table or the trampoline set is non-empty, and the handoff writer publishes the two new CMake variables.

## 5. Testing

- **Validation unit tests** (one per rule): each accepted type and each rejected type/attribute produces the expected outcome and the expected `CPPPINV` code and message.
- **Emission tests** on converted snippets: forwarder declaration and definition text, `extern "C"` prototype with calling convention, mirror struct and `static_assert` generation, call-site casts for pointers, `ref`/`out`/`in` and by-value structs, de-duplication and conflicting-signature errors, library-name normalization, handoff variables (present and empty cases).
- **Callback tests**: trampoline signature, `noexcept`, calling convention, `&Method` lowering, invocation of an unmanaged function pointer.
- **Real-compiler tests** in the existing compile-validation harness (skipped with the same conditions the harness already uses when no compiler is available):
  - `kernel32!GetTickCount64` — the simplest import compiles, links and returns non-zero.
  - `user32!GetCursorPos(out POINT)` — by-value/`out` struct path with the `static_assert`s, in a unity build that also includes `<Windows.h>`, proving the isolation works.
  - `kernel32!EnumSystemLocalesEx` with an `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]` callback taking `(ushort*, uint, nint)` — a callback round trip that counts locales and verifies the `lParam` value passed through unchanged.
- Every real-compiler test program is also run as managed .NET, and its output is compared with the native run (differential check, same idea as the Bepu managed/native harness).
