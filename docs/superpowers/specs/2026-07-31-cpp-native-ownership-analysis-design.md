# C++ Native Ownership Analysis Design

**Status:** Approved design

**Date:** 2026-07-31

## Context

The C#-to-C++ generator lowers managed reference values to native pointers. Native allocations therefore require an explicit lifetime decision that the C# source does not normally expose.

The generator already cleans up some direct, non-escaping local allocations with scope guards. That analysis recognizes direct object, array, and collection creation, plus a narrow array-to-native-list return pattern. It does not generally infer ownership through ordinary factory methods.

This gap causes allocations returned from methods such as `TextRenderEffectPassBuilder.Build(text)` to be treated as unowned. `RenderCommandListBuilder2D.EmitText` calls that method while rebuilding mixed 2D render command lists. The returned `List<TextRenderEffectPass>` receives no native cleanup, causing PS2 heap use to grow by roughly two megabytes per frame until `std::bad_alloc` terminates rendering.

Generated C++ must not be patched after emission. The correct fix is semantic ownership analysis inside code generation.

## Goals

- Infer whether each native pointer-producing expression is owned or borrowed.
- Infer ownership returned by source-visible methods from all reachable return paths.
- Track owned local values until cleanup, transfer, or a compile-time error.
- Automatically emit deterministic cleanup for owned values that remain local.
- Require explicit contracts at boundaries whose implementation cannot be analyzed.
- Reject ambiguous, contradictory, or unsafe ownership with hard codegen errors.
- Preserve existing explicit `NativeOwnership` operations and `NativeNoEscape` contracts.
- Keep ownership decisions platform-independent so every generated C++ target receives identical lifetime semantics.
- Diagnose ownership defects before generated C++ compilation.

## Non-goals

- This feature does not add garbage collection or reference counting to generated C++.
- It does not rewrite generated files or scan generated text to repair ownership.
- It does not infer ownership of objects stored inside collections in the first implementation. Container ownership and element ownership are separate.
- It does not silently preserve unknown ownership for compatibility.
- It does not provide a legacy or warning-only mode.
- It does not change normal managed C# runtime behavior.

## Ownership model

Every analyzed native-pointer value has one of three classifications:

- `Owned`: the current value is responsible for exactly one native cleanup unless ownership is transferred.
- `Borrowed`: the value may be used but must not be deleted by the current scope.
- `Unknown`: the analyzer cannot prove either contract. Reaching emission with this classification is a hard error.

An owned local additionally has a lifecycle state:

- `Live`: usable and still owned by the current scope.
- `Released`: explicitly destroyed; further use is invalid.
- `Transferred`: ownership moved to a verified owner; further use as an owned value is invalid.
- `ScopeCleanup`: ownership remains local and cleanup is emitted automatically.

The model applies only to types that the C++ backend lowers to native pointers requiring ownership. Value types and backend-special types with established non-owning semantics are excluded by the type emission policy.

## Ownership sources

The following expressions produce owned values:

- Object creation.
- Array creation.
- Collection expressions that allocate native storage.
- Clone, copy, or materialization operations registered as ownership-producing intrinsics.
- Calls to methods whose inferred or declared return contract is owned.

The following sources are borrowed unless an explicit ownership contract states otherwise:

- Parameters.
- Fields and properties.
- Cached and singleton values.
- `this`.
- Shared framework values such as `Array.Empty<T>()`.
- Calls to methods whose inferred or declared return contract is borrowed.

Framework and backend intrinsics have a centralized ownership catalog. This avoids requiring source annotations on known runtime operations while keeping their contracts reviewable and testable.

## Method return summaries

The generator computes a `CPPMethodOwnershipSummary` for every relevant method before ordinary C++ lowering.

A source-visible method is inferred as returning owned storage when every reachable non-null return path returns one of the following:

- A fresh owned expression.
- A local that still owns a fresh expression.
- A call to another owned-return method.

A method is inferred as returning a borrowed value when every reachable non-null return path returns borrowed storage.

Returning null does not change the ownership class of the method's non-null values.

A method that mixes owned and borrowed non-null returns is rejected. For example, returning a cached list on one branch and a new list on another branch is not representable by one safe call-site contract and must be redesigned.

