# Generic Type Remap Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that configured `csharpcodegen` type remaps rewrite all emitted C++ type references generically, and fix the generator only if that proof fails.

**Architecture:** Start with targeted conversion regressions that assert remapped output across fields, method signatures, locals, and nested generic arguments. Only if a new regression fails should the implementation touch the shared generic type rendering path in `cs2.cpp`, keeping the fix generic and free of platform-specific logic or generated-file rewriting.

**Tech Stack:** C#, Roslyn, xUnit, `cs2.cpp`, `cs2.cpp.tests`, .NET 9

---

## File Structure

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
  - Add conversion-level regressions that verify configured type remaps rewrite emitted C++ across all relevant declaration surfaces.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
  - Only if needed, centralize the generic remap application in the shared C++ type rendering path.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
  - Only if needed, route any bypassing declaration emission path through the shared remapped type renderer.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
  - Only if needed, route any bypassing statement or expression emission path through the shared remapped type renderer.

## Task 1: Add a Failing End-to-End Remap Regression

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Add one focused conversion regression that covers the full remapped type surface**

```csharp
        /// <summary>
        /// Ensures configured type remaps rewrite emitted C++ across fields, method signatures, locals, and nested generic arguments.
        /// </summary>
        [Fact]
        public void WriteOutput_WithConfiguredTypeRemaps_RewritesAllEmittedTypeReferences() {
            string source = """
                using System.Collections.Generic;
                using System.Numerics;

                namespace Example {
                    public struct Float2 {
                        public float X;
                        public float Y;
                    }

                    public struct Float3 {
                        public float X;
                        public float Y;
                        public float Z;
                    }

                    public struct Float4 {
                        public float X;
                        public float Y;
                        public float Z;
                        public float W;
                    }

                    public sealed class RemapFixture {
                        public Vector4 Orientation;

                        public Vector3 Project(Vector4 input) {
                            Vector3 local = default;
                            List<Vector2> history = new List<Vector2>();
                            return local;
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversionWithTypeRemaps(
                source,
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["System.Numerics.Vector2"] = "Example.Float2",
                    ["System.Numerics.Vector3"] = "Example.Float3",
                    ["System.Numerics.Vector4"] = "Example.Float4"
                });
            string header = File.ReadAllText(Path.Combine(output.OutputPath, "RemapFixture.hpp"));
            string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RemapFixture.cpp"));

            Assert.Contains("::Float4 Orientation;", header, StringComparison.Ordinal);
            Assert.Contains("::Float3 Project(::Float4 input);", header, StringComparison.Ordinal);
            Assert.Contains("::Float3 local", sourceOutput, StringComparison.Ordinal);
            Assert.Contains("List<::Float2>* history", sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector2", header + sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector3", header + sourceOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector4", header + sourceOutput, StringComparison.Ordinal);
        }
```

- [ ] **Step 2: Run the new regression by itself and verify whether it fails for the expected reason**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithConfiguredTypeRemaps_RewritesAllEmittedTypeReferences"
```

Expected:

```text
FAIL if any emitted surface still leaks the source types
PASS if the current generator contract is already complete
```

- [ ] **Step 3: Commit the red test only if it genuinely fails**

```bash
git add cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
git commit -m "test: cover generic type remap output surfaces"
```

## Task 2: Fix the Generator Only If the Regression Fails

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Trace the failing emitted token back to the exact rendering path**

Check whether the leaked source type came from:

- `CPPVariableType.ResolveCppTypeName(...)`
- `CPPClassEmitter.ConvertType(...)`
- `CPPConversiorProcessor` declaration rendering
- one specialized path that bypasses the shared remapped type renderer

- [ ] **Step 2: Apply the smallest generic fix in the shared rendering path**

Rules:

- no helengine names
- no `System.Numerics` special-casing
- no platform branches
- no generated-file rewriting

- [ ] **Step 3: Re-run the targeted regression and verify it passes**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithConfiguredTypeRemaps_RewritesAllEmittedTypeReferences"
```

Expected:

```text
PASS
```

- [ ] **Step 4: Commit the generic fix once the targeted regression is green**

```bash
git add cs2.cpp/model/CPPVariableType.cs cs2.cpp/CPPClassEmitter.cs cs2.cpp/CPPConversiorProcessor.cs cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
git commit -m "fix: apply configured type remaps across emitted cpp types"
```

## Task 3: Verify Adjacent Remap Coverage Without Expanding Scope

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Keep or add only narrowly necessary companion assertions for adjacent emitted surfaces touched by the fix**

Examples:

- nested generic arguments in return types
- remapped local declarations
- remapped parameter declarations

- [ ] **Step 2: Run the smallest touched validation scope**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~TypeRemap"
```

Expected:

```text
PASS
```

- [ ] **Step 3: Commit the final regression lock if Task 3 added coverage beyond Task 1**

```bash
git add cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
git commit -m "test: lock generic cpp type remap coverage"
```
