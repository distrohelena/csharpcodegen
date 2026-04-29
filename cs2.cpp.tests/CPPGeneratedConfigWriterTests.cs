using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies runtime requirement registration and generated config header output for the C++ backend.
/// </summary>
public class CPPGeneratedConfigWriterTests {
    /// <summary>
    /// Ensures the registrar applies the default runtime requirements for the approved first milestone.
    /// </summary>
    [Fact]
    public void RegisterDefaults_RegistersHeadlessCoreRuntimeRequirements() {
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementCatalog catalog = new CPPRuntimeRequirementCatalog();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(catalog, report);

        registrar.RegisterDefaults(CPPConversionOptions.CreateDefault());

        Assert.True(registrar.IsRegistered("NativeString"));
        Assert.True(registrar.IsRegistered("NativeList"));
        Assert.True(registrar.IsRegistered("NativeDictionary"));
        Assert.False(report.HasErrors);
    }

    /// <summary>
    /// Ensures the generated config writer emits profile and runtime requirement defines.
    /// </summary>
    [Fact]
    public void Write_WritesExpectedConfigDefines() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        CPPConversionReport report = new CPPConversionReport();
        CPPRuntimeRequirementRegistrar registrar = new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), report);
        registrar.RegisterDefaults(options);

        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string filePath = CPPGeneratedConfigWriter.Write(outputFolder, options, registrar);
        string output = File.ReadAllText(filePath);

        Assert.Contains("#define HE_CPP_COMPILER_MSVC 1", output);
        Assert.Contains("#define HE_CPP_PLATFORM_WINDOWS 1", output);
        Assert.Contains("#define HE_CPP_RUNTIME_STL_LITE 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_STRING 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_LIST 1", output);
        Assert.Contains("#define HE_CPP_REQ_NATIVE_DICTIONARY 1", output);
    }
}
