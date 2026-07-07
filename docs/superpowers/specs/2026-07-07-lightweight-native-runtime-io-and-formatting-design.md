# Lightweight Native Runtime IO And Formatting Design

## Goal

Reduce shared native runtime size by removing heavyweight standard-library dependencies from the generic `csharpcodegen` support code, starting with `std::filesystem` and locale-heavy stream formatting.

## Context

The new Nintendo DS native binary size report shows the largest generic contributors are not DS renderer code first. The report is dominated by shared `libstdc++` locale, iostream, fstream, and filesystem objects, including:

- `wlocale-inst.o`
- `locale-inst.o`
- `cxx11-locale-inst.o`
- `cxx11-wlocale-inst.o`
- `fstream-inst.o`
- `fs_ops.o`
- `fs_path.o`
- `istream-inst.o`
- `ostream-inst.o`

The shared runtime templates currently pull those dependencies through generic support code in `cs2.cpp/.net.cpp/system`, including:

- `system/io/path.cpp` using `std::filesystem`
- `system/io/directory.cpp` using `std::filesystem`
- `system/app_context.hpp` using `std::filesystem`
- `system/guid.hpp` using `std::ostringstream`
- `system/numerics/vector.hpp` using `std::ostringstream`
- `system/io/file.cpp` using `std::ifstream`

## Problem Statement

The shared runtime uses heavyweight convenience APIs for basic path, directory, file-existence, and string-formatting work. That is acceptable on desktop hosts, but it is wasteful for constrained native builds.

The fix must remain generic:

- no DS-specific runtime code
- no platform-builder output rewrites
- no helengine-local patches to generated code
- no codegen knowledge of platform-specific math or path semantics beyond existing platform macros

## Requirements

- Remove `std::filesystem` usage from shared runtime path and directory helpers.
- Remove stream-based formatting from `Guid` and vector `ToString()` support.
- Remove `std::ifstream` from the shared `File::Exists` fast path.
- Preserve existing public runtime surfaces.
- Preserve existing PS2 and DS device-path semantics already embedded in the path helpers.
- Keep host platforms working.

## Non-Goals

- Rewriting the entire shared runtime around custom allocators or containers.
- Removing all `libstdc++` usage in one pass.
- Changing content stream source behavior or platform builder contracts.
- Making codegen emit platform-specific runtime code paths for this slice.

## Approaches Considered

### 1. DS-only diagnostics and trace cleanup

This removes some local overhead, but it does not attack the largest generic contributors shown by the report.

### 2. Replace heavyweight shared-runtime IO and formatting dependencies

This targets the actual dominant symbols while keeping the change generic. It is the recommended first pass.

### 3. Add a separate “lean runtime profile” flag

This may be useful later, but it is a wider contract change than this first reduction needs.

## Recommended Approach

Implement approach 2.

### Path and directory helpers

Replace `std::filesystem` path operations with lightweight string-based helpers inside the existing `Path` and `Directory` runtime types.

For the first pass:

- `Path::Combine`
- `Path::GetDirectoryName`
- `Path::GetFileName`
- `Path::GetFullPath`
- `Path::ChangeExtension`
- `Path::IsPathRooted`
- `Directory::Exists`
- `Directory::CreateDirectory`

These helpers should preserve existing special cases for:

- PS2 device paths
- DS `nitro:` paths
- host path separators

Use:

- string scanning and concatenation for path logic
- `stat` for file/directory existence
- `mkdir` or `_mkdir` loops for recursive directory creation

### App context

Keep `AppContext::BaseDirectory` behavior, but stop depending on `std::filesystem::path` for simple parent-directory extraction.

### Formatting helpers

Replace `std::ostringstream` formatting with lightweight manual or C-style formatting helpers:

- `Guid` should format the counter-based guid into a fixed-size char buffer.
- `Vector_1<T>::ToString()` should build output with direct `std::string` appends and numeric formatting helpers instead of streams.

## Testing Strategy

- Add runtime template contract tests that assert the shared runtime files no longer depend on:
  - `std::filesystem`
  - `std::ostringstream`
  - `std::ifstream`
- Run focused `csharpcodegen` template tests.
- Rebuild the DS target and compare the new size report against the current baseline.

## Expected Outcome

The DS report should show a measurable drop in the `libstdc++` locale/iostream/filesystem contributors. The generated runtime remains generic and reusable across all native platforms.
