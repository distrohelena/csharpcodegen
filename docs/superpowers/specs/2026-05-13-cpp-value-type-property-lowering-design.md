# C++ Value-Type Property Lowering Cleanup

## Goal

Fix the `cs2.cpp` backend so value types are lowered consistently across property emission, member access, and generated helper code. The immediate regression is the `AxisRotationComponent` export failure, but the scope is broader: the backend must stop treating struct-like runtime types as pointers when their native C++ representation is a direct value.

## Problem Statement

The current backend makes inconsistent pointer-versus-value decisions in multiple places:

- `CPPClassEmitter` can emit value-type properties as pointer-backed storage and pointer setter parameters.
- `CPPConversiorProcessor` can lower value-type member access using `->` instead of `.`.
- Static access chains can emit owner access as pointer-like syntax even when the source member is a static property or static value member.
- Generated serializer and deserializer helpers can call accessors with the wrong native shape because accessor signatures were emitted incorrectly.

This surfaced in the Windows export for the city `AxisRotationComponent`, where generated C++ included patterns such as:

- `float3* Axis`
- `if (this->Axis == float3->Zero)`
- `float3 *normalizedAxis = float3->Normalize(this->Axis);`
- `Core->Instance->DeltaTime`
- `Parent->LocalOrientation`
- generated setter calls passing `float3` into `set_Axis(float3*)`

These are backend bugs. They should be fixed in the transpiler rather than worked around in project code.

## Non-Goals

- No special-case `float3` or `float4` lowering rules.
- No project-side rewrites in `city` to avoid valid C# patterns.
- No “best effort” fallback that guesses pointer/value shape when the backend cannot determine it.
- No cross-backend cleanup for TypeScript or Go in this slice.

## Recommended Approach

Use one authoritative value-shape decision throughout `cs2.cpp`.

The backend already computes native type metadata during lowering. This slice will make that metadata the single source of truth for whether a converted type is emitted as:

- a direct value
- a pointer/reference-like native handle
- a static owner access chain

Every emission path that currently re-derives those semantics independently will instead consume the shared decision.

## Architecture

### 1. Centralize Native Type Shape

Introduce or consolidate a shared utility in `cs2.cpp` that answers these questions for any lowered type:

- Is the emitted native form a pointer?
- Should member access use `.` or `->`?
- Should property getter and setter signatures pass the type by value?
- Should static owner access use `Type::Member` style access?

This utility must operate from Roslyn type information and the backend’s existing `CPPVariableType` / type-conversion metadata. It must not depend on name-based special cases for engine math structs.

### 2. Fix Property Emission

`CPPClassEmitter` will consume the shared shape rules for:

- backing storage emitted for auto-properties
- getter return types
- setter parameter types
- property bridge accessors
- constructor field initialization where property storage participates

Expected outcome:

- value-type auto-properties emit direct-value storage
- value-type setter parameters emit by value unless a broader backend rule explicitly requires otherwise
- no property signature emits `float3*` or equivalent pointer syntax unless the type is genuinely pointer-shaped in the backend

### 3. Fix Expression and Member Access Lowering

`CPPConversiorProcessor` will consume the same shape rules for:

- instance member access
- computed property getter and setter access
- static property chains
- object initializer setter lowering
- any helper path that appends member access operators based on the lowered receiver type

Expected outcome:

- value receivers use `.`
- reference receivers use `->`
- static owners use `::`
- property access chains remain compatible with computed-property lowering rules already covered by existing tests

### 4. Preserve Strict Failure Behavior

If the backend cannot determine a consistent native shape for a type, conversion should fail with a clear unsupported-construct diagnostic. The fix must not introduce fallback coercions between pointer and value forms.

## Expected Behavior After the Fix

For code shaped like `AxisRotationComponent`, the generated backend output should behave like this:

- `Axis` is emitted as a direct `float3` value property, not `float3*`
- `float3.Zero` emits as static owner access, not pointer-style member access
- `float3.Normalize(value)` emits as static invocation on the type owner
- `Core.Instance` emits according to the actual native shape of `Core` and `Instance`, without rewriting static owners into pointer syntax
- `Parent.LocalOrientation` uses the correct receiver access form based on the emitted native shape of `Parent`
- generated serializer and deserializer code passes `float3` values into `AxisRotationComponent::set_Axis(float3 value)`

## Implementation Areas

The main change surface is expected to be:

- `cs2.cpp/model/CPPVariableType.cs`
- `cs2.cpp/CPPClassEmitter.cs`
- `cs2.cpp/CPPConversiorProcessor.cs`
- nearby helper code used by generated runtime accessor or deserializer emission

The implementation should prefer consolidating shape decisions in one place and then simplifying callers, rather than adding more local conditionals in each emitter path.

## Testing Strategy

### 1. Emitter-Level Tests

Add focused tests that verify:

- value-type auto-properties emit direct-value storage
- generated getter and setter signatures for value-type properties do not use pointer syntax
- bridge accessors preserve the same signature shape

These belong in `cs2.cpp.tests` near the existing `CPPClassEmitter` coverage.

### 2. Compile-Validation Regression Tests

Add a focused regression sample that mirrors the problem patterns from `AxisRotationComponent`:

- static value-type member access such as `float3.Zero`
- static value-type invocation such as `float3.Normalize(...)`
- static property chain such as `Core.Instance`
- property reads and writes on mixed receiver shapes such as `Parent.LocalOrientation`

Assertions should verify the emitted text uses the correct operators and signatures. This belongs in the existing compile-validation regression suite because it already covers property lowering behavior.

### 3. Backend Verification

Run targeted `cs2.cpp.tests` verification after the changes.

Then rerun the real city export path that previously failed so the transpiler fix is proven against the original native build regression, not just synthetic samples.

## Risks

### Risk: Regressing Existing Computed Property Lowering

The backend already has tests for computed property getters, inherited property receivers, and static generated property chains. A centralized shape cleanup could accidentally alter those working paths.

Mitigation:

- keep the new regression tests alongside the existing property tests
- verify both old and new property-shape cases in the same targeted test run

### Risk: Overfitting to the Current Export Failure

The `AxisRotationComponent` failure is only one manifestation of the bug family.

Mitigation:

- anchor the fix on generic value-type semantics
- prohibit type-name special cases
- add tests that exercise multiple value-type/property combinations

## Success Criteria

This slice is complete when all of the following are true:

- `cs2.cpp` emits value-type properties and accessors using consistent direct-value semantics
- member-access lowering chooses `.`, `->`, and `::` from one shared rule instead of scattered heuristics
- no new hard-coded `float3` / `float4` exceptions exist
- targeted `cs2.cpp.tests` pass
- the city Windows export no longer fails on the generated `AxisRotationComponent` code
