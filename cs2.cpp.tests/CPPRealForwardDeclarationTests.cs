using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Compiles scalar consumers with tag mismatches treated as errors, matching strict native SDK builds.</summary>
public sealed class CPPRealForwardDeclarationTests {
    /// <summary>Verifies intrinsic scalar declarations match their native struct definitions while ordinary types retain class declarations.</summary>
    [Fact]
    public void ScalarForwardDeclarationsMatchNativeStructDefinitions() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert("real-forward-declarations", """
            namespace helengine {
                public struct Real { }
                public struct RealEvaluation { }
            }
            public struct OrdinaryValue { public int Value; }
            public class ScalarConsumer {
                public void Store(helengine.Real value) { }
                public void Evaluate(helengine.RealEvaluation value) { }
                public void Ordinary(OrdinaryValue value) { }
            }
            """);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("return 0;", """
            #ifdef _MSC_VER
            #pragma warning(error:4099)
            #endif
            #ifdef __clang__
            #pragma clang diagnostic error "-Wmismatched-tags"
            #endif
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "ScalarConsumer.hpp"));
        Assert.Contains("struct Real;", header, StringComparison.Ordinal);
        Assert.Contains("struct RealEvaluation;", header, StringComparison.Ordinal);
        Assert.DoesNotContain("class Real;", header, StringComparison.Ordinal);
        Assert.DoesNotContain("class RealEvaluation;", header, StringComparison.Ordinal);
        Assert.Contains("class OrdinaryValue;", header, StringComparison.Ordinal);
    }
}
