# Direct P/Invoke Lowering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let cs2.cpp lower blittable `[DllImport] static extern` methods and `[UnmanagedCallersOnly]` callbacks into zero-marshalling direct native calls, so the same C# runs as P/Invoke under .NET and as direct calls when transpiled.

**Architecture:** A new pipeline stage (`CPPPInvokeAnalysisStage`) validates every import/callback and builds a `CPPPInvokePlan`. The plan drives four consumers:
- a writer for the isolated `native_imports/` translation unit, whose forwarders hold the real `extern "C"` prototypes;
- the invocation lowering, which redirects calls to `he_pinvoke::<lib>::<Symbol>`;
- the class emitter, which skips extern stubs and emits callback trampolines plus mirror-struct `static_assert`s;
- the handoff/harness writers, which publish the extra source and the link libraries.

**Tech Stack:** C# (.NET 9 codegen, Roslyn 4.12), xunit 2.9.3 tests (net10.0), generated C++20, MSVC via VS2022 `VsDevCmd.bat` for the real-compiler tests.

**Spec:** `docs/superpowers/specs/2026-09-24-direct-pinvoke-lowering-design.md`

## Global Constraints

- Repo rules (`AGENTS.md`):
  - one class per file;
  - substantive XML doc comments on **every** type and member;
  - PascalCase fields;
  - no tuples;
  - nullable disabled (no `?` annotations on reference types);
  - no redundant `private`;
  - opening braces on the same line;
  - order members as constants/fields, constructors, properties, methods;
  - no local helper functions;
  - throw instead of defaulting;
  - never patch generated output, fix the emitter instead.
- Diagnostics are errors with codes `CPPPINV001`–`CPPPINV008` (table in Task 1). Any error fails `AddCsproj` with `InvalidOperationException("<code> <file>(<line>,<col>): <message>")`, exactly like `CPPOwnershipAnalysisStage.FormatFailure`.
- Native symbol names are exact: `DllImportData.EntryPointName` when set, otherwise the method name.
- The library namespace is the file name without directory and without the `.dll`/`.so`/`.dylib`/`.lib` extension, lowercased, with every character outside `[a-z0-9_]` replaced by `_`, and prefixed with `_` if it starts with a digit.
- Generated file layout: `native_imports/native_imports.hpp` (includes only `<cstdint>` and `<cstring>`) and `native_imports/native_imports.cpp` (includes only `"native_imports.hpp"`, compiled outside the unity build).
- Calling-convention macros `HE_CPP_STDCALL`/`HE_CPP_CDECL` live in `runtime/native_calling_convention.hpp` and expand to `__stdcall`/`__cdecl` only when `_WIN32` is defined.
- Method identity across stages is the Roslyn documentation-comment id of `method.OriginalDefinition`.
- Run tests with a byte cap:
  `dotnet test C:\dev\helworks\csharpcodegen\.worktrees\pinvoke-direct-calls\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~<Class>" 2>&1 | Select-Object -Last 40`

## Review Focus

- **The same DllImport declared in two classes** (for example two wrappers both importing `user32!GetForegroundWindow`) must emit exactly one forwarder and link `user32` once. Covered in Task 3 (`Analyze_DuplicateImportsWithSameSignature_EmitsOneImport`) and Task 5 (`WriteOutput_DuplicateImports_EmitsSingleForwarder`).
- **Unity builds that already include `<Windows.h>`** must still compile. Covered in Task 8 (`GetCursorPos_WithWindowsHeaderInUnity_CompilesAndMatchesManaged`).
- **`out var` arguments** (`GetCursorPos(out NativePoint p)`) must declare the local before the call and pass its address. Covered in Task 6 (`WriteOutput_OutVarStructArgument_DeclaresLocalAndPassesAddress`).
- **Enum parameters and returns** must cast to and from the underlying integer, not silently narrow. Covered in Task 6 (`WriteOutput_EnumArgumentAndReturn_CastsThroughUnderlyingType`).
- **Projects with zero imports** must produce byte-identical output to today: no `native_imports/` folder, and empty handoff variables. Covered in Task 5 (`WriteOutput_NoImports_DoesNotEmitNativeImportsFolder`).

---

### Task 1: P/Invoke plan model, diagnostic codes, library-name normalizer, method ids

**Files:**
- Create: `cs2.cpp/pinvoke/CPPPInvokeDiagnosticCodes.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeCallingConvention.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeValueKind.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeLoweredType.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeParameter.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeSignature.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeImport.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeCallback.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeMirrorField.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeMirrorStruct.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokePlan.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeLibraryNameNormalizer.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeMethodIds.cs`
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeModelTests.cs`

**Interfaces:**
- Produces:
  - `static class CPPPInvokeDiagnosticCodes` with `const string` values:
    - `LibraryImportNotSupported = "CPPPINV001"`
    - `UnsupportedType = "CPPPINV002"`
    - `UnsupportedImportSetting = "CPPPINV003"`
    - `UnsupportedCallingConvention = "CPPPINV004"`
    - `ConflictingImportSignature = "CPPPINV005"`
    - `SymbolImportedFromMultipleLibraries = "CPPPINV006"`
    - `InvalidCallbackMethod = "CPPPINV007"`
    - `UnsupportedCallbackType = "CPPPINV008"`
  - `enum CPPPInvokeCallingConvention { StdCall, Cdecl }`
  - `enum CPPPInvokeValueKind { Void, Primitive, Enum, Pointer, Struct, FunctionPointer }`
  - `sealed class CPPPInvokeLoweredType`:
    - constructor `(CPPPInvokeValueKind kind, string mirrorTypeText, ITypeSymbol sourceType)`;
    - properties `Kind`, `MirrorTypeText`, `SourceType`.
  - `sealed class CPPPInvokeParameter`:
    - constructor `(string name, CPPPInvokeLoweredType type, RefKind refKind)`;
    - properties `Name`, `Type`, `RefKind`;
    - `string MirrorParameterText` returns `"void*"` when `RefKind != RefKind.None`, otherwise `Type.MirrorTypeText`.
  - `sealed class CPPPInvokeSignature`:
    - constructor `(CPPPInvokeLoweredType returnType, IReadOnlyList<CPPPInvokeParameter> parameters, CPPPInvokeCallingConvention callingConvention)`;
    - properties `ReturnType`, `Parameters`, `CallingConvention`;
    - `string CallingConventionMacro` returns `"HE_CPP_STDCALL"` or `"HE_CPP_CDECL"`;
    - `string MirrorKey` looks like `"HE_CPP_STDCALL int32_t(intptr_t,void*)"`.
  - `sealed class CPPPInvokeImport`:
    - constructor `(string libraryNamespace, string entryPoint, CPPPInvokeSignature signature)`;
    - properties `LibraryNamespace`, `EntryPoint`, `Signature`, `List<string> MethodIds`;
    - `string ForwarderQualifiedName` returns `"he_pinvoke::" + LibraryNamespace + "::" + EntryPoint`.
  - `sealed class CPPPInvokeCallback`:
    - constructor `(string methodId, string trampolineName, CPPPInvokeSignature signature)`;
    - properties of the same names.
  - `sealed class CPPPInvokeMirrorField`:
    - constructor `(string name, string mirrorTypeText)`;
    - properties `Name`, `MirrorTypeText`.
  - `sealed class CPPPInvokeMirrorStruct`:
    - constructor `(string mirrorName, INamedTypeSymbol structType, int pack, IReadOnlyList<CPPPInvokeMirrorField> fields)`;
    - properties `MirrorName`, `StructType`, `Pack` (0 means the default), `Fields`, `string StructTypeKey` (`structType.OriginalDefinition.ToDisplayString()`).
  - `sealed class CPPPInvokePlan`:
    - constructor `(IReadOnlyList<CPPPInvokeImport> imports, IReadOnlyList<CPPPInvokeCallback> callbacks, IReadOnlyList<CPPPInvokeMirrorStruct> mirrorStructs)`;
    - properties `Imports`, `Callbacks`, `MirrorStructs`, `IReadOnlyList<string> LinkLibraries` (sorted, distinct `LibraryNamespace`), `bool HasNativeImports`;
    - lookups `bool TryGetImport(string methodId, out CPPPInvokeImport import)`, `bool TryGetCallback(string methodId, out CPPPInvokeCallback callback)`, `bool TryGetMirrorStruct(string structTypeKey, out CPPPInvokeMirrorStruct mirrorStruct)`.
  - `static class CPPPInvokeLibraryNameNormalizer` with `static string Normalize(string moduleName)`, which throws `ArgumentException` for blank input.
  - `static class CPPPInvokeMethodIds` with `static string Get(IMethodSymbol method)`, which throws `ArgumentNullException` and throws `InvalidOperationException` when the id is null.

- [ ] **Step 1: Write the failing tests**

```csharp
using cs2.cpp;
using Microsoft.CodeAnalysis;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies the P/Invoke plan model: library-name normalization, signature keys, and plan lookups.
/// </summary>
public sealed class CPPPInvokeModelTests {
    /// <summary>
    /// Ensures library names lose directories and extensions and become lowercase C++ identifiers.
    /// </summary>
    [Theory]
    [InlineData("user32.dll", "user32")]
    [InlineData("USER32", "user32")]
    [InlineData("C:\\libs\\gevo-native.dll", "gevo_native")]
    [InlineData("libfoo.so", "libfoo")]
    [InlineData("3dlib.dylib", "_3dlib")]
    [InlineData("gevo_native.lib", "gevo_native")]
    public void Normalize_StripsDirectoryAndExtension(string moduleName, string expected) {
        Assert.Equal(expected, CPPPInvokeLibraryNameNormalizer.Normalize(moduleName));
    }

    /// <summary>
    /// Ensures a blank module name is rejected instead of producing an empty namespace.
    /// </summary>
    [Fact]
    public void Normalize_Blank_Throws() {
        Assert.Throws<ArgumentException>(() => CPPPInvokeLibraryNameNormalizer.Normalize("  "));
    }

