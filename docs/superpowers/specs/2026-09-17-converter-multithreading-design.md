# Converter speed: quadratic scans and real threads

## Approved intent

C++ conversion of large projects is too slow. Helena approved this design on
2026-09-17: first remove the quadratic work in class emission, then parallelize
the converter with real `System.Threading.Thread` workers, `lock`, and
`Interlocked`. No `Task`, no `async`, no `Parallel.For`. Generated output must
be byte-identical at any worker count. The scope is the C++ backend; the
TypeScript and Go backends only share cs2.core stages.

## Measurements that drove the design

A throwaway harness wrapped every pipeline stage in a stopwatch and converted
synthetic projects of 50, 200 and 800 classes modeled on the runtime
capabilities fixture.

| Classes | Document preprocessing | Ownership analysis | WriteOutput | Total |
|---|---|---|---|---|
| 50 | 2.1 s | 0.5 s | 1.8 s | 8.1 s |
| 200 | 2.7 s | 2.0 s | 5.7 s | 13.4 s |
| 800 | 4.8 s | 3.1 s | 48.3 s | 59.5 s |

All real lowering happens inside `WriteOutput` through `CPPClassEmitter.Emit`;
`ClassProcessingStage` takes milliseconds. `WriteOutput` is quadratic: 13 of 14
main-thread stack samples during the 800-class run were inside two per-name
linear scans over every class in the program:

- `CPPConversiorProcessor.QualifyRenderedCppTypeName` loops over all classes for
  every rendered type name, calling Roslyn `ToDisplayString` and a freshly built
  `Regex.Replace` per class.
- `CPPClassEmitter.TryResolveGeneratedClass` runs two `FirstOrDefault` scans over
  all classes per referenced type, calling `GetEmittedTypeName` per candidate.

Fixed startup cost is about 5 s: a doxygen run over the runtime headers in the
converter constructor and the MSBuild workspace load, which are independent.

Shared mutable state is almost entirely instance-level: the runtime requirement
registrar (with one `currentTypeScope` field), the conversion report, the
profiling manifest, the lazy lookup caches on `ConversionProgram`, the
processor's `temporaryNameCounter`, and `SynchronizeRunState`, which rewrites two
lists on every one of the 186 `RegisterRuntimeRequirement` call sites. There are
no static mutable fields. Each function body gets its own `LayerContext`.
Emission mutates the class model at four sites (`ReferencedClasses.Add`,
`SourceIncludes.Add`); three target the class being emitted, one at
`CPPConversiorProcessor.cs:17101` targets the top of the class stack.

## Design

### Commit 1: remove the quadratic scans (no threads)

Build a `CPPEmittedTypeNameIndex` once at the start of `writeClasses`. It maps
each emitted type name to the first matching generated class in `Classes` order
(preserving `FirstOrDefault` semantics) and exposes the name set. Store it on
`CPPProgram`. Before emission begins the index is null and the existing paths
run unchanged, so pre-emission behavior is untouched.

- `TryResolveGeneratedClass` uses the index for both lookups.
- `QualifyRenderedCppTypeName` scans the rendered name once, left to right, over
  maximal identifier runs (letters, digits, underscore, matching `\b` and `\w`).
  A run that matches an emitted name and is not preceded by `:` gets `::`
  inserted before it. This is exactly what the per-class regex did.
- `RegisterRuntimeRequirement` calls `SynchronizeRunState` only when the
  registrar actually added a new requirement. `TrackEmittedFile` uses a set.

This commit must produce byte-identical output to today for the test suite, the
integration fixture and the synthetic projects.

### Commit 2: overlap startup work on threads

The converter constructor starts the native runtime metadata load
(`CPPProgram.AddDotNet`, which runs doxygen) on a named thread when
`LoadNativeRuntimeMetadata` is set. `CodeConverter.AddCsproj` gains a virtual
hook invoked after the workspace opens and before the pipeline executes; the C++
converter overrides it to join the thread and rethrow any captured exception.
Nothing touches `Program.Classes` between construction and that join.

`WriteOutput` runs the runtime template copy on a thread while classes are
lowered in memory. Generated files are written only after that thread joins, so
a generated file still overwrites a same-named runtime file as it does today.

### Commit 3: parallel class emission

