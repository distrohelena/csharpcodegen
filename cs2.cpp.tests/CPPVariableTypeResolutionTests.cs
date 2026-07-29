using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies C++ variable-type rendering preserves shared runtime generic contracts and stable generated generic identifiers.
/// </summary>
public sealed class CPPVariableTypeResolutionTests {
    /// <summary>
    /// Ensures shared runtime array references render the runtime <c>Array&lt;T&gt;</c> token instead of a synthetic generated external type name.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeArrayGenericType_UsesSharedRuntimeArrayToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "Array",
            genericArgs: [
                new VariableType(VariableDataType.Int32, "Int32")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("Array<int32_t>", emittedTypeName);
    }

    /// <summary>
    /// Ensures generated generic references that already carry one emitted arity suffix do not receive a duplicate suffix during C++ rendering.
    /// </summary>
    [Fact]
    public void ToCPPString_WithGeneratedGenericTypeNameAlreadyUsingAritySuffix_DoesNotAppendSecondSuffix() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        ConversionClass generatedClass = new ConversionClass {
            Name = "CollidableProperty",
            Program = program,
            DeclarationType = MemberDeclarationType.Class,
            GenericArgs = ["T"]
        };
        program.Classes.Add(generatedClass);
        program.SetReachableGeneratedTypes(program.Classes);

        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "CollidableProperty_1",
            genericArgs: [
                new VariableType(VariableDataType.Single, "Single")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("CollidableProperty_1<float>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime delegate metadata names still collapse to the shared native <c>Action&lt;TArgs...&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeActionGenericTypeUsingAritySuffix_UsesSharedRuntimeActionToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "Action_1",
            genericArgs: [
                new VariableType(VariableDataType.Int32, "Int32")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("Action<int32_t>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime function metadata names still collapse to the shared native <c>Func&lt;TArgs..., TResult&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeFuncGenericTypeUsingAritySuffix_UsesSharedRuntimeFuncToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "Func_2",
            genericArgs: [
                new VariableType(VariableDataType.Int32, "Int32"),
                new VariableType(VariableDataType.Boolean, "Boolean")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("Func<int32_t, bool>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime function-pointer metadata names still collapse to the shared native <c>FunctionPointer&lt;TReturn, TArgs...&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeFunctionPointerGenericTypeUsingAritySuffix_UsesSharedRuntimeFunctionPointerToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "FunctionPointer_3",
            genericArgs: [
                new VariableType(VariableDataType.Void, "Void"),
                new VariableType(VariableDataType.Int32, "Int32"),
                new VariableType(VariableDataType.Boolean, "Boolean")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("FunctionPointer<void, int32_t, bool>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime nullable metadata names still collapse to the shared native <c>Nullable&lt;T&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeNullableGenericTypeUsingAritySuffix_UsesSharedRuntimeNullableToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "Nullable_1",
            genericArgs: [
                new VariableType(VariableDataType.Single, "Single")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("Nullable<float>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime tuple metadata names still collapse to the shared native <c>ValueTuple&lt;TItems...&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeValueTupleGenericTypeUsingAritySuffix_UsesSharedRuntimeValueTupleToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "ValueTuple_2",
            genericArgs: [
                new VariableType(VariableDataType.Int32, "Int32"),
                new VariableType(VariableDataType.Boolean, "Boolean")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("ValueTuple<int32_t, bool>", emittedTypeName);
    }

    /// <summary>
    /// Ensures suffixed runtime stack metadata names still collapse to the shared native <c>Stack&lt;T&gt;</c> helper surface.
    /// </summary>
    [Fact]
    public void ToCPPString_WithRuntimeStackGenericTypeUsingAritySuffix_UsesSharedRuntimeStackToken() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        VariableType variableType = new VariableType(
            VariableDataType.Object,
            "Stack_1",
            genericArgs: [
                new VariableType(VariableDataType.Int32, "Int32")
            ]);

        string emittedTypeName = variableType.ToCPPString(program);

        Assert.Equal("Stack<int32_t>", emittedTypeName);
    }
}
