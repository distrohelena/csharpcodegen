using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that source-owned matrix helper implementations are no longer replaced by codegen-owned structural overrides.
/// </summary>
public sealed class CPPGeneratedFunctionBodyOverrideCatalogTests {
    /// <summary>
    /// Ensures codegen no longer claims ownership of source-authored matrix look-at bodies.
    /// </summary>
    [Fact]
    public void TryWriteOverride_WithGenericLookAtShape_DoesNotEmitStructuralOverride() {
        CPPGeneratedFunctionBodyOverrideCatalog catalog = new CPPGeneratedFunctionBodyOverrideCatalog();
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformProfile.GeneratedMathConvention = CPPGeneratedMathConventionKind.NativeColumnVector;
        ConversionFunction function = new ConversionFunction {
            Name = "CreateLookAt",
            InParameters = [
                CreateParameter("cameraPosition", "Example.Math.Vector3", ParameterModifier.Ref),
                CreateParameter("cameraTarget", "Example.Math.Vector3", ParameterModifier.Ref),
                CreateParameter("cameraUpVector", "Example.Math.Vector3", ParameterModifier.Ref),
                CreateParameter("result", "Example.Math.Matrix4x4", ParameterModifier.Out)
            ]
        };
        ConversionClass conversionClass = CreateScalarMatrixConversionClass();
        using StringWriter writer = new StringWriter();
        bool wroteOverride = catalog.TryWriteOverride(options, conversionClass, function, writer);

        Assert.False(wroteOverride);
        Assert.Equal(string.Empty, writer.ToString());
    }

    /// <summary>
    /// Ensures codegen no longer claims ownership of source-authored matrix multiply bodies.
    /// </summary>
    [Fact]
    public void TryWriteOverride_WithGenericMultiplyShape_DoesNotEmitStructuralOverride() {
        CPPGeneratedFunctionBodyOverrideCatalog catalog = new CPPGeneratedFunctionBodyOverrideCatalog();
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformProfile.GeneratedMathConvention = CPPGeneratedMathConventionKind.NativeColumnVector;
        ConversionFunction function = new ConversionFunction {
            Name = "Multiply",
            InParameters = [
                CreateParameter("matrix1", "Example.Math.Matrix4x4", ParameterModifier.Ref),
                CreateParameter("matrix2", "Example.Math.Matrix4x4", ParameterModifier.Ref),
                CreateParameter("result", "Example.Math.Matrix4x4", ParameterModifier.Out)
            ]
        };
        ConversionClass conversionClass = CreateScalarMatrixConversionClass();
        using StringWriter writer = new StringWriter();
        bool wroteOverride = catalog.TryWriteOverride(options, conversionClass, function, writer);

        Assert.False(wroteOverride);
        Assert.Equal(string.Empty, writer.ToString());
    }

    /// <summary>
    /// Creates one function parameter descriptor with the supplied source type identity.
    /// </summary>
    /// <param name="name">Parameter name used by the generated body.</param>
    /// <param name="qualifiedTypeName">Caller-owned fully qualified source type identity.</param>
    /// <param name="modifier">Ref or out modifier captured during preprocessing.</param>
    /// <returns>Configured parameter descriptor.</returns>
    static ConversionVariable CreateParameter(string name, string qualifiedTypeName, ParameterModifier modifier) {
        return new ConversionVariable {
            Name = name,
            Modifier = modifier,
            VarType = new VariableType(VariableDataType.Object, qualifiedTypeName) {
                QualifiedTypeName = qualifiedTypeName
            }
        };
    }

    /// <summary>
    /// Creates one scalar matrix conversion class shape that exposes <c>M11</c> through <c>M44</c> so scalar-matrix matching can be exercised in isolation.
    /// </summary>
    /// <returns>Configured conversion class shape.</returns>
    static ConversionClass CreateScalarMatrixConversionClass() {
        ConversionClass conversionClass = new ConversionClass();
        conversionClass.Variables.Add(new ConversionVariable { Name = "M11" });
        conversionClass.Variables.Add(new ConversionVariable { Name = "M44" });
        return conversionClass;
    }
}
