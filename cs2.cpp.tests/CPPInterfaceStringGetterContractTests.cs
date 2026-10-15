using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Checks that stored and computed string properties preserve one native virtual-dispatch contract.</summary>
public sealed class CPPInterfaceStringGetterContractTests {
    /// <summary>Compiles interface and override calls whose implementations may return freshly computed strings.</summary>
    [Fact]
    public void PolymorphicGetters_ReturnValuesWithCompatibleSignatures() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(PolymorphicGetters_ReturnValuesWithCompatibleSignatures),
            """
            public interface ILabel { string Text { get; } }
            public class StoredLabel : ILabel {
                public string Text { get; set; } = "stored";
                public virtual string Caption => Text;
            }
            public sealed class ComputedLabel : StoredLabel {
                public override string Caption => "computed";
            }
            public sealed class ComputedInterfaceLabel : ILabel {
                public string Text => "computed";
            }
            """);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "ILabel.hpp"));
        Assert.Contains("virtual HeCppString get_Text() = 0;", header);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            StoredLabel stored;
            ILabel* storedView = &stored;
            if (storedView->get_Text() != "stored") return 1;
            ComputedInterfaceLabel computed;
            ILabel* computedView = &computed;
            if (computedView->get_Text() != "computed") return 2;
            ComputedLabel derived;
            StoredLabel* virtualView = &derived;
            if (virtualView->get_Caption() != "computed") return 3;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
