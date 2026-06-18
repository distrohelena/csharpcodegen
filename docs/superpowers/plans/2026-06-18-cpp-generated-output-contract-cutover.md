# C++ Generated Output Contract Cutover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `csharpcodegen` emit final C++ output that `helengine` can stage and compile without any generated-file rewrites, pruning, or compatibility shim synthesis.

**Architecture:** Execute the cutover in three generator-owned slices. First, lock and fix the semantic lowering bugs that currently force `helengine` to rewrite emitted `.cpp` text. Second, replace the compatibility-header and include-rewrite contract with canonical file naming decided at emission time. Third, move runtime inventory selection ahead of file writes so the final output tree is selected, not emitted and cleaned. Once each upstream slice is green, delete the corresponding downstream mutation code immediately.

**Tech Stack:** C#, Roslyn, xUnit, .NET 9, `cs2.cpp`, `cs2.cpp.tests`, `helengine.editor`, `helengine.editor.tests`, `rtk`

---

## File Structure

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
  - Own semantic lowering for the value-type/helper-call bug family that `helengine` currently rewrites.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
  - Keep property and setter signatures aligned with the same native shape rules used by expression lowering.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
  - Own canonical emitted type names and file stems for generated artifacts.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPCodeConverter.cs`
  - Remove compatibility-header emission and post-generation cleanup hooks; decide runtime inventory before writing files.
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPGeneratedOutputAdapter.cs`
  - Remove the post-emission adaptation seam entirely.
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPGeneratedSourcePruner.cs`
  - Remove generated-file deletion as a generator-owned cleanup tactic.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
  - Add and update end-to-end output assertions for semantic lowering and canonical naming.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPQualifiedNameAuditTests.cs`
  - Re-anchor cross-project generated-name coverage on canonical file stems instead of compatibility headers.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPFeaturePruningEndToEndTests.cs`
  - Keep runtime helper presence/absence checks against the new selected-before-write contract.
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPGeneratedOutputAdapterTests.cs`
  - Remove tests that legitimize a post-emission output-adapter seam.
- Create: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPGeneratedOutputContractAuditTests.cs`
  - Add source-audit coverage that the generator no longer contains cleanup passes.
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformCodeCookService.cs`
  - Delete generated-module content normalization.
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
  - Delete generated-core include rewrites, compatibility shim synthesis, and case-collision repair.
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformCodeCookServiceTests.cs`
  - Replace normalization assertions with a no-normalizer boundary check.
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`
  - Remove rewrite-based expectations and assert that generated-core mutation helpers are absent.

### Task 1: Lock The Semantic Lowering Contract Before Deleting The Module Normalizer

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformCodeCookServiceTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Add a failing `cs2.cpp` regression for the exact `float4` repair family**

```csharp
    /// <summary>
    /// Ensures rotation-style value-type flows emit final native syntax without downstream text normalization.
    /// </summary>
    [Fact]
    public void WriteOutput_WithRotationValueTypes_EmitsFinalValidNativeSyntax() {
        string source = """
            public struct float3 {
                public float X;
                public float Y;
                public float Z;

                public static float3 Normalize(float3 value) {
                    return value;
                }
            }

            public struct float4 {
                public void Normalize() {
                }

                public static void CreateFromYawPitchRoll(float yaw, float pitch, float roll, out float4 result) {
                    result = new float4();
                }

                public static void CreateFromAxisAngle(float3 axis, float angle, out float4 result) {
                    result = new float4();
                }

                public static void Concatenate(ref float4 left, ref float4 right, out float4 result) {
                    result = left;
                }
            }

            public sealed class RotationFixture {
                public void Update(float yawRadians, float pitchRadians, float3 axis, float angle, float4 currentOrientation, float4 deltaRotation) {
                    float4 orientation;
                    float4.CreateFromYawPitchRoll(yawRadians, pitchRadians, 0.0f, out orientation);
                    orientation.Normalize();

                    float4 axisAngleRotation;
                    float4.CreateFromAxisAngle(axis, angle, out axisAngleRotation);

                    float4 mergedOrientation;
                    float4.Concatenate(ref currentOrientation, ref deltaRotation, out mergedOrientation);
                }
            }
            """;

        ConversionOutput output = RunConversion(source);
        string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RotationFixture.cpp"));

        Assert.Contains("float4 orientation;", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("float4::CreateFromYawPitchRoll__out3(yawRadians, pitchRadians, 0.0f, orientation);", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("orientation.Normalize();", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("float4::CreateFromAxisAngle__ref0_out2(axis, angle, axisAngleRotation);", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("float4::Concatenate__ref0_ref1_out2(currentOrientation, deltaRotation, mergedOrientation);", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("float4 *orientation;", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("float4->CreateFromYawPitchRoll", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("orientation->Normalize()", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("float4::CreateFromAxisAngle(", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("float4::Concatenate(", sourceOutput, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the focused backend regression and verify it fails before any implementation changes**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithRotationValueTypes_EmitsFinalValidNativeSyntax"
```

