# Generic Type Remap Contract Design

## Goal

Prove and enforce that `csharpcodegen` applies caller-provided type remaps generically across emitted C++ type references, without any helengine-specific logic and without post-generation rewrites.

## Context

`csharpcodegen` already accepts generic type remaps through the CLI options surface and stores them in the shared conversion program:

- `codegen/CodegenCliOptionsBuilder.cs`
- `cs2.cpp/CPPCodeConverter.cs`

`csharpcodegen` also already has generic remap lookup logic in the emitted C++ type renderer:

- `cs2.cpp/model/CPPVariableType.cs`

Existing regression coverage proves one narrow slice of that behavior:

- remapped sequential-layout struct fields
- packed tail padding sizing over remapped value types

That coverage is insufficient for the broader contract required by platform builds. It does not prove that configured remaps flow through all emitted type-reference surfaces such as method parameters, return types, locals, or nested generic arguments.

## Problem Statement

We need a generator-level guarantee that when a caller provides a generic type remap such as:

- `System.Numerics.Vector2 -> Example.Float2`
- `System.Numerics.Vector3 -> Example.Float3`
- `System.Numerics.Vector4 -> Example.Float4`

the emitted C++ output uses the remapped target types consistently everywhere the source types appear.

If this guarantee already holds, no generator code should change.

If it does not hold, the fix must happen inside `csharpcodegen` at the type rendering or emission step. Generated output must not be patched later by regex rewrites, string replacements, compatibility shims, or platform-local post-processing.

## Non-Goals

- No helengine-specific code in `csharpcodegen`
- No platform-specific compatibility logic
- No generated-file rewriting
- No changes to project build configs in this pass

## Recommended Approach

### 1. Expand regression coverage first

Add generator-level regression tests that run conversion with configured type remaps and assert the emitted C++ uses remapped types across:

- field declarations
- method parameters
- method return types
- local variable declarations
- nested generic arguments

The test fixtures must use generic example type names rather than helengine names.

### 2. Use the tests to prove current behavior

Run only the new targeted remap regressions first.

- If they pass, stop. The generator contract is already satisfied.
- If they fail, use the failing case to identify the exact emitter path that bypasses the configured remap table.

### 3. Fix only the generic emission gap

If a failure is found, update the shared generator path so configured remaps are applied before final emitted type rendering for the failing surface.

The fix must remain generic:

- no `System.Numerics` special-casing
- no helengine naming
- no platform checks

### 4. Lock the repaired behavior with tests

Keep the failing regression and add any narrowly necessary companion coverage for adjacent emission paths touched by the fix.

## Candidate Root Cause To Verify

Current evidence suggests the configured remap pipeline may already be correct in `CPPVariableType.ToCPPString(...)`, while existing tests only cover a subset of emitted surfaces.

That means the likely outcomes are:

1. the generator is already correct and downstream build configuration is missing remap inputs, or
2. one emission path bypasses the shared rendered type path and needs a generic fix.

The tests should decide between those outcomes before any code change.

## Validation Plan

Run the smallest possible scope:

1. targeted `csharpcodegen` remap regression tests
2. if needed, the touched `csharpcodegen` test project

Do not validate by patching generated output or platform builders.

## Risks

- A test might accidentally prove only one declaration form while leaving another emitter path uncovered.
- A fix in a local emitter helper could miss other call sites that independently render type names.

The mitigation is to keep coverage centered on emitted output rather than implementation details.

## Decision

Proceed test-first. Change generator code only if the expanded generic remap regressions demonstrate that the current generator behavior is incomplete.
