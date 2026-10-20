using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Executes portable two-byte engine storage with actual float32 intermediate arithmetic.</summary>
[Collection(CPPRealNumericCollection.Name)]
public sealed class CPPBinary16StorageCompilationTests {
    /// <summary>Compiled engine wrappers retain IEEE half storage, signed zero, and independent float evaluation.</summary>
    [Fact]
    public void Binary16StorageRetainsFloat32Evaluation() {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            public sealed class HalfStorageFixture {
                public RealEvaluation Transient() { Real a = Real.FromDouble(2048d); Real b = Real.FromDouble(1d); return (a + b) - a; }
                public RealEvaluation Stored() { Real a = Real.FromDouble(2048d); Real b = a + Real.FromDouble(1d); return b - a; }
                public Real Boundary(double value) { return Real.FromDouble(value); }
            }
            """, 16, 32, includeEngineVectors: true);
        Assert.False(output.Report.HasErrors, string.Join(Environment.NewLine, output.Report.Diagnostics.Select(d => d.Message)));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            static_assert(sizeof(Real) == 2 && alignof(Real) == 2);
            static_assert(sizeof(RealEvaluation) == 4);
            static_assert(sizeof(float3) == 6 && sizeof(float4) == 8 && sizeof(float4x4) == 32);
            HalfStorageFixture fixture;
            if (fixture.Transient().ToDouble() != 1.0) return 1;
            if (fixture.Stored().ToDouble() != 0.0) return 2;
            if (fixture.Boundary(65504.0).ToDouble() != 65504.0) return 3;
            if (std::bit_cast<std::uint16_t>(fixture.Boundary(-0.0)) != 0x8000) return 4;
            if (std::bit_cast<std::uint16_t>(fixture.Boundary(0x1p-24)) != 1) return 5;
            if (std::bit_cast<std::uint16_t>(fixture.Boundary(1.0 + 0x1p-11)) != 0x3c00) return 6;
            if (std::bit_cast<std::uint16_t>(fixture.Boundary(1.0 + 3.0 * 0x1p-11)) != 0x3c02) return 7;
            if (fixture.Boundary(65520.0).ToDouble() != std::numeric_limits<double>::infinity()) return 8;
            if (std::bit_cast<std::uint16_t>(fixture.Boundary(1.0 + 0x1p-11 + 0x1p-40)) != 0x3c01) return 9;
            for (unsigned pattern = 0; pattern <= 0xffffu; ++pattern) {
                const auto bits = static_cast<std::uint16_t>(pattern);
                const float actual = static_cast<float>(he_binary16::FromBits(bits));
                const float expected = Binary16Codec::Decode(bits);
                const bool nan = (bits & 0x7c00u) == 0x7c00u && (bits & 0x03ffu) != 0;
                if (nan) { if (!std::isnan(actual) || !std::isnan(expected)) return 10; }
                else {
                    if (std::bit_cast<std::uint32_t>(actual) != std::bit_cast<std::uint32_t>(expected)) return 11;
                    if (std::bit_cast<std::uint16_t>(fixture.Boundary(static_cast<double>(actual))) != bits) return 12;
                }
            }
            Real negativeZero = fixture.Boundary(-0.0);
            Real positiveZero = fixture.Boundary(0.0);
            if (!negativeZero.Equals(positiveZero) || negativeZero.GetHashCode() != positiveZero.GetHashCode()) return 13;
            Real firstNan = fixture.Boundary(std::numeric_limits<double>::quiet_NaN());
            Real secondNan = fixture.Boundary(-std::numeric_limits<double>::quiet_NaN());
            if (!firstNan.Equals(secondNan) || firstNan.GetHashCode() != secondNan.GetHashCode()) return 14;

            """, "#include <bit>");
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput + " exit=" + result.ProgramExitCode);
    }
}
