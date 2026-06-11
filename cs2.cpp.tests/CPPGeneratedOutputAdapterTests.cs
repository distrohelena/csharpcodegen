namespace cs2.cpp.tests;

/// <summary>
/// Verifies that generated-output adaptation runs after runtime files are emitted.
/// </summary>
public class CPPGeneratedOutputAdapterTests {
    /// <summary>
    /// Ensures the generated-output adapter leaves platform-specific runtime files untouched now that platform rewrites live outside csharpcodegen.
    /// </summary>
    [Fact]
    public void Apply_ForCustomPlatform_DoesNotRewritePlatformSpecificRuntimeFiles() {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string originalSource = """
void float4x4::CreateTranslation(float x, float y, float z, ::float4x4& result)
{
result.M41 = x;
result.M42 = y;
result.M43 = z;
}
""";
        File.WriteAllText(Path.Combine(root, "float4x4.cpp"), originalSource);

        CPPConversionOptions options = new() {
            CompilerProfile = CPPCompilerProfile.CreateGcc(),
            PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("retroppc", false, CPPGeneratedMathConventionKind.NativeColumnVector, 4),
            RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
            CollectDiagnostics = true,
            BuildFeatureProfile = CPPBuildFeatureProfile.CreateDefault(),
            LoadNativeRuntimeMetadata = true
        };

        new CPPGeneratedOutputAdapter().Apply(root, options);

        string output = File.ReadAllText(Path.Combine(root, "float4x4.cpp"));
        Assert.Equal(originalSource, output);
    }
}