Expected:

```text
FAIL
```

- [ ] **Step 3: Replace the current `helengine` normalization tests with one red boundary test that the normalizer must disappear**

Delete the tests named:

```csharp
Compile_code_modules_normalizes_generated_float4_orientation_temporaries
Compile_code_modules_normalizes_generated_float4_axis_angle_calls
Compile_code_modules_normalizes_generated_float4_concatenate_calls
```

Add this replacement:

```csharp
    [Fact]
    public void EditorPlatformCodeCookService_does_not_define_generated_native_module_normalizer() {
        MethodInfo? normalizer = typeof(EditorPlatformCodeCookService).GetMethod(
            "NormalizeGeneratedNativeModuleSources",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.Null(normalizer);
    }
```

- [ ] **Step 4: Run the `helengine` boundary test and verify it fails while the normalizer still exists**

Run:

```powershell
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "FullyQualifiedName~EditorPlatformCodeCookService_does_not_define_generated_native_module_normalizer"
```

Expected:

```text
FAIL
```

- [ ] **Step 5: Commit the red tests**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
rtk git -C C:\dev\helworks\csharpcodegen commit -m "test: lock semantic generated output contract"
rtk git -C C:\dev\helworks\helengine add engine/helengine.editor.tests/managers/project/EditorPlatformCodeCookServiceTests.cs
rtk git -C C:\dev\helworks\helengine commit -m "test: require module codegen normalizer removal"
```

### Task 2: Fix Semantic Lowering Upstream And Delete The Module Normalizer

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformCodeCookService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformCodeCookServiceTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Route out/ref helper-call lowering through semantic type-shape logic instead of emitted-text repair**

In `CPPConversiorProcessor`, update the helper-call lowering path that currently emits the bad `float4` forms so it chooses the final call shape before writing source.

Target pattern:

```csharp
CPPTypeData ownerTypeData;
ConvertToCPPType(ownerVariableType, out ownerTypeData);

string ownerAccess = ownerTypeData.IsPointer ? "->" : "::";
string emittedHelperName = ResolveConvertedFunctionName(targetMethodSymbol, targetFunction);
lines.Add($"{ownerTypeName}{ownerAccess}{emittedHelperName}(");
```

For `out` temporaries, emit the final local storage form directly:

```csharp
CPPTypeData outTypeData;
ConvertToCPPType(outVariableType, out outTypeData);
string outTypeName = ConvertType(outVariableType);
string localDeclaration = outTypeData.IsPointer
    ? $"{outTypeName}* {localName};"
    : $"{outTypeName} {localName};";
```

The direct-value case must be what makes the new regression pass. Do not add any `Replace(...)`-style cleanup.

- [ ] **Step 2: Keep property/setter signatures aligned with the same final value-shape**

Where `CPPClassEmitter` emits value-type property setters or locals that participate in the same call family, keep the signatures direct-value:

```csharp
headerWriter.WriteLine($"void set_{variable.Name}({typeName} value);");
sourceWriter.WriteLine($"void {qualifiedClassName}::set_{variable.Name}({typeName} value)");
```

Do not emit a pointer-only setter signature for direct-value structs and then rely on later call-site repair.

- [ ] **Step 3: Delete the downstream module normalizer**

Remove the call:

```csharp
NormalizeGeneratedNativeModuleSources(moduleRootPath);
```

Delete both methods:

```csharp
static void NormalizeGeneratedNativeModuleSources(string moduleRootPath)
static string RewriteGeneratedFloat4OrientationTemporary(string contents)
```

- [ ] **Step 4: Run the focused backend and `helengine` tests and verify both are green**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithRotationValueTypes_EmitsFinalValidNativeSyntax"
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "FullyQualifiedName~EditorPlatformCodeCookService_does_not_define_generated_native_module_normalizer"
```