    /// <summary>
    /// Ensures signature keys include the calling convention, return type and ref-lowered parameters.
    /// </summary>
    [Fact]
    public void MirrorKey_IncludesConventionReturnAndParameters() {
        CPPPInvokeLoweredType int32 = new CPPPInvokeLoweredType(CPPPInvokeValueKind.Primitive, "int32_t", null);
        CPPPInvokeLoweredType intPtr = new CPPPInvokeLoweredType(CPPPInvokeValueKind.Primitive, "intptr_t", null);
        CPPPInvokeSignature signature = new CPPPInvokeSignature(
            int32,
            new[] {
                new CPPPInvokeParameter("hWnd", intPtr, RefKind.None),
                new CPPPInvokeParameter("point", int32, RefKind.Out)
            },
            CPPPInvokeCallingConvention.StdCall);

        Assert.Equal("HE_CPP_STDCALL int32_t(intptr_t,void*)", signature.MirrorKey);
    }

    /// <summary>
    /// Ensures the plan exposes sorted distinct link libraries and resolves imports by method id.
    /// </summary>
    [Fact]
    public void Plan_LinkLibrariesAreSortedDistinct_AndImportsResolveByMethodId() {
        CPPPInvokeSignature signature = new CPPPInvokeSignature(
            new CPPPInvokeLoweredType(CPPPInvokeValueKind.Void, "void", null),
            Array.Empty<CPPPInvokeParameter>(),
            CPPPInvokeCallingConvention.StdCall);
        CPPPInvokeImport user32 = new CPPPInvokeImport("user32", "GetForegroundWindow", signature);
        user32.MethodIds.Add("M:A.GetForegroundWindow");
        CPPPInvokeImport kernel32 = new CPPPInvokeImport("kernel32", "GetTickCount64", signature);
        kernel32.MethodIds.Add("M:A.GetTickCount64");
        CPPPInvokeImport user32Again = new CPPPInvokeImport("user32", "GetDesktopWindow", signature);
        user32Again.MethodIds.Add("M:B.GetDesktopWindow");

        CPPPInvokePlan plan = new CPPPInvokePlan(new[] { user32, kernel32, user32Again }, Array.Empty<CPPPInvokeCallback>(), Array.Empty<CPPPInvokeMirrorStruct>());

        Assert.Equal(new[] { "kernel32", "user32" }, plan.LinkLibraries);
        Assert.True(plan.HasNativeImports);
        Assert.True(plan.TryGetImport("M:B.GetDesktopWindow", out CPPPInvokeImport resolved));
        Assert.Equal("he_pinvoke::user32::GetDesktopWindow", resolved.ForwarderQualifiedName);
        Assert.False(plan.TryGetImport("M:C.Missing", out _));
    }

