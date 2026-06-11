# NativeMemory Aligned Allocation Portability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the shared `NativeMemory` runtime surface dependency on `std::aligned_alloc` and make aligned allocation portable across non-MSVC toolchains.

**Architecture:** Add a focused regression that inspects the emitted shared runtime asset, then replace the non-MSVC `AlignedAlloc`/`AlignedFree` path with a generic manual aligned-allocation fallback built on `std::malloc` and stored base-pointer metadata. Revalidate both `csharpcodegen` and the normal city PS Vita build.

**Tech Stack:** C#, xUnit, C++ runtime templates, `cs2.cpp`, `cs2.cpp.tests`, .NET 9

---

## File Structure

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
  - Add focused runtime portability coverage for the emitted `native_memory.hpp` asset.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\runtime\interopservices\native_memory.hpp`
  - Replace the non-MSVC aligned-allocation implementation with a generic fallback.

## Task 1: Add the Failing Runtime Portability Regression

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Add a regression that inspects the emitted `native_memory.hpp` runtime asset**

```csharp
        /// <summary>
        /// Ensures the shared NativeMemory runtime asset does not require std::aligned_alloc on non-MSVC toolchains.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNativeMemoryStaticCall_EmitsPortableAlignedAllocationRuntime() {
            string source = """
                public unsafe class Fixture {
                    public void* Run() {
                        return System.Runtime.InteropServices.NativeMemory.AlignedAlloc((nuint)16, (nuint)8);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source, allowUnsafe: true);
            string runtimeHeaderOutput = File.ReadAllText(Path.Combine(output.OutputPath, "system", "runtime", "interopservices", "native_memory.hpp"));

            Assert.DoesNotContain("std::aligned_alloc", runtimeHeaderOutput, StringComparison.Ordinal);
            Assert.Contains("std::malloc", runtimeHeaderOutput, StringComparison.Ordinal);
        }
```

- [ ] **Step 2: Run the new regression to verify the current runtime fails for the expected reason**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithNativeMemoryStaticCall_EmitsPortableAlignedAllocationRuntime"
```

Expected:

```text
FAIL because the current runtime header still contains std::aligned_alloc
```

## Task 2: Replace the Non-MSVC Runtime Path With a Generic Fallback

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\.net.cpp\system\runtime\interopservices\native_memory.hpp`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Implement the smallest generic fallback**

Requirements:

- normalize alignment to at least `alignof(void*)`
- allocate enough extra space for alignment adjustment and one stored base pointer
- compute one aligned returned pointer inside the allocated block
- store the original base pointer immediately before the aligned returned pointer
- free the original base pointer in `AlignedFree`

- [ ] **Step 2: Re-run the targeted portability regression**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithNativeMemoryStaticCall_EmitsPortableAlignedAllocationRuntime"
```

Expected:

```text
PASS
```

- [ ] **Step 3: Re-run the existing lowering regression**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithNativeMemoryStaticCall_UsesRuntimeNativeMemorySurface"
```

Expected:

```text
PASS
```

## Task 3: Revalidate the Real Consumer Build

**Files:**
- No additional source changes expected

- [ ] **Step 1: Re-run the normal city PS Vita build**

Run the standard editor-driven city PS Vita build into the existing temporary output path.

Expected:

```text
Build advances past native_memory.hpp aligned allocation compilation
```

- [ ] **Step 2: Commit the portability fix and tests**

```bash
git add cs2.cpp.tests/CPPCompileValidationRegressionTests.cs cs2.cpp/.net.cpp/system/runtime/interopservices/native_memory.hpp
git commit -m "fix: make native memory aligned allocation portable"
```
