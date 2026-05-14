# C++ Value-Type Property Lowering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `cs2.cpp` so value-type properties, accessors, member access chains, and generated helper calls consistently emit direct-value C++ instead of incorrect pointer syntax.

**Architecture:** Centralize the native pointer-versus-value decision in the C++ backend type-shape layer, then route both `CPPClassEmitter` and `CPPConversiorProcessor` through that shared rule. Validate the cleanup with focused emitter and compile-regression tests anchored by the `AxisRotationComponent` failure patterns and then re-run the real export path.

**Tech Stack:** C#, Roslyn, xUnit, `cs2.cpp`, `cs2.cpp.tests`, .NET 9

---

## File Structure

- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
  - Own the native type-shape decision used by the C++ backend.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
  - Emit property storage and accessor signatures from the shared type-shape rules.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
  - Lower member access, property chains, and setter calls with the same shape rules.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPClassEmitterTests.cs`
  - Add focused property-emission regressions for value types.
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
  - Add broader compile-regression coverage for static value-type access, property chains, and mixed receiver shapes.

## Task 1: Lock In the Broken Property Shape With Tests

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPClassEmitterTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPClassEmitterTests.cs`

- [ ] **Step 1: Add a failing emitter regression for value-type auto-properties**

```csharp
    /// <summary>
    /// Ensures value-type auto-properties emit direct-value storage and accessors instead of pointer forms.
    /// </summary>
    [Fact]
    public void EmitAutoProperty_WithValueType_UsesDirectValueStorageAndAccessors() {
        CPPClassEmitter emitter = CreateEmitter();
        ConversionClass conversionClass = new ConversionClass {
            Name = "AxisRotationComponent",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Axis",
            AccessType = MemberAccessType.Public,
            IsGet = true,
            IsSet = true,
            VarType = new VariableType(typeName: "float3")
        });

        (string header, string source) = Emit(emitter, conversionClass);

        Assert.Contains("float3 Axis;", header);
        Assert.Contains("float3 get_Axis();", header);
        Assert.Contains("void set_Axis(float3 value);", header);
        Assert.DoesNotContain("float3* Axis;", header, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Axis(float3* value)", header, StringComparison.Ordinal);
        Assert.Contains("float3 AxisRotationComponent::get_Axis()", source);
        Assert.Contains("void AxisRotationComponent::set_Axis(float3 value)", source);
    }
```

- [ ] **Step 2: Run the emitter test to verify the current backend fails**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~EmitAutoProperty_WithValueType_UsesDirectValueStorageAndAccessors"
```

Expected:

```text
FAIL
```

- [ ] **Step 3: Add a failing bridge-accessor regression for value-type signatures**

```csharp
    /// <summary>
    /// Ensures inherited value-type properties preserve direct-value bridge signatures.
    /// </summary>
    [Fact]
    public void EmitInheritedPropertyBridge_WithValueType_UsesDirectValueSetterSignature() {
        ConversionOutput output = RunConversion("""
            public struct float3 {
                public float X;
            }

            public class EntityBase {
                float3 orientation;

                public float3 LocalOrientation {
                    get {
                        return orientation;
                    }
                    set {
                        orientation = value;
                    }
                }
            }

            public class Entity : EntityBase {
            }
            """);

        string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "Entity.hpp"));

        Assert.Contains("float3 get_LocalOrientation();", headerOutput);
        Assert.Contains("void set_LocalOrientation(float3 value);", headerOutput);
        Assert.DoesNotContain("set_LocalOrientation(float3* value)", headerOutput, StringComparison.Ordinal);
    }
```

- [ ] **Step 4: Run the bridge-signature test to verify it fails**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~EmitInheritedPropertyBridge_WithValueType_UsesDirectValueSetterSignature"
```

Expected:

```text
FAIL
```

## Task 2: Add a Compile Regression That Mirrors the Export Failure

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Add a failing compile-regression test for mixed value-type and property access lowering**

