using System.Collections.Generic;
using System.IO;
using cs2.core;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that class emission skips invalid include references collected during preprocessing.
/// </summary>
public class CPPClassEmitterIncludeFilteringTests {
    /// <summary>
    /// Ensures raw inference or unknown sentinel tokens do not become generated include directives.
    /// </summary>
    [Fact]
    public void Emit_WithInvalidReferencedClasses_SkipsInvalidIncludes() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "IncludeCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.ReferencedClasses.Add("var");
        conversionClass.ReferencedClasses.Add("?");

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.DoesNotContain("#include \"var.hpp\"", header);
        Assert.DoesNotContain("#include \"?.hpp\"", header);
    }

    /// <summary>
    /// Ensures external generic runtime references still emit concrete generated header includes in the owning header.
    /// </summary>
    [Fact]
    public void Emit_WithExternalGenericField_EmitsGeneratedInclude() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "ExternalGenericCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Machine",
            VarType = VariableUtil.GetVarType("helengine.FiniteStateMachine<city.game.TestState>")
        });

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.Contains("#include \"FiniteStateMachine_1.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("FiniteStateMachine_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("FiniteStateMachine<", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime generics and already-emitted external generic aliases keep their existing native header/type names.
    /// </summary>
    [Fact]
    public void Emit_WithRuntimeAndPreNormalizedGenericFields_UsesStableTypeAndHeaderNames() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "RuntimeGenericCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "PressedKeys",
            VarType = VariableUtil.GetVarType("Array<Keys>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "ActiveVoiceIds",
            VarType = VariableUtil.GetVarType("System.Collections.Generic.HashSet<Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Processor",
            VarType = VariableUtil.GetVarType("IContentProcessor_1<Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Properties",
            VarType = VariableUtil.GetVarType("CollidableProperty_1<Single>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Updater",
            VarType = VariableUtil.GetVarType("Action_1<Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Reader",
            VarType = VariableUtil.GetVarType("Func_2<Int32, Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Lanes",
            VarType = VariableUtil.GetVarType("Vector256_1<Single>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Thunk",
            VarType = new VariableType(
                VariableDataType.Callback,
                "FunctionPointer_2",
                genericArgs: new List<VariableType> {
                    new VariableType(VariableDataType.Void, "void"),
                    new VariableType(VariableDataType.Int32, "Int32")
                })
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Frames",
            VarType = VariableUtil.GetVarType("Stack_1<Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Items",
            VarType = VariableUtil.GetVarType("List_1<Int32>")
        });

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Region",
            VarType = VariableUtil.GetVarType("ValueTuple_2<Int32, Int32>")
        });

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.Contains("#include \"runtime/array.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/native_hash_set.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"IContentProcessor_1.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"CollidableProperty_1.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"system/action.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"system/func.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"system/runtime/intrinsics/vector256.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/function_pointer.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/native_stack.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/native_list.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/native_tuple.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("Array<", header, StringComparison.Ordinal);
        Assert.Contains("HashSet<", header, StringComparison.Ordinal);
        Assert.Contains("IContentProcessor_1<", header, StringComparison.Ordinal);
        Assert.Contains("CollidableProperty_1<", header, StringComparison.Ordinal);
        Assert.Contains("Action<int32_t>*", header, StringComparison.Ordinal);
        Assert.Contains("Func<int32_t, int32_t>*", header, StringComparison.Ordinal);
        Assert.Contains("Vector256<float>", header, StringComparison.Ordinal);
        Assert.Contains("FunctionPointer<void, int32_t>", header, StringComparison.Ordinal);
        Assert.Contains("Stack<int32_t>*", header, StringComparison.Ordinal);
        Assert.Contains("List<int32_t>*", header, StringComparison.Ordinal);
        Assert.Contains("ValueTuple<int32_t, int32_t>", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Array_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("HashSet_1.hpp", header, StringComparison.Ordinal);
        Assert.DoesNotContain("IContentProcessor_1_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("CollidableProperty_1_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Action_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Func_2<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector256_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("FunctionPointer_2<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Stack_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("List_1<", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTuple_2<", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures primitive fields do not emit nonexistent synthetic native scalar headers.
    /// </summary>
    [Fact]
    public void Emit_WithPrimitiveField_DoesNotEmitSyntheticPrimitiveInclude() {
        CPPClassEmitter emitter = new CPPClassEmitter(CppProcessorTestHarness.CreateProcessor(), new CPPProgram(new CPPConversionRules()));
        ConversionClass conversionClass = new ConversionClass {
            Name = "PrimitiveCarrier",
            DeclarationType = MemberDeclarationType.Class
        };

        conversionClass.Variables.Add(new ConversionVariable {
            Name = "Count",
            VarType = new VariableType(VariableDataType.Int32, "Int32")
        });

        using StringWriter headerWriter = new StringWriter();
        using StringWriter sourceWriter = new StringWriter();

        emitter.Emit(conversionClass, headerWriter, sourceWriter);

        string header = headerWriter.ToString();

        Assert.DoesNotContain("#include \"int.hpp\"", header, StringComparison.Ordinal);
    }

}
