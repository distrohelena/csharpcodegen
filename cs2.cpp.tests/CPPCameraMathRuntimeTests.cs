using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Compiles and executes the trigonometric framing and exponential zoom operations used by authored cameras.</summary>
public sealed class CPPCameraMathRuntimeTests {
    /// <summary>Verifies both math providers expose Atan and Exp with the managed double and single return contracts.</summary>
    /// <param name="useStandardMath">Whether the generated runtime uses standard C++ math or the custom C provider.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Runtime_AtanAndExp_CompileAndMatchManagedPrecision(bool useStandardMath) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "builds", "codegen-tests"));
        string fixture = Path.Combine(root, "camera-math", useStandardMath ? "standard" : "custom");
        Directory.CreateDirectory(fixture);
        string project = Path.Combine(fixture, "Fixture.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>");
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.RuntimeProfile.UseStdMath = useStandardMath;
        if (!useStandardMath) {
            options.PlatformOptionValues = new Dictionary<string, string> {
                [CPPCodegenOptionNames.RuntimeMathHeader] = "math.h"
            };
        }
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(project);
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);

        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output).Run("""
            static_assert(std::is_same_v<decltype(Math::Atan(1.0)), double>);
            static_assert(std::is_same_v<decltype(Math::Exp(1.0)), double>);
            static_assert(std::is_same_v<decltype(MathF::Atan(1.0f)), float>);
            static_assert(std::is_same_v<decltype(MathF::Exp(1.0f)), float>);
            for (double value : {-1.0, 0.0, 0.4, 1.0}) {
                if (std::abs(Math::Atan(value) - std::atan(value)) > 1e-15) return 1;
                if (std::abs(Math::Exp(value) - std::exp(value)) > 1e-15) return 2;
                float single = static_cast<float>(value);
                if (std::abs(MathF::Atan(single) - std::atan(single)) > 1e-7f) return 3;
                if (std::abs(MathF::Exp(single) - std::exp(single)) > 3e-7f) return 4;
            }
            """, "#include \"system/math.hpp\"\n#include <cmath>\n#include <type_traits>");
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
