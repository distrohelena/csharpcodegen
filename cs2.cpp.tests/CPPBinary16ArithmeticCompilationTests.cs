using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Executes generated binary16 evaluation rather than testing a size-only native alias.</summary>
[Collection(CPPRealNumericCollection.Name)]
public sealed class CPPBinary16ArithmeticCompilationTests {
    /// <summary>Primitive, compound and math operations each commit to half before their result is consumed.</summary>
    [Fact]
    public void Binary16RoundsEachPrimitiveOperation() {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            public sealed class HalfArithmeticFixture {
                public RealEvaluation Sequence() { Real a = 2048d; Real b = 1d; return (a + b) - a; }
                public RealEvaluation Compound() { RealEvaluation a = 2048d; a += 1d; return a - 2048d; }
                public RealEvaluation Divide() { return (RealEvaluation)1d / (RealEvaluation)3d; }
                public RealEvaluation Sqrt() { return RealMath.Sqrt(2d); }
                public float4 Normalize() { float4 q = new float4(1d, 2d, 3d, 4d); q.Normalize(); return q; }
            }
            """, 16, 16, includeEngineVectors: true);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            static_assert(sizeof(Real) == 2 && sizeof(RealEvaluation) == 2);
            static_assert(sizeof(float4) == 8 && sizeof(float4x4) == 32);
            HalfArithmeticFixture fixture;
            if (fixture.Sequence().ToDouble() != 0.0 || fixture.Compound().ToDouble() != 0.0) return 1;
            if (fixture.Divide().Value.Bits != 0x3555u) return 2;
            if (fixture.Sqrt().Value.Bits != 0x3da8u) return 3;
            RealEvaluation tiny = he_binary16::FromBits(1);
            if ((tiny / RealEvaluation(2)).Value.Bits != 0) return 4;
            if (RealMath::Min(RealEvaluation(-0.0), RealEvaluation(0.0)).Value.Bits != 0x8000u) return 5;
            if (RealMath::Max(RealEvaluation(-0.0), RealEvaluation(0.0)).Value.Bits != 0) return 6;
            float4 q = fixture.Normalize();
            if (q.X.Value.Bits != 0x31d8u || q.Y.Value.Bits != 0x35d8u || q.Z.Value.Bits != 0x3862u || q.W.Value.Bits != 0x39d8u) return 7;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }
    /// <summary>Native primitive results match System.Half exactly; trig outputs allow at most one half ULP across math libraries.</summary>
    [Fact]
    public void Binary16OperationsMatchIndependentHalfOracle() {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("public sealed class HalfOracleFixture { }", 16, 16);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        System.Text.StringBuilder cases = new();
        uint state = 0x6a09e667u;
        for (int index = 0; index < 2048; index++) {
            state = unchecked(state * 1664525u + 1013904223u); ushort leftBits = (ushort)(state >> 8);
            state = unchecked(state * 1664525u + 1013904223u); ushort rightBits = (ushort)(state >> 8);
            Half a = BitConverter.UInt16BitsToHalf(leftBits); Half b = BitConverter.UInt16BitsToHalf(rightBits);
            Half[] expected = { a + b, a - b, a * b, a / b, a % b, Half.Sqrt(a), Half.Sin(a), Half.Cos(a), Half.Atan2(a, b) };
            cases.Append('{').Append(leftBits).Append(',').Append(rightBits);
            foreach (Half value in expected) { cases.Append(',').Append(BitConverter.HalfToUInt16Bits(value)); }
            cases.AppendLine("},");
        }
        string program = "const std::uint16_t cases[][11] = {" + cases + "};" + """
            for (const auto& row : cases) {
                RealEvaluation a(he_binary16::FromBits(row[0]));
                RealEvaluation b(he_binary16::FromBits(row[1]));
                RealEvaluation actual[] = {a + b, a - b, a * b, a / b, RealMath::Remainder(a,b), RealMath::Sqrt(a), RealMath::Sin(a), RealMath::Cos(a), RealMath::Atan2(a,b)};
                for (unsigned operation = 0; operation < 9; ++operation) {
                    const auto expected = row[operation + 2];
                    const auto observed = actual[operation].Value.Bits;
                    const bool expectedNaN = (expected & 0x7c00u) == 0x7c00u && (expected & 0x3ffu) != 0;
                    const bool observedNaN = (observed & 0x7c00u) == 0x7c00u && (observed & 0x3ffu) != 0;
                    if (expectedNaN) { if (!observedNaN) return 20 + operation; continue; }
                    if (observedNaN) return 40 + operation;
                    const unsigned tolerance = operation >= 6 ? 1u : 0u;
                    const unsigned difference = observed > expected ? observed - expected : expected - observed;
                    if (difference > tolerance) return 60 + operation;
                }
            }
            """;
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run(program, "#include \"runtime/native_real.hpp\"");
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }

}
