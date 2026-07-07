# Lightweight Native Runtime IO And Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut shared native runtime binary size by replacing heavyweight `std::filesystem` and stream-formatting dependencies in `csharpcodegen` runtime templates with lightweight generic helpers.

**Architecture:** Keep the public shared runtime surfaces unchanged, but rewrite their implementations in `cs2.cpp/.net.cpp/system` to use string logic, `stat`/`mkdir`, and direct buffer-based formatting. Lock the contract with runtime-template source tests, then verify the effect through a fresh DS build-size report.

**Tech Stack:** C#, xUnit, `csharpcodegen`, C++ shared runtime templates, DS packaged build verification

---

## File Structure

### Runtime template tests

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPRuntimeTemplateContractTests.cs`

These tests lock the requirement that the shared runtime templates no longer depend on heavyweight filesystem and stream-formatting APIs.

### Shared runtime templates

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\io\path.cpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\io\directory.cpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\io\file.cpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\app_context.hpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\guid.hpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\numerics\vector.hpp`

These files remove the heavyweight standard-library dependencies while preserving the existing runtime API shape.

## Task 1: Lock the runtime template contract

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPRuntimeTemplateContractTests.cs`

- [ ] **Step 1: Write failing runtime-template tests**

Add tests that assert:

- `path.cpp` and `directory.cpp` do not contain `std::filesystem`
- `file.cpp` does not contain `std::ifstream`
- `guid.hpp` and `vector.hpp` do not contain `std::ostringstream`

- [ ] **Step 2: Run the focused runtime-template tests to verify they fail**

Run: `dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter CPPRuntimeTemplateContractTests`

Expected: FAIL because the current templates still use those APIs.

## Task 2: Replace lightweight path and directory implementations

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\io\path.cpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\io\directory.cpp`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\app_context.hpp`

- [ ] **Step 1: Implement string-based path helpers**

Preserve PS2 and DS device-path semantics while removing `std::filesystem`.

- [ ] **Step 2: Implement directory existence and recursive create helpers**

Use `stat` and `mkdir`/`_mkdir` rather than `std::filesystem`.

- [ ] **Step 3: Remove `std::filesystem` from app-context base-directory resolution**

Keep behavior equivalent for host use.

## Task 3: Replace stream-heavy formatting and file-exists logic

**Files:**
- Modify: `C:\dev\\helworks\\csharpcodegen\\cs2.cpp\\.net.cpp\\system\\io\\file.cpp`
- Modify: `C:\dev\\helworks\\csharpcodegen\\cs2.cpp\\.net.cpp\\system\\guid.hpp`
- Modify: `C:\dev\\helworks\\csharpcodegen\\cs2.cpp\\.net.cpp\\system\\numerics\\vector.hpp`

- [ ] **Step 1: Replace `File::Exists` stream probing**

Use `stat` or equivalent lightweight existence checks.

- [ ] **Step 2: Replace guid stream formatting**

Use fixed-size buffer formatting for the hex counter representation.

- [ ] **Step 3: Replace vector `ToString()` stream formatting**

Use direct string building and lightweight numeric formatting.

## Task 4: Verify and measure

**Files:**
- Verify: `C:\dev\helprojs\city\ds-build\helengine_ds-native-binary-size-report.txt`

- [ ] **Step 1: Run the focused `csharpcodegen` template tests**

Run: `dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter CPPRuntimeTemplateContractTests`

Expected: PASS.

- [ ] **Step 2: Rebuild the DS target**

Run the standard `build-platform.ps1` DS flow against `city`.

- [ ] **Step 3: Compare the new DS size report**

Confirm the package size and the top `libstdc++` locale/iostream/filesystem contributors decreased from the current baseline.