    /// <summary>
    /// Ensures an empty plan reports no native imports and no link libraries.
    /// </summary>
    [Fact]
    public void Plan_Empty_HasNoNativeImports() {
        CPPPInvokePlan plan = new CPPPInvokePlan(Array.Empty<CPPPInvokeImport>(), Array.Empty<CPPPInvokeCallback>(), Array.Empty<CPPPInvokeMirrorStruct>());
        Assert.False(plan.HasNativeImports);
        Assert.Empty(plan.LinkLibraries);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test ... --filter "FullyQualifiedName~CPPPInvokeModelTests"`. Expected: a compile error, because the `CPPPInvoke*` types do not exist.

- [ ] **Step 3: Implement the model files.** One class per file, and every member gets an XML doc comment. Key code:

```csharp
// CPPPInvokeLibraryNameNormalizer.cs
namespace cs2.cpp;

/// <summary>
/// Converts a DllImport module name into the C++ namespace and link-library identifier used for its forwarders.
/// </summary>
public static class CPPPInvokeLibraryNameNormalizer {
    /// <summary>
    /// File extensions removed from module names because the platform adds them when resolving libraries.
    /// </summary>
    static readonly string[] KnownExtensions = { ".dll", ".so", ".dylib", ".lib" };

    /// <summary>
    /// Normalizes a module name such as <c>C:\libs\User32.dll</c> into <c>user32</c>.
    /// </summary>
    /// <param name="moduleName">Module name exactly as written in the DllImport attribute.</param>
    /// <returns>A lowercase C++ identifier naming the library.</returns>
    public static string Normalize(string moduleName) {
        if (string.IsNullOrWhiteSpace(moduleName)) {
            throw new ArgumentException("A DllImport module name is required.", nameof(moduleName));
        }

        string fileName = moduleName.Trim().Replace('\\', '/');
        int slashIndex = fileName.LastIndexOf('/');
        if (slashIndex >= 0) {
            fileName = fileName.Substring(slashIndex + 1);
        }

        foreach (string extension in KnownExtensions) {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                fileName = fileName.Substring(0, fileName.Length - extension.Length);
                break;
            }
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(fileName.Length + 1);
        foreach (char character in fileName.ToLowerInvariant()) {
            bool isIdentifierCharacter = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_';
            builder.Append(isIdentifierCharacter ? character : '_');
        }

        if (builder.Length == 0) {
            throw new ArgumentException($"DllImport module name '{moduleName}' does not contain a library name.", nameof(moduleName));
        }

        if (char.IsDigit(builder[0])) {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
```

```csharp
// CPPPInvokeSignature.cs — MirrorKey and CallingConventionMacro
/// <summary>
/// Gets the calling-convention macro emitted before the function name in prototypes and function-pointer types.
/// </summary>
public string CallingConventionMacro => CallingConvention == CPPPInvokeCallingConvention.StdCall ? "HE_CPP_STDCALL" : "HE_CPP_CDECL";

/// <summary>
/// Gets a stable text key that is equal for two signatures exactly when their lowered native ABI is equal.
/// </summary>
public string MirrorKey => CallingConventionMacro + " " + ReturnType.MirrorTypeText + "(" + string.Join(",", Parameters.Select(parameter => parameter.MirrorParameterText)) + ")";
```

```csharp
// CPPPInvokePlan.cs — constructor body
Imports = imports ?? throw new ArgumentNullException(nameof(imports));
Callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
MirrorStructs = mirrorStructs ?? throw new ArgumentNullException(nameof(mirrorStructs));
LinkLibraries = imports.Select(import => import.LibraryNamespace).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
foreach (CPPPInvokeImport import in imports) {
    foreach (string methodId in import.MethodIds) {
        ImportsByMethodId[methodId] = import;
    }
}
foreach (CPPPInvokeCallback callback in callbacks) {
    CallbacksByMethodId[callback.MethodId] = callback;
}
foreach (CPPPInvokeMirrorStruct mirrorStruct in mirrorStructs) {
    MirrorStructsByKey[mirrorStruct.StructTypeKey] = mirrorStruct;
}
```

`ImportsByMethodId`, `CallbacksByMethodId` and `MirrorStructsByKey` are `readonly Dictionary<string, ...>` fields created with `StringComparer.Ordinal`. `HasNativeImports => Imports.Count > 0`. `CPPPInvokeMirrorStruct.StructTypeKey => StructType.OriginalDefinition.ToDisplayString()`. The `CPPPInvokeMirrorStruct` constructor must accept a null `structType` only if the tests need it; they do not, so throw `ArgumentNullException`.

```csharp
// CPPPInvokeMethodIds.cs
/// <summary>
/// Returns the documentation-comment id used to match one method across the analysis stage and parallel emission workers.
/// </summary>
/// <param name="method">Method symbol from any compilation of the project.</param>
/// <returns>An id such as <c>M:Native.User32.GetCursorPos(Native.NativePoint@)</c>.</returns>
public static string Get(IMethodSymbol method) {
    if (method == null) {
        throw new ArgumentNullException(nameof(method));
    }

    string id = method.OriginalDefinition.GetDocumentationCommentId();
    if (string.IsNullOrWhiteSpace(id)) {
        throw new InvalidOperationException($"Method '{method.ToDisplayString()}' has no documentation-comment id.");
    }

    return id;
}
```

- [ ] **Step 4: Run the tests and confirm they pass.** Same command. Expected: all `CPPPInvokeModelTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add cs2.cpp/pinvoke cs2.cpp.tests/pinvoke
git commit -m "feat(pinvoke): add the P/Invoke plan model and library-name normalizer"
```

---

### Task 2: Blittable type lowering

**Files:**
- Create: `cs2.cpp/pinvoke/CPPPInvokeTypeLowerer.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeTypeLoweringResult.cs`
- Create: `cs2.cpp.tests/pinvoke/CPPPInvokeTestCompilation.cs` (a test helper that builds an in-memory Roslyn compilation)
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeTypeLowererTests.cs`

**Interfaces:**
- Consumes the Task 1 types.
- Produces:
  - `sealed class CPPPInvokeTypeLoweringResult`:
    - properties `bool Succeeded`, `CPPPInvokeLoweredType Type`, `string FailureReason`, `string Recommendation`;
    - static factories `Success(CPPPInvokeLoweredType type)` and `Failure(string reason, string recommendation)`.
  - `sealed class CPPPInvokeTypeLowerer`:
    - constructor `()`;
    - `CPPPInvokeTypeLoweringResult LowerParameter(ITypeSymbol type, RefKind refKind, bool isCallback)`;
    - `CPPPInvokeTypeLoweringResult LowerReturn(ITypeSymbol type, bool isCallback)`;
    - property `IReadOnlyList<CPPPInvokeMirrorStruct> MirrorStructs`, which holds every struct lowered **by value**, de-duplicated by `StructTypeKey`, with nested structs listed before their containers.
  - `static class CPPPInvokeTestCompilation` (test project) with `static CSharpCompilation Create(string source)`, which uses `AllowUnsafe = true`, references every assembly in `TRUSTED_PLATFORM_ASSEMBLIES`, and throws if the compilation has errors. Also `static IMethodSymbol GetMethod(CSharpCompilation compilation, string typeName, string methodName)`.

Lowering rules (spec Section 1):

| C# type | Kind | MirrorTypeText |
|---|---|---|
| `sbyte`/`byte`/`short`/`ushort`/`int`/`uint`/`long`/`ulong` | Primitive | `int8_t`/`uint8_t`/`int16_t`/`uint16_t`/`int32_t`/`uint32_t`/`int64_t`/`uint64_t` |
| `float`/`double` | Primitive | `float`/`double` |
| `nint`/`IntPtr` / `nuint`/`UIntPtr` | Primitive | `intptr_t` / `uintptr_t` |
| enum | Enum | mirror text of the underlying type |
| `T*` (T blittable or `void`) | Pointer | `void*` |
| struct (not an enum, not generic, `LayoutKind.Sequential`, all instance fields blittable, no fixed buffers) | Struct | `he_pinvoke_` + the type's `ToDisplayString()` with every non-identifier character replaced by `_` (for example `Native.NativePoint` → `he_pinvoke_Native_NativePoint`) |
| `delegate* unmanaged[Stdcall\|Cdecl]` or bare `unmanaged` with an allowed signature | FunctionPointer | `<ret> (HE_CPP_STDCALL*)(<params>)` built from the mirror texts |
| `void` (return only) | Void | `void` |

Rejections return `Failure(reason, recommendation)`:
- `bool`: "bool has no fixed native size under DllImport marshalling"; "use int (Win32 BOOL) or byte".
- `char`: "char marshals as a 1-byte ANSI character by default"; "use ushort for UTF-16 or byte for ANSI".
- `string`, arrays, classes, interfaces, delegates, generic structs, type parameters: "requires marshalling"; "pass a pointer (byte*/ushort*/T*) instead".
- `ref`/`out` on a type that is not Primitive/Enum/Struct: "only blittable values can be passed by ref/out".
- `in`: "in arguments may be rvalues without an address"; "use ref or a pointer".
- A struct with a fixed buffer, whether passed by value or by ref: "fixed-size buffers are not supported by the C++ backend yet"; "use explicit fields".
- An `Explicit` or `Auto` layout struct passed by value (by ref is fine): "only sequential structs can cross by value"; "pass it by ref or pointer".
- A managed `delegate*<...>`: "managed function pointers cannot be called from native code"; "use delegate* unmanaged[Stdcall]".
- `unmanaged[Thiscall|Fastcall]`: "unsupported calling convention".
- A function pointer used as a return type: "function-pointer returns are not supported"; "return nint and cast".
- `isCallback && Struct` by value: "callbacks cannot take structs by value"; "take a pointer".

A struct passed by ref/out/pointer is **not** added to `MirrorStructs`, but its fields still have to be blittable. Validate them recursively with the same rules, without the by-value layout rule.

- [ ] **Step 1: Write the failing tests**

```csharp
using cs2.cpp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies which C# types may cross a direct P/Invoke boundary and the native mirror text each one lowers to.
/// </summary>
public sealed class CPPPInvokeTypeLowererTests {
    /// <summary>
    /// Shared fixture source declaring every type the tests lower.
    /// </summary>
    const string Source = """
        using System.Runtime.InteropServices;
        namespace Native {
            public enum ShowCommand : uint { Hide = 0, Show = 5 }
            public struct NativePoint { public int X; public int Y; }
            public struct NativeRect { public NativePoint TopLeft; public NativePoint BottomRight; }
            [StructLayout(LayoutKind.Explicit)] public struct Overlay { [FieldOffset(0)] public int A; [FieldOffset(0)] public float B; }
            public unsafe struct WithFixed { public fixed byte Name[8]; }
            public struct Generic<T> { public T Value; }
            public unsafe static class Probe {
                public static void Primitives(sbyte a, byte b, short c, ushort d, int e, uint f, long g, ulong h, float i, double j, nint k, nuint l) {}
                public static void Enums(ShowCommand command) {}
                public static void Pointers(int* a, void* b, NativePoint* c) {}
                public static void Structs(NativePoint p, NativeRect r, ref NativePoint rp, out NativePoint op, ref Overlay ro, Overlay vo) { op = default; }
                public static void Rejected(bool a, char b, string c, int[] d, object e, WithFixed f, Generic<int> g, in NativePoint h) {}
                public static void Pointers2(delegate* unmanaged[Stdcall]<nint, uint, nint> a, delegate* unmanaged[Cdecl]<int, void> b, delegate* unmanaged<int> c, delegate*<int> d) {}
            }
        }
        """;

    /// <summary>
    /// Lowers one parameter of a Probe method.
    /// </summary>
    static CPPPInvokeTypeLoweringResult LowerParameter(CPPPInvokeTypeLowerer lowerer, string method, int index, bool isCallback = false) {
        CSharpCompilation compilation = CPPPInvokeTestCompilation.Create(Source);
        IParameterSymbol parameter = CPPPInvokeTestCompilation.GetMethod(compilation, "Native.Probe", method).Parameters[index];
        return lowerer.LowerParameter(parameter.Type, parameter.RefKind, isCallback);
    }

    /// <summary>
    /// Ensures every blittable primitive maps to its fixed-width C type.
    /// </summary>
    [Fact]
    public void LowerParameter_Primitives_MapToFixedWidthTypes() {
        string[] expected = { "int8_t", "uint8_t", "int16_t", "uint16_t", "int32_t", "uint32_t", "int64_t", "uint64_t", "float", "double", "intptr_t", "uintptr_t" };
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 0; index < expected.Length; index++) {
            CPPPInvokeTypeLoweringResult result = LowerParameter(lowerer, "Primitives", index);
            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(CPPPInvokeValueKind.Primitive, result.Type.Kind);
            Assert.Equal(expected[index], result.Type.MirrorTypeText);
        }
    }

    /// <summary>
    /// Ensures enums lower through their underlying integer type.
    /// </summary>
    [Fact]
    public void LowerParameter_Enum_UsesUnderlyingType() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Enums", 0);
        Assert.Equal(CPPPInvokeValueKind.Enum, result.Type.Kind);
        Assert.Equal("uint32_t", result.Type.MirrorTypeText);
    }

    /// <summary>
    /// Ensures every pointer crosses as void* and records no mirror struct.
    /// </summary>
    [Fact]
    public void LowerParameter_Pointers_BecomeVoidPointerWithoutMirrors() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 0; index < 3; index++) {
            CPPPInvokeTypeLoweringResult result = LowerParameter(lowerer, "Pointers", index);
            Assert.Equal(CPPPInvokeValueKind.Pointer, result.Type.Kind);
            Assert.Equal("void*", result.Type.MirrorTypeText);
        }
        Assert.Empty(lowerer.MirrorStructs);
    }

    /// <summary>
    /// Ensures by-value structs create mirrors with nested mirrors first, while by-ref structs need none.
    /// </summary>
    [Fact]
    public void LowerParameter_Structs_ByValueMirrorsNestedFirst_ByRefNoMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.Equal("he_pinvoke_Native_NativePoint", LowerParameter(lowerer, "Structs", 0).Type.MirrorTypeText);
        Assert.Equal("he_pinvoke_Native_NativeRect", LowerParameter(lowerer, "Structs", 1).Type.MirrorTypeText);
        Assert.True(LowerParameter(lowerer, "Structs", 2).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 3).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 4).Succeeded);

        Assert.Equal(new[] { "he_pinvoke_Native_NativePoint", "he_pinvoke_Native_NativeRect" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
        CPPPInvokeMirrorStruct rect = lowerer.MirrorStructs[1];
        Assert.Equal(new[] { "TopLeft", "BottomRight" }, rect.Fields.Select(field => field.Name));
        Assert.Equal("he_pinvoke_Native_NativePoint", rect.Fields[0].MirrorTypeText);
    }

    /// <summary>
    /// Ensures an explicit-layout struct is rejected by value.
    /// </summary>
    [Fact]
    public void LowerParameter_ExplicitStructByValue_Fails() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Structs", 5);
        Assert.False(result.Succeeded);
        Assert.Contains("sequential", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures each non-blittable or marshalled type is rejected with a recommendation.
    /// </summary>
    [Theory]
    [InlineData(0, "bool")]
    [InlineData(1, "char")]
    [InlineData(2, "marshalling")]
    [InlineData(3, "marshalling")]
    [InlineData(4, "marshalling")]
    [InlineData(5, "fixed-size buffers")]
    [InlineData(6, "marshalling")]
    [InlineData(7, "rvalue")]
    public void LowerParameter_Rejected_FailsWithReason(int index, string reasonFragment) {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Rejected", index);
        Assert.False(result.Succeeded);
        Assert.Contains(reasonFragment, result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.Recommendation));
    }

    /// <summary>
    /// Ensures unmanaged function pointers keep their calling convention and managed ones are rejected.
    /// </summary>
    [Fact]
    public void LowerParameter_FunctionPointers_CarryCallingConvention() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.Equal("intptr_t (HE_CPP_STDCALL*)(intptr_t,uint32_t)", LowerParameter(lowerer, "Pointers2", 0).Type.MirrorTypeText);
        Assert.Equal("void (HE_CPP_CDECL*)(int32_t)", LowerParameter(lowerer, "Pointers2", 1).Type.MirrorTypeText);
        Assert.Equal("int32_t (HE_CPP_STDCALL*)()", LowerParameter(lowerer, "Pointers2", 2).Type.MirrorTypeText);
        CPPPInvokeTypeLoweringResult managed = LowerParameter(lowerer, "Pointers2", 3);
        Assert.False(managed.Succeeded);
        Assert.Contains("managed function pointers", managed.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures callbacks reject structs by value.
    /// </summary>
    [Fact]
    public void LowerParameter_CallbackStructByValue_Fails() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Structs", 0, isCallback: true);
        Assert.False(result.Succeeded);
        Assert.Contains("callbacks", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail** with `--filter "FullyQualifiedName~CPPPInvokeTypeLowererTests"`. Expected: a compile error, because the lowerer and the helper are missing.

- [ ] **Step 3: Implement.** Implementation notes:
  - Use `type.SpecialType` for the primitives: `System_SByte` … `System_Double`, plus `System_IntPtr`/`System_UIntPtr`. Roslyn reports `nint` as `System_IntPtr` with `IsNativeIntegerType`.
  - Check `bool` and `char` **before** the struct rule, because they are structs in Roslyn.
  - Detect enums with `type.TypeKind == TypeKind.Enum` and recurse on `((INamedTypeSymbol)type).EnumUnderlyingType`.
  - Detect pointers with `type is IPointerTypeSymbol pointer`. Validate `pointer.PointedAtType` (`void` is allowed) with the by-ref rules, then return `void*`.
  - Detect function pointers with `type is IFunctionPointerTypeSymbol functionPointer`:
    - `functionPointer.Signature.CallingConvention` is a `SignatureCallingConvention`: `StdCall` → StdCall, `CDecl` → Cdecl, `Unmanaged` → look at `Signature.UnmanagedCallingConventionTypes` (empty → StdCall; a single `CallConvStdcall`/`CallConvCdecl` → the matching one; anything else → failure), `Default` → managed failure, `ThisCall`/`FastCall` → unsupported convention.
    - Lower every parameter with `isCallback: true` and the return with `LowerReturn(..., isCallback: true)`.
    - Build the text as `<ret> (<macro>*)(<p1>,<p2>)` with no spaces after the commas, matching the tests.
  - Struct layout comes from `type.GetAttributes()`, where `AttributeClass.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute"`. The first constructor argument is the `LayoutKind` value (`0` Sequential, `2` Explicit, `3` Auto). Read the `Pack` named argument when present. With no attribute, a C# struct is Sequential.
  - Enumerate the fields with `type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic && !field.IsConst)` in declaration order. `field.IsFixedSizeBuffer` means the struct fails.
  - For by-value mirrors, add nested struct mirrors first, then the container, and skip keys already present (`Dictionary<string, CPPPInvokeMirrorStruct>` plus an ordered `List`).
  - Mirror-name sanitizer: replace each character outside `[A-Za-z0-9_]` with `_` in `ToDisplayString()` and prefix `he_pinvoke_`. Put this in a `static string CreateMirrorName(INamedTypeSymbol)` method on the lowerer.
  - `CPPPInvokeTestCompilation.Create`: build the references with `((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path))`, use `new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)`, and throw `InvalidOperationException` listing the errors if `GetDiagnostics()` has any with `Severity == DiagnosticSeverity.Error`.

- [ ] **Step 4: Run the tests and confirm they pass.**

- [ ] **Step 5: Commit** with `feat(pinvoke): lower blittable types to native mirror types`.

---

### Task 3: Import and callback analyzer with CPPPINV diagnostics

**Files:**
- Create: `cs2.cpp/pinvoke/CPPPInvokeAnalyzer.cs`
- Create: `cs2.cpp/pinvoke/CPPPInvokeAnalysisResult.cs`
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeAnalyzerTests.cs`

**Interfaces:**
- Consumes: the Task 1 model, `CPPPInvokeTypeLowerer`, and `CPPOwnershipDiagnosticFactory.Create(string code, SyntaxNode node, ISymbol member, string message, string recommendation)` (`cs2.cpp/ownership/CPPOwnershipDiagnosticFactory.cs:19`), which is reused for source-located error diagnostics.
- Produces:
  - `sealed class CPPPInvokeAnalysisResult` with properties `CPPPInvokePlan Plan`, `IReadOnlyList<CPPConversionDiagnostic> Diagnostics`, `bool HasErrors`.
  - `sealed class CPPPInvokeAnalyzer` with `CPPPInvokeAnalysisResult Analyze(IReadOnlyList<Compilation> compilations)`.

Behavior:
- Walk every `IMethodSymbol` in `compilation.SourceModule.GlobalNamespace`, recursively through namespaces and nested types.
- An `[LibraryImport]` method (attribute display name `System.Runtime.InteropServices.LibraryImportAttribute`) → `CPPPINV001`, "LibraryImport is not supported; use DllImport with a blittable signature."
- A method with `GetDllImportData() != null`:
  - Settings check → `CPPPINV003` for:
    - `SetLastError == true`;
    - `CharacterSet` equal to `CharSet.Unicode` or `CharSet.Auto`;
    - `BestFitMapping == true`;
    - `ThrowOnUnmappableCharacter == true`;
    - `(method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == 0`;
    - any `MarshalAsAttribute` on a parameter (`parameter.GetAttributes()`) or on the return (`method.GetReturnTypeAttributes()`).
  - Calling convention → `CPPPINV004` for `CallingConvention.ThisCall` or `FastCall`. `Winapi`/`StdCall` → StdCall; `Cdecl` → Cdecl.
  - Types → `CPPPINV002` for each failed parameter or return lowering, with the lowerer's reason and recommendation. Report every failing parameter, not only the first.
  - Entry point = `EntryPointName` when non-empty, else `method.Name`. Library = `CPPPInvokeLibraryNameNormalizer.Normalize(ModuleName)`.
  - De-duplication by entry point:
    - same entry point, same library and same `MirrorKey` → add the method id to the existing import;
    - same library but a different `MirrorKey` → `CPPPINV005`;
    - different library → `CPPPINV006`.
- A method with `UnmanagedCallersOnlyAttribute` (display name `System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute`):
  - `CPPPINV007` for: not static; generic; an `EntryPoint` named argument; another UnmanagedCallersOnly method with the same name in the same type.
  - `CallConvs` named argument: an array of `INamedTypeSymbol`. Empty or missing → StdCall; `System.Runtime.CompilerServices.CallConvStdcall` → StdCall; `CallConvCdecl` → Cdecl; anything else or more than one → `CPPPINV004`.
  - Lower the parameters and the return with `isCallback: true`. Failures → `CPPPINV008`.
  - Trampoline name = `"he_pinvoke_cb_" + sanitized(containingType.ToDisplayString()) + "_" + method.Name`, where sanitized replaces each non-`[A-Za-z0-9_]` character with `_`.
- Imports are sorted by (LibraryNamespace, EntryPoint) and callbacks by TrampolineName, both ordinal, so the output is deterministic.
- The syntax node for diagnostics is `method.DeclaringSyntaxReferences[0].GetSyntax()`.

- [ ] **Step 1: Write the failing tests.** Each test builds a compilation with `CPPPInvokeTestCompilation.Create` and runs `new CPPPInvokeAnalyzer().Analyze(new[] { compilation })`.

```csharp
using cs2.cpp;
using Microsoft.CodeAnalysis;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies import-table construction, de-duplication, and every CPPPINV rejection produced by the analyzer.
/// </summary>
public sealed class CPPPInvokeAnalyzerTests {
    /// <summary>
    /// Analyzes one source snippet.
    /// </summary>
    static CPPPInvokeAnalysisResult Analyze(string source) {
        return new CPPPInvokeAnalyzer().Analyze(new[] { (Compilation)CPPPInvokeTestCompilation.Create(source) });
    }

    /// <summary>
    /// Ensures a valid import produces one entry with an exact symbol, library namespace and stdcall convention.
    /// </summary>
    [Fact]
    public void Analyze_ValidImport_BuildsImportTable() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class U {
                [DllImport("User32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true)]
                static extern int Move(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
            }
            """);

        Assert.False(result.HasErrors);
        CPPPInvokeImport import = Assert.Single(result.Plan.Imports);
        Assert.Equal("user32", import.LibraryNamespace);
        Assert.Equal("SetWindowPos", import.EntryPoint);
        Assert.Equal("HE_CPP_STDCALL int32_t(intptr_t,intptr_t,int32_t,int32_t,int32_t,int32_t,uint32_t)", import.Signature.MirrorKey);
        Assert.Equal(new[] { "user32" }, result.Plan.LinkLibraries);
    }

    /// <summary>
    /// Ensures the same symbol imported by two classes with an identical signature yields one import holding both method ids.
    /// </summary>
    [Fact]
    public void Analyze_DuplicateImportsWithSameSignature_EmitsOneImport() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("user32.dll")] public static extern nint GetForegroundWindow(); }
            static class B { [DllImport("user32")] public static extern nint GetForegroundWindow(); }
            """);

        Assert.False(result.HasErrors);
        CPPPInvokeImport import = Assert.Single(result.Plan.Imports);
        Assert.Equal(2, import.MethodIds.Count);
    }

    /// <summary>
    /// Ensures each invalid declaration produces its stable diagnostic code.
    /// </summary>
    [Theory]
    [InlineData("[DllImport(\"user32.dll\")] static extern int F(bool value);", "CPPPINV002")]
    [InlineData("[DllImport(\"user32.dll\", SetLastError = true)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", PreserveSig = false)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\")] static extern int F([MarshalAs(UnmanagedType.I4)] int value);", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", CallingConvention = CallingConvention.ThisCall)] static extern int F();", "CPPPINV004")]
    [InlineData("[LibraryImport(\"user32.dll\")] static partial int F(); static partial int F() { return 0; }", "CPPPINV001")]
    public void Analyze_InvalidImport_ReportsCode(string declaration, string expectedCode) {
        CPPPInvokeAnalysisResult result = Analyze($$"""
            using System.Runtime.InteropServices;
            static partial class U {
                {{declaration}}
            }
            """);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode && diagnostic.Severity == CPPDiagnosticSeverity.Error && diagnostic.LineNumber > 0);
    }

    /// <summary>
    /// Ensures the same symbol with two different lowered signatures is rejected.
    /// </summary>
    [Fact]
    public void Analyze_ConflictingSignatures_ReportsCPPPINV005() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("user32.dll")] public static extern int GetKeyState(int key); }
            static class B { [DllImport("user32.dll")] public static extern short GetKeyState(int key); }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPPINV005");
    }

    /// <summary>
    /// Ensures one symbol imported from two libraries is rejected.
    /// </summary>
    [Fact]
    public void Analyze_SymbolFromTwoLibraries_ReportsCPPPINV006() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("a.dll")] public static extern int Init(); }
            static class B { [DllImport("b.dll")] public static extern int Init(); }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPPINV006");
    }

    /// <summary>
    /// Ensures valid callbacks get deterministic trampoline names and conventions.
    /// </summary>
    [Fact]
    public void Analyze_ValidCallbacks_BuildTrampolines() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Shell {
                static unsafe class Win {
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                    static nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam) { return 0; }
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                    static int Compare(void* a, void* b) { return 0; }
                }
            }
            """);

        Assert.False(result.HasErrors);
        Assert.Equal(new[] { "he_pinvoke_cb_Shell_Win_Compare", "he_pinvoke_cb_Shell_Win_WindowProc" }, result.Plan.Callbacks.Select(callback => callback.TrampolineName));
        Assert.Equal(CPPPInvokeCallingConvention.Cdecl, result.Plan.Callbacks[0].Signature.CallingConvention);
    }

    /// <summary>
    /// Ensures invalid callbacks are rejected with CPPPINV007 or CPPPINV008.
    /// </summary>
    [Theory]
    [InlineData("[UnmanagedCallersOnly(EntryPoint = \"Exported\")] static int F() { return 0; }", "CPPPINV007")]
    [InlineData("[UnmanagedCallersOnly] static int F() { return 0; } [UnmanagedCallersOnly] static int F(int a) { return a; }", "CPPPINV007")]
    [InlineData("[UnmanagedCallersOnly] static int F(P point) { return 0; }", "CPPPINV008")]
    [InlineData("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvThiscall) })] static int F() { return 0; }", "CPPPINV004")]
    public void Analyze_InvalidCallback_ReportsCode(string declaration, string expectedCode) {
        CPPPInvokeAnalysisResult result = Analyze($$"""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            struct P { public int X; }
            static class U {
                {{declaration}}
            }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail** with `--filter "FullyQualifiedName~CPPPInvokeAnalyzerTests"`. Expected: a compile error.
