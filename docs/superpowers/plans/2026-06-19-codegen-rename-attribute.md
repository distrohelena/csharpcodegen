# CodeGenRename Attribute Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated attributes assembly and a type-level `CodeGenRename` contract that the C++ backend honors when resolving generated type names.

**Architecture:** Implement this in three slices. First, add a tiny `cs2.attributes` assembly and reference it from the test fixtures that need the attribute. Second, extend the shared conversion model so declared source types can carry one validated emitted-name override. Third, update the C++ naming pipeline and lookups to use one canonical emitted-type-name path, then verify attribute and configured-remap behavior through focused regression tests.

**Tech Stack:** C#, Roslyn, xUnit, .NET 9, `cs2.core`, `cs2.cpp`, `cs2.cpp.tests`, `rtk`

---

## File Structure

- Create: `C:\dev\helworks\csharpcodegen\cs2.attributes\cs2.attributes.csproj`
  - Attributes-only assembly referenced by source projects and generator tests.
- Create: `C:\dev\helworks\csharpcodegen\cs2.attributes\CodeGenRenameAttribute.cs`
  - Declares the type-level rename contract.
- Modify: `C:\dev\helworks\csharpcodegen\codegen.sln`
  - Includes the new attributes project in the solution.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\model\ConversionClass.cs`
  - Stores resolved source rename metadata and exposes one canonical emitted-name helper.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\ConversionPreProcessor.cs`
  - Reads `CodeGenRenameAttribute` from Roslyn type declarations and validates payload presence.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\ConversionProgram.cs`
  - Adds canonical generated-type lookup helpers that key off final emitted names.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPCodeConverter.cs`
  - Keeps configured remap precedence explicit and validates collisions against final emitted names.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPProgram.cs`
  - Rebuilds emitted file-stem groupings from canonical emitted names.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
  - Uses the shared emitted-name helper everywhere it currently needs generated type names.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\util\CPPUtils.cs`
  - Keeps inheritance rendering aligned with canonical emitted names.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`
  - References `cs2.attributes` so temporary fixture projects can compile the attribute.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
  - Adds focused end-to-end tests for attribute rename behavior, precedence, and duplicate-name failures.

### Task 1: Add The Attributes Assembly And Lock The Expected Public Contract

**Files:**
- Create: `C:\dev\helworks\csharpcodegen\cs2.attributes\cs2.attributes.csproj`
- Create: `C:\dev\helworks\csharpcodegen\cs2.attributes\CodeGenRenameAttribute.cs`
- Modify: `C:\dev\helworks\csharpcodegen\codegen.sln`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj`

- [ ] **Step 1: Create the attributes project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add the first source-level attribute**

```csharp
namespace cs2.attributes;

[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Interface |
    AttributeTargets.Enum |
    AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CodeGenRenameAttribute : Attribute {
    public string Name { get; }

    public CodeGenRenameAttribute(string name) {
        Name = name;
    }
}
```

- [ ] **Step 3: Add the project to the solution and test project**

```xml
<ProjectReference Include="..\cs2.attributes\cs2.attributes.csproj" />
```

- [ ] **Step 4: Run the test project build to verify the new assembly wiring**

Run: `rtk dotnet build cs2.cpp.tests\cs2.cpp.tests.csproj -v minimal`

Expected: build succeeds and the test project resolves `cs2.attributes`

- [ ] **Step 5: Commit the isolated attributes-assembly slice if the tree is safe**

```bash
git add codegen.sln cs2.attributes cs2.cpp.tests/cs2.cpp.tests.csproj
git commit -m "feat: add codegen attributes assembly"
```

### Task 2: Write The Failing Rename Regression Tests

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Add one test that proves type-level `CodeGenRename` changes emitted C++ names**

