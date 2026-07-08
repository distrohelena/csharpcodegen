# Lightweight Native Integer Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the shared `std::to_string` dependency from generated native integer string conversion paths so constrained targets like DS avoid dragging formatted-I/O code into the final binary.

**Architecture:** Route transpiled primitive numeric `ToString()` and interpolated string conversions through the shared `String` runtime helper instead of emitting `std::to_string` directly. Implement a lightweight decimal formatter in the native string runtime and reuse it from `StringBuilder` so both generated code and shared runtime helpers share the same low-footprint integer formatting path.

**Tech Stack:** C#, xUnit, shared C++ runtime templates, DS native build verification

---

### Task 1: Lock the shared formatting contract with failing tests

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPInterpolatedStringAuditTests.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPRuntimeTemplateContractTests.cs`

- [ ] **Step 1: Write failing tests**

Add assertions that:
- simple numeric interpolation emits `String::ToJoinString(value)` instead of `std::to_string(value)`
- nullable primitive `Value.ToString()` lowering uses `String::ToJoinString(...)`
- `runtime/native_string.hpp` and `system/text/string-builder.hpp` no longer contain `std::to_string`

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "CPPInterpolatedStringAuditTests|CPPCompileValidationRegressionTests|CPPRuntimeTemplateContractTests"`

Expected: FAIL on `std::to_string` assertions that still match the old implementation.

### Task 2: Implement the shared lightweight integer formatter

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\runtime\native_string.hpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\text\string-builder.hpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`

- [ ] **Step 1: Add runtime integer formatting helpers**

Implement decimal append helpers in `String` that cover signed and unsigned integral arithmetic without `std::to_string`.

- [ ] **Step 2: Route codegen numeric string emission through the runtime helper**

Update generated native lowering for primitive numeric `ToString()` and interpolation/concatenation expressions to emit `String::ToJoinString(...)`, registering the runtime requirement where needed.

- [ ] **Step 3: Reuse the helper in `StringBuilder`**

Replace direct integer `Append(...)` calls that currently use `std::to_string`.

### Task 3: Verify behavior and measure DS impact

**Files:**
- Verify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Verify: `C:\dev\helworks\helengine-ds\build\helengine_ds.map`

- [ ] **Step 1: Re-run focused codegen tests**

Run: `rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "CPPInterpolatedStringAuditTests|CPPCompileValidationRegressionTests|CPPRuntimeTemplateContractTests"`

Expected: PASS

- [ ] **Step 2: Rebuild the DS city binary**

Run the established DS city build command already used in this session.

- [ ] **Step 3: Compare size and symbol roots**

Confirm the packaged size and accounted native size, then inspect `helengine_ds.map` for removal or reduction of `std::__cxx11::to_string` / formatted-I/O roots.