```csharp
    /// <summary>
    /// Ensures value-type properties, static value members, and mixed receiver shapes emit the correct native access operators.
    /// </summary>
    [Fact]
    public void WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes() {
        string source = """
            public struct float3 {
                public float X;
                public float Y;
                public float Z;

                public static float3 Zero {
                    get {
                        return new float3();
                    }
                }

                public static float3 Normalize(float3 value) {
                    return value;
                }
            }

            public struct float4 {
                public static float4 CreateFromAxisAngle(float3 axis, float angle) {
                    return new float4();
                }
            }

            public class Core {
                static Core instance;

                public static Core Instance {
                    get {
                        return instance;
                    }
                }

                public float DeltaTime {
                    get {
                        return 1.0f;
                    }
                }
            }

            public class Entity {
                float4 localOrientation;

                public float4 LocalOrientation {
                    get {
                        return localOrientation;
                    }
                    set {
                        localOrientation = value;
                    }
                }
            }

            public class AxisRotationComponent {
                public float3 Axis { get; set; }
                public Entity Parent { get; set; }
                public float AngularSpeedRadiansPerSecond { get; set; }

                public void Update() {
                    if (Axis.Equals(float3.Zero)) {
                        return;
                    }

                    float3 normalizedAxis = float3.Normalize(Axis);
                    float deltaAngleRadians = AngularSpeedRadiansPerSecond * Core.Instance.DeltaTime;
                    float4 deltaRotation = float4.CreateFromAxisAngle(normalizedAxis, deltaAngleRadians);
                    Parent.LocalOrientation = deltaRotation;
                }
            }
            """;

        ConversionOutput output = RunConversion(source);
        string headerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AxisRotationComponent.hpp"));
        string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "AxisRotationComponent.cpp"));

        Assert.Contains("float3 Axis;", headerOutput);
        Assert.Contains("void set_Axis(float3 value);", headerOutput);
        Assert.Contains("if (this->get_Axis().Equals(float3::get_Zero()))", sourceOutput);
        Assert.Contains("float3 normalizedAxis = float3::Normalize(this->get_Axis());", sourceOutput);
        Assert.Contains("float deltaAngleRadians = this->get_AngularSpeedRadiansPerSecond() * Core::get_Instance()->get_DeltaTime();", sourceOutput);
        Assert.Contains("float4 deltaRotation = float4::CreateFromAxisAngle(normalizedAxis, deltaAngleRadians);", sourceOutput);
        Assert.Contains("this->get_Parent()->set_LocalOrientation(deltaRotation);", sourceOutput);
        Assert.DoesNotContain("float3->", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Core->Instance", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Parent->LocalOrientation", sourceOutput, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the compile-regression test to capture the current failure**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes"
```

Expected:

```text
FAIL
```

## Task 3: Centralize the Native Type-Shape Decision

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Add explicit native-shape helpers to `CPPVariableType`**

```csharp
    /// <summary>
    /// Gets a value indicating whether the emitted native shape is a direct value rather than a pointer.
    /// </summary>
    public bool IsDirectValue {
        get {
            return !IsPointer;
        }
    }

    /// <summary>
    /// Gets the member access operator that matches the emitted native shape.
    /// </summary>
    public string MemberAccessOperator {
        get {
            if (IsPointer) {
                return "->";
            }

            return ".";
        }
    }
```

- [ ] **Step 2: Add one processor helper that resolves the emitted receiver access form**

```csharp
    /// <summary>
    /// Gets the native member access operator for a lowered receiver expression result.
    /// </summary>
    /// <param name="receiverResult">Lowered receiver metadata.</param>
    /// <returns><c>.</c> for direct values and <c>-></c> for pointer-shaped receivers.</returns>
    string GetMemberAccessOperator(ExpressionResult receiverResult) {
        if (receiverResult?.Type == null) {
            throw new InvalidOperationException("Receiver type is required to determine member access lowering.");
        }

        CPPTypeData typeData;
        ConvertToCPPType(receiverResult.Type, out typeData);
        if (typeData.IsPointer) {
            return "->";
        }

        return ".";
    }
```