Recursive and mutually dependent methods are resolved with a call-graph fixed-point pass. If the fixed point cannot classify a relevant return, the result remains `Unknown` and causes a hard error at the boundary that requires the classification.

## Boundary contracts

Methods without analyzable bodies require explicit ownership contracts when they produce or retain relevant pointer values. The contract vocabulary consists of:

- `NativeOwnedReturn`: the caller receives ownership of a returned native allocation.
- `NativeBorrowedReturn`: the caller receives a non-owning reference.
- Existing `NativeNoEscape`: the callee does not retain or transfer the annotated argument.
- `NativeTakesOwnership`: the callee assumes cleanup responsibility for the annotated argument.
- `NativeOwnedMember`: a field or property owns its assigned value and has verified cleanup.

Contract attributes are compile-time metadata and are not emitted as runtime C++ types.

For source-visible methods and members, explicit contracts are assertions rather than overrides. The analyzer validates them against inferred behavior and reports contradictions. They cannot be used to suppress a real ownership defect.

## Transfers and escapes

Ownership may leave a local only through a proven transfer:

- Returning it from a method with an owned-return summary.
- Passing it to a `NativeTakesOwnership` parameter.
- Assigning it to a `NativeOwnedMember` whose destruction or replacement cleanup is verified.
- Releasing it through a supported `NativeOwnership` operation.

The following are hard errors unless covered by a valid contract:

- Assigning an owned local into an ordinary field, property, static, or captured variable.
- Passing an owned value to a parameter that may escape but does not take ownership.
- Returning an owned value from a borrowed-return method.
- Deleting or releasing borrowed storage.
- Using a value after release or transfer.
- Replacing an owned value without first cleaning up or transferring the previous value.
- Capturing an owned local in a lambda, delegate, or local function whose lifetime cannot be proven.

Passing an owned value to a `NativeNoEscape` parameter preserves local ownership. Ordinary collection operations such as `list.Add(item)` do not transfer ownership of `item` in the first implementation.

## Control-flow analysis

`CPPLocalOwnershipAnalyzer` performs per-method flow analysis over Roslyn control flow rather than syntax-only scanning.

Each branch carries ownership state for relevant locals. Branches merge only when their states are compatible. A local that is live-owned on one path and transferred, released, borrowed, or unknown on another path causes a diagnostic unless subsequent control flow proves a safe uniform state.

Loops are evaluated conservatively to a stable state. The analyzer rejects ownership whose cleanup or transfer depends on an iteration count it cannot prove. Early returns are supported because scope cleanup is associated with the owned local rather than appended only to the lexical end of a method.

Reassignment of an owned local is valid only when the previous value is cleaned up or transferred before the new ownership begins. Existing structured reassignment cleanup remains supported and moves under this semantic plan.

## Components

The implementation is split into focused types, with one class or enum per file:

- `CPPOwnershipKind` defines `Owned`, `Borrowed`, and `Unknown`.
- `CPPMethodOwnershipSummary` records a method's return and parameter ownership contracts.
- `CPPMethodOwnershipSummaryResolver` computes and validates summaries across the call graph.
- `CPPLocalOwnershipAnalyzer` performs per-method ownership flow analysis.
- `CPPOwnershipEmissionPlan` records cleanup guards, disarm operations, and explicit transfers for the existing emitter.
- `CPPOwnershipDiagnosticFactory` creates consistent source-located hard errors.
- `CPPIntrinsicOwnershipCatalog` contains reviewed framework and backend ownership contracts.

The existing `CPPGeneratedOwnershipValidator` remains responsible for its narrow generated-output contracts. General ownership correctness belongs to semantic pre-emission analysis and must not become generated-text pattern matching.

## Pipeline

Ownership analysis runs after Roslyn compilation is available and before ordinary C++ lowering:

```text
Roslyn compilation
  -> intrinsic and declared ownership contracts
  -> method ownership summaries to fixed point
  -> per-method control-flow ownership analysis
  -> hard-error validation
  -> ordinary C++ lowering using ownership emission plans
  -> generated C++ compilation
```

No C++ source is emitted for a method with unresolved ownership errors.

## C++ emission

An owned local that remains local receives one scope guard at its declaration. The guard tracks whether the scope still owns the current value:

