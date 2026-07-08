# Lightweight Native Vector Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the shared native vector runtime dependency on `std::snprintf` without changing the generated public `Vector_1<T>::ToString()` surface.

**Architecture:** Keep the entire change inside the shared `csharpcodegen` runtime templates and their source-contract tests. Replace the float formatting helper in `system/numerics/vector.hpp` with a smaller locale-free implementation, then verify the impact through the DS native size report.

**Tech Stack:** C#, xUnit, shared C++ runtime templates, Nintendo DS native build pipeline

---

### Task 1: Lock The Runtime Contract

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPRuntimeTemplateContractTests.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\numerics\vector.hpp`

- [ ] **Step 1: Write the failing test**

Add an assertion in `RuntimeTemplates_vector_runtime_avoids_ostringstream` or split out a dedicated test so `vector.hpp` must not contain `std::snprintf`.

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter RuntimeTemplates_vector_runtime`

Expected: FAIL because `vector.hpp` still contains `std::snprintf`.

- [ ] **Step 3: Write minimal implementation**

Replace the floating-point branch in `Vector_1<T>::AppendValueToString` with a template-owned helper that formats finite floats without `std::snprintf`.

- [ ] **Step 4: Run test to verify it passes**

Run: `rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter RuntimeTemplates_vector_runtime`

Expected: PASS and no assertion references to `std::snprintf` remain.

- [ ] **Step 5: Commit**

Commit the runtime-template contract and implementation once focused verification is green.

### Task 2: Verify No Regression In Shared Runtime Output

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPManagedRuntimeContractAuditTests.cs` only if an explicit generated-runtime assertion is needed
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\numerics\vector.hpp`

- [ ] **Step 1: Add any generated-runtime assertion only if needed**

If the contract test is not enough, assert the emitted `system/numerics/vector.hpp` still contains the expected `ToString()` surface and no longer uses stdio-based float formatting.

- [ ] **Step 2: Run targeted tests**

Run: `rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter CPPRuntimeTemplateContractTests|CPPManagedRuntimeContractAuditTests`

Expected: PASS for the touched runtime-template and runtime-copy coverage.

- [ ] **Step 3: Commit**

Commit any follow-up assertion adjustments together with the runtime helper only if they were necessary.

### Task 3: Measure DS Binary Impact

**Files:**
- Inspect: `C:\dev\helprojs\city\ds-build\helengine_ds-native-binary-size-report.txt`
- Inspect: `C:\dev\helworks\helengine-ds\builder\NintendoDsNativeBuildExecutor.cs` only if report generation regresses

- [ ] **Step 1: Rebuild the DS target**

Run the existing city DS build flow that emits `helengine_ds.nds` and `helengine_ds-native-binary-size-report.txt`.

- [ ] **Step 2: Compare report output**

Compare the fresh package size, accounted native binary size, and top `printf`/locale contributors against the current baseline:

- `Package Size: 1933312`
- `Accounted Native Binary Size: 769374`
- `libc_a-vfiprintf.o: 33370`
- `libc_a-svfprintf.o: 9988`
- `libc_a-svfiprintf.o: 7369`
- `libc_a-categories.o: 14420`
- `libc_a-locale.o: 4073`

- [ ] **Step 3: Commit**

Commit the verified generic size win once the report confirms the effect and the relevant tests are green.