Expected:

```text
PASS
PASS
```

- [ ] **Step 5: Commit the semantic cutover**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add cs2.cpp/CPPConversiorProcessor.cs cs2.cpp/CPPClassEmitter.cs cs2.cpp.tests/CPPCompileValidationRegressionTests.cs
rtk git -C C:\dev\helworks\csharpcodegen commit -m "fix: emit final value-type helper call syntax"
rtk git -C C:\dev\helworks\helengine add engine/helengine.editor/managers/project/EditorPlatformCodeCookService.cs engine/helengine.editor.tests/managers/project/EditorPlatformCodeCookServiceTests.cs
rtk git -C C:\dev\helworks\helengine commit -m "Remove gameplay module generated-code normalizer"
```

### Task 3: Lock The Canonical Naming Contract And The End Of Generated-Core Rewrite Helpers

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPQualifiedNameAuditTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Replace the alias-header regression with a canonical-artifact regression**

Delete:

```csharp
WriteOutput_WithLowercaseValueType_EmitsNamespaceQualifiedAliasHeader
```

Add:

```csharp
    [Fact]
    public void WriteOutput_WithLowercaseValueType_UsesCanonicalQualifiedArtifactsWithoutCompatibilityHeaders() {
        string source = """
            namespace helengine {
                public struct int2 {
                    public int X;
                    public int Y;
                }

                public class OverlayBox {
                    public int2 Padding { get; set; }
                }
            }
            """;

        ConversionOutput output = RunConversion(source);
        string overlayHeader = File.ReadAllText(Path.Combine(output.OutputPath, "OverlayBox.hpp"));

        Assert.True(File.Exists(Path.Combine(output.OutputPath, "helengine_int2.hpp")));
        Assert.True(File.Exists(Path.Combine(output.OutputPath, "helengine_int2.cpp")));
        Assert.False(File.Exists(Path.Combine(output.OutputPath, "int2.hpp")));
        Assert.False(File.Exists(Path.Combine(output.OutputPath, "helengine_helengine_int2.hpp")));
        Assert.Contains("#include \"helengine_int2.hpp\"", overlayHeader, StringComparison.Ordinal);
        Assert.Contains("::helengine_int2 Padding;", overlayHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("::int2 Padding;", overlayHeader, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Update the cross-project qualified-name audit to reject compatibility headers**

In `CPPQualifiedNameAuditTests.cs`, invert the current expectations:

```csharp
Assert.True(File.Exists(Path.Combine(outputPath, "helengine_int2.cpp")));
Assert.True(File.Exists(Path.Combine(outputPath, "helengine_int2.hpp")));
Assert.False(File.Exists(Path.Combine(outputPath, "int2.hpp")));
```

Do the same for the existing test that currently reads and asserts the contents of `int2.hpp`.

- [ ] **Step 3: Replace the `helengine` generated-core rewrite test with a no-helper source audit**

Delete:

```csharp
Normalize_merged_generated_source_case_insensitive_conflicts_rewrites_duplicate_short_name_artifacts
```

Extend the existing source audit with:

```csharp
Assert.DoesNotContain("NormalizeMergedGeneratedSourceCaseInsensitiveConflicts(", source, StringComparison.Ordinal);
Assert.DoesNotContain("EnsureGeneratedIncludeCompatibilityShims(", source, StringComparison.Ordinal);
Assert.DoesNotContain("RewriteGeneratedIncludeReferences(", source, StringComparison.Ordinal);
Assert.DoesNotContain("RewriteHeaderAsCompatibilityShim(", source, StringComparison.Ordinal);
```

- [ ] **Step 4: Run the naming and boundary tests and verify they fail on the current compatibility-header contract**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithLowercaseValueType_UsesCanonicalQualifiedArtifactsWithoutCompatibilityHeaders|FullyQualifiedName~CPPQualifiedNameAuditTests"
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "FullyQualifiedName~Generated_core_regeneration_service_contains_no_native_cpp_rewrite_inventory"
```

Expected:

```text
FAIL
FAIL
```

- [ ] **Step 5: Commit the red naming tests**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add cs2.cpp.tests/CPPCompileValidationRegressionTests.cs cs2.cpp.tests/CPPQualifiedNameAuditTests.cs
rtk git -C C:\dev\helworks\csharpcodegen commit -m "test: require canonical generated artifact names"
rtk git -C C:\dev\helworks\helengine add engine/helengine.editor.tests/managers/project/EditorGeneratedCoreRegenerationServiceTests.cs
rtk git -C C:\dev\helworks\helengine commit -m "test: require generated-core rewrite helper removal"
```

### Task 4: Emit Canonical Generated Names Directly And Delete Generated-Core Rewrite Logic

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPCodeConverter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPQualifiedNameAuditTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Make the emitted file stem itself canonical**

In `CPPVariableType.GetEmittedFileStem(...)`, return the final collision-safe stem directly instead of relying on a later alias header:

```csharp
public static string GetEmittedFileStem(this ConversionClass conversionClass, ConversionProgram program) {
    string emittedTypeName = conversionClass.GetEmittedTypeName();
    string qualifiedPrefix = BuildQualifiedFileStemPrefix(conversionClass);

    if (ShouldQualifyFileStem(conversionClass, program, emittedTypeName, qualifiedPrefix)) {
        return $"{qualifiedPrefix}_{emittedTypeName}";
    }

    return emittedTypeName;
}
```

The result should be the only source/header stem consumers use.

- [ ] **Step 2: Remove compatibility-header emission from `CPPCodeConverter`**

Delete the block that writes:

```csharp
string compatibilityHeaderPath = Path.Combine(folder, cl.Name + ".hpp");
compatibilityHeaderWriter.WriteLine($"#include \"{cl.GetEmittedFileStem(program)}.hpp\"");
```

Delete the methods:

```csharp
WriteNamespaceQualifiedLowercaseAliasHeader(...)
TryGetNamespaceQualifiedLowercaseAliasInfo(...)
GetNamespacePrefix(...)
```

Do not replace them with another header-synthesis pass.

- [ ] **Step 3: Ensure include emission resolves the canonical generated stem directly**

Where `CPPClassEmitter` resolves generated include names, route the include through the generated conversion class when available:

```csharp
ConversionClass? generatedClass = program.FindGeneratedClass(namedTypeSymbol);
if (generatedClass != null) {
    includePath = generatedClass.GetEmittedFileStem(program) + ".hpp";
    return true;
}
```

The same canonical stem must be used in both generated declarations and generated include references.

- [ ] **Step 4: Delete the downstream generated-core rewrite/shim helpers**

Remove these calls:

```csharp
NormalizeMergedGeneratedSourceCaseInsensitiveConflicts(generatedCoreOutputRoot);
EnsureGeneratedIncludeCompatibilityShims(generatedCoreOutputRoot);
```

Delete these methods:

```csharp
NormalizeMergedGeneratedSourceCaseInsensitiveConflicts(...)
EnsureGeneratedIncludeCompatibilityShims(...)
RewriteGeneratedIncludeReferences(...)
RewriteHeaderAsCompatibilityShim(...)
```

Keep `WriteGeneratedCoreTranslationUnit(...)` and report merging, but only as orchestration, not mutation.

- [ ] **Step 5: Run the naming and generated-core boundary tests and verify they pass**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithLowercaseValueType_UsesCanonicalQualifiedArtifactsWithoutCompatibilityHeaders|FullyQualifiedName~CPPQualifiedNameAuditTests"
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "FullyQualifiedName~Generated_core_regeneration_service_contains_no_native_cpp_rewrite_inventory"
```

Expected:

```text
PASS
PASS
```

- [ ] **Step 6: Commit the naming cutover**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add cs2.cpp/model/CPPVariableType.cs cs2.cpp/CPPCodeConverter.cs cs2.cpp/CPPClassEmitter.cs cs2.cpp.tests/CPPCompileValidationRegressionTests.cs cs2.cpp.tests/CPPQualifiedNameAuditTests.cs
rtk git -C C:\dev\helworks\csharpcodegen commit -m "fix: emit canonical generated artifact names"
rtk git -C C:\dev\helworks\helengine add engine/helengine.editor/managers/project/EditorGeneratedCoreRegenerationService.cs engine/helengine.editor.tests/managers/project/EditorGeneratedCoreRegenerationServiceTests.cs
rtk git -C C:\dev\helworks\helengine commit -m "Remove generated-core rewrite and shim helpers"
```

### Task 5: Remove Post-Emission Cleanup And Decide Runtime Inventory Before Write

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPCodeConverter.cs`
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPGeneratedOutputAdapter.cs`
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPGeneratedSourcePruner.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPFeaturePruningEndToEndTests.cs`
- Delete: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPGeneratedOutputAdapterTests.cs`
- Create: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPGeneratedOutputContractAuditTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`

- [ ] **Step 1: Add one source-audit test that the generator no longer contains cleanup passes**

Create `CPPGeneratedOutputContractAuditTests.cs`:

```csharp
namespace cs2.cpp.tests;

public sealed class CPPGeneratedOutputContractAuditTests {
    [Fact]
    public void CodeConverter_source_contains_no_generated_output_cleanup_pass() {
        string repositoryRootPath = ResolveRepositoryRootPath();
        string converterSource = File.ReadAllText(Path.Combine(repositoryRootPath, "cs2.cpp", "CPPCodeConverter.cs"));

        Assert.DoesNotContain("new CPPGeneratedOutputAdapter().Apply", converterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PruneDisabledFeatureRuntimeFiles(", converterSource, StringComparison.Ordinal);
    }

    static string ResolveRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
```

- [ ] **Step 2: Run the new audit test and verify it fails while cleanup hooks still exist**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~CodeConverter_source_contains_no_generated_output_cleanup_pass"
```

Expected:

```text
FAIL
```

- [ ] **Step 3: Decide runtime inventory before file write**

Move the current cleanup responsibilities into pre-write selection logic:

```csharp
if (!ShouldEmitGeneratedSourceClass(cl)) {
    continue;
}
```

Expand `ShouldEmitGeneratedSourceClass(...)` so editor-only attribute types never emit:

```csharp
return !string.Equals(conversionClass.Name, "NativeFreeFunctionAttribute", StringComparison.Ordinal) &&
    !string.Equals(conversionClass.Name, "NativeNoEscapeAttribute", StringComparison.Ordinal) &&
    !string.Equals(conversionClass.Name, "EditorPropertyDisplayNameAttribute", StringComparison.Ordinal) &&
    !string.Equals(conversionClass.Name, "EditorPropertyHiddenAttribute", StringComparison.Ordinal) &&
    !string.Equals(conversionClass.Name, "EditorPropertyOrderAttribute", StringComparison.Ordinal) &&
    !string.Equals(conversionClass.Name, "ScenePersistenceIgnoreAttribute", StringComparison.Ordinal);
```

For runtime template files, select before copy rather than copy-then-delete:

```csharp
if (!ShouldCopyRuntimeTemplateFile(relativeRuntimePath)) {
    continue;
}
```

and implement `ShouldCopyRuntimeTemplateFile(...)` from `RuntimeRequirementRegistrar` / `RuntimeRequirementCatalog` so disabled feature-owned helpers are skipped entirely.

- [ ] **Step 4: Delete the cleanup seam**

Delete:

```csharp
new CPPGeneratedOutputAdapter().Apply(outputFolder, Options);
PruneDisabledFeatureRuntimeFiles(outputFolder);
```

Delete the files:

```csharp
cs2.cpp/CPPGeneratedOutputAdapter.cs
cs2.cpp/CPPGeneratedSourcePruner.cs
cs2.cpp.tests/CPPGeneratedOutputAdapterTests.cs
```

- [ ] **Step 5: Run the audit and feature-pruning tests and verify they pass**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~CodeConverter_source_contains_no_generated_output_cleanup_pass|FullyQualifiedName~CPPFeaturePruningEndToEndTests"
```

Expected:

```text
PASS
```

- [ ] **Step 6: Commit the runtime inventory cutover**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add cs2.cpp/CPPCodeConverter.cs cs2.cpp.tests/CPPFeaturePruningEndToEndTests.cs cs2.cpp.tests/CPPGeneratedOutputContractAuditTests.cs
rtk git -C C:\dev\helworks\csharpcodegen rm cs2.cpp/CPPGeneratedOutputAdapter.cs cs2.cpp/CPPGeneratedSourcePruner.cs cs2.cpp.tests/CPPGeneratedOutputAdapterTests.cs
rtk git -C C:\dev\helworks\csharpcodegen commit -m "fix: select generated output inventory before write"
```

### Task 6: Final Verification Across Both Repositories

**Files:**
- Modify: any touched file above only if the verification runs expose one last contract mismatch
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Run the focused `csharpcodegen` contract slice**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithRotationValueTypes_EmitsFinalValidNativeSyntax|FullyQualifiedName~WriteOutput_WithLowercaseValueType_UsesCanonicalQualifiedArtifactsWithoutCompatibilityHeaders|FullyQualifiedName~CPPQualifiedNameAuditTests|FullyQualifiedName~CPPFeaturePruningEndToEndTests|FullyQualifiedName~CodeConverter_source_contains_no_generated_output_cleanup_pass"
```

Expected:

```text
PASS
```

- [ ] **Step 2: Run the focused `helengine` contract slice**

Run:

```powershell
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "FullyQualifiedName~EditorPlatformCodeCookService_does_not_define_generated_native_module_normalizer|FullyQualifiedName~Generated_core_regeneration_service_contains_no_native_cpp_rewrite_inventory"
```

Expected:

```text
PASS
```

- [ ] **Step 3: Run the full backend and editor test projects**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj
```

Expected:

```text
PASS
PASS
```

- [ ] **Step 4: Search both repos for banned downstream mutation entry points**

Run:

```powershell
rtk rg -n "NormalizeGeneratedNativeModuleSources|RewriteGeneratedFloat4OrientationTemporary|NormalizeMergedGeneratedSourceCaseInsensitiveConflicts|EnsureGeneratedIncludeCompatibilityShims|RewriteGeneratedIncludeReferences|CPPGeneratedOutputAdapter|CPPGeneratedSourcePruner|PruneDisabledFeatureRuntimeFiles" C:\dev\helworks\csharpcodegen C:\dev\helworks\helengine
```

Expected:

```text
No matches outside historical docs or git metadata.
```

- [ ] **Step 5: Commit any final cleanup exposed by the verification runs**

```powershell
rtk git -C C:\dev\helworks\csharpcodegen add -A
rtk git -C C:\dev\helworks\csharpcodegen commit -m "Verify generated output contract cutover"
rtk git -C C:\dev\helworks\helengine add -A
rtk git -C C:\dev\helworks\helengine commit -m "Verify generated output contract consumers"
```

Skip either commit if the verification run leaves that repository clean.

## Self-Review Checklist

- Spec coverage:
  - semantic lowering ownership is covered by Tasks 1-2
  - canonical artifact naming ownership is covered by Tasks 3-4
  - selected-before-write runtime inventory is covered by Task 5
  - downstream no-mutation boundaries are covered by Tasks 1, 3, and 6
- Placeholder scan:
  - no `TODO`, `TBD`, or "similar to Task N" placeholders remain
  - every validation step includes an exact command
  - every code-changing task includes concrete target snippets
- Type consistency:
  - the plan consistently uses `float3`, `float4`, `int2`, `helengine_int2`, `CPPCodeConverter`, `CPPVariableType`, `EditorPlatformCodeCookService`, and `EditorGeneratedCoreRegenerationService`
  - the deleted downstream helpers named in the tests match the production methods named in the cleanup tasks