```csharp
[Fact]
public void WriteOutput_WithCodeGenRenameAttribute_UsesRequestedEmittedTypeName() {
    IReadOnlyDictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["Fixture.cs"] = """
            using cs2.attributes;

            namespace Example {
                [CodeGenRename("RenamedThing")]
                public class OriginalThing {
                }

                public class Fixture {
                    public OriginalThing Value;
                }
            }
            """
    };

    ConversionOutput output = RunConversion(sources, includeAttributesProjectReference: true);
    string fixtureHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.hpp"));
    string renamedHeader = File.ReadAllText(Path.Combine(output.OutputPath, "RenamedThing.hpp"));

    Assert.Contains("::RenamedThing* Value;", fixtureHeader, StringComparison.Ordinal);
    Assert.DoesNotContain("OriginalThing.hpp", output.GeneratedText, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Add one test that proves configured type remaps still override the source attribute**

```csharp
[Fact]
public void WriteOutput_WithTypeRemapAndCodeGenRename_PrefersConfiguredRemap() {
    string source = """
        using cs2.attributes;

        namespace Example {
            [CodeGenRename("FromAttribute")]
            public class OriginalThing {
            }

            public class Fixture {
                public OriginalThing Value;
            }
        }
        """;

    ConversionOutput output = RunConversionWithTypeRemaps(
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Fixture.cs"] = source },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Example.OriginalThing"] = "Example.FromRemap" },
        includeAttributesProjectReference: true);

    string fixtureHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Fixture.hpp"));
    Assert.Contains("::FromRemap* Value;", fixtureHeader, StringComparison.Ordinal);
}
```

- [ ] **Step 3: Add one test that proves duplicate final emitted names fail clearly**

```csharp
[Fact]
public void WriteOutput_WithDuplicateFinalEmittedTypeNames_Throws() {
    IReadOnlyDictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["Fixture.cs"] = """
            using cs2.attributes;

            namespace Example {
                [CodeGenRename("SharedName")]
                public class First {
                }

                [CodeGenRename("SharedName")]
                public class Second {
                }
            }
            """
    };

    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
        () => RunConversion(sources, includeAttributesProjectReference: true));

    Assert.Contains("SharedName", exception.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Extend the temporary-project helper so fixture projects can reference `cs2.attributes`**

```csharp
static string CreateProjectFile(bool allowUnsafe, bool includeAttributesProjectReference) {
    string attributesReference = includeAttributesProjectReference
        ? "  <ItemGroup>\n" +
          "    <ProjectReference Include=\"<ABSOLUTE_PATH_TO_CS2_ATTRIBUTES_CSPROJ>\" />\n" +
          "  </ItemGroup>\n"
        : string.Empty;

    return
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net9.0</TargetFramework>\n" +
        "    <LangVersion>preview</LangVersion>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "    <Nullable>disable</Nullable>\n" +
        "  </PropertyGroup>\n" +
        attributesReference +
        "</Project>\n";
}
```

- [ ] **Step 5: Run only the new tests and verify they fail for the right reason before implementation**

Run: `rtk dotnet test cs2.cpp.tests\cs2.cpp.tests.csproj --filter CodeGenRename -v minimal`

Expected: tests fail because emitted names still ignore the attribute or collisions are not yet validated

### Task 3: Implement Shared Rename Metadata And Canonical Emitted Name Resolution

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\model\ConversionClass.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\ConversionPreProcessor.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.core\ConversionProgram.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPCodeConverter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPProgram.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\util\CPPUtils.cs`

- [ ] **Step 1: Add source rename state and one canonical emitted-name helper to `ConversionClass`**

```csharp
public string SourceCodeGenRename { get; set; }

public string GetEmittedTypeName() {
    if (Program != null && Program.TryGetConfiguredTypeName(this, out string configuredTypeName)) {
        return configuredTypeName;
    }

    if (!string.IsNullOrWhiteSpace(SourceCodeGenRename)) {
        return SourceCodeGenRename;
    }

    return Name;
}
```

- [ ] **Step 2: Read `CodeGenRenameAttribute` during preprocessing**

```csharp
foreach (AttributeData attribute in cl.TypeSymbol.GetAttributes()) {
    if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "cs2.attributes.CodeGenRenameAttribute", StringComparison.Ordinal)) {
        continue;
    }

    string requestedName = attribute.ConstructorArguments.Length > 0
        ? attribute.ConstructorArguments[0].Value as string ?? string.Empty
        : string.Empty;
    if (string.IsNullOrWhiteSpace(requestedName)) {
        throw new InvalidOperationException($"Type '{cl.TypeSymbol.ToDisplayString()}' must supply one non-empty CodeGenRename value.");
    }

    cl.SourceCodeGenRename = requestedName;
}
```

- [ ] **Step 3: Add canonical generated-class lookup helpers to `ConversionProgram`**

```csharp
public bool TryGetConfiguredTypeName(ConversionClass conversionClass, out string configuredTypeName) { ... }
public ConversionClass FindGeneratedClass(string typeName, int genericArity) { ... }
public ConversionClass FindGeneratedClass(INamedTypeSymbol typeSymbol) { ... }
```

- [ ] **Step 4: Validate duplicate final emitted names before emission**

```csharp
HashSet<string> collisions = Program.GetBaseEmittedTypeNameCollisions(candidate => candidate.GetEmittedTypeName());
if (collisions.Count > 0) {
    throw new InvalidOperationException(...);
}
```

- [ ] **Step 5: Keep `CPPProgram`, `CPPClassEmitter`, and `CPPUtils` on the shared emitted-name helper**

```csharp
string emittedTypeName = conversionClass.GetEmittedTypeName();
ConversionClass generatedClass = program.FindGeneratedClass(variableType);
```

- [ ] **Step 6: Run the focused CodeGenRename tests and verify they now pass**

Run: `rtk dotnet test cs2.cpp.tests\cs2.cpp.tests.csproj --filter CodeGenRename -v minimal`

Expected: all `CodeGenRename` tests pass

- [ ] **Step 7: Run a broader regression slice around existing type-remap behavior**

Run: `rtk dotnet test cs2.cpp.tests\cs2.cpp.tests.csproj --filter ConfiguredTypeRemaps -v minimal`

Expected: existing remap tests stay green

- [ ] **Step 8: Commit the implementation**

```bash
git add cs2.attributes codegen.sln cs2.core cs2.cpp cs2.cpp.tests
git commit -m "feat: add CodeGenRename type attribute"
```

## Self-Review

- Spec coverage:
  - dedicated attributes assembly: covered in Task 1
  - type-only `CodeGenRename`: covered in Tasks 1 and 3
  - C++ emitted-name adoption: covered in Task 3
  - configured remap precedence and collision validation: covered in Tasks 2 and 3
- Placeholder scan:
  - no `TODO` or deferred validation markers remain
- Type consistency:
  - plan uses `SourceCodeGenRename`, `GetEmittedTypeName`, and `TryGetConfiguredTypeName` consistently across tasks

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-19-codegen-rename-attribute.md`. The user already chose inline execution, so implementation should continue in this session using the plan order above.
