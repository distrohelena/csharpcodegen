# Freestanding engine core: 65816 compile measurement

Date: 2026-09-19
Status: blocked before compile — Step 1 (generation) failed for every preset tried

## Summary

This report was supposed to measure how much of the generated `helengine.core`
compiles on the `mos-snes-far-clang++` (llvm-mos-65816) toolchain under the
`native-core-boot-freestanding` preset. Generation itself never completed: the
CLI's ownership-analysis stage rejects `helengine.core` with a hard error
(`CPPOWN001`) before any C++ file is written, for the freestanding preset and
for the plain (already-shipping) `native-core-boot` preset alike. No
`.cpp`/`.hpp` file, no `generated_unity.cpp`, no conversion report, and no
`helcpp_config.hpp` were produced by this run. This is not specific to the
freestanding runtime work in Tasks 1-8; it is a pre-existing gap that blocks
generating this engine core at all today. No source, generated, or runtime
code was modified while producing this report.

## Environment

- `csharpcodegen` at commit `08a65b9e8bcc3cae9a985f2a95eeb95454d69448` (branch `master`), rebuilt in Release before running: `dotnet build codegen/codegen.csproj -c Release` — 3 projects, 0 errors, 0 warnings.
- `helengine` at commit `d794fcc94613a28a135fd40db2f3353a088ea559`. `engine/helengine.core/assets/PackagedAssetBinarySerializer.cs` (the file that trips the error) was last touched by commit `cc06017e40fe7ef67e199442b41a9c70af0c9174` (2026-09-18).
- Docker image `helengine-snes-toolchain` confirmed present locally (`sha256:4ae0f74c06a8...`), unused because there was nothing to compile.
- Output directory: `C:\dev\helworks\builds\csharpcodegen\core-freestanding` (empty after the run; no files under %TEMP% were used).

## Step 1: Generate the engine core

### Command as written in the brief (rejected)

```bash
cd /c/dev/helworks/csharpcodegen && mkdir -p /c/dev/helworks/builds/csharpcodegen/core-freestanding
./codegen/bin/Release/net9.0/codegen.exe --cpp \
  --project /c/dev/helworks/helengine/engine/helengine.core/helengine.core.csproj \
  --output /c/dev/helworks/builds/csharpcodegen/core-freestanding \
  --feature-catalog /c/dev/helworks/helengine/engine/helengine.editor/codegen/features/helengine-feature-catalog.json \
  --platform retroppc --language cpp --endianness big \
  --preset native-core-boot-freestanding \
  --set include-project-defined-preprocessor-symbols=false \
  --set write-conversion-report=true
```

Result: `Codegen failed: Custom platform 'retroppc' must provide a generated-math-convention option.`

