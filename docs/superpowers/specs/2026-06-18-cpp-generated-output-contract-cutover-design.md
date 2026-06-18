# C++ Generated Output Contract Cutover Design

## Goal

Make `csharpcodegen` the sole owner of final emitted C++ output shape so downstream consumers such as `helengine` can stage and compile generated output without rewriting file contents, pruning generated files, or synthesizing compatibility shims.

## Status

This design supersedes the downstream-repair direction implied by earlier `helengine` cleanup docs. The end-state is stricter:

- generated output must be correct when `csharpcodegen` finishes
- downstream repos may copy, merge, validate, and compile generated output
- downstream repos may not normalize, patch, delete, or repair generated output

## Current Problem

The active ownership boundary is still wrong.

`helengine` currently mutates generated output after codegen in at least two ways:

- authored gameplay module output is rewritten by `EditorPlatformCodeCookService.NormalizeGeneratedNativeModuleSources(...)`
- merged generated-core output is rewritten and reshaped by `EditorGeneratedCoreRegenerationService.NormalizeMergedGeneratedSourceCaseInsensitiveConflicts(...)` and `EnsureGeneratedIncludeCompatibilityShims(...)`

Those fixes currently handle symptoms such as:

- invalid emitted `float4` temporary and helper-call syntax
- merged-tree header short-name collisions
- generated include directives that only compile after downstream include rewriting
- synthetic compatibility headers added after generation

`csharpcodegen` also still owns a post-emission cleanup seam through `CPPGeneratedOutputAdapter` and `CPPGeneratedSourcePruner`. Even though that logic currently deletes files rather than rewriting contents, it still means the final runtime file inventory is not determined at emission time.

This violates the required rule:

- no downstream regex or string-replacement cleanup
- no downstream normalization pass
- no generator-owned "emit first, repair later" stage
- emitted output must be ready to use as-is

## Non-Goals

- No helengine-specific hacks inside `csharpcodegen`
- No platform-specific regex or string-replacement post-processing moved upstream
- No permanent compatibility layer that preserves downstream mutation behavior
- No phased migration that keeps both the old and new ownership models active
- No design that requires consumers to understand generated-file repair rules

## Recommended Approach

Use a single cutover where `csharpcodegen` becomes responsible for the final generated output contract and `helengine` deletes all generated-output mutation logic immediately after the upstream contract is in place.

The fix must be semantic rather than textual:

- emitter logic decides native expression shape before writing files
- artifact naming logic decides canonical file names before writing files
- ownership/filtering logic decides which files exist before writing files
- downstream consumers validate the contract, but never mutate it

If a generated file would need a later `Replace(...)`, the actual bug is in emission, naming, ownership, or dependency planning.

## Architecture

### 1. Semantic Native Lowering Ownership

`csharpcodegen` must emit valid native syntax directly from type and call-shape metadata.

This specifically covers the family of value-type and helper-call bugs currently repaired in `helengine`, including the `float4` orientation temporary rewrite path. The fix belongs in typed lowering logic inside `cs2.cpp`, not in any textual cleanup step.

Required properties of the final implementation:

- pointer-versus-value decisions come from shared type-shape metadata
- helper-call lowering selects the correct native owner/operator form before emission
- out/ref/value temporary storage is emitted in its final valid form
- no post-generation content pass changes generated `.cpp` text

### 2. Canonical Artifact Naming Ownership

`csharpcodegen` must assign final generated header/source names before files are written.

The current merged-tree collision repair in `helengine` proves that short-name ownership is still ambiguous after generation. That ambiguity must be resolved upstream by a generic naming contract that is collision-safe across merged generated trees.

Required properties:

- generated file names are canonical at first write
- include directives are emitted against canonical names
- canonical names remain stable and deterministic for a given source/program
- merged trees never require include rewriting
- merged trees never require synthetic compatibility headers

The naming contract must be generic. It cannot depend on helengine-specific type names.

### 3. Final Runtime Inventory Ownership

`csharpcodegen` must decide the final emitted runtime file set during generation.

The current `CPPGeneratedOutputAdapter` / `CPPGeneratedSourcePruner` seam means the runtime inventory is still "generated, then cleaned." That must become "selected, then emitted."

Required properties:

- editor-only or runtime-incompatible generated types are excluded by generator ownership rules
- unwanted support files are never emitted for the target output contract
- consumers do not delete generated files to make the result usable
- generated output ownership remains explicit and testable

If target-specific runtime filtering is necessary, it must be driven by explicit conversion options or typed feature metadata, not by downstream file deletion.

### 4. Output Contract Validation

`csharpcodegen` should publish enough structured metadata for downstream validation, but not for downstream mutation.

