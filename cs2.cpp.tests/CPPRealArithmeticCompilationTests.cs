using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Executes generated numeric code and verifies layout, arithmetic width and fixed primitive boundaries.</summary>
public sealed class CPPRealArithmeticCompilationTests {
    /// <summary>The same actual engine source lowers to each selected native representation and executes its semantics.</summary>
    [Theory]
    [InlineData(32, 32)] [InlineData(64, 64)] [InlineData(32, 64)]
    public void RealNumericCodeExecutesSelectedPolicy(int storageBits, int arithmeticBits) {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            public sealed class NumericFixture {
                public Real Stored;
                public float FixedSingle;
                public double FixedDouble;
                public RealEvaluation Cancellation() {
                    Real large = Real.FromDouble(16777216d);
                    Real one = Real.FromDouble(1d);
                    return (large + one) - large;
                }
                public Real Store() { return Real.FromDouble(16777216d) + Real.FromDouble(1d); }
                public RealEvaluation Root() { return RealMath.Sqrt(Real.FromDouble(2d)); }
                public RealEvaluation Sine() { return RealMath.Sin(Real.FromDouble(0.5d)); }
                public RealEvaluation Cosine() { return RealMath.Cos(Real.FromDouble(0.5d)); }
                public RealEvaluation Angle() { return RealMath.Atan2(Real.FromDouble(1d), Real.FromDouble(2d)); }
                public RealEvaluation Modulo() { return RealMath.Remainder(Real.FromDouble(5.5d), Real.FromDouble(2d)); }
                public RealEvaluation Interpolate() { return RealMath.Lerp(Real.FromDouble(16777216d), Real.FromDouble(16777218d), Real.FromDouble(0.5d)); }
                public int SelectedStorage() { return RealRepresentation.StorageBits; }
                public int SelectedArithmetic() { return RealRepresentation.ArithmeticBits; }
            }
            """, storageBits, arithmeticBits);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        string cancellation = arithmeticBits == 64 ? "1.0" : "0.0";
        string stored = storageBits == 64 && arithmeticBits == 64 ? "16777217.0" : "16777216.0";
        double expectedRoot = arithmeticBits == 32 ? (double)MathF.Sqrt(2f) : Math.Sqrt(2d);
        string rootLiteral = expectedRoot.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string sine = (arithmeticBits == 32 ? (double)MathF.Sin(0.5f) : Math.Sin(0.5d)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string cosine = (arithmeticBits == 32 ? (double)MathF.Cos(0.5f) : Math.Cos(0.5d)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string angle = (arithmeticBits == 32 ? (double)MathF.Atan2(1f, 2f) : Math.Atan2(1d, 2d)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string interpolation = arithmeticBits == 32 ? "16777216.0" : "16777217.0";
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            NumericFixture fixture;
            if (sizeof(Real) != {{storageBits / 8}}) return 1;
            if (sizeof(RealEvaluation) != {{arithmeticBits / 8}}) return 2;
            if (sizeof(fixture.FixedSingle) != 4 || sizeof(fixture.FixedDouble) != 8) return 3;
            if (fixture.Cancellation().ToDouble() != {{cancellation}}) return 4;
            if (fixture.Store().ToDouble() != {{stored}}) return 5;
            if (fixture.SelectedStorage() != {{storageBits}} || fixture.SelectedArithmetic() != {{arithmeticBits}}) return 6;
            if (fixture.Root().ToDouble() != {{rootLiteral}}) return 7;
            if (fixture.Sine().ToDouble() != {{sine}}) return 8;
            if (fixture.Cosine().ToDouble() != {{cosine}}) return 9;
            if (fixture.Angle().ToDouble() != {{angle}}) return 10;
            if (fixture.Modulo().ToDouble() != 1.5) return 11;
            if (fixture.Interpolate().ToDouble() != {{interpolation}}) return 12;
            if (!std::signbit(Real::FromDouble(-0.0).ToDouble())) return 13;
            Real firstNan = Real::FromDouble(std::bit_cast<double>(uint64_t(0x7ff8000000000000ULL)));
            Real secondNan = Real::FromDouble(std::bit_cast<double>(uint64_t(0x7ff9000000000000ULL)));
            if (!firstNan.Equals(secondNan)) return 14;
            if (firstNan.GetHashCode() != secondNan.GetHashCode()) return 15;
            """, "#include <bit>");
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }

    /// <summary>A same-named user scalar is never replaced by the engine's intrinsic representation.</summary>
    [Fact]
    public void UnrelatedRealTypeIsUnchanged() {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert(
            "namespace other { public struct Real { public double Value; } }", 32, 32, false);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("if (sizeof(Real) != 8) return 1;");
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