- [ ] **Step 3: Implement `CPPPInvokeAnalyzer` and `CPPPInvokeAnalysisResult`** per the behavior list above. Use one `CPPPInvokeTypeLowerer` instance per `Analyze` call so the mirror structs are shared across the whole plan. Build `CPPPInvokePlan(imports, callbacks, lowerer.MirrorStructs)`.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** with `feat(pinvoke): analyze DllImport and UnmanagedCallersOnly methods into a validated plan`.

---

### Task 4: Pipeline stage, converter/host plumbing, and skipping extern stubs

**Files:**
- Create: `cs2.cpp/pinvoke/CPPPInvokeAnalysisStage.cs`
- Modify: `cs2.cpp/CPPCodeConverter.cs`:
  - `:220-233` (`ConfigurePipeline`): insert the stage after `DocumentPreprocessingStage`, before `CPPOwnershipAnalysisStage`;
  - `:44` / `:543` / `:560` (area): add `CPPPInvokePlan PInvokePlan`, clear it in `ResetRunState`, add `SetPInvokePlan(CPPPInvokePlan)`.
- Modify: `cs2.cpp/ICPPConversionHost.cs:27` (area): add `CPPPInvokePlan PInvokePlan { get; }`.
- Modify: `cs2.cpp/CPPEmissionWorker.cs:69` (area): expose the host plan exactly the way the ownership result is exposed there.
- Modify: `cs2.core/model/ConversionFunction.cs:38-39,71-72`: add `string MethodId` (initialized to `string.Empty`), `bool IsDllImport`, and `bool IsUnmanagedCallersOnly`.
- Modify: `cs2.core/ConversionPreProcessor.cs:394-400` (area): when `methodSymbol != null`, set `func.MethodId = methodSymbol.OriginalDefinition.GetDocumentationCommentId() ?? string.Empty`, `func.IsDllImport = methodSymbol.GetDllImportData() != null`, and `func.IsUnmanagedCallersOnly` from the attribute display name `System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute`.
- Modify: `cs2.cpp/CPPClassEmitter.cs`:
  - `:3364`, `:3444`, `:3486`: extend the `IsNativeFreeFunctionStub(function)` guards so they also return early for `function.IsDllImport`;
  - `:636`: do not add the `runtime/native_exceptions.hpp` include for body-less functions that have `IsDllImport`.
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeConversionTests.cs`

**Interfaces:**
- Consumes: `CPPPInvokeAnalyzer.Analyze`.
- Produces:
  - `CPPCodeConverter.PInvokePlan` (never null after `AddCsproj` succeeds; an empty plan when the project has no imports).
  - `ICPPConversionHost.PInvokePlan`.
  - `ConversionFunction.MethodId` / `IsDllImport` / `IsUnmanagedCallersOnly`.

Stage `Execute`:
1. Collect compilations exactly like `CPPOwnershipAnalysisStage.CollectCompilations`. Copy that private method into the stage; it is a static member of this class, not a local function.
2. Run `new CPPPInvokeAnalyzer().Analyze(compilations)` and append the diagnostics to `Owner.Report.Diagnostics` with the same de-dup rule.
3. On errors, throw `InvalidOperationException` with the first error, formatted `"{Code} {file}({line},{col}): {Message}"`.
4. Otherwise call `Owner.SetPInvokePlan(result.Plan)`.

- [ ] **Step 1: Write the failing tests.** They use `RunConversion` (defined in `CPPCompileValidationRegressionTests.cs:11750`). Make it `internal static` if it is private, or copy the pattern through `CPPOwnershipConversionTestWorkspace`. Prefer making the existing helper `internal static` and calling `CPPCompileValidationRegressionTests.RunConversion(source, allowUnsafe: true)`.

```csharp
namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// End-to-end conversion tests for DllImport and UnmanagedCallersOnly lowering through the real converter pipeline.
/// </summary>
public sealed class CPPPInvokeConversionTests {
    /// <summary>
    /// Ensures an invalid DllImport fails the conversion with a source-located CPPPINV error.
    /// </summary>
    [Fact]
    public void AddCsproj_InvalidImport_FailsWithCPPPINV() {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class U {
                [DllImport("user32.dll")] public static extern int MessageBeep(bool flag);
                public static int Run() { return MessageBeep(true); }
            }
            """, allowUnsafe: true));
        Assert.StartsWith("CPPPINV002", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an extern import method has no generated member declaration or runtime-throwing stub.
    /// </summary>
    [Fact]
    public void WriteOutput_ExternImport_SkipsStubEmission() {
        cs2.cpp.tests.CPPCompileValidationRegressionTests.ConversionOutput output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Clock {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static ulong Now() { return GetTickCount64(); }
            }
            """, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.cpp"));
        Assert.DoesNotContain("GetTickCount64(", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Method has no generated body", source, StringComparison.Ordinal);
    }
}
```

`ConversionOutput`'s real name and accessibility are at `CPPCompileValidationRegressionTests.cs:11750-11800`. Make the type and the helper `internal` so these tests can use them.

- [ ] **Step 2: Run the tests and confirm they fail.** Expected: the first fails because conversion succeeds (no stage yet); the second fails because a declaration is emitted.
- [ ] **Step 3: Implement** the stage, the plumbing, the preprocessor flags and the emitter guards as listed above.
- [ ] **Step 4: Run the new tests, then the regression guard.** Run `--filter "FullyQualifiedName~CPPPInvokeConversionTests"`, then `--filter "FullyQualifiedName~NativeFreeFunction|FullyQualifiedName~CPPOwnershipAnalysisStageTests"`. Expected: all pass.
- [ ] **Step 5: Commit** with `feat(pinvoke): run the P/Invoke analysis stage and skip extern stub emission`.

---

### Task 5: Native imports writer, harness, and handoff

**Files:**
- Create: `cs2.cpp/pinvoke/CPPNativeImportsWriter.cs`
- Create: `cs2.cpp/.net.cpp/runtime/native_calling_convention.hpp`
- Modify: `cs2.cpp/CPPCodeConverter.cs:280` (area, before `CPPCompileHarnessWriter.Write` at `:291`): call `CPPNativeImportsWriter.Write(outputFolder, PInvokePlan)` and register each returned path with `TrackEmittedFile` (`:924`).
- Modify: `cs2.cpp/CPPCodeConverter.cs:297`: call `CPPWindowsHandoffWriter.Write(outputFolder, PInvokePlan)`.
- Modify: `cs2.cpp/CPPWindowsHandoffWriter.cs`: change the signature to `Write(string outputFolder, CPPPInvokePlan plan)` and publish the two new variables.
- Modify: `cs2.cpp/CPPCompileHarnessWriter.cs:38-43, 94-121`: exclude `native_imports/native_imports.cpp` from the unity list, and have both build scripts compile it as a second object when it exists.
- Modify: `cs2.cpp.tests/CPPWindowsHandoffWriterTests.cs` for the new signature.
- Test: add to `cs2.cpp.tests/pinvoke/CPPPInvokeConversionTests.cs`.

**Interfaces:**
- Consumes: `CPPPInvokePlan`.
- Produces:
  - `CPPNativeImportsWriter`:
    - `public const string FolderName = "native_imports"`, `HeaderFileName = "native_imports.hpp"`, `SourceFileName = "native_imports.cpp"`;
    - `static IReadOnlyList<string> Write(string outputFolder, CPPPInvokePlan plan)`, which returns an empty list and writes nothing when `!plan.HasNativeImports && plan.MirrorStructs.Count == 0`.
  - Include path used by generated code: `"native_imports/native_imports.hpp"`.

`native_calling_convention.hpp`:

```cpp
#ifndef HE_CPP_RUNTIME_NATIVE_CALLING_CONVENTION_HPP
#define HE_CPP_RUNTIME_NATIVE_CALLING_CONVENTION_HPP

#if defined(_WIN32)
#define HE_CPP_STDCALL __stdcall
#define HE_CPP_CDECL __cdecl
#else
#define HE_CPP_STDCALL
#define HE_CPP_CDECL
#endif

#endif
```

The header the writer generates for `SetWindowPos`, `GetCursorPos(out NativePoint)` and `WindowFromPoint(NativePoint)` must be exactly:

```cpp
// Generated direct P/Invoke forwarders. Do not edit.
#ifndef HE_PINVOKE_NATIVE_IMPORTS_HPP
#define HE_PINVOKE_NATIVE_IMPORTS_HPP

#include <cstdint>
#include <cstring>

#include "../runtime/native_calling_convention.hpp"

struct he_pinvoke_Native_NativePoint {
    int32_t X;
    int32_t Y;
};

template <typename TTo, typename TFrom>
inline TTo he_pinvoke_bit_copy(const TFrom& value) {
    static_assert(sizeof(TTo) == sizeof(TFrom), "he_pinvoke_bit_copy requires layout-compatible types.");
    TTo result;
    std::memcpy(&result, &value, sizeof(TTo));
    return result;
}

namespace he_pinvoke::user32 {
    int32_t GetCursorPos(void* lpPoint);
    int32_t SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags);
    intptr_t WindowFromPoint(he_pinvoke_Native_NativePoint Point);
}

