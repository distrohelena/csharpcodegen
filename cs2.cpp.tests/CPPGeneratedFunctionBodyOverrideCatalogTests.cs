using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generated function body overrides are selected from structural function shape rather than caller-owned engine type names.
/// </summary>
public class CPPGeneratedFunctionBodyOverrideCatalogTests {
    /// <summary>
    /// Ensures the native-column-vector look-at override applies to arbitrary caller-owned vector and matrix type identities.
    /// </summary>
    [Fact]
    public void TryWriteOverride_WithGenericLookAtShape_DoesNotRequireEngineTypeNames() {
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

        using StringWriter writer = new StringWriter();
        bool wroteOverride = catalog.TryWriteOverride(options, function, writer);
        string output = writer.ToString();

        Assert.True(wroteOverride);
        Assert.Contains("float3::Normalize(cameraPosition - cameraTarget)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("helengine.", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the native-column-vector multiply override applies to arbitrary caller-owned matrix type identities.
    /// </summary>
    [Fact]
    public void TryWriteOverride_WithGenericMultiplyShape_DoesNotRequireEngineTypeNames() {
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

        using StringWriter writer = new StringWriter();
        bool wroteOverride = catalog.TryWriteOverride(options, function, writer);
        string output = writer.ToString();

        Assert.True(wroteOverride);
        Assert.Contains("matrix2.M11 * matrix1.M11", output, StringComparison.Ordinal);
        Assert.DoesNotContain("helengine.", output, StringComparison.OrdinalIgnoreCase);
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
}