The existing conversion report and related artifacts should become an assertion surface:

- what files were intentionally emitted
- what naming contract was used
- what feature/runtime ownership rules affected output

Consumers may use that data to fail fast on contract violations. They may not use it to rewrite emitted output.

## Downstream Boundary After Cutover

After the cutover, `helengine` remains responsible for:

- invoking `csharpcodegen` with the correct options
- merging codegen outputs and authored native support trees
- staging files into build workspaces
- compiling and packaging outputs
- failing when upstream output contract assumptions are violated

After the cutover, `helengine` must not:

- rewrite generated source content
- rewrite generated include directives
- synthesize compatibility headers for generated output
- delete generated files as a cleanup step
- normalize generated file naming collisions after merge

## Affected Code Areas

### Upstream (`csharpcodegen`)

Expected primary design surface:

- `cs2.cpp/CPPCodeConverter.cs`
- `cs2.cpp/CPPGeneratedOutputAdapter.cs`
- `cs2.cpp/CPPGeneratedSourcePruner.cs`
- `cs2.cpp/CPPClassEmitter.cs`
- `cs2.cpp/CPPConversiorProcessor.cs`
- shared type-shape and artifact-naming helpers used by those emitters

This slice should prefer introducing or consolidating small focused services rather than expanding ad hoc conditionals in existing emitters.

### Downstream (`helengine`)

Expected deletion surface:

- `engine/helengine.editor/managers/project/EditorPlatformCodeCookService.cs`
- `engine/helengine.editor/managers/project/EditorGeneratedCoreRegenerationService.cs`
- tests that currently assert downstream generated-output mutation behavior

The downstream repo should end simpler, not replaced by another hidden repair stage.

## Cutover Plan Shape

This is a single cutover rather than a phased migration.

Sequence:

1. Add upstream tests that encode the final output contract.
2. Fix `csharpcodegen` until those tests pass without textual cleanup.
3. Delete downstream generated-output mutation logic in `helengine`.
4. Update downstream tests to assert orchestration and no-mutation boundaries.
5. Validate real generator consumers against the new contract.

There should be no long-lived dual path where both upstream semantic fixes and downstream repairs remain active.

## Testing Strategy

### 1. Upstream Generator Contract Tests

`csharpcodegen` must gain tests for:

- semantic value/out/ref lowering cases covering the current `float4` repair family
- canonical generated file naming under collision scenarios
- include emission against canonical names
- target-aware runtime inventory selection without post-emission pruning

These tests must assert final emitted files and contents, not internal implementation details.

### 2. Downstream No-Mutation Tests

`helengine` tests must shift from "repairs work" to "repairs do not exist."

The downstream suite should assert:

- no generated-output mutation helpers remain
- generation orchestration still succeeds
- merged generated-core output compiles/stages without downstream patching

Tests that currently prove rewriting behavior should be removed or inverted.

### 3. Real Consumer Validation

At minimum, validate the real build/export flows that motivated the current repairs:

- the authored gameplay module generation path that currently requires `NormalizeGeneratedNativeModuleSources(...)`
- generated-core regeneration/merge flows that currently require include rewriting and compatibility shims

Success is not just passing synthetic unit tests. The real consumer paths must work with no downstream mutation code present.

## Risks

### Risk: Naming Contract Breaks Existing Merge Assumptions

Canonical file names may differ from current emitted short names.

Mitigation:

- make naming deterministic and generic
- assert the new naming contract in upstream tests
- update only the consumers that truly depend on generated file names

### Risk: Hidden Generator Gaps Surface Immediately

Removing downstream repairs will expose any remaining emission bugs at once.

Mitigation:

- encode each known downstream repair as an upstream regression first
- do the cutover only after upstream contract tests pass

### Risk: Inventory Filtering Gets Reimplemented as Another Cleanup Stage

A naive implementation could keep `CPPGeneratedOutputAdapter` and simply relocate deletions.

Mitigation:

- require file presence/absence to be decided before write
- treat post-emission pruning as a failed design

## Success Criteria

This cutover is complete when all of the following are true:

- `csharpcodegen` emits valid final native syntax for the current downstream rewrite cases
- `csharpcodegen` emits canonical collision-safe generated artifact names directly
- `csharpcodegen` determines final runtime file inventory without post-emission pruning
- `helengine` contains no generated-output content rewrites
- `helengine` contains no generated-output pruning/deletion cleanup
- `helengine` contains no generated compatibility shim synthesis for codegen output
- real downstream generation/build flows succeed with the mutation code removed

## Decision

Proceed with a semantic upstream cutover in `csharpcodegen` and immediate downstream deletion in `helengine`.