#endif
```

A mirror with `Pack > 0` is wrapped in `#pragma pack(push, N)` / `#pragma pack(pop)`. Mirror struct field and parameter names are the C# names. The source:

```cpp
// Generated direct P/Invoke forwarders. Do not edit.
#include "native_imports.hpp"

extern "C" int32_t HE_CPP_STDCALL GetCursorPos(void* lpPoint);
extern "C" int32_t HE_CPP_STDCALL SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags);
extern "C" intptr_t HE_CPP_STDCALL WindowFromPoint(he_pinvoke_Native_NativePoint Point);

namespace he_pinvoke::user32 {
    int32_t GetCursorPos(void* lpPoint) {
        return ::GetCursorPos(lpPoint);
    }

    int32_t SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags) {
        return ::SetWindowPos(hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags);
    }

    intptr_t WindowFromPoint(he_pinvoke_Native_NativePoint Point) {
        return ::WindowFromPoint(Point);
    }
}
```

Void returns emit `::Name(args);` without `return`. The parameter names come from the first method id's C# parameter names. Store them in `CPPPInvokeParameter.Name`. They are only names and are not part of `MirrorKey`.

The handoff text for a plan with imports:

```cmake
set(CPP_GENERATED_CORE_ROOT "${CMAKE_CURRENT_LIST_DIR}")
set(CPP_GENERATED_CONFIG_HEADER "${CPP_GENERATED_CORE_ROOT}/helcpp_config.hpp")
set(CPP_GENERATED_UNITY_SOURCE "${CPP_GENERATED_CORE_ROOT}/generated_unity.cpp")
set(CPP_GENERATED_FEATURE_MANIFEST_HEADER "${CPP_GENERATED_CORE_ROOT}/runtime/feature_manifest.hpp")
set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE "${CPP_GENERATED_CORE_ROOT}/native_imports/native_imports.cpp")
set(CPP_GENERATED_NATIVE_LINK_LIBRARIES "kernel32;user32")
```

