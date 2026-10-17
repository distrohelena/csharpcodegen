using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Compiles formatted primitive values used by scene diagnostics and verifies invariant output rather than invalid C++ member invocations.</summary>
public sealed class CPPFormattedNumericCompilationTests {
    /// <summary>Integer padding, fixed precision, named format arguments and invariant providers retain their managed results in a native executable.</summary>
    [Fact]
    public void NumericToStringPreservesInvariantPaddingAndPrecision() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(NumericToStringPreservesInvariantPaddingAndPrecision),
            """
            using System.Globalization;
            public sealed class NumericFixture {
                public string Build(int number, float amount, double duration) {
                    return number.ToString("D6", CultureInfo.InvariantCulture) + ":" + amount.ToString("F2", CultureInfo.InvariantCulture) + ":" + duration.ToString("F1");
                }
                public string Negative(int number) { return number.ToString(provider: CultureInfo.InvariantCulture, format: "D6"); }
                public string ProviderOnly(int number) { return number.ToString(CultureInfo.InvariantCulture); }
            }
            """);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "NumericFixture.cpp"));
        Assert.Contains("String::FormatNumeric", source);
        Assert.DoesNotContain("CultureInfo", source);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            NumericFixture fixture;
            if (fixture.Build(12, 3.5f, 8.25) != "000012:3.50:8.2") return 1;
            if (fixture.Negative(-12) != "-000012") return 2;
            if (fixture.ProviderOnly(42) != "42") return 3;
            if (String::FormatNumeric(0xffffffffu, "X8") != "FFFFFFFF") return 4;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
