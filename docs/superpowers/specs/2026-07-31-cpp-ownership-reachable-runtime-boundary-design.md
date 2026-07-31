# C++ Ownership Reachable Runtime Boundary Design

**Status:** Approved design

**Date:** 2026-07-31

## Context

The C++ ownership analyzer currently examines every method in the root C# project and its transitive project-reference closure before native reachability is resolved. This causes hard ownership diagnostics in editor-only and compiler-only code even when those types will never be emitted into a native runtime build.

That behavior is incorrect. Ownership analysis exists to make emitted native C++ safe. Code excluded from the final native type inventory cannot create a native leak or invalid native lifetime and must not require native ownership annotations.

The current ordering also encourages unnecessary source attributes. Ordinary managed code should not be decorated merely because an unreachable method happens to be present in a referenced C# assembly.

## Decision

Ownership analysis will run only for methods belonging to the final reachable native type set.

Native reachability will be resolved before local ownership validation. The ownership summary resolver may inspect contracts on methods called by reachable code, but it will not validate unrelated method bodies or report diagnostics for types outside the emitted runtime inventory.

Editor shader compilation types, tooling services, importers, and other pruned code will therefore remain outside ownership validation unless a native runtime entry point actually makes them reachable.

## Pipeline

The conversion pipeline becomes:

```text
Roslyn compilation and preprocessing
  -> conversion class discovery
  -> feature resolution
  -> final native type reachability
  -> ownership summaries required by reachable methods
  -> ownership control-flow validation for reachable methods
  -> C++ lowering and emission
  -> generated ownership validation
  -> native compilation
```

Reachability must be calculated once and shared by ownership analysis and emission. The analyzer and emitter must not independently derive different native inventories.

## Ownership inference

The existing inference rules remain the default:

- fresh objects, arrays, and materialized collections are owned;
- fields, properties, parameters, caches, singletons, and shared empty values are borrowed;
- local cleanup is emitted automatically when ownership remains local;
- explicit attributes are reserved for genuine API-boundary behavior that cannot be inferred;
- ordinary editor or compiler code requires no native ownership metadata when it is not emitted.

This change does not weaken hard errors for reachable native code. Ambiguous ownership that can reach emitted C++ remains a build failure.

## Components

`CPPReachabilityPlanner` remains the authority for the emitted native type inventory. Its result will be made available before ownership validation.

`CPPOwnershipAnalysisStage` will receive the reachable type set and restrict method-body analysis to that set.

`CPPMethodOwnershipSummaryResolver` will resolve summaries only for reachable methods and the directly or transitively called methods needed to classify them. Unrelated source methods will not generate ownership diagnostics.

`CPPLocalOwnershipAnalyzer` will validate control flow only for reachable method bodies.

`CPPCodeConverter` will reuse the same reachability plan during emission, preventing validation/emission drift.

## Rollback of incorrect compiler changes

Ownership edits added solely in response to diagnostics from unreachable shader compiler code will be reverted. This includes compile-service, compile-cache, and compile-request annotations or tests that do not represent real native runtime boundaries.

Runtime shader fixes discovered independently remain in scope and will be retained when they represent real emitted ownership, such as owned runtime containers, borrowed runtime texture lookups, and independent bytecode storage.

## Error handling

If reachability cannot be resolved, conversion fails before ownership analysis with a source-oriented reachability diagnostic.

If a reachable method calls a method whose ownership summary is required but cannot be classified, the existing hard ownership diagnostic remains.

No warning mode, platform waiver, legacy path, or generated-source post-processing is introduced.

## Verification

Focused tests will prove:

- an unreachable method containing an ownership error produces no diagnostic and no generated source;
- the same method produces the expected hard diagnostic when made reachable;
- a reachable method calling a helper receives the helper's inferred ownership summary;
- unrelated methods in the same project and transitive project references are not analyzed;
- the ownership analyzer and emitter consume the same reachability plan;
- existing reachable ownership regression tests continue to pass.

Repository validation will then:

1. convert the Windows runtime without ownership diagnostics from editor shader compilation;
2. compile the exact generated Windows artifact;
3. launch that exact artifact and verify it remains running through splash, loading, and menu startup;
4. only after Windows passes, repeat the native build validation for PS2.

## Acceptance criteria

- Ownership diagnostics are emitted only for methods that can contribute code to the native artifact.
- Editor/compiler code absent from native output requires no ownership annotations.
- Reachable ownership errors remain hard failures.
- Validation and emission use one identical reachability plan.
- Compiler-only ownership changes introduced during diagnosis are removed.
- Focused codegen tests pass.
- The Windows native DemoDisc builds and launches from the exact newly generated artifact before PS2 validation resumes.