**Confirmed by reading the source before running anything further**
(`codegen/CodegenCliOptionsBuilder.cs:22` calls
`CreatePlatformProfile(parsedArguments.PlatformId, ...)` unconditionally,
*before* `CPPConversionPresetCatalog.ApplyTo` ever runs and overwrites
`options.PlatformProfile` with the preset's own
`CreateCustomHeadless("retroppc", false, NativeColumnVector, 4)`
(`cs2.cpp/CPPConversionPresetCatalog.cs:292`). `retroppc` is not one of the
CLI's hardcoded platform ids (`ds`, `ps2`, `n64`, `windows`), so it falls into
`CreateCustomPlatformProfile` (`codegen/CodegenCliOptionsBuilder.cs:156-176`),
which throws unless the caller also passes `generated-math-convention` and a
positive `pointer-size-bytes` — exactly the brief's predicted failure mode.
The preset's own platform profile is never reached because the CLI-level
platform build throws first.

### Command actually used (brief's documented fallback)

```bash
./codegen/bin/Release/net9.0/codegen.exe --cpp \
  --project /c/dev/helworks/helengine/engine/helengine.core/helengine.core.csproj \
  --output /c/dev/helworks/builds/csharpcodegen/core-freestanding \
  --feature-catalog /c/dev/helworks/helengine/engine/helengine.editor/codegen/features/helengine-feature-catalog.json \
  --platform generic --language cpp --endianness big \
  --set pointer-size-bytes=2 --set generated-math-convention=native-column-vector \
  --preset native-core-boot-freestanding \
  --set include-project-defined-preprocessor-symbols=false \
  --set write-conversion-report=true
```

(The `--set pointer-size-bytes=2 --set generated-math-convention=...` pair
only has to satisfy the CLI's initial, throwaway platform build; once the
`--preset` is applied, `CPPConversionPresetCatalog.ApplyTo`
(`cs2.cpp/CPPConversionPresetCatalog.cs:22-23`) unconditionally replaces
`options.PlatformProfile` with the preset's own `retroppc` / 4-byte-pointer /
`NativeColumnVector` profile, so the CLI-level values chosen here do not leak
into the actual conversion. This was verified by reading
`CPPConversionPresetCatalog.ApplyTo` rather than assumed.)

Output (tail):

```
-- Processing: helengine.nativeownership.GlobalUsings.g.cs
Codegen failed: CPPOWN001 C:\dev\helworks\helengine\engine\helengine.core\assets\PackagedAssetBinarySerializer.cs(703,9): Return ownership for method 'ReadSceneOverrideScopeSteps' with return type 'helengine.SceneOverrideScopeStepAsset[]', type kind 'Array', special type 'None', and reference flag 'True' cannot be inferred because a non-null boundary is unclassified.
```

No `.cpp`, `.hpp`, unity file, conversion report, or `helcpp_config.hpp` was
written to the output directory (verified: `find
/c/dev/helworks/builds/csharpcodegen/core-freestanding -type f` returns
nothing). This is expected once the failure mode is understood — see
"Why generation fails" below.

### Cross-check: is this specific to the freestanding preset?

No. The same project fails identically under the existing, already-shipping
`native-core-boot` preset (not `-freestanding`), using the same fallback
platform flags:

```
Codegen failed: CPPOWN001 C:\dev\helworks\helengine\engine\helengine.core\assets\PackagedAssetBinarySerializer.cs(703,9): Return ownership for method 'ReadSceneOverrideScopeSteps' with return type 'helengine.SceneOverrideScopeStepAsset[]', type kind 'Array', special type 'None', and reference flag 'True' cannot be inferred because a non-null boundary is unclassified.
```

So generating `helengine.core` with this codegen commit is broken today
independent of the freestanding runtime work; it is not a regression
introduced by Tasks 1-8.

## Why generation fails (root-cause evidence, not fixed)

The offending method, `assets/PackagedAssetBinarySerializer.cs:703`:

```csharp
static SceneOverrideScopeStepAsset[] ReadSceneOverrideScopeSteps(EngineBinaryReader reader) {
    return reader.ReadArray(ReadSceneOverrideScopeStep) ?? Array.Empty<SceneOverrideScopeStepAsset>();
}
```

Both halves of the `??` are individually classified by the ownership
analyzer:
- `EngineBinaryReader.ReadArray<T>` carries `[NativeOwnedReturn]`
  (`helengine.core/serialization/EngineBinaryReader.cs:256-258`) → `Owned`.
- `System.Array.Empty<T>()` is a recognized framework intrinsic classified as
  `Borrowed` (`cs2.cpp/ownership/CPPIntrinsicOwnershipCatalog.cs:21-24`).

The bug is in how the resolver walks the return expression to collect this
evidence. `CPPMethodOwnershipSummaryResolver.CollectReturnEvidence`
(`cs2.cpp/ownership/CPPMethodOwnershipSummaryResolver.cs:272-328`) explicitly
recurses into `ParenthesizedExpressionSyntax`, `CastExpressionSyntax`,
`ConditionalExpressionSyntax` (`?:`), and `SwitchExpressionSyntax` to inspect
each branch separately — but it has **no case for
`BinaryExpressionSyntax` with the `??` (null-coalescing) operator**. A `??`
return expression therefore falls through to the generic path at line 302
(`semanticModel.GetOperation(expression)`), which resolves the whole
expression to a single `ICoalesceOperation`. `ExpressionClassifier.Classify`
does not recognize that operation kind, and because it is not an
`IInvocationOperation` either, the fallback at line 325-327 fires:
`unknownCount++` — one single "unknown" vote, not the "owned vs. borrowed"
mix that `??` actually represents. That single unknown vote plus
`hasNonNullReturn = true` (an `Array.Empty<T>()` fallback is never null) is
exactly the precondition for `CPPOWN001`
(`cs2.cpp/ownership/CPPMethodOwnershipSummaryResolver.cs:667-674`).

**This looks like a real ownership-analyzer gap** (missing `??` decomposition
in `CollectReturnEvidence`), not a bug in `helengine.core`'s authored code —
the pattern `reader.ReadArray(...) ?? Array.Empty<T>()` is an ordinary,
already-annotated-at-the-boundary idiom. Per this task's scope, it was not
fixed; only diagnosed with file:line evidence for whoever picks up
sub-project 2 or a follow-up codegen fix.

Because the ownership-analysis stage
(`cs2.cpp/CPPCodeConverter.cs:230`, `CPPOwnershipAnalysisStage`) runs before
`ClassProcessingStage`/`ProgramSortingStage` (the stages that lower to C++)
and long before the restriction-validation check
(`cs2.cpp/CPPCodeConverter.cs:257`) or conversion-report writing, the failure
is a hard, whole-pipeline abort: zero output files, zero restriction
diagnostics, zero conversion report, regardless of preset.

## Step 2: Compile with the SNES toolchain — not reached

There is no `generated_unity.cpp` (or `helengine_core_unity.cpp`) and no
generated `.cpp`/`.hpp` files to compile: Step 1 produced none. Per the
brief's "if it does not compile, also try compiling a few individual small
generated files" fallback: there are no generated files of any size to try —
generation aborted before writing anything, not after writing some files that
then failed to compile. Running the Docker `mos-snes-far-clang++` step would
have nothing to point at, so it was not run. The `helengine-snes-toolchain`
image is confirmed present locally
(`docker image inspect helengine-snes-toolchain` →
`sha256:4ae0f74c06a883795dcb35280ff9a99e5d681f56c870cefd7f62de2bab7c6504`).

## Additional measurements requested for this report

- **Generated file count / bytes**: 0 files, 0 bytes (`find
  .../core-freestanding -type f` empty after both the `retroppc` and the
  fallback-platform attempts, and after cross-checking with the unmodified
  `native-core-boot` preset).
- **`llvm-size` / second `-Os` compile attempt**: not applicable, no object
  file was ever produced.
- **Individual small-file compile attempts**: not applicable, no source files
  exist.
- **`HE_CPP_*` defines in the generated `helcpp_config.hpp`**: this run
  produced no `helcpp_config.hpp` (nothing was generated). For reference,
  the freestanding runtime profile's config header is generation-input
  independent (fixed set of defines keyed off the runtime/restriction
  profile, not the source project), and the checked-in Task 8 fixture at
  `tests/runtime-capabilities-integration/freestanding/helcpp_config.hpp`
  shows what the freestanding preset emits by default:

  ```c
  #define HE_CPP_RUNTIME_FREESTANDING 1
  #define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0
  #define HE_CPP_USE_STD_STRING 0
  #define HE_CPP_USE_STD_VECTOR 0
  #define HE_CPP_USE_STD_UNORDERED_MAP 0
  #define HE_CPP_USE_STD_UNORDERED_SET 0
  #define HE_CPP_USE_STD_FUNCTION 0
  #define HE_CPP_USE_STD_SHARED_PTR 0
  #define HE_CPP_USE_STD_CHRONO 0
  #define HE_CPP_USE_STD_MATH 0
  #define HE_CPP_USE_HOSTED_FILE_SYSTEM 0
  #define HE_CPP_USE_EXCEPTIONS 0
  #define HE_CPP_USE_RTTI 0
  #define HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES 1
  #define HE_CPP_RUNTIME_PROVIDER_HEADER "runtime/freestanding/freestanding_provider.hpp"
  #define HE_CPP_RUNTIME_MATH_HEADER "runtime/freestanding/freestanding_math.hpp"
  ```

  This is quoted from the repository fixture, not produced by this run; it is
  included only so the "what sub-project 2 must solve" section below has a
  concrete baseline to work from once Step 1 is unblocked.
- **Restriction diagnostics**: none emitted. The ownership-analysis stage
  aborts the pipeline before the restriction-validation stage
  (`cs2.cpp/CPPCodeConverter.cs:257`) ever runs, so `ForbidHostedServices`
  and the other `native-core-boot-freestanding` restrictions were never
  evaluated against `helengine.core`'s authored code in this run.
- **Conversion report file**: none produced (`write-conversion-report=true`
  never reaches the report-writing stage because the pipeline aborts first).

## Error categories (Step 2 grouping requested by the brief)

Not applicable — Step 2 never ran. For completeness, the one error actually
observed (Step 1's `CPPOWN001`) does not fit any of the brief's four Step-2
buckets (provider/runtime gaps, pointer-size/address-space issues, compiler
limitations, authored-code hosted services); it is a fifth, earlier-stage
category: **ownership-analyzer expression-coverage gap**, unrelated to the
freestanding runtime, the 65816 target, or hosted services.

## What sub-project 2 (far-memory / banked-code emission) must solve

1. **Prerequisite, not sub-project 2's own scope**: `helengine.core` cannot
   be generated at all today with this `csharpcodegen` commit, under any
   preset. Before any SNES-specific measurement can be taken, someone needs
   to either (a) fix `CPPMethodOwnershipSummaryResolver.CollectReturnEvidence`
   to decompose `BinaryExpressionSyntax` `??` expressions the same way it
   already decomposes `?:` and `switch` expressions, or (b) add an explicit
   ownership annotation/workaround in `helengine.core` at
   `PackagedAssetBinarySerializer.ReadSceneOverrideScopeSteps` (and any other
   method with the same `X ?? Array.Empty<T>()` / `X ?? <borrowed-intrinsic>`
   shape — this file has at least one more similar pattern at line 692 that
   happens not to trip the check only because it is nested inside an object
   initializer rather than being a method's direct return expression).
2. Once generation succeeds, re-run this exact measurement (Steps 1-2 of this
   report's brief) to get the real 65816 compile numbers: error count, the
   four Step-2 categories, and object size if it links.
3. The freestanding runtime's own defaults (`HE_CPP_RUNTIME_FREESTANDING`,
   `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES=0`, the codegen-owned provider/math
   headers) are already proven independently by the Task 3-8 host and
   cross-compile tests (`tests/runtime-capabilities-integration/`); what is
   still unverified is whether `helengine.core`'s authored code — which is
   much larger and less curated than the runtime's own fixtures — reaches
   any hosted-service or pointer-size assumption that
   `ForbidHostedServices`/the 16-bit-`size_t` freestanding containers cannot
   satisfy. That question is exactly what this report was meant to answer
   and could not, because of the Step 1 blocker above.

## Deviations from the brief

- `--platform retroppc` was rejected as the brief predicted; the documented
  fallback (`--platform generic --set pointer-size-bytes=2 --set
  generated-math-convention=native-column-vector`) was used instead, and
  verified by reading `CodegenCliOptionsBuilder.cs` and
  `CPPConversionPresetCatalog.cs` (not just observed from the error message)
  that the fallback's platform-shape values are discarded once
  `--preset native-core-boot-freestanding` applies its own `PlatformProfile`.
- Step 2 (SNES cross-compile) and all of the additional measurements that
  depend on generated output (unity object size, `llvm-size`, individual
  small-file compiles, conversion-report counts) were not performed because
  Step 1 produced no output under any preset tried, including the
  already-shipping `native-core-boot` preset. This is recorded as a finding,
  per the task's instruction to record blocking runtime/emitter bugs rather
  than fix them.
- No source, generated, or runtime code was modified while investigating
  this. The root-cause section above is analysis of already-committed code,
  offered as evidence for a follow-up task.