Without imports, the last two lines are `set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE "")` and `set(CPP_GENERATED_NATIVE_LINK_LIBRARIES "")`.

Harness scripts, when `native_imports/native_imports.cpp` exists:
- MSVC adds `cl /nologo /std:c++20 /EHsc /I"%SCRIPT_DIR%." /I"%SCRIPT_DIR%runtime" /c "%SCRIPT_DIR%native_imports\native_imports.cpp" /Fo"%BUILD_DIR%\native_imports.obj"`.
- GCC adds the equivalent `g++ ... -c ... -o "$BUILD_DIR/native_imports.o"`.

- [ ] **Step 1: Write the failing tests** (add them to `CPPPInvokeConversionTests`):

```csharp
/// <summary>
/// Shared source importing two user32 symbols, one kernel32 symbol, and a by-value struct.
/// </summary>
const string ImportsSource = """
    using System.Runtime.InteropServices;
    namespace Native {
        public struct NativePoint { public int X; public int Y; }
        public static class User32 {
            [DllImport("user32.dll")] public static extern int SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
            [DllImport("user32.dll")] public static extern int GetCursorPos(out NativePoint lpPoint);
            [DllImport("user32.dll")] public static extern nint WindowFromPoint(NativePoint Point);
        }
        public static class Kernel32 { [DllImport("kernel32.dll")] public static extern ulong GetTickCount64(); }
        public static class Other { [DllImport("user32.dll")] public static extern nint WindowFromPoint(NativePoint Point); }
    }
    """;

/// <summary>
/// Ensures the header holds only portable includes, mirrors and namespaced forwarder declarations.
/// </summary>
[Fact]
public void WriteOutput_Imports_EmitsIsolatedHeader() {
    var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
    string header = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.hpp"));
    Assert.DoesNotContain("Windows.h", header, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("struct he_pinvoke_Native_NativePoint {", header, StringComparison.Ordinal);
    Assert.Contains("namespace he_pinvoke::user32 {", header, StringComparison.Ordinal);
    Assert.Contains("int32_t GetCursorPos(void* lpPoint);", header, StringComparison.Ordinal);
    Assert.Contains("namespace he_pinvoke::kernel32 {", header, StringComparison.Ordinal);
}

/// <summary>
/// Ensures one extern "C" prototype and forwarder is emitted per distinct symbol even when imported twice.
/// </summary>
[Fact]
public void WriteOutput_DuplicateImports_EmitsSingleForwarder() {
    var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
    string source = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.cpp"));
    Assert.Equal(1, CountOccurrences(source, "extern \"C\" intptr_t HE_CPP_STDCALL WindowFromPoint("));
    Assert.Contains("return ::GetTickCount64();", source, StringComparison.Ordinal);
}

/// <summary>
/// Ensures the unity build excludes the isolated translation unit and the handoff publishes it with link libraries.
/// </summary>
[Fact]
public void WriteOutput_Imports_ExcludedFromUnityAndPublishedInHandoff() {
    var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
    string unity = File.ReadAllText(Path.Combine(output.OutputPath, "generated_unity.cpp"));
    string handoff = File.ReadAllText(Path.Combine(output.OutputPath, "generated_windows_handoff.cmake"));
    string msvc = File.ReadAllText(Path.Combine(output.OutputPath, "build_msvc.bat"));
    Assert.DoesNotContain("native_imports/native_imports.cpp", unity, StringComparison.Ordinal);
    Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"${CPP_GENERATED_CORE_ROOT}/native_imports/native_imports.cpp\")", handoff, StringComparison.Ordinal);
    Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"kernel32;user32\")", handoff, StringComparison.Ordinal);
    Assert.Contains("native_imports.obj", msvc, StringComparison.Ordinal);
}

/// <summary>
/// Ensures projects without imports keep their previous output shape.
/// </summary>
[Fact]
public void WriteOutput_NoImports_DoesNotEmitNativeImportsFolder() {
    var output = CPPCompileValidationRegressionTests.RunConversion("public static class Plain { public static int Two() { return 2; } }");
    Assert.False(Directory.Exists(Path.Combine(output.OutputPath, "native_imports")));
    string handoff = File.ReadAllText(Path.Combine(output.OutputPath, "generated_windows_handoff.cmake"));
    Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"\")", handoff, StringComparison.Ordinal);
    Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"\")", handoff, StringComparison.Ordinal);
}

/// <summary>
/// Counts ordinal occurrences of a fragment in generated text.
/// </summary>
static int CountOccurrences(string text, string fragment) {
    int count = 0;
    int index = text.IndexOf(fragment, StringComparison.Ordinal);
    while (index >= 0) {
        count++;
        index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
    }
    return count;
}
```

Also update `CPPWindowsHandoffWriterTests` to call `Write(folder, emptyPlan)` and assert on the two empty variables.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** the writer, the runtime header, the converter calls, the handoff signature and the harness exclusion plus the second compile line. The runtime header lives under `.net.cpp/runtime`, which is copied to bin with `CopyToOutputDirectory=Always`. Check that `native_calling_convention.hpp` lands in `<output>/runtime/`, and if it does not, register it the same way `function_pointer.hpp` is registered (`CPPRuntimeRequirementCatalog.cs:37`).
- [ ] **Step 4: Run the tests and confirm they pass,** including `--filter "FullyQualifiedName~CPPWindowsHandoffWriterTests|FullyQualifiedName~CPPCompileHarnessWriterTests"`.
- [ ] **Step 5: Commit** with `feat(pinvoke): emit isolated native import forwarders and publish them to the Windows handoff`.

---

### Task 6: Call-site lowering to forwarders

**Files:**
- Modify: `cs2.cpp/CPPConversiorProcessor.cs`:
  - add `TryProcessPInvokeInvocation` next to `TryProcessNativeFreeFunctionInvocation` (definition near `:8691`);
  - call it right before the native free-function interceptor at `:5828`.
- Test: add to `cs2.cpp.tests/pinvoke/CPPPInvokeConversionTests.cs`.

**Interfaces:**
- Consumes:
  - the host's `PInvokePlan` (reach it the way the processor reaches the ownership result through the host);
  - `CPPPInvokeMethodIds.Get`, `CPPPInvokeImport`, `CPPNativeImportsWriter.FolderName`/`HeaderFileName`;
  - the existing `AppendInvocationArgument` (`:7498`), `ResolveInvokedMethodSymbol` (`:9574`), `GetOwningEmissionClass`, and the generated-type text helper `GetCppTypeToken` (`:11947`).

