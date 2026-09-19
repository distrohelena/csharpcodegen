# Freestanding engine core: 65816 compile measurement

Date: 2026-09-19
Status: generation succeeded; SNES cross-compile of the unity file fails on 7 provider/runtime
gaps with RTTI enabled (as required by the generator), and on 129 errors (122 of them purely
from missing RTTI) if RTTI is disabled. No source, generated, or runtime code beyond the two
engine ownership fixes below was modified.

## Summary

`helengine.core` now converts cleanly under `native-core-boot-freestanding` once (a) two small
ownership-consistency fixes are applied to engine source and (b) the generator is told to keep
compiler RTTI available (`--set codegen-use-rtti=true`), matching what the PS1 platform
definition already does for the same reason. With RTTI enabled, the unity file (348 `.cpp` /
443 `.hpp`, 2,226,382 bytes total) fails to compile on the 65816 toolchain with 7 errors, all
missing provider/runtime methods — no pointer-size, address-space, RTTI, or hosted-service
errors remain. Disabling RTTI again (`-fno-rtti`) reproduces 122 additional `dynamic_cast`/
`typeid` errors on top of the same 7, which is the measured cost of turning RTTI off for this
codebase.

## Engine fixes made (file:line, before/after)

1. `engine/helengine.core/assets/PackagedAssetBinarySerializer.cs:704` (already applied before
   this pass): `return reader.ReadArray(ReadSceneOverrideScopeStep) ?? Array.Empty<SceneOverrideScopeStepAsset>();`
   -> `return reader.ReadArray(ReadSceneOverrideScopeStep) ?? new SceneOverrideScopeStepAsset[0];`
2. `engine/helengine.core/assets/raw/scene/SceneOverrideScopePath.cs:51` (`Normalize`):
   `return Common();` -> `return new SceneOverrideScopeStepAsset[0];`

Both fixes replace a `Borrowed` (`Array.Empty<T>()` / a method that returns it) branch with a
fresh `Owned` array literal so the method's non-null return values are ownership-consistent,
per the code generator's ownership analyzer (`CPPOWN001`/`CPPOWN005`). `Common()` itself was
left unchanged; its 4 remaining call sites (all in test projects) only consume its Borrowed
return value and never mix it with an Owned one.