- [ ] **Step 3: Run the focused compile-regression test and confirm it still fails before callers are rewired**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes"
```

Expected:

```text
FAIL
```

## Task 4: Rewire Property Emission to Use the Shared Shape Rules

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPClassEmitterTests.cs`

- [ ] **Step 1: Update property storage and accessor emission to consume the shared type shape**

```csharp
            string typeName = ConvertType(variable.VarType, conversionClass);

            if (variable.IsGet) {
                headerWriter.WriteLine($"    {staticKeyword}{typeName} get_{variable.Name}();");
                WriteTemplateDeclaration(conversionClass, sourceWriter);
                sourceWriter.WriteLine($"{typeName} {GetQualifiedClassName(conversionClass)}::get_{variable.Name}()");
                sourceWriter.WriteLine("{");
                sourceWriter.WriteLine(variable.IsStatic
                    ? $"return {GetQualifiedClassName(conversionClass)}::{variable.Name};"
                    : $"return this->{variable.Name};");
                sourceWriter.WriteLine("}");
                sourceWriter.WriteLine();
            }

            if (variable.IsSet) {
                headerWriter.WriteLine($"    {staticKeyword}void set_{variable.Name}({typeName} value);");
                WriteTemplateDeclaration(conversionClass, sourceWriter);
                sourceWriter.WriteLine($"void {GetQualifiedClassName(conversionClass)}::set_{variable.Name}({typeName} value)");
                sourceWriter.WriteLine("{");
                sourceWriter.WriteLine(variable.IsStatic
                    ? $"{GetQualifiedClassName(conversionClass)}::{variable.Name} = value;"
                    : $"this->{variable.Name} = value;");
                sourceWriter.WriteLine("}");
                sourceWriter.WriteLine();
            }
```

- [ ] **Step 2: Ensure any bridge accessor emission uses the same `ConvertType(variable.VarType, ...)` shape**

```csharp
            string typeName = ConvertType(VariableUtil.GetVarType(interfacePropertySymbol.Type), conversionClass);
```

- [ ] **Step 3: Run the emitter-focused tests and verify they pass**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~EmitAutoProperty_WithValueType_UsesDirectValueStorageAndAccessors|FullyQualifiedName~EmitInheritedPropertyBridge_WithValueType_UsesDirectValueSetterSignature"
```

Expected:

```text
PASS
```

## Task 5: Rewire Member Access and Property Chains to Use the Shared Shape Rules

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Replace ad hoc receiver operator selection with the shared helper**

```csharp
            string memberAccessOperator = GetMemberAccessOperator(result);
```

- [ ] **Step 2: Apply the shared operator rule in object initializer setter lowering and direct member access lowering**

```csharp
            lines.Add(objectName);
            lines.Add(memberAccessOperator);
            lines.Add($"set_{propertyName}(");
```

```csharp
            lines.Add(receiverAccessOperator);
            lines.Add(memberName);
```

- [ ] **Step 3: Preserve explicit static owner lowering for generated static property chains**

```csharp
            lines.Add(ownerTypeName);
            lines.Add("::");
            lines.Add($"get_{propertySymbol.Name}()");
```

- [ ] **Step 4: Run the compile-regression test and verify it passes**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes"
```

Expected:

```text
PASS
```

## Task 6: Protect Existing Property Lowering Behavior

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Run the existing property-lowering regression coverage that could be affected by the cleanup**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~WriteOutput_WithStaticGeneratedPropertyChain_EmitsOwnerIncludeAndStaticGetterAccess|FullyQualifiedName~WriteOutput_WithExpressionBodiedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithGetterBodiedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithInheritedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithComputedPropertyMemberAccess_UsesGetterAndSetterCalls|FullyQualifiedName~WriteOutput_WithInterfaceTypedPropertyReceiver_UsesGetterCall"
```

Expected:

```text
PASS
```

- [ ] **Step 2: If any assertion now expects the wrong operator for a direct value, update the assertion to the correct emitted text and rerun the same filter**

```csharp
        Assert.Contains("return camera->get_Viewport().X;", sourceOutput);
