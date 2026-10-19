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

    /// <summary>Compiles all actual vector and matrix methods with their selected storage and evaluation widths.</summary>
    [Theory]
    [InlineData(32, 32)] [InlineData(64, 64)] [InlineData(32, 64)]
    public void EngineVectorsExecuteSelectedPolicy(int storageBits, int arithmeticBits) {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            public sealed class VectorFixture {
                public float3 Interpolate() { return float3.Lerp(new float3(16777216d, 0d, 0d), new float3(16777218d, 2d, 0d), 0.5d); }
                public RealEvaluation MixedLiteral() { Real value = 2d; return value * 0.5d + 1d; }
                public double FixedOutput() { return (double)Real.FromDouble(3d); }
                public RealEvaluation Bounded() { return RealMath.Clamp(3d, 1d, 2d); }
                public RealEvaluation DotCancellation() { return float3.Dot(new float3(16777216d, 1d, 0d), new float3(1d, 1d, 0d)) - Real.FromDouble(16777216d); }
            }
            """, storageBits, arithmeticBits, includeEngineVectors: true);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        string expected = storageBits == 64 && arithmeticBits == 64 ? "16777217.0" : "16777216.0";
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            VectorFixture fixture;
            if (sizeof(float3) != {{3 * storageBits / 8}}) return 1;
            if (sizeof(float4x4) != {{16 * storageBits / 8}}) return 2;
            if (fixture.Interpolate().X.ToDouble() != {{expected}}) return 3;
            if (fixture.Interpolate().Y.ToDouble() != 1.0) return 4;
            if (fixture.MixedLiteral().ToDouble() != 2.0) return 5;
            if (fixture.FixedOutput() != 3.0) return 6;
            if (RealMath::Round(RealEvaluation(2.5)).ToDouble() != 2.0) return 7;
            if (RealMath::Round(RealEvaluation(3.5)).ToDouble() != 4.0) return 8;
            if (RealMath::Round(RealEvaluation(-2.5), MidpointRounding::AwayFromZero).ToDouble() != -3.0) return 9;
            if (!std::signbit(RealMath::Min(RealEvaluation(-0.0), RealEvaluation(0.0)).ToDouble())) return 10;
            if (std::signbit(RealMath::Max(RealEvaluation(0.0), RealEvaluation(-0.0)).ToDouble())) return 11;
            if (!std::signbit(RealMath::Round(RealEvaluation(-0.25)).ToDouble())) return 12;
            if (RealMath::Round(RealEvaluation(8388609.0)).ToDouble() != 8388609.0) return 13;
            if (fixture.Bounded().ToDouble() != 2.0) return 14;
            if (fixture.DotCancellation().ToDouble() != {{(arithmeticBits == 64 ? "1.0" : "0.0")}}) return 15;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }

    /// <summary>Executes the actual constrained layout specialization, including selected-width integer ingress.</summary>
    [Theory]
    [InlineData(32, 32)] [InlineData(64, 64)] [InlineData(32, 64)]
    public void LayoutKernelExecutesSelectedPolicy(int storageBits, int arithmeticBits) {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            namespace helengine {
                // Fixture declarations expose component constants only; every formula is linked from the actual checkout.
                public static class ViewportComponent { public const int NoScalingMode = 0, ReferenceCanvasScalingMode = 1, ReferenceCanvasStretchScalingMode = 2, DensityScalingMode = 3; }
                public static class LayoutComponent { public const byte LeftAnchorFlag = 1, RightAnchorFlag = 2, TopAnchorFlag = 4, BottomAnchorFlag = 8; }
            }
            public sealed class LayoutFixture {
                public int Dimension() { return LayoutNumericKernel<RealEvaluation, RuntimeLayoutNumericOperations>.ScaleInteger(16777217, 1d, new RuntimeLayoutNumericOperations()); }
                public int FitWidth() { return LayoutNumericKernel<RealEvaluation, RuntimeLayoutNumericOperations>.Fit(320d, 240d, 1280, 720, 1d, new RuntimeLayoutNumericOperations()).X; }
                public Real Density() { return LayoutNumericKernel<RealEvaluation, RuntimeLayoutNumericOperations>.Resolve(3, new int2(1, 1), new int2(320, 240), 1d, 1d, 16777217d, 16777216d, new RuntimeLayoutNumericOperations()).ScaleX; }

                public float3 Position() {
                    ViewportLayoutMetrics metrics = new ViewportLayoutMetrics { ScaleX = 3d, ScaleY = 1d, GlyphScale = 1d };
                    return LayoutNumericKernel<RealEvaluation, RuntimeLayoutNumericOperations>.Position(new float3(16777217d, 2d, 3d), metrics, new RuntimeLayoutNumericOperations());
                }
            }
            """, storageBits, arithmeticBits, includeEngineVectors: true, includeEngineLayout: true);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            LayoutFixture fixture;
            if (fixture.Dimension() != {{(arithmeticBits == 32 ? "16777216" : "16777217")}}) return 1;
            if (fixture.Position().X.ToDouble() != {{(storageBits == 64 ? "50331651.0" : "50331648.0")}}) return 2;
            if (fixture.FitWidth() != 320) return 3;
            if (fixture.Density().ToDouble() != {{(storageBits == 64 ? "1.0000000596046448" : "1.0")}}) return 4;
            static_assert(!std::is_polymorphic_v<RuntimeLayoutNumericOperations>);

            """);
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
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }
}
