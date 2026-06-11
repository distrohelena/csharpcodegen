# NativeMemory Aligned Allocation Portability Design

## Goal

Make the shared `csharpcodegen` `NativeMemory` runtime surface portable across native toolchains that do not provide `std::aligned_alloc`, without introducing helengine-specific behavior or platform-local generated-file rewrites.

## Context

`csharpcodegen` lowers managed `System.Runtime.InteropServices.NativeMemory` calls to the shared runtime requirement:

- `NativeMemory`
- `system/runtime/interopservices/native_memory.hpp`

The generated PS Vita build now reaches that runtime surface correctly and fails later during native compilation because the bundled runtime header assumes:

- `_aligned_malloc` on MSVC
- `std::aligned_alloc` everywhere else

That assumption is too narrow for some non-MSVC toolchains, including the Vita toolchain currently compiling the generated core.

## Problem Statement

The shared runtime header must provide aligned allocation and release behavior without depending on `std::aligned_alloc`.

The fix must remain generic:

- no helengine names
- no Vita-specific branches
- no generated-file rewriting
- no platform-builder post-processing

## Recommended Approach

### 1. Prove the runtime portability gap with regression coverage

Add a targeted regression that verifies the emitted `native_memory.hpp` runtime asset no longer hard-depends on `std::aligned_alloc`.

Keep the existing lowering regression that proves managed `NativeMemory` calls are routed to the shared runtime surface.

### 2. Replace the non-MSVC path with a generic fallback

Implement aligned allocation on non-MSVC toolchains using a manual fallback built on:

- `std::malloc`
- pointer alignment arithmetic
- a small header slot that stores the original allocation pointer for later release

This keeps the runtime generic and avoids depending on optional standard-library or libc aligned-allocation entry points.

### 3. Keep the free path symmetric

`AlignedFree` must recover the original base pointer from the metadata slot and free that original allocation.

Null inputs must remain safe.

### 4. Re-run end-to-end validation

Validate with:

- targeted `csharpcodegen` `NativeMemory` runtime regressions
- the normal city PS Vita build

## Non-Goals

- no changes to platform builders
- no changes to scene/build configuration beyond what was already fixed
- no platform-specific compatibility shims
- no generated-output patching

## Risks

- incorrect metadata placement could break alignment or freeing
- zero-byte or undersized allocation edge cases could introduce undefined behavior
- the regression could accidentally validate implementation wording instead of portability intent

## Mitigations

- normalize alignment to at least pointer alignment
- reserve enough space for both alignment slack and the stored base pointer
- assert on emitted runtime surface behavior rather than generated project specifics

## Decision

Proceed test-first: add the runtime portability regression, verify it fails for the current `std::aligned_alloc` dependency, then replace the non-MSVC implementation with one generic manual aligned-allocation fallback.
