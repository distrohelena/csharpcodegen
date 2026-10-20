using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Exercises build-policy properties through the same optimized-property lowering used by complete Core builds.</summary>
[Collection(CPPRealNumericCollection.Name)]
public sealed class CPPRealRepresentationCompilationTests {
    /// <summary>Qualified and imported policy getters use the intrinsic accessor and preserve the selected native identity.</summary>
    [Theory]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    public void RepresentationPolicyGetterExecutesSelectedNativeContract(int storageBits, int arithmeticBits) {
        using CPPOwnershipConversionOutput output = new CPPRealNumericTestWorkspace().Convert("""
            using helengine;
            using static helengine.RealRepresentation;
            public sealed class ImportedRepresentationFixture {
                readonly NumericPrecisionPolicy Captured = RealRepresentation.Policy;
                public NumericPrecisionPolicy Read() { return Captured; }
            }
            public sealed class RepresentationFixture {
                public NumericPrecisionPolicy Qualified() { return RealRepresentation.Policy; }
                public NumericPrecisionPolicy Imported() { return Policy; }
                public int StoredWidth() { return RealRepresentation.Policy.StorageBits; }
                public int EvaluationWidth() { return RealRepresentation.Policy.ArithmeticBits; }
            }
            """, storageBits, arithmeticBits);
        Assert.False(output.Report.HasErrors);
        Assert.Contains("#include \"RealRepresentation.hpp\"", File.ReadAllText(Path.Combine(output.OutputPath, "ImportedRepresentationFixture.cpp")));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            ImportedRepresentationFixture imported;
            RepresentationFixture fixture;
            if (imported.Read() != fixture.Qualified()) return 4;
            if (fixture.Qualified() != fixture.Imported()) return 1;
            if (fixture.StoredWidth() != {{storageBits}}) return 2;
            if (fixture.EvaluationWidth() != {{arithmeticBits}}) return 3;
            return 0;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.Equal(0, result.ProgramExitCode);
    }
}