```

## Task 7: Run the Full Backend Test Slice

**Files:**
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPClassEmitterTests.cs`
- Test: `C:\dev\helworks\csharpcodegen\cs2.cpp.tests\CPPCompileValidationRegressionTests.cs`

- [ ] **Step 1: Run the combined focused backend suite**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj --filter "FullyQualifiedName~EmitAutoProperty_WithValueType_UsesDirectValueStorageAndAccessors|FullyQualifiedName~EmitInheritedPropertyBridge_WithValueType_UsesDirectValueSetterSignature|FullyQualifiedName~WriteOutput_WithValueTypePropertyAndStaticAccessChain_UsesConsistentNativeShapes|FullyQualifiedName~WriteOutput_WithStaticGeneratedPropertyChain_EmitsOwnerIncludeAndStaticGetterAccess|FullyQualifiedName~WriteOutput_WithExpressionBodiedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithGetterBodiedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithInheritedPropertyReceiver_UsesGetterCall|FullyQualifiedName~WriteOutput_WithComputedPropertyMemberAccess_UsesGetterAndSetterCalls|FullyQualifiedName~WriteOutput_WithInterfaceTypedPropertyReceiver_UsesGetterCall"
```

Expected:

```text
PASS
```

- [ ] **Step 2: Run the full `cs2.cpp.tests` project**

Run:

```powershell
rtk dotnet test C:\dev\helworks\csharpcodegen\cs2.cpp.tests\cs2.cpp.tests.csproj
```

Expected:

```text
PASS
```

## Task 8: Verify the Original Export Path

**Files:**
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPClassEmitter.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\CPPConversiorProcessor.cs`
- Modify: `C:\dev\helworks\csharpcodegen\cs2.cpp\model\CPPVariableType.cs`
- Test: `C:\dev\helprojs\city\assets\codebase\rendering\AxisRotationComponent.cs`

- [ ] **Step 1: Re-run the city gameplay code generation path that feeds the Windows export**

Run:

```powershell
rtk dotnet build C:\dev\helprojs\city\user_settings\generated_code\projects\gameplay\gameplay.csproj
```

Expected:

```text
PASS
```

- [ ] **Step 2: Re-run the Windows export path and inspect the generated `AxisRotationComponent` output if the export still fails**

Run:

```powershell
rtk dotnet run --project C:\dev\helworks\helengine\engine\helengine.editor\helengine.editor.csproj
```

Expected:

```text
AxisRotationComponent native export no longer fails with value-type pointer-shape errors.
```

- [ ] **Step 3: If the editor-driven export is not practical from the CLI, inspect the regenerated emitted C++ text directly**

```csharp
// Verification target after regeneration:
// - float3 Axis;
// - void set_Axis(float3 value);
// - float3::Normalize(this->get_Axis());
// - Core::get_Instance()->get_DeltaTime();
// - this->get_Parent()->set_LocalOrientation(deltaRotation);
```

## Self-Review Checklist

- Spec coverage:
  - centralized native type-shape rule is covered by Tasks 3, 4, and 5
  - property emission cleanup is covered by Tasks 1 and 4
  - member access and static chain cleanup is covered by Tasks 2 and 5
  - strict regression protection is covered by Tasks 6 and 7
  - original `AxisRotationComponent` export verification is covered by Task 8
- Placeholder scan:
  - no `TODO`, `TBD`, or “similar to” placeholders remain
  - every verification step includes an explicit command or concrete inspection target
- Type consistency:
  - the plan consistently uses `Axis`, `LocalOrientation`, `Parent`, `Core.Instance`, and `float3.Normalize`
  - direct-value setter signatures consistently use `float3 value`