Committed to `helengine` `main` as `b58109ef` ("core: return owned arrays from the override
scope path helpers so the core converts again").

## Exact generation command

```bash
cd /c/dev/helworks/csharpcodegen && rm -rf /c/dev/helworks/builds/csharpcodegen/core-freestanding && mkdir -p /c/dev/helworks/builds/csharpcodegen/core-freestanding && ./codegen/bin/Release/net9.0/codegen.exe --cpp --project /c/dev/helworks/helengine/engine/helengine.core/helengine.core.csproj --output /c/dev/helworks/builds/csharpcodegen/core-freestanding --feature-catalog /c/dev/helworks/helengine/engine/helengine.editor/codegen/features/helengine-feature-catalog.json --platform generic --language cpp --endianness big --set pointer-size-bytes=2 --set generated-math-convention=native-column-vector --preset native-core-boot-freestanding --set include-project-defined-preprocessor-symbols=false --set write-conversion-report=true --set codegen-use-rtti=true
```

Exit code: 0. Final log line: `C++ conversion completed.`

## Generation diagnostics

- No `CPPOWN*` diagnostics (0 occurrences in the log).
- No restriction diagnostics of any kind (no `ForbidHostedServices`, no `CPP1001`, nothing
  matching `restriction`/`hosted`/`forbid` besides the profile name string itself).
- Two pre-existing, engine-unrelated warnings from the generator's own runtime header,
  unchanged from prior runs:
  ```
  C:/dev/helworks/csharpcodegen/codegen/bin/Release/net9.0/.net.cpp/runtime/native_algorithm.hpp:146: warning: Detected potential recursive class relation between class he_cpp_alg::detail::MakeIndexSequenceImpl and base class he_cpp_alg::detail::MakeIndexSequenceImpl< N - 1, N - 1, Indexes... >!
  C:/dev/helworks/csharpcodegen/codegen/bin/Release/net9.0/.net.cpp/runtime/native_algorithm.hpp:146: warning: Detected potential recursive class relation between class he_cpp_alg::detail::MakeIndexSequenceImpl and base class MakeIndexSequenceImpl< N - 1, N - 1, Indexes... >!
  ```
- `cpp-conversion-report.json` (`write-conversion-report=true`): `"hasErrors": false`,
  `"errorCount": 0`, `"warningCount": 0`, `"infoCount": 0`, `"unsupportedConstructCount": 0`,
  `"unsupportedMemberCount": 0`, `"unsupportedSyntaxSummary": []`, `"processedTypeCount": 0`,
  `"emittedTypeCount": 334`, `"emittedFileCount": 678`. `"diagnostics": []` and
  `"diagnosticsByTypeMember": []` are both empty. `activeProfiles`: compiler `gcc`, platform
  `retroppc-headless`, runtime `freestanding`, restrictions `native-core-boot-freestanding`.

## File and byte counts

- 348 `.cpp` files, 443 `.hpp` files, 797 files total under
  `C:\dev\helworks\builds\csharpcodegen\core-freestanding` (includes the `runtime/` and
  `system/` support subtrees the generator copies alongside the emitted types).
- Total size: 2,226,382 bytes (`du -sb`).
- Unity/compile-harness file: `generated_unity.cpp` (12.8 KB). Conversion report:
  `cpp-conversion-report.json` (66.9 KB).

## `HE_CPP_*` defines (`helcpp_config.hpp`)

```c
#define HE_CPP_GENERATED_CONFIG 1
#define HE_CPP_COMPILER_GCC 1
#define HE_CPP_PLATFORM_RETROPPC 1
#define HE_CPP_RUNTIME_FREESTANDING 1
#define HE_CPP_USE_STD_STRING 0
#define HE_CPP_USE_STD_VECTOR 0
#define HE_CPP_USE_STD_UNORDERED_MAP 0
#define HE_CPP_USE_STD_UNORDERED_SET 0
#define HE_CPP_USE_STD_FUNCTION 0
#define HE_CPP_USE_STD_CHRONO 0
#define HE_CPP_USE_STD_SHARED_PTR 0
#define HE_CPP_USE_STD_MATH 0
#define HE_CPP_USE_HOSTED_FILE_SYSTEM 0
#define HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES 0
#define HE_CPP_GENERATED_FUNCTION_PROFILING 0
#define HE_CPP_USE_EXCEPTIONS 0
#define HE_CPP_USE_RTTI 1
#define HE_CPP_PLATFORM_IS_LITTLE_ENDIAN 0
#define HE_CPP_PLATFORM_IS_WINDOWS_HOST 0
#define HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM 0
#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 0
#define HE_CPP_RUNTIME_PROVIDER_HEADER "runtime/freestanding/freestanding_provider.hpp"
#define HE_CPP_RUNTIME_MATH_HEADER "runtime/freestanding/freestanding_math.hpp"
#define HE_CPP_FEATURE_DEBUG_OVERLAY 0
#define HE_CPP_FEATURE_HOST_FILE_SYSTEM 0
#define HE_CPP_FEATURE_MANAGED_METADATA_ONLY 0
#define HE_CPP_FEATURE_PHYSICS3D_DIAGNOSTICS 0
#define HE_CPP_FEATURE_REFLECTION_LIKE_RUNTIME 0
#define HE_CPP_FEATURE_RENDER2D 1
#define HE_CPP_FEATURE_SHADERS 0
#define HE_CPP_FEATURE_SPRITES 1
#define HE_CPP_FEATURE_TEXT2D 1
#define HE_CPP_FEATURE_TEXT_PROCESSING 0
#define HE_CPP_REQ_BINARY_PRIMITIVES 1
#define HE_CPP_REQ_BIT_CONVERTER 1
#define HE_CPP_REQ_DEBUG 1
#define HE_CPP_REQ_ENCODING 1
#define HE_CPP_REQ_FILE_STREAM 1
#define HE_CPP_REQ_MATH 1
#define HE_CPP_REQ_MEMORY_STREAM 1
```

The only difference from the freestanding preset's previously-documented default is
`HE_CPP_USE_RTTI`, which is `1` here (was `0` in the Task 8 fixture) because of
`--set codegen-use-rtti=true`. `HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` is still `0`.

## RTTI

The generator needs compiler RTTI to select a concrete generated implementation whenever a
generic method is invoked through an abstract/interface-typed receiver with more than one
concrete implementation reachable — exactly the shape of
`EngineBinaryReader.ReadArray<T>(Func<EngineBinaryReader, T>)` (abstract, 258) called through
the abstract `EngineBinaryReader` parameter type from `PackagedAssetBinarySerializer`'s many
`Read*Asset` helpers (e.g. `ReadAnimationClipAsset`, `ReadAudioAsset`). Without RTTI the
generator cannot decide which of `BinaryReaderBE`/`BinaryReaderLE`'s monomorphized C++ method to
call and raises `CPP1001` ("Generic implementation dispatch requires RTTI to select a concrete
generated implementation"), aborting the whole pipeline. This is not new to the freestanding
core: the PS1 platform definition
(`C:\dev\helworks\helengine-ps1\builder\PlayStation1PlatformDefinitionFactory.cs:86`) already
sets `codegen-use-rtti` (`cs2.cpp/CPPCodegenOptionNames.cs:77`) to `true` for the same reason —
the shipping PS1 core boot already relies on RTTI for this pattern. This measurement follows
suit and sets `--set codegen-use-rtti=true` for the freestanding preset too.

On the 65816 toolchain, RTTI turned out to be affordable at the generation/compile level (it
only changes which `dynamic_cast`/`typeid` uses succeed, not pointer size or memory layout) but
was not free: compiling the same unity file with `-fno-rtti` instead produces 122 additional
errors (121 `dynamic_cast` + 1 `typeid`, each "requires -frtti"), on top of the same 7
provider/runtime-gap errors seen with RTTI enabled. In other words, on this codebase RTTI is a
functional requirement of the generated code (not just an optional space/time tradeoff) once
`codegen-use-rtti=true` is used — turning the compiler flag back off does not save any of the
122 call sites, it only breaks them, since the C++ they compile to directly uses
`dynamic_cast`/`typeid`.

## Step 2: SNES compile

Command run (RTTI enabled, matching the generation setting):

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/builds/csharpcodegen/core-freestanding helengine-snes-toolchain bash -c '/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ -std=c++20 -fno-exceptions -Os -ferror-limit=0 -I. -c generated_unity.cpp -o core-freestanding-rtti.o'
```

Result: `cc exit=1`, 7 errors, no object file produced. Error messages (verbatim, paths
stripped to basenames), grouped:

**(a) Provider or runtime gaps — all 7 errors**
```
4  path.cpp: no member named 'find_last_of' in 'he_cpp_freestanding::FreestandingString'
2  SceneOverrideScopePath.cpp: no member named 'get_Length' in 'StringBuilder'
1  FontAssetBinarySerializer.cpp: no matching constructor for initialization of 'Dictionary<char, ::FontChar>'
```
No errors fell into categories (b) pointer size/address-space, (c) compiler limitations,
(d) authored-code hosted services, or (e) other.

Second attempt, `-fno-rtti` added (measuring RTTI's cost, per instruction):

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/dev/helworks:/hw" -w /hw/builds/csharpcodegen/core-freestanding helengine-snes-toolchain bash -c '/usr/lib/llvm-mos-65816/bin/mos-snes-far-clang++ -std=c++20 -fno-exceptions -fno-rtti -Os -ferror-limit=0 -I. -c generated_unity.cpp -o core-freestanding-nortti.o'
```

Result: `cc exit=1`, 129 errors:
```
121  use of dynamic_cast requires -frtti
  4  path.cpp: no member named 'find_last_of' in 'he_cpp_freestanding::FreestandingString'
  2  SceneOverrideScopePath.cpp: no member named 'get_Length' in 'StringBuilder'
  1  use of typeid requires -frtti
  1  FontAssetBinarySerializer.cpp: no matching constructor for initialization of 'Dictionary<char, ::FontChar>'
```
The 122 `dynamic_cast`/`typeid` errors are category (c) compiler-limitation-shaped (the compiler
correctly refuses the construct given the flag), but they are a direct, expected consequence of
disabling RTTI on generator output that was produced assuming RTTI is available — not an
independent finding. Per the coordinator's ruling, this is a category (a)/(c) finding, not a
blocker: it confirms the generated code needs RTTI and quantifies the cost of not having it.

Neither attempt produced a linkable object file, so **no `llvm-size` for the unity file**. As a
partial substitute, the 4 smallest generated `.cpp` files by byte size
(`system/io/stream.cpp` 22 B, `LogLevel.cpp` 69 B, `LightType.cpp` 70 B, `RenderOrder2D.cpp`
74 B) were each compiled individually with RTTI enabled and the same flags (minus `-I.` unity
concerns): all 4 compiled with `cc exit=0` and 0 errors. `llvm-size` on these individual `.o`
files reported `error: '<file>.o': The file was not recognized as a valid object file` for all
4 — a toolchain quirk worth flagging separately (the file exists, non-empty, e.g. 4256 bytes for
`LightType.o`, but `llvm-size` from the same `llvm-mos-65816` toolchain does not parse it),
distinct from and not blocking the compile-error measurement above.

## Remaining hosted facilities

None identified as reached from `helengine.core`'s authored code in this pass:
`HE_CPP_RUNTIME_HAS_HOSTED_SERVICES` is `0`, no restriction diagnostics were emitted during
generation (the restriction-validation stage ran to completion with nothing to report), and no
compile error in either attempt names a hosted-service symbol. This does not prove the absence
of hosted-service reachability in code paths the unity/whole-project compile doesn't exercise
(e.g. behind `#ifdef`s the current feature-catalog selection disables) — only that none surfaced
in this measurement.

## What sub-project 2 must solve

1. Close the 7 provider/runtime gaps found here: `FreestandingString::find_last_of` (used 4
   times from `system/io/path.cpp`, itself generated from .NET's `Path` helpers),
   `StringBuilder::get_Length` (2 uses, from the now-converting
   `SceneOverrideScopePath.Format`), and a `Dictionary<char, FontChar>` constructor overload
   used by `FontAssetBinarySerializer.cpp:201`. These are runtime/provider-library gaps, not
   engine authoring errors — the C# source uses ordinary `.Length`/`.LastIndexOf`-shaped APIs
   that the freestanding C++ runtime substitutes only partially.
2. Decide whether `codegen-use-rtti=true` is the intended long-term default for the freestanding
   preset (as it already is for PS1) or whether `EngineBinaryReader`'s abstract-class/generic-
   method shape should instead be restructured to avoid needing RTTI at all — the latter would
   let a true `-fno-rtti` freestanding build exist, trading engine-code changes for toolchain
   footprint (RTTI's own code/data size cost on the 65816 was not separately measured here since
   no object file linked).
3. Re-run this exact measurement once the 7 provider gaps are closed to get real `llvm-size`
   numbers for the linked unity object — that is the next concrete milestone for sub-project 2's
   far-memory/banked-code footprint planning.

## Deviations from the brief

- Generation required `--set codegen-use-rtti=true` in addition to the brief's original command;
  without it, generation aborts on `CPP1001` in `ReadAnimationClipAsset` (see the RTTI section
  above). This was a coordinator ruling made mid-task, justified by the PS1 platform definition
  already relying on RTTI for the same generic-dispatch pattern.
- The SNES compile step used `-std=c++20 -fno-exceptions -Os -ferror-limit=0 -I.` without
  `-fno-rtti` for the primary measurement (RTTI is now required by the generated code); a second
  `-fno-rtti` attempt was run purely to quantify RTTI's cost, per instruction.
- No `llvm-size` for the unity object (it never compiled cleanly); individual small-file
  `llvm-size` also failed for unrelated toolchain reasons (see above). Both are recorded as
  findings.