```cpp
bool __owns_effectPasses = true;
auto __effectPassesDeleteGuard = he_cpp_make_scope_exit([&]() {
    if (__owns_effectPasses) {
        delete effectPasses;
    }
});
```

A valid ownership transfer disarms the guard immediately before or as part of the transfer:

```cpp
target->SetPasses(effectPasses);
__owns_effectPasses = false;
```

An explicit deletion disarms the guard after deletion. Reassignment cleans up the currently owned value before installing and owning the replacement.

The emission plan is produced from Roslyn symbols and control flow. The emitter does not rediscover ownership from formatted C++ text.

Container cleanup destroys the container only. Pointee or element cleanup remains governed by existing explicit element ownership rules until collection element ownership is designed separately.

## Diagnostics

Ownership failures are codegen errors with the C# source location, ownership origin, invalid sink or state transition, and a correction-oriented message.

- `CPPOWN001`: ownership cannot be inferred.
- `CPPOWN002`: owned value escapes without a valid transfer.
- `CPPOWN003`: borrowed value is deleted or released.
- `CPPOWN004`: value is used after release or transfer.
- `CPPOWN005`: method mixes owned and borrowed return values.
- `CPPOWN006`: declared ownership contradicts inferred behavior.
- `CPPOWN007`: ownership is transferred into a destination without verified cleanup.
- `CPPOWN008`: owned value is overwritten without cleanup or transfer.
- `CPPOWN009`: control-flow paths merge incompatible ownership states.

Diagnostics are hard errors on every C++ target. There is no platform-specific waiver, compatibility warning, or legacy behavior.

## Compatibility with existing ownership support

The existing direct-local cleanup behavior becomes a simple case handled by the semantic analyzer. Existing generated behavior remains valid for correctly owned locals.

Supported `NativeOwnership` helpers continue to express explicit destruction:

- `Delete`
- `DisposeAndDelete`
- `Release`
- `DisposeAndRelease`
- Existing array item release helpers

Existing `NativeNoEscape` behavior remains valid and participates directly in escape analysis.

The first rollout may reveal previously hidden leaks or ambiguous APIs. Those are build failures to correct, not legacy cases to preserve.

## Verification strategy

Unit and compile-validation fixtures cover:

- Direct object, array, and collection allocations.
- Owned factory returns.
- The `TextRenderEffectPassBuilder.Build(text)` regression.
- Multiple nested factories and fixed-point summary propagation.
- Recursive and mutually recursive summary resolution.
- Borrowed parameters, fields, properties, caches, and singletons.
- `Array.Empty<T>()` and other shared intrinsic values.
- Null combined with uniformly owned or borrowed non-null returns.
- Mixed owned and borrowed returns producing `CPPOWN005`.
- Unannotated external returns producing `CPPOWN001`.
- Correct and contradictory return annotations.
- `NativeNoEscape` arguments preserving ownership.
- `NativeTakesOwnership` arguments transferring ownership.
- Owned-member transfer with verified replacement and destruction cleanup.
- Illegal ordinary field and property escapes.
- Explicit `NativeOwnership` cleanup without duplicate scope deletion.
- Deleting borrowed storage.
- Use after release and use after transfer.
- Early returns, conditional transfers, loops, and incompatible branch merges.
- Reassignment cleanup.
- Lambda and delegate captures.
- Generated C++ compilation for representative owned, borrowed, and transferred paths.

Repository-level smoke validation converts the engine with ownership validation enabled and compiles the generated C++ output.

Runtime acceptance uses a PS2 full DemoDisc main-menu build. With the mixed 2D command list rebuilding continuously, heap usage must rise during initialization and then plateau. It must not continue growing by roughly two megabytes per frame, and the run must not terminate with `std::bad_alloc`.

## Acceptance criteria

The feature is complete when:

- Factory-returned native allocations receive correct caller cleanup or a proven transfer.
- Unknown ownership cannot reach C++ emission.
- Existing explicit ownership helpers produce no duplicate deletion.
- All ownership diagnostics include actionable source locations.
- Ownership regression and generated-compilation tests pass.
- The full engine conversion succeeds after all real ownership ambiguities are explicitly resolved.
- The PS2 main-menu heap remains stable during the runtime soak test.