`ConversionWorkerPool` in cs2.core owns N background threads named
`cs2-worker-N`. `Run(workerCount, itemCount, body)` hands out item indices
through an `Interlocked` cursor, joins every thread, and rethrows the first
captured exception by lowest item index. A worker exception stops the pool from
handing out further items.

The processor's dependency on `CPPCodeConverter` is replaced by an
`ICPPConversionHost` interface exposing `Options`, `CPPRules`, `Program`,
`OwnershipAnalysisResult`, `RuntimeRequirementRegistrar`, `Report`,
`RegisterRuntimeRequirement`, `ReportUnsupportedConstruct`,
`ReportRuntimeCapabilityViolation` and `GetInstantiatedGeneratedTypes`.
`CPPCodeConverter` implements it for the main thread. `CPPEmissionWorker`
implements it per worker with its own `CPPConversiorProcessor`,
`CPPClassEmitter`, `CPPRuntimeRequirementRegistrar` (same catalog, same build
usage report), `CPPConversionReport` and `CPPGeneratedFunctionProfilingManifest`.
Read-only members delegate to the converter.

Each worker lowers one class into two string buffers and records a
`CPPClassEmissionResult`: file stem, header text, source text, registered
requirement names in registration order, diagnostics, profiling scopes. After
the pool joins, the main thread walks results in reachability order: registers
requirements on the converter registrar, runs `SynchronizeRunState` once,
appends diagnostics and profiling scopes, writes both files, tracks them and
increments `EmittedTypeCount`.

Before workers start, the main thread sorts members of every reachable class
and warms the lazy lookup caches on `ConversionProgram` and the emitted-name
index, so no worker triggers a rebuild while another reads. The processor's
temporary-name counter resets at the start of each class so temporary names
depend only on the class; Helena accepted that generated temporary names change
once. Reference registration at `CPPConversiorProcessor.cs:17101` targets the
owning emission class like the other three sites, so a worker only mutates its
own class. Any golden-output difference this causes is shown to Helena before
proceeding.

The worker count comes from `--set codegen-worker-threads=N` through
`PlatformOptionValues` and a `CPPWorkerThreadOptionResolver`. Absent means
`Environment.ProcessorCount`, `1` runs the same code path with one worker, and a
non-positive or non-numeric value throws.

### Commit 4: gated parallelism for the front half

Ownership analysis: `CPPLocalOwnershipAnalyzer` runs per-syntax-tree local
analysis on the pool with per-tree aggregates (local plans, transitions,
diagnostics) merged in tree order. The fixed-point summary resolver stays
sequential because its in-place updates are order-sensitive.

Document preprocessing: a parallel warm-up stage resolves each document's
syntax tree, root and semantic model on the pool and binds declared symbols,
then hands those exact instances to the existing sequential
`DocumentPreprocessingStage` walk, which keeps `ConversionContext` single
threaded.

Each half is kept only if the harness shows at least a 20 percent drop in that
stage at 800 classes; otherwise it is reverted before merge.

## Scope and validation

Unit tests: the emitted-name index and the tokenizer compared against the
previous regex implementation on `List<Foo>`, `::Foo`, `Foo_1<Bar>`, `NotFoo`,
`const Foo&`, `Foo::Nested` and nested generics; the worker pool processes every
item exactly once, propagates exceptions, and stops on failure; the option
resolver rejects invalid values.

Determinism test: convert one fixture with cross-class references, throw
expressions and temporaries at 1 and 4 workers, twice at 4, and compare every
generated file and the report.

The existing cs2.cpp test suite (835 facts and theories) passes; the ten
assertions pinning `_0000000N` temporary names are updated once for the
per-class reset. The integration fixture still converts. Harness timings at 200
and 800 classes are recorded after each commit in the implementation plan's
validation notes.

Non-goals: caching doxygen output across runs, the TypeScript and Go backends,
and lowering `lock` statements in generated C++.

## Alternatives considered

Threads without the algorithmic fix would divide quadratic work by the core
count and make the registrar lock the new hot spot. The algorithmic fix alone
leaves 10 to 15 s of linear work single threaded and does not deliver the
requested threading. Parallelizing the preprocessor walk itself would require
making the 80 KB `ConversionPreProcessor` and `ConversionContext` thread-safe;
warming Roslyn binding on threads captures most of that cost without touching
them.
