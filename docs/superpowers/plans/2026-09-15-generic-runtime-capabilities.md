# Generic runtime capabilities implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans to implement task by task.

**Goal:** Make generic C++ runtime capabilities govern generated code and shared runtime helpers.

**Architecture:** Resolve caller options after presets. Shared runtime types select standard storage or a caller provider. Disabled exceptions use an explicit fatal hook; unsupported recovery and runtime type operations fail visibly.

**Tech Stack:** C#, Roslyn, C++20, xUnit, host C++ compiler, pinned PS1 GCC consumer check.

**Spec:** ../specs/2026-09-15-generic-runtime-capabilities-design.md

## Global constraints

- No PS1-specific generator conditions or generated-output rewriting.
- Generic options work for every platform and preserve enabled hosted behavior.
- Missing provider and unsupported operations fail explicitly.
- Worktree: `csharpcodegen/.worktrees/generic-freestanding-runtime`.
- User selected Luna xhigh for delegated work. No standalone independent review.

## Task 1: Resolve options and emit restricted code

Files: `CPPCodegenOptionNames.cs`, new `CPPRuntimeOptionResolver.cs`,
`CPPCodeConverter.cs`, `CPPGeneratedConfigWriter.cs`,
`CPPConversiorProcessor.cs`, `CPPClassEmitter.cs`, focused xUnit tests.

- [x] Add failing option tests for each capability, invalid booleans and caller-over-preset precedence.
- [x] Add conversion tests for custom string construction, throw/finally, catch/rethrow rejection and RTTI-dependent casts.
- [x] Resolve capabilities once, emit matching config, and route string lowering through the resolved type.
- [x] Route nonrecoverable throws through the shared failure helper; report disabled recovery/type operations.
- [x] Run focused tests and related conversion regressions; record exact failures and results.

## Task 2: Shared runtime provider and helpers

Files: `.net.cpp/runtime/native_runtime.hpp`, existing string/list/dictionary,
stack/array/exceptions/cast helpers and affected consumers; runtime fixtures.

- [x] Compile the existing collection/string fixture with disabled hosted storage/exceptions/RTTI and observe failure.
- [x] Add provider-backed aliases without namespace-std injection; require a provider when selected storage is unavailable.
- [x] Adapt shared runtime templates to selected storage and failure policy, preserving managed ownership behavior.
- [x] Remove incidental hosted character/math dependencies from the restricted string path.
- [x] Compile and execute standard and custom provider fixtures, including failure-hook checks.

## Task 3: Integration and documentation

- [x] Compile a custom provider fixture against EASTL with pinned PS1 GCC, `-fno-exceptions -fno-rtti`.
- [x] Verify generated custom string code compiles against the same runtime contract.
- [x] Run relevant existing tests, inspect diff and record provider contract and default migration details.
- [x] Commit only verified source/docs. Distinguish capability support from full generated Helengine Core link/memory validation.

## Initial evidence

Baseline: 28 conversion-options/runtime-template tests pass at `3ff39bf`.
Pinned compiler supports algorithm, cstdint, functional, array, utility and
type_traits; cctype and cmath are absent. Build logs are external under
`C:/dev/helworks/builds/helengine-ps1`.

## Completion boundary

Generic capability implementation and fixtures are complete. See
[verification and limitations](../../generic-runtime-capabilities.md). Full Core
runtime-source selection, remaining hosted-service adapters, and PS1 scene
linking, memory validation and emulator execution remain integration work.