Algorithm for `TryProcessPInvokeInvocation(semantic, context, invocationExpression, lines, out VariableType resultType)`:
1. Resolve the method symbol. If it is null, or `GetDllImportData()` is null, return false.
2. `plan.TryGetImport(CPPPInvokeMethodIds.Get(symbol), out import)`. If this fails, throw `InvalidOperationException("DllImport method '<name>' is missing from the P/Invoke plan.")`, because the stage guarantees every import is present.
3. Add `"native_imports/native_imports.hpp"` to `GetOwningEmissionClass(context).SourceIncludes`.
4. Build the call tokens: `import.ForwarderQualifiedName`, `"("`, then for each argument i, paired with `import.Signature.Parameters[i]`:
   - lower the argument into a scratch `List<string>` with `AppendInvocationArgument`, which already handles `out var` declarations through before-lines, and join it into `argumentText`;
   - wrap it by kind:
     - `RefKind.Ref`/`RefKind.Out` → `reinterpret_cast<void*>(&(argumentText))`;
     - `Pointer` → `reinterpret_cast<void*>(argumentText)`;
     - `Enum` → `static_cast<MirrorTypeText>(argumentText)`;
     - `Struct` → `he_pinvoke_bit_copy<MirrorTypeText>(argumentText)`;
     - `FunctionPointer` → `reinterpret_cast<MirrorTypeText>(he_cpp_raw_function_pointer(argumentText))` (the helper arrives in Task 7; until Task 7 lands, function-pointer arguments are covered only by Task 7's tests);
     - `Primitive` → `argumentText` unchanged;
   - separate the arguments with `", "`, then `")"`.
5. Wrap the return by kind:
   - `Enum` → `static_cast<GeneratedEnumType>(call)`;
   - `Pointer` → `reinterpret_cast<GeneratedPointerType>(call)`;
   - `Struct` → `he_pinvoke_bit_copy<GeneratedStructType>(call)`;
   - otherwise the call unchanged.
   Generated type text comes from `GetCppTypeToken(symbol.ReturnType ...)`. Use whichever overload renders a return type; confirm it at `:11947`.
6. `resultType = VariableUtil.GetVarType(symbol.ReturnType)` for non-void returns.

The arguments must stay in the parameter order of the C# method (named arguments are out of scope; the analyzer should also have rejected optional parameters). Add this rule to Task 3 if a named-argument test fails: raise `CPPPINV002`, "named or omitted arguments are not supported for DllImport calls".

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>
/// Ensures a primitive import call targets the namespaced forwarder and includes the forwarder header.
/// </summary>
[Fact]
public void WriteOutput_PrimitiveImportCall_UsesForwarder() {
    var output = CPPCompileValidationRegressionTests.RunConversion("""
        using System.Runtime.InteropServices;
        public static class Clock {
            [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
            public static ulong Now() { return GetTickCount64(); }
        }
        """, allowUnsafe: true);
    string source = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.cpp"));
    Assert.Contains("#include \"native_imports/native_imports.hpp\"", source, StringComparison.Ordinal);
    Assert.Contains("he_pinvoke::kernel32::GetTickCount64()", source, StringComparison.Ordinal);
}

/// <summary>
/// Ensures out-var struct arguments declare the local first and pass its address as void*.
/// </summary>
[Fact]
public void WriteOutput_OutVarStructArgument_DeclaresLocalAndPassesAddress() {
    var output = CPPCompileValidationRegressionTests.RunConversion("""
        using System.Runtime.InteropServices;
        public struct NativePoint { public int X; public int Y; }
        public static class Cursor {
            [DllImport("user32.dll")] static extern int GetCursorPos(out NativePoint point);
            public static int ReadX() { GetCursorPos(out NativePoint p); return p.X; }
        }
        """, allowUnsafe: true);
    string source = File.ReadAllText(Path.Combine(output.OutputPath, "Cursor.cpp"));
    Assert.Contains("he_pinvoke::user32::GetCursorPos(reinterpret_cast<void*>(&(p)))", source, StringComparison.Ordinal);
    Assert.True(source.IndexOf("NativePoint p", StringComparison.Ordinal) < source.IndexOf("he_pinvoke::user32::GetCursorPos", StringComparison.Ordinal));
}

/// <summary>
/// Ensures enum arguments and returns cast through the underlying integer type.
/// </summary>
[Fact]
public void WriteOutput_EnumArgumentAndReturn_CastsThroughUnderlyingType() {
    var output = CPPCompileValidationRegressionTests.RunConversion("""
        using System.Runtime.InteropServices;
        public enum Show : int { Hide = 0, Normal = 1 }
        public static class Win {
            [DllImport("user32.dll")] static extern Show ShowWindow(nint hWnd, Show command);
            public static Show Hide(nint hWnd) { return ShowWindow(hWnd, Show.Hide); }
        }
        """, allowUnsafe: true);
    string source = File.ReadAllText(Path.Combine(output.OutputPath, "Win.cpp"));
    Assert.Contains("static_cast<int32_t>(", source, StringComparison.Ordinal);
    Assert.Contains("static_cast<Show>(he_pinvoke::user32::ShowWindow(", source, StringComparison.Ordinal);
}

/// <summary>
/// Ensures pointer arguments cross as void* and by-value structs are bit-copied into mirrors.
/// </summary>
[Fact]
public void WriteOutput_PointerAndByValueStruct_AreConverted() {
    var output = CPPCompileValidationRegressionTests.RunConversion("""
        using System.Runtime.InteropServices;
        public struct NativePoint { public int X; public int Y; }
        public static unsafe class Win {
            [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
            [DllImport("user32.dll")] static extern int GetWindowRect(nint hWnd, int* rect);
            public static nint At(NativePoint point) { return WindowFromPoint(point); }
            public static int Rect(nint hWnd, int* rect) { return GetWindowRect(hWnd, rect); }
        }
        """, allowUnsafe: true);
    string source = File.ReadAllText(Path.Combine(output.OutputPath, "Win.cpp"));
    Assert.Contains("he_pinvoke::user32::WindowFromPoint(he_pinvoke_bit_copy<he_pinvoke_NativePoint>(point))", source, StringComparison.Ordinal);
    Assert.Contains("he_pinvoke::user32::GetWindowRect(hWnd, reinterpret_cast<void*>(rect))", source, StringComparison.Ordinal);
}
```

The exact spelling of emitted locals and parameters (`p`, `point`, `hWnd`) follows the existing emitter. If the emitter renames identifiers (it sanitizes C++ keywords only), adjust the literal to what it prints and keep the shape of the assertion.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement `TryProcessPInvokeInvocation`** per the algorithm.
- [ ] **Step 4: Run the tests and confirm they pass,** and re-run `--filter "FullyQualifiedName~NativeFreeFunction|FullyQualifiedName~Invocation"` as a regression guard.
- [ ] **Step 5: Commit** with `feat(pinvoke): lower DllImport calls to namespaced native forwarders`.

---

### Task 7: Unmanaged function pointers, callback trampolines, and mirror layout asserts

**Files:**
- Create: `cs2.cpp/.net.cpp/runtime/unmanaged_function_pointer.hpp`
- Modify: `cs2.core/util/VariableUtil.cs:665-686`: for an unmanaged signature (`Signature.CallingConvention` is `StdCall`/`CDecl`/`Unmanaged`), set the type name and `QualifiedTypeName` to `StdcallFunctionPointer` or `CdeclFunctionPointer`, using the same convention resolution as `CPPPInvokeTypeLowerer`. Managed pointers keep `FunctionPointer`.
- Modify: every site that matches the literal `"FunctionPointer"` type name so it also accepts the two new names, via a new `static class CPPFunctionPointerTypeNames { static bool IsFunctionPointerTypeName(string name) }` in `cs2.cpp/CPPFunctionPointerTypeNames.cs`. Known sites: `CPPVariableType.cs:516`, `CPPConversiorProcessor.cs:13878-13885`, `CPPClassEmitter.cs:1519-1521`, `CPPRuntimeRequirementCatalog.cs:37`. Grep for the rest with `"FunctionPointer"`.
- Modify: `cs2.cpp/CPPRuntimeRequirementCatalog.cs:37`: the NativeFunctionPointer requirement also brings `runtime/unmanaged_function_pointer.hpp` and `runtime/native_calling_convention.hpp`.
- Modify: `cs2.cpp/CPPConversiorProcessor.cs:12428-12465` (`TryProcessFunctionPointerAddressOfExpression` / `RenderQualifiedMethodPointerTarget`): when the target method's `MethodId` is a callback in the plan, render `&<TrampolineName>` instead of the `static_cast<...>(&Type::Name)`.
- Modify: `cs2.cpp/CPPClassEmitter.cs` near `WriteFunction` (`:3363`) and the free-function writers (`:3424`, `:3616`):
  - for each function with `IsUnmanagedCallersOnly`, look up the plan callback by `MethodId` and emit:
    - in the class `.hpp`, after the class definition: `<ret> <macro> <TrampolineName>(<generated params>) noexcept;`
    - in the class `.cpp`: `<ret> <macro> <TrampolineName>(<generated params>) noexcept { return <GeneratedType>::<Method>(<param names>); }` (no `return` for void);
    - inside the class definition: `friend <ret> <macro> <TrampolineName>(<generated params>) noexcept;`, because C# callbacks are usually `private static` and the free trampoline must be able to call them;
  - for a class whose type key is in `plan.MirrorStructs`, add `#include "native_imports/native_imports.hpp"` to its `.cpp` and emit, for mirror `M` and generated struct `S`:
    - `static_assert(sizeof(S) == sizeof(M), "...");`
    - `static_assert(alignof(S) == alignof(M), "...");`
    - one `static_assert(offsetof(S, F) == offsetof(M, F), "...");` per field, which also needs `#include <cstddef>`.

  Use the emitter's existing parameter-type rendering (`GetParameterType`, `:340`) for the generated params.
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeCallbackTests.cs`

`unmanaged_function_pointer.hpp`: copy the API of `FunctionPointer` from `runtime/function_pointer.hpp` (constructors, assignment, bool, `==`/`!=` with nullptr, `get()`, `operator()`, and the free `==`/`!=` operators) into two class templates, `StdcallFunctionPointer` and `CdeclFunctionPointer`. The only difference from `FunctionPointer` is:
- `using PointerType = TReturn(HE_CPP_STDCALL*)(TArgs...);`
- `using PointerType = TReturn(HE_CPP_CDECL*)(TArgs...);`

Then add:

```cpp
template <typename TReturn, typename... TArgs>
constexpr typename StdcallFunctionPointer<TReturn, TArgs...>::PointerType he_cpp_raw_function_pointer(const StdcallFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value.get();
}

template <typename TReturn, typename... TArgs>
constexpr typename CdeclFunctionPointer<TReturn, TArgs...>::PointerType he_cpp_raw_function_pointer(const CdeclFunctionPointer<TReturn, TArgs...>& value) noexcept {
    return value.get();
}

template <typename TPointer>
constexpr TPointer* he_cpp_raw_function_pointer(TPointer* value) noexcept {
    return value;
}
```

The header includes `<cstddef>`, `"native_algorithm.hpp"` and `"native_calling_convention.hpp"`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies UnmanagedCallersOnly trampolines, unmanaged function-pointer lowering, and mirror layout assertions.
/// </summary>
public sealed class CPPPInvokeCallbackTests {
    /// <summary>
    /// Source with a stdcall enumeration callback passed to a DllImport.
    /// </summary>
    const string Source = """
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        public struct NativePoint { public int X; public int Y; }
        public static unsafe class Locales {
            static int Count;
            [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
            [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            static int OnLocale(ushort* name, uint flags, nint lParam) { Count++; return 1; }
            public static int Run() {
                Count = 0;
                delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback = &OnLocale;
                EnumSystemLocalesEx(callback, 0, 42, 0);
                return Count;
            }
        }
        """;

    /// <summary>
    /// Ensures the trampoline is emitted with the calling convention, noexcept and a forwarding body.
    /// </summary>
    [Fact]
    public void WriteOutput_Callback_EmitsNoexceptTrampoline() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.cpp"));
        Assert.Contains("int32_t HE_CPP_STDCALL he_pinvoke_cb_Locales_OnLocale(", header, StringComparison.Ordinal);
        Assert.Contains(") noexcept;", header, StringComparison.Ordinal);
        Assert.Contains("return Locales::OnLocale(name, flags, lParam);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the unmanaged function-pointer local uses the stdcall wrapper, takes the trampoline address, and is unwrapped at the call.
    /// </summary>
    [Fact]
    public void WriteOutput_UnmanagedFunctionPointer_UsesTrampolineAndRawPointer() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.cpp"));
        Assert.Contains("StdcallFunctionPointer<int32_t, uint16_t*, uint32_t, intptr_t> callback = &he_pinvoke_cb_Locales_OnLocale;", source, StringComparison.Ordinal);
        Assert.Contains("reinterpret_cast<int32_t (HE_CPP_STDCALL*)(void*,uint32_t,intptr_t)>(he_cpp_raw_function_pointer(callback))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a by-value mirrored struct gets size, alignment and per-field offset assertions in its own source file.
    /// </summary>
    [Fact]
    public void WriteOutput_MirrorStruct_EmitsLayoutAsserts() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "NativePoint.cpp"));
        Assert.Contains("static_assert(sizeof(NativePoint) == sizeof(he_pinvoke_NativePoint)", source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(NativePoint) == alignof(he_pinvoke_NativePoint)", source, StringComparison.Ordinal);
        Assert.Contains("static_assert(offsetof(NativePoint, Y) == offsetof(he_pinvoke_NativePoint, Y)", source, StringComparison.Ordinal);
    }
}
```

The literal texts for generated types (`uint16_t*`, `int32_t`, the `NativePoint` stem) follow what the existing emitter prints for these C# types. If a literal differs only in spelling (for example `unsigned short*`), adjust the literal to the emitter's spelling, not the emitter to the literal.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** the runtime header, the type-name change and helper, the address-of redirect, the trampolines and the layout asserts.
- [ ] **Step 4: Run the tests and confirm they pass,** plus the existing function-pointer regressions: `--filter "FullyQualifiedName~FunctionPointer"`. The address-of tests are at `CPPCompileValidationRegressionTests.cs:7368` and `:7464-7552`. Managed `delegate*` output must be unchanged.
- [ ] **Step 5: Commit** with `feat(pinvoke): lower UnmanagedCallersOnly callbacks and unmanaged function pointers`.

---

### Task 8: Real-compiler and managed-vs-native differential tests

**Files:**
- Modify: `cs2.cpp.tests/TestHelpers/CPPGeneratedProgramRunner.cs`:
  - add the overload `Run(string entryPointBody, string entryPointPrelude)`; the existing `Run(body)` calls it with `string.Empty`;
  - the prelude is written **before** the unity include;
  - when `native_imports/native_imports.cpp` exists in `OutputPath`, add it to the `cl` (and `g++`) command line;
  - append every library from `generated_windows_handoff.cmake`'s `CPP_GENERATED_NATIVE_LINK_LIBRARIES` as `<name>.lib` on Windows, parsed by a new `static IReadOnlyList<string> CPPHandoffLinkLibraryReader.Read(string handoffPath)` in `TestHelpers/CPPHandoffLinkLibraryReader.cs`.
- Create: `cs2.cpp.tests/TestHelpers/CPPManagedSnippetRunner.cs`, with `static long Run(string source, string typeName, string methodName)`:
  - compile with `CPPPInvokeTestCompilation.Create` (move that helper to `TestHelpers` if Task 2 put it elsewhere);
  - emit to a `MemoryStream`;
  - load with `System.Runtime.Loader.AssemblyLoadContext` (collectible);
  - invoke the public static method and return its value converted to `long`.
- Test: `cs2.cpp.tests/pinvoke/CPPPInvokeNativeExecutionTests.cs`

These tests need VS2022 on Windows (`CPPOwnershipConversionOutput.ResolveVisualStudioDeveloperCommandPath`). Follow the existing `CPPNativeRuntimeExecutionTests` convention: if that suite runs unconditionally, so do these. Mark the class `[Trait("Category", "NativeCompile")]` so they can be filtered.

- [ ] **Step 1: Write the failing tests**

```csharp
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Compiles and runs generated C++ that calls real Windows APIs, and compares each result with the same C# run as managed P/Invoke.
/// </summary>
[Trait("Category", "NativeCompile")]
public sealed class CPPPInvokeNativeExecutionTests {
    /// <summary>
    /// Converts the probe, runs it natively, and returns the printed value together with the managed value.
    /// </summary>
    static void AssertNativeMatchesManaged(string source, string prelude, Func<long, bool> sanity) {
        var output = CPPCompileValidationRegressionTests.RunConversion(source, allowUnsafe: true);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("std::cout << static_cast<long long>(Probe::Run());", prelude);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.Equal(0, result.ProgramExitCode);
        long native = long.Parse(result.ProgramOutput.Trim().Split(Environment.NewLine)[0]);
        long managed = CPPManagedSnippetRunner.Run(source, "Probe", "Run");
        Assert.True(sanity(native), $"native value {native} failed the sanity check");
        Assert.Equal(managed, native);
    }

    /// <summary>
    /// Ensures the simplest import compiles, links against kernel32 and returns the same success flag in both modes.
    /// </summary>
    [Fact]
    public void GetTickCount64_CompilesLinksAndMatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public static class Probe {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static long Run() { return GetTickCount64() > 0 ? 1 : 0; }
            }
            """, string.Empty, value => value == 1);
    }

    /// <summary>
    /// Ensures a by-ref struct import compiles in a unity build that already includes Windows.h and matches the managed result.
    /// </summary>
    [Fact]
    public void GetCursorPos_WithWindowsHeaderInUnity_CompilesAndMatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public struct NativePoint { public int X; public int Y; }
            public static class Probe {
                [DllImport("user32.dll")] static extern int GetCursorPos(out NativePoint point);
                [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
                public static long Run() {
                    int ok = GetCursorPos(out NativePoint point);
                    nint window = WindowFromPoint(point);
                    return ok != 0 ? 1 : 0;
                }
            }
            """, "#include <Windows.h>", value => value == 0 || value == 1);
    }

    /// <summary>
    /// Ensures a stdcall callback round trip enumerates the same number of locales natively and managed, with lParam intact.
    /// </summary>
    [Fact]
    public void EnumSystemLocalesEx_CallbackRoundTrip_MatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            public static unsafe class Probe {
                static long Count;
                static long BadParameter;
                [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                static int OnLocale(ushort* name, uint flags, nint lParam) {
                    if (lParam != 42) { BadParameter++; }
                    Count++;
                    return 1;
                }
                public static long Run() {
                    Count = 0;
                    BadParameter = 0;
                    EnumSystemLocalesEx(&OnLocale, 0, 42, 0);
                    return BadParameter == 0 ? Count : -1;
                }
            }
            """, string.Empty, value => value > 0);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail** with `--filter "FullyQualifiedName~CPPPInvokeNativeExecutionTests"`. Expected: a compile error on the `Run(body, prelude)` overload and `CPPManagedSnippetRunner`.
- [ ] **Step 3: Implement** the runner overload, the link-library reader and the managed snippet runner. When a native test fails, fix the **emitter** (the rule in `AGENTS.md`); never edit the generated files.
- [ ] **Step 4: Run the tests and confirm they pass.** Then run the whole P/Invoke suite, `--filter "FullyQualifiedName~pinvoke"`, and the function-pointer and native-free-function regressions from Tasks 4–7.
- [ ] **Step 5: Commit** with `test(pinvoke): verify direct P/Invoke against real Windows APIs and managed results`.
